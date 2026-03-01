using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 文件监控服务 - 使用单一 FileSystemWatcher 监控整个解决方案目录
/// 只收集文件变化，不自动编译。在工具调用时通过 FlushPendingChangesAsync 触发编译。
/// </summary>
internal sealed class FileWatcherService : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _solutionPath;
    private readonly string _solutionDirectory;
    private readonly Func<IReadOnlyDictionary<string, FileChangeType>, CancellationToken, Task> _onFileChanged;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly object _pendingChangesLock = new();

    private readonly Dictionary<string, FileChangeType> _pendingFileChanges = new();
    private bool _disposed;

    // 编译状态跟踪
    private volatile bool _hasPendingChanges;
    private TaskCompletionSource? _processingTcs;
    private readonly object _processingTcsLock = new();

    // MD5 过滤机制相关字段
    private readonly Dictionary<string, string> _applyingSnapshots = new();  // filePath -> MD5
    private readonly List<(string filePath, FileChangeType changeType)> _deferredChanges = new();
    private bool _isInCallback;
    private readonly object _stateLock = new();

    public FileWatcherService(
        string solutionPath,
        string solutionDirectory,
        Func<IReadOnlyDictionary<string, FileChangeType>, CancellationToken, Task> onFileChanged,
        ILogger logger)
    {
        _solutionPath = solutionPath;
        _solutionDirectory = solutionDirectory;
        _onFileChanged = onFileChanged;
        _logger = logger;

        // 创建单一的 FileSystemWatcher 监控整个解决方案目录
        _watcher = new FileSystemWatcher(_solutionDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };

        // 注册事件处理器
        // 注意：不监听 Created 事件，因为：
        // 1. Created 事件时文件可能还未完全写入，MD5 快照不准确
        // 2. 依赖 Changed 事件检测新文件（文件写入时会触发）
        // 3. 这样可以保持 MD5 防循环机制有效
        _watcher.Changed += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;

        _watcher.EnableRaisingEvents = true;

        _logger.LogInformation("FileWatcherService initialized for: {Directory}", _solutionDirectory);
        _logger.LogInformation("Watching: *.sln, *.csproj, *.cs (auto-compile disabled)");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var changeType = GetChangeType(e.FullPath);
        if (!changeType.HasValue) return;

        // 检查是否正在执行回调（即正在 ApplyChanges）
        lock (_stateLock)
        {
            if (_isInCallback)
            {
                // 在回调期间，检查是否是快照中的文件
                if (_applyingSnapshots.ContainsKey(e.FullPath))
                {
                    try
                    {
                        var content = ReadFileWithShare(e.FullPath);
                        var currentMd5 = ComputeMd5(content);
                        var snapshotMd5 = _applyingSnapshots[e.FullPath];
                        _logger.LogInformation("MD5 check for {Path}: Current={Current}, Snapshot={Snapshot}, Match={Match}",
                            e.FullPath, currentMd5, snapshotMd5, currentMd5 == snapshotMd5);

                        if (currentMd5 == snapshotMd5)
                        {
                            // MD5 相同，是 ApplyChanges 的副作用，忽略
                            _logger.LogInformation("Ignoring duplicate change for {Path} (MD5 matches snapshot)", e.FullPath);
                            return;
                        }
                        else
                        {
                            // MD5 不同，是真实的外部修改，缓存起来等回调完再处理
                            _logger.LogInformation("Deferring real change for {Path} (MD5 differs from snapshot)", e.FullPath);
                            _deferredChanges.Add((e.FullPath, changeType.Value));
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        // MD5 检查失败（文件被占用），保守地忽略这个变化
                        // 假设是 ApplyChanges 的副作用，避免无限循环
                        _logger.LogInformation(ex, "Ignoring change for {Path} (MD5 check failed during callback)", e.FullPath);
                        return;
                    }
                }
            }
        }

        // 正常处理：加入待处理队列
        _logger.LogTrace("File changed: {Type} - {Path}", changeType.Value, e.FullPath);
        ScheduleChange(changeType.Value, e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        var changeType = GetChangeType(e.FullPath);
        if (changeType.HasValue)
        {
            _logger.LogInformation("File renamed: {Type} - {OldPath} -> {NewPath}", changeType.Value, e.OldFullPath, e.FullPath);
            ScheduleChange(changeType.Value, e.FullPath);
        }
    }

    /// <summary>
    /// 根据文件扩展名确定变化类型
    /// </summary>
    private FileChangeType? GetChangeType(string filePath)
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
    /// 安排文件变化处理（只收集，不自动编译）
    /// </summary>
    private void ScheduleChange(FileChangeType changeType, string filePath)
    {
        lock (_pendingChangesLock)
        {
            _pendingFileChanges[filePath] = changeType;
            _hasPendingChanges = true;
        }

        // 不再自动触发编译，等待工具调用时通过 FlushPendingChangesAsync 触发
        _logger.LogDebug("File change recorded: {Type} - {Path} (waiting for manual flush)", changeType, filePath);
    }

    /// <summary>
    /// 是否有待处理的文件变更
    /// </summary>
    public bool HasPendingChanges => _hasPendingChanges;

    /// <summary>
    /// 立即处理待处理的文件变更，并等待编译完成
    /// 用于符号查询前确保工作区是最新的
    /// </summary>
    public async Task FlushPendingChangesAsync(CancellationToken cancellationToken = default)
    {
        if (!_hasPendingChanges)
            return;

        TaskCompletionSource tcs;
        lock (_processingTcsLock)
        {
            // 如果已有等待的 TCS，复用它（说明已有编译在进行）
            if (_processingTcs != null)
            {
                tcs = _processingTcs;
            }
            else
            {
                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _processingTcs = tcs;
            }
        }

        // 立即触发处理
        ProcessPendingChanges();

        // 等待编译完成
        await tcs.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 处理待处理的文件变更（由 FlushPendingChangesAsync 调用）
    /// </summary>
    private void ProcessPendingChanges()
    {
        if (_disposed)
            return;

        // 获取所有待处理的文件（在锁内复制并清空）
        Dictionary<string, FileChangeType> changesToProcess;
        lock (_pendingChangesLock)
        {
            if (_pendingFileChanges.Count == 0)
            {
                // 没有待处理的变更，但可能有等待的 TCS 需要完成
                CompleteProcessingTcs();
                return;
            }

            changesToProcess = new Dictionary<string, FileChangeType>(_pendingFileChanges);
            _pendingFileChanges.Clear();
            _hasPendingChanges = false; // 清除待处理标记
        }

        _logger.LogInformation("Processing {Count} file change(s): {Files}", changesToProcess.Count, string.Join(", ", changesToProcess.Keys));

        // 确保不会同时处理多个变化
        if (!_processingLock.Wait(0))
        {
            // 如果已经在处理中，重新调度这些变更
            lock (_pendingChangesLock)
            {
                foreach (var kvp in changesToProcess)
                {
                    _pendingFileChanges[kvp.Key] = kvp.Value;
                }
                _hasPendingChanges = true;
            }
            return;
        }

        // ========== 回调开始前：创建 MD5 快照 ==========
        lock (_stateLock)
        {
            _isInCallback = true;
            _applyingSnapshots.Clear();
            _deferredChanges.Clear();

            // 对即将处理的文件做 MD5 快照
            foreach (var filePath in changesToProcess.Keys)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var content = ReadFileWithShare(filePath);
                        _applyingSnapshots[filePath] = ComputeMd5(content);
                        _logger.LogInformation("Created MD5 snapshot for {Path}: {MD5}", filePath, _applyingSnapshots[filePath]);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create MD5 snapshot for {Path}", filePath);
                }
            }

            if (_applyingSnapshots.Count > 0)
            {
                _logger.LogInformation("Created MD5 snapshots for {Count} file(s)", _applyingSnapshots.Count);
            }
        }

        try
        {
            // 在后台线程中执行异步操作
            _ = Task.Run(async () =>
            {
                try
                {
                    await _onFileChanged(changesToProcess, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file changes");
                }
                finally
                {
                    // ========== 回调完成后：清理状态并处理延迟的变化 ==========
                    lock (_stateLock)
                    {
                        _isInCallback = false;
                        foreach (var deferredChange in _applyingSnapshots.Keys)
                        {
                            _deferredChanges.RemoveAll(tuple => tuple.filePath ==  deferredChange);
                        }
                        _applyingSnapshots.Clear();

                        // 处理期间积累的真实变化
                        if (_deferredChanges.Count > 0)
                        {
                            _logger.LogInformation("Processing {Count} deferred change(s) from during callback: {Files}",
                                _deferredChanges.Count, string.Join(", ", _deferredChanges.Select(x => x.filePath)));
                            foreach (var (filePath, changeType) in _deferredChanges)
                            {
                                ScheduleChange(changeType, filePath);
                            }
                            _deferredChanges.Clear();
                        }

                    }

                    // 完成等待的 TCS
                    CompleteProcessingTcs();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scheduling file change processing");
            // 确保清理状态
            lock (_stateLock)
            {
                _isInCallback = false;
                _applyingSnapshots.Clear();
            }
            // 完成等待的 TCS（即使失败也要完成）
            CompleteProcessingTcs();
        }
        finally
        {
            _processingLock.Release();
        }
    }

    /// <summary>
    /// 完成等待的 TaskCompletionSource
    /// </summary>
    private void CompleteProcessingTcs()
    {
        lock (_processingTcsLock)
        {
            if (_processingTcs != null)
            {
                _processingTcs.TrySetResult();
                _processingTcs = null;
            }
        }
    }

    /// <summary>
    /// 计算文件内容的 MD5 哈希值
    /// </summary>
    private static string ComputeMd5(string content)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = md5.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 读取文件内容，使用 FileShare.ReadWrite 允许读取被占用的文件
    /// </summary>
    private static string ReadFileWithShare(string path)
    {
        using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        using var sr = new StreamReader(fs, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _processingLock.Dispose();

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
