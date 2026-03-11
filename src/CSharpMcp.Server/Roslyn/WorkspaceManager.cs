using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 工作区管理器实现
/// </summary>
internal sealed partial class WorkspaceManager : IWorkspaceManager, IDisposable
{
    private readonly ILogger<WorkspaceManager> _logger;
    private readonly MSBuildWorkspace _workspace;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Solution? _currentSolution;
    private IEnumerable<Project> _userProjects;
    private string? _loadedPath;
    private DateTime _lastUpdate;
    private FileWatcherService? _fileWatcher;
    private int _isCompiling;
    private bool _isUnityProject;

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
        _workspace = MSBuildWorkspace.Create();

        _workspace.WorkspaceFailed += (s, e) =>
        {
            _logger.LogWarning("Workspace failed: {Diagnostic}", e.Diagnostic.Message);
        };
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
                _workspace.CloseSolution();
                _currentSolution = null;
                _loadedPath = null;
                _userProjects = null;
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

            var extension = Path.GetExtension(path).ToLowerInvariant();
            WorkspaceKind kind;

            // Normalize the path to use full path
            var normalizedPath = Path.GetFullPath(path);
            _logger.LogInformation("Opening {Extension} file: {Path}", extension, normalizedPath);

            if (extension == ".sln" || extension == ".slnx")
            {
                try
                {
                    _currentSolution = await _workspace.OpenSolutionAsync(normalizedPath, cancellationToken: cancellationToken);
                    kind = WorkspaceKind.Solution;
                    _isUnityProject = await IsUnityProjectAsync(_workspace.CurrentSolution.Projects, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to open solution: {Path}", normalizedPath);
                    throw new InvalidOperationException($"Failed to open solution file: {normalizedPath}. Error: {ex.Message}", ex);
                }
            }
            else if (extension == ".csproj")
            {
                try
                {
                    var project = await _workspace.OpenProjectAsync(normalizedPath, cancellationToken: cancellationToken);
                    _currentSolution = project.Solution;
                    kind = WorkspaceKind.Project;
                    _isUnityProject = await IsUnityProjectAsync(new[] { project }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to open project: {Path}", normalizedPath);
                    throw new InvalidOperationException($"Failed to open project file: {normalizedPath}. Error: {ex.Message}", ex);
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported file type: {extension}");
            }

            _loadedPath = Path.GetFullPath(path);
            _lastUpdate = DateTime.UtcNow;

            // Start file watcher for automatic workspace updates
            StartFileWatcher();

            var info = new WorkspaceInfo(
                _loadedPath,
                kind,
                _currentSolution.Projects.Count(),
                _currentSolution.Projects.Sum(p => p.DocumentIds.Count)
            );

            _logger.LogInformation(
                "Workspace loaded: {Kind} with {ProjectCount} projects and {DocumentCount} documents",
                kind,
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
        var documentIds = _currentSolution.GetDocumentIdsWithFilePath(filePath);
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
            project = _currentSolution.Projects.FirstOrDefault(p =>
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
            project = _currentSolution.Projects.FirstOrDefault(p =>
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
        if (!_isUnityProject)
        {
            return _workspace.CurrentSolution.Projects;
        }

        return _workspace.CurrentSolution.Projects
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
    public async Task<Microsoft.CodeAnalysis.Text.SourceText?> GetSourceTextAsync(string filePath, CancellationToken cancellationToken = default)
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
    /// 确保工作区是最新的（处理待处理的文件变更）
    /// 用于符号查询前确保编译状态是最新的
    /// </summary>
    public async Task EnsureUpToDateAsync(CancellationToken cancellationToken = default)
    {
        if (_fileWatcher?.HasPendingChanges == true)
        {
            _logger.LogInformation("Flushing pending file changes before symbol query");
            await _fileWatcher.FlushPendingChangesAsync(cancellationToken);
        }
    }

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
        // 跳过增量更新，因为 ForceRecompileAsync 会从磁盘重新加载所有文件
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
        if (_currentSolution == null)
        {
            return;
        }

        _logger.LogInformation("Force recompiling workspace for fresh diagnostics...");

        try
        {
            // 通过重新加载解决方案来强制重新编译
            var originalPath = _loadedPath;
            if (!string.IsNullOrEmpty(originalPath))
            {
                // 保存当前加载的路径
                _loadedPath = null;
                _currentSolution = null;

                // 重新加载
                await LoadAsync(originalPath, cancellationToken);

                _logger.LogInformation("Force recompile completed");
            }
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

    public void Dispose()
    {
        StopFileWatcher();
        _loadLock.Dispose();
        _workspace.Dispose();
    }
}
