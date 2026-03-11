using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace CSharpMcp.Server.Roslyn;

internal sealed partial class WorkspaceManager
{
    private void StartFileWatcher()
    {
        try
        {
            _fileWatcher?.Dispose();

            if (_currentSolution == null || string.IsNullOrEmpty(_loadedPath))
            {
                _logger.LogWarning("Cannot start file watcher: no solution loaded");
                return;
            }

            var solutionDirectory = Path.GetDirectoryName(_loadedPath);

            _fileWatcher = new FileWatcherService(
                _loadedPath,
                solutionDirectory!,
                _logger
            );

            _logger.LogInformation("File watcher started for: {Path}", _loadedPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start file watcher");
        }
    }

    private void StopFileWatcher()
    {
        try
        {
            _fileWatcher?.Dispose();
            _fileWatcher = null;
            _logger.LogInformation("File watcher stopped");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping file watcher");
        }
    }
}
