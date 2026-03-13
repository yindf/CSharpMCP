using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 文件监控服务 - 使用 FileSystemWatcher 监控解决方案目录
/// 支持防抖处理，区分项目重载和文档增量更新
/// </summary>
internal sealed class FileWatcherService : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _solutionDirectory;
    private readonly ILogger _logger;

    // Debounced change processing (large capacity for git checkout scenarios)
    private readonly BlockingCollection<FileChangeEventArgs> _changeQueue = new(boundedCapacity: 10000);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(300);
    private readonly Task _processorTask;

    // Change tracking (for backward compatibility)
    private readonly object _pendingChangesLock = new();
    private readonly Dictionary<string, FileChangeType> _pendingFileChanges = new();
    private bool _disposed;

    // 需要重新加载工作区的标记（当 sln/csproj 变更或文件删除/添加时）
    private volatile bool _needsWorkspaceReload;
    private readonly object _needsReloadLock = new();

    // Unity 项目需要刷新的标记（当 Unity 项目的 cs 文件增删时）
    private volatile bool _needsUnityRefresh;
    private readonly List<string> _unityRefreshReasons = new();
    private readonly object _unityRefreshLock = new();

    /// <summary>
    /// Fired when a project file (.csproj/.sln) changes and needs reload
    /// </summary>
    public event EventHandler<ProjectReloadNeededEventArgs>? ProjectReloadNeeded;

    /// <summary>
    /// Fired when source files (.cs) are updated and can be incrementally applied
    /// </summary>
    public event EventHandler<DocumentsUpdatedEventArgs>? DocumentsUpdated;

    public FileWatcherService(
        string solutionPath,
        string solutionDirectory,
        ILogger logger)
    {
        _solutionDirectory = solutionDirectory;
        _logger = logger;

        // 创建单一的 FileSystemWatcher 监控整个解决方案目录
        _watcher = new FileSystemWatcher(_solutionDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };

        // 注册事件处理器
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;

        _watcher.EnableRaisingEvents = true;

        // Start background processor for debounced changes
        _processorTask = Task.Run(() => ProcessChangesAsync(_shutdownCts.Token));

        _logger.LogInformation("FileWatcherService initialized for: {Directory}", _solutionDirectory);
        _logger.LogInformation("Watching: *.sln, *.csproj, *.cs with debounced processing");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsValidFile(e.FullPath, out var changeType)) return;

        _logger.LogTrace("File changed: {Type} - {Path}", changeType, e.FullPath);

        // Queue for debounced processing
        if (!_changeQueue.TryAdd(new FileChangeEventArgs(e.FullPath, changeType.Value, e.ChangeType), 0))
        {
            _logger.LogWarning("Change queue full, dropping change: {Path}", e.FullPath);
        }

        // Also record for backward compatibility
        RecordChange(changeType.Value, e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsValidFile(e.FullPath, out var changeType)) return;

        _logger.LogTrace("File renamed: {Type} - {OldPath} -> {NewPath}", changeType, e.OldFullPath, e.FullPath);

        // Queue for debounced processing
        if (!_changeQueue.TryAdd(new FileChangeEventArgs(e.FullPath, changeType.Value, e.ChangeType), 0))
        {
            _logger.LogWarning("Change queue full, dropping change: {Path}", e.FullPath);
        }

        // Also record for backward compatibility
        RecordChange(changeType.Value, e.FullPath);
    }

    /// <summary>
    /// Check if file is valid for watching (not temp file, correct extension)
    /// </summary>
    private bool IsValidFile(string filePath, out FileChangeType? changeType)
    {
        changeType = GetChangeType(filePath);
        if (!changeType.HasValue) return false;

        // Filter out temp files
        var fileName = Path.GetFileName(filePath);
        if (fileName.StartsWith("~", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("~RF", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(fileName).Length > 4) // Skip temp files with long extensions
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 根据文件扩展名确定变化类型
    /// </summary>
    private static FileChangeType? GetChangeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        return ext switch
        {
            ".sln" => FileChangeType.Solution,
            ".csproj" => FileChangeType.Project,
            ".cs" => FileChangeType.SourceFile,
            _ => null
        };
    }

    /// <summary>
    /// 记录文件变化 (for backward compatibility)
    /// </summary>
    private void RecordChange(FileChangeType changeType, string filePath)
    {
        lock (_pendingChangesLock)
        {
            _pendingFileChanges[filePath] = changeType;
        }

        _logger.LogDebug("File change recorded: {Type} - {Path}", changeType, filePath);
    }

    /// <summary>
    /// Background processor for debounced file changes
    /// </summary>
    private async Task ProcessChangesAsync(CancellationToken cancellationToken)
    {
        var pendingChanges = new Dictionary<string, (FileChangeType Type, DateTime Time)>();
        var lastProcessTime = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Try to take from queue with timeout
                if (_changeQueue.TryTake(out var change, 100, cancellationToken))
                {
                    pendingChanges[change.FilePath] = (change.ChangeType, DateTime.UtcNow);
                }

                // Check if we should process pending changes
                if (pendingChanges.Count > 0)
                {
                    var now = DateTime.UtcNow;
                    var oldest = pendingChanges.Values.Min(v => v.Time);

                    if (now - oldest >= _debounceDelay)
                    {
                        var filesToProcess = pendingChanges.ToList();
                        pendingChanges.Clear();

                        // Separate .cs and .csproj/.sln changes
                        var csFiles = filesToProcess
                            .Where(f => f.Value.Type == FileChangeType.SourceFile)
                            .Select(f => f.Key)
                            .ToList();
                        var projectFiles = filesToProcess
                            .Where(f => f.Value.Type == FileChangeType.Project || f.Value.Type == FileChangeType.Solution)
                            .Select(f => f.Key)
                            .ToList();

                        // Process .csproj/.sln changes first (project reload)
                        foreach (var projectFile in projectFiles)
                        {
                            _logger.LogInformation("Project reload needed: {Path}", projectFile);
                            ProjectReloadNeeded?.Invoke(this, new ProjectReloadNeededEventArgs(projectFile));
                            MarkNeedsReload();
                        }

                        // Process .cs changes (incremental document update)
                        if (csFiles.Any())
                        {
                            _logger.LogInformation("Documents updated: {Count} files", csFiles.Count);
                            DocumentsUpdated?.Invoke(this, new DocumentsUpdatedEventArgs(csFiles));
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file changes");
            }
        }
    }

    /// <summary>
    /// 是否有待处理的文件变更
    /// </summary>
    public bool HasPendingChanges
    {
        get
        {
            lock (_pendingChangesLock)
            {
                return _pendingFileChanges.Count > 0;
            }
        }
    }

    /// <summary>
    /// 是否需要重新加载整个工作区（当 sln/csproj 变更或文件删除/添加时）
    /// </summary>
    public bool NeedsWorkspaceReload => _needsWorkspaceReload;

    /// <summary>
    /// 标记需要重新加载工作区
    /// </summary>
    internal void MarkNeedsReload()
    {
        lock (_needsReloadLock)
        {
            _needsWorkspaceReload = true;
            _logger.LogInformation("Workspace reload marked as needed");
        }
    }

    /// <summary>
    /// 清除重新加载标记（在重新加载完成后调用）
    /// </summary>
    internal void ClearNeedsReload()
    {
        lock (_needsReloadLock)
        {
            _needsWorkspaceReload = false;
            _logger.LogDebug("Workspace reload flag cleared");
        }
    }

    /// <summary>
    /// 清除待处理的文件变更（在强制重新编译后调用）
    /// </summary>
    internal void ClearPendingChanges()
    {
        lock (_pendingChangesLock)
        {
            _pendingFileChanges.Clear();
            _logger.LogDebug("Pending changes cleared");
        }
    }

    /// <summary>
    /// 是否需要 Unity 刷新（当 Unity 项目的 cs 文件增删时）
    /// </summary>
    public bool NeedsUnityRefresh => _needsUnityRefresh;

    /// <summary>
    /// 获取 Unity 刷新原因列表
    /// </summary>
    public IReadOnlyList<string> GetUnityRefreshReasons()
    {
        lock (_unityRefreshLock)
        {
            return _unityRefreshReasons.ToList();
        }
    }

    /// <summary>
    /// 标记需要 Unity 刷新
    /// </summary>
    internal void MarkNeedsUnityRefresh(string reason)
    {
        lock (_unityRefreshLock)
        {
            _needsUnityRefresh = true;
            if (!_unityRefreshReasons.Contains(reason))
            {
                _unityRefreshReasons.Add(reason);
            }
            _logger.LogInformation("Unity refresh marked as needed: {Reason}", reason);
        }
    }

    /// <summary>
    /// 清除 Unity 刷新标记
    /// </summary>
    internal void ClearUnityRefresh()
    {
        lock (_unityRefreshLock)
        {
            _needsUnityRefresh = false;
            _unityRefreshReasons.Clear();
            _logger.LogDebug("Unity refresh flag cleared");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            // Signal shutdown and wait for processor task
            _shutdownCts.Cancel();
            _processorTask.Wait(TimeSpan.FromSeconds(2));
            _shutdownCts.Dispose();

            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing file watcher");
        }

        _logger.LogInformation("FileWatcherService disposed");
    }
}

/// <summary>
/// Event args for file changes
/// </summary>
public record FileChangeEventArgs(string FilePath, FileChangeType ChangeType, WatcherChangeTypes WatcherChangeType);

/// <summary>
/// Event args for project reload
/// </summary>
public record ProjectReloadNeededEventArgs(string ProjectPath);

/// <summary>
/// Event args for document updates
/// </summary>
public record DocumentsUpdatedEventArgs(IReadOnlyList<string> Files);
