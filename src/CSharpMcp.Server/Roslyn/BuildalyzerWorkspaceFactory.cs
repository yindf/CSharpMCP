using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Buildalyzer;
using Buildalyzer.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// Factory for creating AdhocWorkspace using Buildalyzer with parallel project loading
/// </summary>
internal static class BuildalyzerWorkspaceFactory
{
    /// <summary>
    /// Create AdhocWorkspace from Buildalyzer with parallel project loading
    /// </summary>
    public static async Task<(AdhocWorkspace workspace, AnalyzerManager manager)> CreateWorkspaceAsync(
        string solutionOrProjectPath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var manager = new AnalyzerManager(solutionOrProjectPath);
        var workspace = CreateWorkspace(manager, logger);

        // Build all projects in parallel
        var results = manager.Projects.Values
            .AsParallel()
            .Select(p =>
            {
                try
                {
                    return p.Build().FirstOrDefault();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to build project: {ProjectPath}", p.ProjectFile.Path);
                    return null;
                }
            })
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
            var projectsInOrder = manager.SolutionFile?.ProjectsInOrder
                .Select(p => p.AbsolutePath)
                .ToList() ?? [];

            results = results
                .OrderBy(p => projectsInOrder.FindIndex(g => string.Equals(g, p?.ProjectFilePath, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        // Add each project to workspace
        foreach (var result in results)
        {
            if (result == null) continue;

            if (workspace.CurrentSolution.Projects.All(p => !string.Equals(p.FilePath, result.ProjectFilePath, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    result.AddToWorkspace(workspace, false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to add project to workspace: {ProjectPath}", result.ProjectFilePath);
                }
            }
        }

        // Fix Unity project compilation options
        FixUnityProjectCompilationOptions(workspace);

        logger.LogInformation(
            "Buildalyzer workspace created: {ProjectCount} projects, {DocumentCount} documents",
            workspace.CurrentSolution.Projects.Count(),
            workspace.CurrentSolution.Projects.Sum(p => p.DocumentIds.Count));

        return (workspace, manager);
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
                (r.Display.Contains("UnityEngine", StringComparison.OrdinalIgnoreCase) ||
                 r.Display.Contains("UnityEditor", StringComparison.OrdinalIgnoreCase) ||
                 r.Display.Contains("Unity.", StringComparison.OrdinalIgnoreCase)));

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

    /// <summary>
    /// Rebuild a single project with Buildalyzer
    /// </summary>
    public static IProjectAnalyzer? GetProjectAnalyzer(AnalyzerManager manager, string projectPath)
    {
        return manager.Projects.TryGetValue(projectPath, out var analyzer)
            ? analyzer
            : null;
    }
}
