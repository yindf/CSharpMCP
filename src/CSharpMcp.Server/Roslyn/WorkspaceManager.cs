using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using CSharpMcp.Server.Cache;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 工作区管理器实现
/// </summary>
internal sealed partial class WorkspaceManager : IWorkspaceManager, IDisposable
{
    private readonly ILogger<WorkspaceManager> _logger;
    private readonly MSBuildWorkspace _workspace;
    private readonly ICompilationCache _compilationCache;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Solution? _currentSolution;
    private string? _loadedPath;
    private DateTime _lastUpdate;

    public WorkspaceManager(ILogger<WorkspaceManager> logger)
    {
        _logger = logger;
        _workspace = MSBuildWorkspace.Create();
        _compilationCache = CacheFactory.CreateCompilationCache();

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
            _logger.LogInformation("Loading workspace from: {Path}", path);

            if (!File.Exists(path))
            {
                // Check if it's a directory
                if (Directory.Exists(path))
                {
                    // Load as folder - find .sln or .csproj
                    var slnFile = Directory.GetFiles(path, "*.sln").FirstOrDefault();
                    if (slnFile != null)
                    {
                        path = slnFile;
                    }
                    else
                    {
                        var csprojFile = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
                        if (csprojFile != null)
                        {
                            path = csprojFile;
                        }
                        else
                        {
                            throw new FileNotFoundException($"No solution or project file found in: {path}");
                        }
                    }
                }
                else
                {
                    throw new FileNotFoundException($"File not found: {path}");
                }
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            WorkspaceKind kind;

            if (extension == ".sln")
            {
                _logger.LogInformation("Opening solution: {Path}", path);
                _currentSolution = await _workspace.OpenSolutionAsync(path, cancellationToken: cancellationToken);
                kind = WorkspaceKind.Solution;
            }
            else if (extension == ".csproj")
            {
                _logger.LogInformation("Opening project: {Path}", path);
                var project = await _workspace.OpenProjectAsync(path, cancellationToken: cancellationToken);
                _currentSolution = project.Solution;
                kind = WorkspaceKind.Project;
            }
            else
            {
                throw new NotSupportedException($"Unsupported file type: {extension}");
            }

            _loadedPath = Path.GetFullPath(path);
            _lastUpdate = DateTime.UtcNow;

            // Clear cache when loading new workspace
            _compilationCache.Clear();

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
        var docs = _currentSolution.Projects
            .SelectMany(p => p.Documents)
            .Where(d => string.Equals(
                Path.GetFileName(d.FilePath),
                fileName,
                StringComparison.OrdinalIgnoreCase));

        var doc = docs.FirstOrDefault();
        if (doc != null)
        {
            _logger.LogDebug("Found document by file name: {FilePath}", filePath);
            return doc;
        }

        _logger.LogWarning("Document not found: {FilePath}", filePath);
        return null;
    }

    /// <summary>
    /// 获取编译
    /// </summary>
    public async Task<Compilation?> GetCompilationAsync(string? projectPath = null, CancellationToken cancellationToken = default)
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

        var cacheKey = $"{project.Id.Id}";

        return await _compilationCache.GetOrAddAsync(
            cacheKey,
            async () =>
            {
                try
                {
                    var compilation = await project.GetCompilationAsync(cancellationToken);
                    if (compilation != null)
                    {
                        _logger.LogDebug("Created compilation for project: {ProjectName}", project.Name);
                    }
                    return compilation;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create compilation for project: {ProjectName}", project.Name);
                    return null;
                }
            },
            cancellationToken
        );
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
    /// 刷新工作区
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_loadedPath == null)
        {
            return;
        }

        _logger.LogInformation("Refreshing workspace: {Path}", _loadedPath);
        _compilationCache.Clear();

        try
        {
            // Reload the workspace
            if (File.Exists(_loadedPath))
            {
                var extension = Path.GetExtension(_loadedPath).ToLowerInvariant();
                if (extension == ".sln")
                {
                    _currentSolution = await _workspace.OpenSolutionAsync(_loadedPath, cancellationToken: cancellationToken);
                }
                else if (extension == ".csproj")
                {
                    var project = await _workspace.OpenProjectAsync(_loadedPath, cancellationToken: cancellationToken);
                    _currentSolution = project.Solution;
                }
            }

            _lastUpdate = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh workspace");
        }
    }

    /// <summary>
    /// 获取工作区状态
    /// </summary>
    public WorkspaceStatus GetStatus()
    {
        var cacheStats = _compilationCache.GetStatistics();

        return new WorkspaceStatus(
            _currentSolution != null,
            _currentSolution?.Projects.Count() ?? 0,
            _currentSolution?.Projects.Sum(p => p.DocumentIds.Count) ?? 0,
            cacheStats.HitRate,
            _lastUpdate
        );
    }

    /// <summary>
    /// 获取当前解决方案
    /// </summary>
    public Solution? GetCurrentSolution() => _currentSolution;

    /// <summary>
    /// 获取所有项目
    /// </summary>
    public IReadOnlyList<Project> GetProjects()
    {
        return _currentSolution?.Projects.ToList() ?? [];
    }

    public void Dispose()
    {
        _loadLock.Dispose();
        _workspace.Dispose();
    }
}
