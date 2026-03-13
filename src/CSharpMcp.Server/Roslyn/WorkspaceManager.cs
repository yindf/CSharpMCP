using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Buildalyzer;
using Buildalyzer.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 工作区管理器实现 - 使用 Buildalyzer + AdhocWorkspace
/// </summary>
internal sealed partial class WorkspaceManager : IWorkspaceManager, IDisposable
{
    private readonly ILogger<WorkspaceManager> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private AdhocWorkspace? _workspace;
    private AnalyzerManager? _manager;
    private Solution? _currentSolution;
    private IEnumerable<Project>? _userProjects;
    private string? _loadedPath;
    private DateTime _lastUpdate;
    private FileWatcherService? _fileWatcher;
    private bool _isUnityProject;

    // File-to-project mapping for fast incremental updates
    private readonly ConcurrentDictionary<string, ProjectId> _fileToProjectMap = new();

    // 已删除文件跟踪
    private readonly HashSet<string> _deletedFilePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _deletedFilesLock = new();

    private IEnumerable<Project> UserProjects
    {
        get
        {
            if (_userProjects == null)
            {
                _userProjects = GetUserProjects();
            }

            return _userProjects;
        }
    }

    public WorkspaceManager(ILogger<WorkspaceManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 加载解决方案或项目
    /// </summary>
    public async Task<WorkspaceInfo> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));
            }

            _logger.LogInformation("Loading workspace from: {Path}", path);

            // 如果工作区已加载，先关闭当前工作区以便重新加载
            if (_currentSolution != null)
            {
                _logger.LogInformation("Closing current workspace to reload");
                StopFileWatcher();
                _workspace?.Dispose();
                _workspace = null;
                _manager = null;
                _currentSolution = null;
                _loadedPath = null;
                _userProjects = null;
                _fileToProjectMap.Clear();
                lock (_deletedFilesLock)
                {
                    _deletedFilePaths.Clear();
                }
            }

            // First, check if it's a directory (search for .sln, .slnx, or .csproj)
            if (Directory.Exists(path))
            {
                // First, try to find .sln or .slnx file in top level only
                var slnFile = Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault()
                    ?? Directory.GetFiles(path, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (slnFile != null)
                {
                    path = slnFile;
                    _logger.LogInformation("Found solution file: {Path}", path);
                }
                else
                {
                    // If no .sln/.slnx, look for .csproj in top level only (not recursive)
                    var csprojFile = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (csprojFile != null)
                    {
                        path = csprojFile;
                        _logger.LogInformation("Found project file: {Path}", path);
                    }
                    else
                    {
                        throw new FileNotFoundException($"No .sln, .slnx, or .csproj file found in directory: {path}");
                    }
                }
            }
            // Not a directory, check if it's an existing file
            else if (File.Exists(path))
            {
                var fileExtension = Path.GetExtension(path).ToLowerInvariant();
                if (fileExtension != ".sln" && fileExtension != ".slnx" && fileExtension != ".csproj")
                {
                    throw new NotSupportedException($"Unsupported file type: {fileExtension}. Expected .sln, .slnx, or .csproj file.");
                }
            }
            // Neither a directory nor an existing file
            else
            {
                throw new FileNotFoundException($"Path not found: {path}");
            }

            var normalizedPath = Path.GetFullPath(path);
            _logger.LogInformation("Opening workspace file: {Path}", normalizedPath);

            try
            {
                // Use Buildalyzer to create workspace
                (_workspace, _manager) = await BuildalyzerWorkspaceFactory.CreateWorkspaceAsync(
                    normalizedPath,
                    _logger,
                    cancellationToken);

                _currentSolution = _workspace.CurrentSolution;
                _isUnityProject = await IsUnityProjectAsync(_currentSolution.Projects, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Buildalyzer workspace: {Path}", normalizedPath);
                throw new InvalidOperationException($"Failed to open workspace file: {normalizedPath}. Error: {ex.Message}", ex);
            }

            _loadedPath = normalizedPath;
            _lastUpdate = DateTime.UtcNow;

            // Build file-to-project mapping for incremental updates
            BuildFileToProjectMap();

            // Start file watcher for automatic workspace updates
            StartFileWatcher();

            var info = new WorkspaceInfo(
                _loadedPath,
                DetermineWorkspaceKind(normalizedPath),
                _currentSolution.Projects.Count(),
                _currentSolution.Projects.Sum(p => p.DocumentIds.Count)
            );

            _logger.LogInformation(
                "Workspace loaded: {Kind} with {ProjectCount} projects and {DocumentCount} documents",
                info.Kind,
                info.ProjectCount,
                info.DocumentCount
            );

            return info;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private static WorkspaceKind DetermineWorkspaceKind(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".sln" or ".slnx" => WorkspaceKind.Solution,
            ".csproj" => WorkspaceKind.Project,
            _ => WorkspaceKind.Folder
        };
    }

    /// <summary>
    /// 获取文档
    /// </summary>
    public async Task<Document?> GetDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // 跳过已删除的文件
        if (IsFileDeleted(filePath))
        {
            _logger.LogDebug("Skipping deleted file: {Path}", filePath);
            return null;
        }

        if (_currentSolution == null)
        {
            _logger.LogInformation("Workspace not loaded, attempting auto-load");
            try
            {
                // Try to find a solution near the target file
                string searchPath = Directory.GetCurrentDirectory();
                bool solutionFound = false;

                if (!string.IsNullOrEmpty(filePath))
                {
                    var fileDir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(fileDir) && Directory.Exists(fileDir))
                    {
                        // Search up the directory tree for a .sln file
                        var currentDir = new DirectoryInfo(fileDir);
                        while (currentDir != null && !solutionFound)
                        {
                            _logger.LogInformation("Searching for solution in: {Path}", currentDir.FullName);

                            // Check for .sln file in current directory
                            var slnFile = currentDir.GetFiles("*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
                            if (slnFile != null)
                            {
                                searchPath = slnFile.FullName;
                                solutionFound = true;
                                _logger.LogInformation("Found solution file: {Path}", searchPath);
                                break;
                            }

                            // Move up to parent directory (stop at drive root or a limit)
                            if (currentDir.Parent == null || currentDir.FullName.Length < 10)
                            {
                                break;
                            }
                            currentDir = currentDir.Parent;
                        }
                    }
                }

                await LoadAsync(searchPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-load workspace");
                return null;
            }
        }

        // Try exact path match first
        var documentIds = _currentSolution!.GetDocumentIdsWithFilePath(filePath);
        if (documentIds.Any())
        {
            return _currentSolution.GetDocument(documentIds.First());
        }

        // Try relative path
        if (_loadedPath != null)
        {
            var baseDir = Path.GetDirectoryName(_loadedPath);
            if (baseDir != null)
            {
                var fullPath = Path.IsPathRooted(filePath)
                    ? filePath
                    : Path.Combine(baseDir, filePath);

                documentIds = _currentSolution.GetDocumentIdsWithFilePath(fullPath);
                if (documentIds.Any())
                {
                    return _currentSolution.GetDocument(documentIds.First());
                }
            }
        }

        // Try file name only
        var fileName = Path.GetFileName(filePath);
        var docs = UserProjects
            .SelectMany(p => p.Documents)
            .Where(d => string.Equals(
                Path.GetFileName(d.FilePath),
                fileName,
                StringComparison.OrdinalIgnoreCase));

        var doc = docs.FirstOrDefault();
        if (doc != null)
        {
            _logger.LogInformation("Found document by file name: {FilePath}", filePath);
            return doc;
        }

        _logger.LogWarning("Document not found: {FilePath}", filePath);
        return null;
    }

    /// <summary>
    /// 获取编译
    /// </summary>
    public async Task<Compilation?> GetCompilationAsync(string? projectPath = null,
        CancellationToken cancellationToken = default)
    {
        if (_currentSolution == null)
        {
            _logger.LogInformation("Workspace not loaded, attempting auto-load from current directory");
            try
            {
                // Auto-load workspace from current directory
                await LoadAsync(Directory.GetCurrentDirectory(), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-load workspace");
                return null;
            }
        }

        Project? project;

        if (string.IsNullOrEmpty(projectPath))
        {
            // Get the first project that can compile
            project = _currentSolution!.Projects.FirstOrDefault(p =>
            {
                try
                {
                    return p.SupportsCompilation;
                }
                catch
                {
                    return false;
                }
            });

            if (project == null)
            {
                _logger.LogWarning("No compilable project found");
                return null;
            }
        }
        else
        {
            // Find specific project
            project = _currentSolution!.Projects.FirstOrDefault(p =>
                string.Equals(p.FilePath, projectPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    Path.GetFileName(p.FilePath),
                    Path.GetFileName(projectPath),
                    StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                _logger.LogWarning("Project not found: {ProjectPath}", projectPath);
                return null;
            }
        }

        try
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation != null)
            {
                _logger.LogInformation("Created compilation for project: {ProjectName}", project.Name);
            }

            return compilation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create compilation for project: {ProjectName}", project.Name);
            return null;
        }

    }

    /// <summary>
    /// 获取语义模型
    /// </summary>
    public async Task<SemanticModel?> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync(filePath, cancellationToken);
        if (document == null)
        {
            return null;
        }

        try
        {
            return await document.GetSemanticModelAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get semantic model for: {FilePath}", filePath);
            return null;
        }
    }

    private IEnumerable<Project> GetUserProjects()
    {
        if (_currentSolution == null)
        {
            return [];
        }

        if (!_isUnityProject)
        {
            return _currentSolution.Projects;
        }

        return _currentSolution.Projects
            .Where(p =>
            {
                var doc = p.Documents.FirstOrDefault();
                if (doc == null)
                    return false;

                var filePath = doc.FilePath;
                if (string.IsNullOrEmpty(filePath))
                    return false;

                if (filePath.Contains("Packages", StringComparison.OrdinalIgnoreCase) ||
                    filePath.Contains("PackageCache", StringComparison.OrdinalIgnoreCase))
                    return false;

                _logger.LogInformation("[UserProject] Found project {ProjectName}", p.Name);
                return true;
            });
    }

    public async Task<IEnumerable<ISymbol>> SearchSymbolsWithPatternAsync(string query, SymbolFilter filter, CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        foreach (var project in UserProjects)
        {
            _logger.LogInformation("Searching symbols for project {ProjectName}", project.Name);
            symbols.AddRange(await SymbolFinder.FindSourceDeclarationsWithPatternAsync(project, query, filter, cancellationToken));
        }
        return symbols;
    }

    public async Task<IEnumerable<ISymbol>> SearchSymbolsAsync(string query, SymbolFilter filter, CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        foreach (var project in UserProjects)
        {
            _logger.LogInformation("Searching symbols for project {ProjectName}", project.Name);
            symbols.AddRange(await SymbolFinder.FindSourceDeclarationsAsync(project, query, true, filter, cancellationToken));
        }
        return symbols;
    }

    /// <summary>
    /// 获取源文本
    /// </summary>
    public async Task<SourceText?> GetSourceTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync(filePath, cancellationToken);
        if (document == null)
        {
            return null;
        }

        try
        {
            return await document.GetTextAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get source text for: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// 获取当前解决方案
    /// </summary>
    public Solution? GetCurrentSolution() => _currentSolution;

    /// <summary>
    /// 获取所有项目
    /// </summary>
    public IEnumerable<Project> GetProjects()
    {
        return UserProjects;
    }

    /// <summary>
    /// 获取工作区是否已加载
    /// </summary>
    public bool IsWorkspaceLoaded => _currentSolution != null;

    /// <summary>
    /// 检查工作区是否需要重新加载（当 sln/csproj 变更或文件删除/添加时）
    /// </summary>
    public bool NeedsWorkspaceReload => _fileWatcher?.NeedsWorkspaceReload ?? false;

    /// <summary>
    /// 是否需要 Unity 刷新（当 Unity 项目的 cs 文件增删时）
    /// </summary>
    public bool NeedsUnityRefresh => _fileWatcher?.NeedsUnityRefresh ?? false;

    /// <summary>
    /// 获取 Unity 刷新提示信息（用于显示给大模型）
    /// </summary>
    public string? GetUnityRefreshHint()
    {
        if (!_isUnityProject || _fileWatcher?.NeedsUnityRefresh != true)
        {
            return null;
        }

        return "> **Unity Project:** .cs files have been added or deleted. Please switch to Unity Editor to refresh project files, then the diagnostics will be accurate.";
    }

    /// <summary>
    /// 确保工作区是新鲜的（如果需要会重新加载整个工作区）
    /// 在需要最新编译状态的工具调用前使用
    /// </summary>
    public async Task EnsureRefreshAsync(CancellationToken cancellationToken = default)
    {
        // 如果有待处理的文件变更或需要重新加载，直接强制重新编译
        if (_fileWatcher?.HasPendingChanges == true || _fileWatcher?.NeedsWorkspaceReload == true)
        {
            _logger.LogInformation("Forcing full recompile for accurate diagnostics...");
            await ForceRecompileAsync(cancellationToken);
            _fileWatcher?.ClearNeedsReload();
            _fileWatcher?.ClearPendingChanges();
            _logger.LogInformation("Workspace recompiled");
        }
    }

    /// <summary>
    /// 清除 Unity 刷新提示（在提示显示后调用）
    /// </summary>
    public void ClearUnityRefreshHint()
    {
        _fileWatcher?.ClearUnityRefresh();
    }

    /// <summary>
    /// 应用 Solution 变更到工作区并持久化到磁盘
    /// 用于重构工具（如 RenameSymbol）完成后保存更改
    /// </summary>
    public async Task<ApplyChangesResult> ApplyChangesAsync(Solution newSolution, CancellationToken cancellationToken = default)
    {
        if (_workspace == null || _currentSolution == null)
        {
            return new ApplyChangesResult(false, [], "Workspace not loaded");
        }

        // Get changed documents before applying (compare old vs new)
        var changes = newSolution.GetChanges(_currentSolution);
        var changedDocIds = changes
            .GetProjectChanges()
            .SelectMany(p => p.GetChangedDocuments())
            .ToList();
        var changedFiles = new List<string>();

        _logger.LogInformation("Applying solution changes: {Count} documents changed", changedDocIds.Count);

        // Persist to disk - get text from NEW solution, not from workspace after applying
        foreach (var docId in changedDocIds)
        {
            // Get document from the NEW solution (which has the updated content)
            var doc = newSolution.GetDocument(docId);
            if (doc?.FilePath == null) continue;

            try
            {
                var text = await doc.GetTextAsync(cancellationToken);
                await File.WriteAllTextAsync(doc.FilePath, text.ToString(), cancellationToken);
                changedFiles.Add(doc.FilePath);
                _logger.LogDebug("Saved changed file: {FilePath}", doc.FilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save file: {FilePath}", doc.FilePath);
            }
        }

        // Apply to workspace after persisting to disk
        if (!_workspace.TryApplyChanges(newSolution))
        {
            return new ApplyChangesResult(false, [], "Failed to apply changes to workspace");
        }

        _currentSolution = _workspace.CurrentSolution;
        _userProjects = null; // Clear cached projects to force refresh from new solution

        _logger.LogInformation("Solution changes applied and saved: {Count} files", changedFiles.Count);

        return new ApplyChangesResult(true, changedFiles);
    }

    /// <summary>
    /// 检查文件是否已被删除（物理文件不存在，但仍在 Workspace 中）
    /// </summary>
    public bool IsFileDeleted(string filePath)
    {
        lock (_deletedFilesLock)
        {
            return _deletedFilePaths.Contains(filePath);
        }
    }

    /// <summary>
    /// 获取所有已删除的文件路径
    /// </summary>
    public IReadOnlySet<string> GetDeletedFilePaths()
    {
        lock (_deletedFilesLock)
        {
            return new HashSet<string>(_deletedFilePaths, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 强制重新编译整个工作区，用于获取最新的诊断信息
    /// </summary>
    public async Task ForceRecompileAsync(CancellationToken cancellationToken)
    {
        if (_currentSolution == null || string.IsNullOrEmpty(_loadedPath))
        {
            return;
        }

        _logger.LogInformation("Force recompiling workspace for fresh diagnostics...");

        try
        {
            // 通过重新加载解决方案来强制重新编译
            var originalPath = _loadedPath;

            // 保存当前加载的路径
            _loadedPath = null;
            _currentSolution = null;

            // 重新加载
            await LoadAsync(originalPath!, cancellationToken);

            _logger.LogInformation("Force recompile completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to force recompile");
        }
    }

    /// <summary>
    /// 标记文件为已删除
    /// </summary>
    internal void MarkFileAsDeleted(string filePath)
    {
        lock (_deletedFilesLock)
        {
            _deletedFilePaths.Add(filePath);
            _logger.LogInformation("File marked as deleted: {Path} (total: {Count})",
                filePath, _deletedFilePaths.Count);
        }
    }

    private static async Task<bool> IsUnityProjectAsync(IEnumerable<Project> projects, CancellationToken cancellationToken)
    {
        foreach (var project in projects)
        {
            var symbols = await SymbolFinder.FindDeclarationsAsync(project, "UnityEngine", false, SymbolFilter.Namespace, cancellationToken);
            if (symbols.Any())
            {
                return true;
            }
        }
        return false;
    }

    #region Incremental Updates

    /// <summary>
    /// Build file-to-project mapping after workspace load
    /// </summary>
    private void BuildFileToProjectMap()
    {
        _fileToProjectMap.Clear();

        if (_currentSolution == null) return;

        foreach (var project in _currentSolution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath != null)
                {
                    _fileToProjectMap[document.FilePath] = project.Id;
                }
            }
        }

        _logger.LogInformation("File-to-project mapping built: {Count} files", _fileToProjectMap.Count);
    }

    /// <summary>
    /// Update mapping for a single project
    /// </summary>
    private void UpdateFileToProjectMapping(ProjectId projectId)
    {
        if (_currentSolution == null) return;

        var project = _currentSolution.GetProject(projectId);
        if (project == null) return;

        // Remove old entries for this project
        foreach (var kvp in _fileToProjectMap.Where(kvp => kvp.Value == projectId).ToList())
        {
            _fileToProjectMap.TryRemove(kvp.Key, out _);
        }

        // Add new entries
        foreach (var document in project.Documents)
        {
            if (document.FilePath != null)
            {
                _fileToProjectMap[document.FilePath] = project.Id;
            }
        }
    }

    /// <summary>
    /// Reload a single project in-place, preserving ProjectId and cross-project references
    /// </summary>
    public async Task<bool> ReloadProjectAsync(string projectPath, CancellationToken cancellationToken)
    {
        if (_currentSolution == null || _manager == null || _workspace == null) return false;

        var projectAnalyzer = BuildalyzerWorkspaceFactory.GetProjectAnalyzer(_manager, projectPath);
        if (projectAnalyzer == null)
        {
            _logger.LogWarning("Project analyzer not found for: {ProjectPath}", projectPath);
            return false;
        }

        // Rebuild project with Buildalyzer
        IAnalyzerResult? newResult;
        try
        {
            newResult = projectAnalyzer.Build().FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild project: {ProjectPath}", projectPath);
            return false;
        }

        if (newResult == null)
        {
            _logger.LogWarning("Build result is null for: {ProjectPath}", projectPath);
            return false;
        }

        // Find existing project (keep ProjectId!)
        var oldProject = _currentSolution.Projects
            .FirstOrDefault(p => string.Equals(p.FilePath, projectPath, StringComparison.OrdinalIgnoreCase));

        if (oldProject == null)
        {
            _logger.LogWarning("Project not found in solution: {ProjectPath}", projectPath);
            return false;
        }

        _logger.LogInformation("Reloading project in-place: {ProjectName}", oldProject.Name);

        // Build new solution with updated project
        var newSolution = _currentSolution;

        // Update metadata references
        var metadataRefs = newResult.References
            .Select(r => MetadataReference.CreateFromFile(r))
            .ToList();

        newSolution = newSolution.WithProjectMetadataReferences(oldProject.Id, metadataRefs);

        // Keep existing compilation options (they rarely change during reload)
        // For Unity projects, AllowUnsafe should already be set from initial load

        // Update documents - remove old ones first
        foreach (var doc in oldProject.DocumentIds.ToList())
        {
            newSolution = newSolution.RemoveDocument(doc);
        }

        // Add new documents
        foreach (var sourceFile in newResult.SourceFiles)
        {
            var sourceText = await ReadFileWithRetryAsync(sourceFile);
            if (sourceText == null) continue;

            var docId = DocumentId.CreateNewId(oldProject.Id);
            newSolution = newSolution.AddDocument(
                docId,
                Path.GetFileName(sourceFile),
                sourceText,
                filePath: sourceFile);
        }

        // Apply changes - same ProjectId, cross-project references preserved!
        if (_workspace.TryApplyChanges(newSolution))
        {
            _currentSolution = _workspace.CurrentSolution;

            // Update file-to-project mapping for this project
            UpdateFileToProjectMapping(oldProject.Id);

            _logger.LogInformation("Project reloaded successfully: {ProjectName}", oldProject.Name);
            return true;
        }

        _logger.LogWarning("Failed to apply changes for project: {ProjectName}", oldProject.Name);
        return false;
    }

    /// <summary>
    /// Incremental update for changed .cs files
    /// </summary>
    public async Task<bool> UpdateDocumentsAsync(IEnumerable<string> changedFiles, CancellationToken cancellationToken)
    {
        if (_currentSolution == null || _workspace == null) return false;

        var filesByProject = changedFiles
            .Where(f => _fileToProjectMap.TryGetValue(f, out _))
            .GroupBy(f => _fileToProjectMap[f])
            .ToList();

        if (!filesByProject.Any())
        {
            _logger.LogDebug("No tracked files in change set");
            return false;
        }

        var newSolution = _currentSolution;

        foreach (var group in filesByProject)
        {
            var projectId = group.Key;
            var project = newSolution.GetProject(projectId);
            if (project == null) continue;

            foreach (var filePath in group)
            {
                var document = project.Documents.FirstOrDefault(d =>
                    string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

                if (document == null) continue;

                var sourceText = await ReadFileWithRetryAsync(filePath);
                if (sourceText == null) continue;

                newSolution = newSolution.WithDocumentText(document.Id, sourceText);
                _logger.LogDebug("Updated document: {FilePath}", filePath);
            }
        }

        if (newSolution != _currentSolution)
        {
            if (_workspace.TryApplyChanges(newSolution))
            {
                _currentSolution = _workspace.CurrentSolution;
                _logger.LogInformation("Incremental document update completed: {Count} files", changedFiles.Count());
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Read file with retry for editor-locked files
    /// </summary>
    private static async Task<SourceText?> ReadFileWithRetryAsync(string filePath)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();
                return SourceText.From(content);
            }
            catch (IOException) when (i < 4)
            {
                await Task.Delay(50 * (i + 1));
            }
            catch (Exception)
            {
                return null;
            }
        }
        return null;
    }

    #endregion

    public void Dispose()
    {
        StopFileWatcher();
        _loadLock.Dispose();
        _workspace?.Dispose();
    }
}
