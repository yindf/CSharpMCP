using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 文件监控服务 - 使用 FileSystemWatcher 监控解决方案目录
/// 只收集文件变化，不进行增量编译。工具调用时通过 ForceRecompileAsync 完全重新编译。
/// </summary>
internal sealed class FileWatcherService : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _solutionDirectory;
    private readonly ILogger _logger;
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
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;

        _watcher.EnableRaisingEvents = true;

        _logger.LogInformation("FileWatcherService initialized for: {Directory}", _solutionDirectory);
        _logger.LogInformation("Watching: *.sln, *.csproj, *.cs (incremental compilation disabled)");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var changeType = GetChangeType(e.FullPath);
        if (!changeType.HasValue) return;

        _logger.LogTrace("File changed: {Type} - {Path}", changeType.Value, e.FullPath);
        RecordChange(changeType.Value, e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        var changeType = GetChangeType(e.FullPath);
        if (changeType.HasValue)
        {
            _logger.LogTrace("File renamed: {Type} - {OldPath} -> {NewPath}", changeType.Value, e.OldFullPath, e.FullPath);
            RecordChange(changeType.Value, e.FullPath);
        }
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
    /// 记录文件变化
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
