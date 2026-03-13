# Refactoring Plan: Buildalyzer + AdhocWorkspace

## Overview

Replace `MSBuildWorkspace` with `Buildalyzer` + `AdhocWorkspace` to enable:
- Parallel project loading
- True incremental compilation
- In-place project reload (preserving cross-project references)

---

## Phase 1: Add Dependencies

### File: `src/CSharpMcp.Server/CSharpMcp.Server.csproj`

Add Buildalyzer packages:

```xml
<!-- Buildalyzer for workspace loading -->
<PackageReference Include="Buildalyzer" Version="7.x.x" />
<PackageReference Include="Buildalyzer.Workspaces" Version="7.x.x" />
```

Keep `Microsoft.CodeAnalysis.Workspaces.MSBuild` as fallback (optional).

---

## Phase 2: New Workspace Loading

### Create: `src/CSharpMcp.Server/Roslyn/BuildalyzerWorkspaceFactory.cs`

```csharp
using Buildalyzer;
using Buildalyzer.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace CSharpMcp.Server.Roslyn;

internal static class BuildalyzerWorkspaceFactory
{
    /// <summary>
    /// Create AdhocWorkspace from Buildalyzer with parallel project loading
    /// </summary>
    public static async Task<AdhocWorkspace> CreateWorkspaceAsync(
        string solutionPath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var manager = new AnalyzerManager(solutionPath);
        var workspace = manager.CreateWorkspace(logger);

        // Register workspace diagnostics
        workspace.WorkspaceFailed += (sender, e) =>
            logger.LogError("Workspace failed: {Diagnostic}", e.Diagnostic);

        // Build all projects in parallel
        var results = manager.Projects.Values
            .AsParallel()
            .Select(p => p.Build().FirstOrDefault())
            .Where(x => x != null)
            .ToList();

        // Add solution info if we have one
        if (!string.IsNullOrEmpty(manager.SolutionFilePath))
        {
            var solutionInfo = SolutionInfo.Create(
                SolutionId.CreateNewId(),
                VersionStamp.Default,
                manager.SolutionFilePath);
            workspace.AddSolution(solutionInfo);

            // Sort projects in solution file order
            var projectsInOrder = manager.SolutionFile.ProjectsInOrder
                .Select(p => p.AbsolutePath)
                .ToList();

            results = results
                .OrderBy(p => projectsInOrder.FindIndex(g => g == p.ProjectFilePath))
                .ToList();
        }

        // Add each project to workspace
        foreach (var result in results)
        {
            if (workspace.CurrentSolution.Projects.All(p => p.FilePath != result.ProjectFilePath))
            {
                result.AddToWorkspace(workspace, false);
            }
        }

        // Fix Unity project compilation options
        FixUnityProjectCompilationOptions(workspace);

        return workspace;
    }

    /// <summary>
    /// Create AdhocWorkspace with logging support
    /// </summary>
    private static AdhocWorkspace CreateWorkspace(this IAnalyzerManager manager, ILogger logger)
    {
        var workspace = new AdhocWorkspace();
        workspace.WorkspaceChanged += (sender, args) =>
            logger.LogDebug("Workspace changed: {Kind}", args.Kind);
        workspace.WorkspaceFailed += (sender, args) =>
            logger.LogError("Workspace failed: {Diagnostic}", args.Diagnostic);
        return workspace;
    }

    /// <summary>
    /// Enable AllowUnsafe for Unity projects
    /// </summary>
    private static void FixUnityProjectCompilationOptions(AdhocWorkspace workspace)
    {
        var solution = workspace.CurrentSolution;
        var newSolution = solution;

        foreach (var project in solution.Projects)
        {
            // Detect Unity project by metadata references
            bool isUnityProject = project.MetadataReferences.Any(r =>
                r.Display != null &&
                (r.Display.Contains("UnityEngine") ||
                 r.Display.Contains("UnityEditor") ||
                 r.Display.Contains("Unity.")));

            if (!isUnityProject) continue;

            var compilationOptions = (CSharpCompilationOptions?)project.CompilationOptions;
            if (compilationOptions == null) continue;

            if (!compilationOptions.AllowUnsafe)
            {
                var newOptions = compilationOptions.WithAllowUnsafe(true);
                newSolution = newSolution.WithProjectCompilationOptions(project.Id, newOptions);
            }
        }

        if (newSolution != solution)
        {
            workspace.TryApplyChanges(newSolution);
        }
    }
}
```

