using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.Refactoring;

[McpServerToolType]
public class SimplifyUsingsTool
{
    private const string UnusedUsingDiagnosticId = "CS8019"; // Unnecessary using directive

    [McpServerTool, Description("Remove unused using directives and sort remaining ones. Uses compiler diagnostics (CS8019) for 100% accuracy.")]
    public static async Task<string> SimplifyUsings(
        IWorkspaceManager workspaceManager,
        ILogger<SimplifyUsingsTool> logger,
        CancellationToken cancellationToken,
        [Description("Path to specific file to process (null = entire workspace)")] string filePath = null,
        [Description("Whether to sort usings after removing unused ones")] bool sortUsings = true,
        [Description("Whether to place System usings first when sorting")] bool systemUsingsFirst = true)
    {
        try
        {
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Simplify Usings");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            var solution = workspaceManager.GetCurrentSolution();
            if (solution == null)
            {
                return GetErrorHelpResponse("Solution not available.");
            }

            logger.LogInformation("Simplifying usings for: {FilePath}", filePath ?? "entire workspace");

            var results = new List<FileUsingsResult>();
            var newSolution = solution;

            if (!string.IsNullOrEmpty(filePath))
            {
                // Single file mode
                var document = await workspaceManager.GetDocumentAsync(filePath, cancellationToken);
                if (document == null)
                {
                    return GetErrorHelpResponse($"File not found: `{filePath}`");
                }

                var (result, updatedSolution) = await ProcessDocumentInSolutionAsync(
                    newSolution, document.Id, sortUsings, systemUsingsFirst, logger, cancellationToken);

                if (result != null)
                {
                    results.Add(result);
                    if (result.Modified && updatedSolution != null)
                    {
                        newSolution = updatedSolution;
                    }
                }
            }
            else
            {
                // Entire workspace mode - process each project
                foreach (var project in workspaceManager.GetProjects())
                {
                    // Get compilation once per project for efficiency
                    var compilation = await project.GetCompilationAsync(cancellationToken);
                    if (compilation == null) continue;

                    foreach (var document in project.Documents)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var (result, updatedSolution) = await ProcessDocumentInSolutionAsync(
                            newSolution, document.Id, sortUsings, systemUsingsFirst, logger, cancellationToken);

                        if (result != null)
                        {
                            results.Add(result);
                            if (result.Modified && updatedSolution != null)
                            {
                                newSolution = updatedSolution;
                            }
                        }
                    }
                }
            }

            // Apply changes to workspace and persist to disk
            if (results.Any(r => r.Modified))
            {
                var applyResult = await workspaceManager.ApplyChangesAsync(newSolution, cancellationToken);
                if (!applyResult.Success)
                {
                    return GetErrorHelpResponse(applyResult.ErrorMessage ?? "Failed to apply changes.");
                }

                logger.LogInformation("Simplified usings in {Count} files", applyResult.ChangedFiles.Count);
            }

            return BuildResponse(results);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error executing SimplifyUsingsTool");
            return GetErrorHelpResponse($"Failed to simplify usings: {ex.Message}");
        }
    }

    private static async Task<(FileUsingsResult? Result, Solution? UpdatedSolution)> ProcessDocumentInSolutionAsync(
        Solution solution,
        DocumentId documentId,
        bool sortUsings,
        bool systemUsingsFirst,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(documentId);
        if (document == null) return (null, null);

        var tree = await document.GetSyntaxTreeAsync(cancellationToken);
        if (tree == null) return (null, null);

        var root = await tree.GetRootAsync(cancellationToken) as Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax;
        if (root == null) return (null, null);

        var usingDirectives = root.Usings;
        if (!usingDirectives.Any())
            return (null, null);

        // Get compilation for this document's project
        var project = solution.GetProject(documentId.ProjectId);
        if (project == null) return (null, null);

        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation == null) return (null, null);

        // Get CS8019 diagnostics for this file (unused using directives)
        var unusedDiagnostics = compilation.GetDiagnostics(cancellationToken)
            .Where(d => d.Id == UnusedUsingDiagnosticId && d.Location.SourceTree == tree)
            .ToList();

        if (!unusedDiagnostics.Any() && !sortUsings)
        {
            // No changes needed
            return (new FileUsingsResult(
                document.FilePath ?? document.Name,
                usingDirectives.Count,
                0,
                false,
                []
            ), null);
        }

        // Find unused using nodes
        var unusedUsings = unusedDiagnostics
            .Select(d => root.FindNode(d.Location.SourceSpan) as Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax)
            .Where(u => u != null)
            .ToList();

        var unusedNames = unusedUsings
            .Select(u => u!.Name?.ToString() ?? "")
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        // Remove unused usings
        var newRoot = root;
        if (unusedUsings.Any())
        {
            newRoot = newRoot.RemoveNodes(unusedUsings!, SyntaxRemoveOptions.KeepNoTrivia);
        }

        // Sort usings if requested
        if (sortUsings && newRoot != null)
        {
            var remainingUsings = newRoot.Usings.ToList();
            if (remainingUsings.Any())
            {
                var sortedUsings = systemUsingsFirst
                    ? remainingUsings.OrderBy(u => u.Name?.ToString() ?? "", new SystemFirstComparer()).ToList()
                    : remainingUsings.OrderBy(u => u.Name?.ToString() ?? "").ToList();

                // Only reorder if actually different
                if (!sortedUsings.SequenceEqual(remainingUsings))
                {
                    newRoot = newRoot.WithUsings(
                        Microsoft.CodeAnalysis.CSharp.SyntaxFactory.List(sortedUsings));
                }
            }
        }

        if (newRoot == null || newRoot == root)
        {
            return (new FileUsingsResult(
                document.FilePath ?? document.Name,
                usingDirectives.Count,
                unusedNames.Count,
                false,
                unusedNames
            ), null);
        }

        // Apply changes to document
        var newDocument = document.WithSyntaxRoot(newRoot);
        newDocument = await Formatter.FormatAsync(newDocument, cancellationToken: cancellationToken);

        return (new FileUsingsResult(
            document.FilePath ?? document.Name,
            usingDirectives.Count,
            unusedNames.Count,
            true,
            unusedNames
        ), newDocument.Project.Solution);
    }

    private static string BuildResponse(List<FileUsingsResult> results)
    {
        var sb = new StringBuilder();

        var modified = results.Where(r => r.Modified).ToList();

        sb.AppendLine("## Usings Simplified");
        sb.AppendLine();

        if (modified.Any())
        {
            sb.AppendLine($"**Modified Files**: {modified.Count}");
            sb.AppendLine();

            foreach (var file in modified.Take(20))
            {
                sb.AppendLine($"- `{file.FileName}`: Removed {file.UnusedCount} unused usings");
                if (file.UnusedUsings.Any())
                {
                    sb.AppendLine($"  - Removed: {string.Join(", ", file.UnusedUsings.Take(5))}{(file.UnusedUsings.Count > 5 ? "..." : "")}");
                }
            }

            if (modified.Count > 20)
            {
                sb.AppendLine($"- ... and {modified.Count - 20} more files");
            }
        }
        else
        {
            sb.AppendLine("**No files modified** - all using directives are in use.");
        }

        sb.AppendLine();
        sb.AppendLine($"**Files Scanned**: {results.Count}");
        sb.AppendLine($"**Total Usings Removed**: {results.Sum(r => r.UnusedCount)}");

        return sb.ToString();
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Simplify Usings",
            message,
            "SimplifyUsings()\nSimplifyUsings(filePath: \"path/to/File.cs\")",
            "- `SimplifyUsings()` - Process entire workspace\n- `SimplifyUsings(filePath: \"MyClass.cs\", sortUsings: true)`"
        );
    }

    private record FileUsingsResult(
        string FileName,
        int TotalUsings,
        int UnusedCount,
        bool Modified,
        List<string> UnusedUsings
    );

    /// <summary>
    /// Comparer that puts System namespaces first, then alphabetically
    /// </summary>
    private class SystemFirstComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return 1;
            if (y == null) return -1;

            bool xIsSystem = x.StartsWith("System") || x == "System";
            bool yIsSystem = y.StartsWith("System") || y == "System";

            if (xIsSystem && !yIsSystem) return -1;
            if (!xIsSystem && yIsSystem) return 1;

            return string.Compare(x, y, System.StringComparison.Ordinal);
        }
    }
}