---

## Phase 3: In-Place Project Reload

### Add to: `src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs`

```csharp
/// <summary>
/// Reload a single project in-place, preserving ProjectId and cross-project references
/// </summary>
public async Task<bool> ReloadProjectAsync(string projectPath, CancellationToken cancellationToken)
{
    if (_currentSolution == null || _manager == null) return false;

    var projectAnalyzer = _manager.Projects.TryGetValue(projectPath, out var analyzer)
        ? analyzer
        : null;

    if (projectAnalyzer == null) return false;

    // Rebuild project with Buildalyzer
    var newResult = projectAnalyzer.Build().FirstOrDefault();
    if (newResult == null) return false;

    // Find existing project (keep ProjectId!)
    var oldProject = _currentSolution.Projects
        .FirstOrDefault(p => string.Equals(p.FilePath, projectPath, StringComparison.OrdinalIgnoreCase));

    if (oldProject == null) return false;

    _logger.LogInformation("Reloading project in-place: {ProjectName}", oldProject.Name);

    // Build new solution with updated project
    var newSolution = _currentSolution;

    // Update metadata references
    var metadataRefs = newResult.References
        .Select(r => MetadataReference.CreateFromFile(r))
        .ToList();

    newSolution = newSolution.WithProjectMetadataReferences(oldProject.Id, metadataRefs);

    // Update compilation options
    if (newResult.CompilationOptions is CSharpCompilationOptions csharpOptions)
    {
        // Preserve AllowUnsafe for Unity projects
        var existingOptions = (CSharpCompilationOptions?)oldProject.CompilationOptions;
        if (existingOptions?.AllowUnsafe == true)
        {
            csharpOptions = csharpOptions.WithAllowUnsafe(true);
        }
        newSolution = newSolution.WithProjectCompilationOptions(oldProject.Id, csharpOptions);
    }

    // Update documents - remove old ones first
    foreach (var doc in oldProject.DocumentIds.ToList())
    {
        newSolution = newSolution.RemoveDocument(doc);
    }

    // Add new documents
    foreach (var sourceFile in newResult.SourceFiles)
    {
        if (!File.Exists(sourceFile)) continue;

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
```

---

## Phase 4: Incremental Document Updates

### Add to: `src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs`

```csharp
// File-to-project mapping for fast lookup
private readonly ConcurrentDictionary<string, ProjectId> _fileToProjectMap = new();

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
/// Incremental update for changed .cs files
/// </summary>
public async Task<bool> UpdateDocumentsAsync(IEnumerable<string> changedFiles, CancellationToken cancellationToken)
{
    if (_currentSolution == null) return false;

    var filesByProject = changedFiles
        .Where(f => _fileToProjectMap.TryGetValue(f, out _))
        .GroupBy(f => _fileToProjectMap[f])
        .ToList();

    if (!filesByProject.Any()) return false;

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
        }
    }

    if (newSolution != _currentSolution)
    {
        if (_workspace.TryApplyChanges(newSolution))
        {
            _currentSolution = _workspace.CurrentSolution;
            return true;
        }
    }

    return false;
}
```

---

## Phase 5: Debounced File Change Processing

### Update: `src/CSharpMcp.Server/Roslyn/FileWatcherService.cs`

```csharp
using System.Collections.Concurrent;

internal sealed class FileWatcherService : IDisposable
{
    private readonly BlockingCollection<FileChangeEventArgs> _changeQueue = new(boundedCapacity: 1000);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(300);
    private readonly Task _processorTask;

    public event EventHandler<ProjectReloadNeededEventArgs>? ProjectReloadNeeded;
    public event EventHandler<DocumentsUpdatedEventArgs>? DocumentsUpdated;

    public FileWatcherService(string solutionDirectory, ILogger logger)
    {
        // ... existing initialization ...

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;

        // Start background processor
        _processorTask = Task.Run(() => ProcessChangesAsync(_shutdownCts.Token));
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsValidCsFile(e.FullPath)) return;
        _changeQueue.TryAdd(new FileChangeEventArgs(e.FullPath, e.ChangeType), 0);
    }

    private static bool IsValidCsFile(string path)
    {
        var fileName = Path.GetFileName(path);
        if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.StartsWith("~") || fileName.Contains("~RF")) return false;
        if (Path.GetExtension(fileName).Length > 3) return false; // Skip temp files
        return true;
    }

    private async Task ProcessChangesAsync(CancellationToken cancellationToken)
    {
        var pendingChanges = new Dictionary<string, DateTime>();
        var lastProcessTime = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_changeQueue.TryTake(out var change, 100, cancellationToken))
            {
                pendingChanges[change.FilePath] = DateTime.UtcNow;
            }

            if (pendingChanges.Count > 0)
            {
                var now = DateTime.UtcNow;
                var oldest = pendingChanges.Values.Min();

                if (now - oldest >= _debounceDelay)
                {
                    var filesToProcess = pendingChanges.Keys.ToList();
                    pendingChanges.Clear();

                    // Separate .cs and .csproj changes
                    var csFiles = filesToProcess.Where(f => f.EndsWith(".cs")).ToList();
                    var csprojFiles = filesToProcess.Where(f => f.EndsWith(".csproj")).ToList();

                    // Process .csproj changes first (project reload)
                    foreach (var csproj in csprojFiles)
                    {
                        ProjectReloadNeeded?.Invoke(this, new ProjectReloadNeededEventArgs(csproj));
                    }

                    // Process .cs changes (incremental document update)
                    if (csFiles.Any())
                    {
                        DocumentsUpdated?.Invoke(this, new DocumentsUpdatedEventArgs(csFiles));
                    }
                }
            }
        }
    }
}

public record FileChangeEventArgs(string FilePath, WatcherChangeTypes ChangeType);
public record ProjectReloadNeededEventArgs(string ProjectPath);
public record DocumentsUpdatedEventArgs(IReadOnlyList<string> Files);
```

---

## Phase 6: Integration in WorkspaceManager

### Update: `src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs`

```csharp
private void StartFileWatcher()
{
    if (_loadedPath == null) return;

    var directory = Path.GetDirectoryName(_loadedPath);
    if (directory == null) return;

    _fileWatcher = new FileWatcherService(directory, _logger);

    // Handle incremental document updates
    _fileWatcher.DocumentsUpdated += async (sender, e) =>
    {
        _logger.LogInformation("Processing {Count} changed documents", e.Files.Count);
        await UpdateDocumentsAsync(e.Files, CancellationToken.None);
    };

    // Handle project reloads
    _fileWatcher.ProjectReloadNeeded += async (sender, e) =>
    {
        _logger.LogInformation("Project reload needed: {Path}", e.ProjectPath);
        await ReloadProjectAsync(e.ProjectPath, CancellationToken.None);
    };
}
```

---

## Summary: Change Processing Flow

```
File Change Detected
        │
        ▼
┌───────────────────┐
│ Debounce (300ms)  │
└─────────┬─────────┘
          │
          ▼
    ┌─────┴─────┐
    │           │
    ▼           ▼
 .csproj       .cs
    │           │
    ▼           ▼
┌──────────┐ ┌────────────────┐
│ Reload   │ │ Update Docs    │
│ Project  │ │ (WithDocText)  │
│ In-Place │ │                │
└────┬─────┘ └───────┬────────┘
     │               │
     ▼               ▼
┌─────────────────────────────┐
│ Same ProjectId preserved    │
│ Cross-project refs intact   │
│ TryApplyChanges()           │
└─────────────────────────────┘
```

---

## Testing Strategy

1. **Unit tests for in-place reload**
   - Verify ProjectId unchanged after reload
   - Verify cross-project references still resolve

2. **Integration tests**
   - Load solution, modify .cs file, verify incremental update
   - Load solution, modify .csproj, verify project reload

3. **Performance benchmarks**
   - Compare full reload vs incremental update time
   - Measure memory usage

---

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Buildalyzer build failures | Keep MSBuildWorkspace as fallback |
| Lost project references | Log warnings, suggest full reload |
| File lock during read | Retry with backoff (5 attempts) |
| Memory growth | Clear old solution references after update |
