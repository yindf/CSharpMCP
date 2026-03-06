using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.Essential;

[McpServerToolType]
public class FindReferencesTool
{
    [McpServerTool, Description("Find all references to a symbol across the workspace, grouped by project.")]
    public static async Task<string> FindReferences(
        [Description("The name of the symbol to find references for")] string symbolName,
        IWorkspaceManager workspaceManager,
        ILogger<FindReferencesTool> logger,
        CancellationToken cancellationToken,
        [Description("Path to the file containing the symbol")] string filePath = "",
        [Description("1-based line number near the symbol declaration")] int lineNumber = 0,
        [Description("Maximum number of references to show per file")] int maxReferencesPerFile = 15,
        [Description("Maximum number of files to show per project")] int maxFilesPerProject = 10)
    {
        try
        {
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Find References");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            await workspaceManager.EnsureRefreshAsync(cancellationToken);

            logger.LogInformation("Finding references: {FilePath}:{LineNumber} - {SymbolName}",
                filePath, lineNumber, symbolName);

            var symbol = await SymbolResolver.ResolveSymbolAsync(filePath, lineNumber, symbolName ?? "", workspaceManager, SymbolFilter.TypeAndMember, cancellationToken);
            if (symbol == null)
            {
                var errorDetails = await MarkdownHelper.BuildSymbolNotFoundErrorDetailsAsync(
                    filePath, lineNumber, symbolName ?? "Not specified", workspaceManager.GetCurrentSolution(), cancellationToken);
                logger.LogWarning("Symbol not found: {Details}", errorDetails);
                return GetErrorHelpResponse(errorDetails.ToString());
            }

            var solution = workspaceManager.GetCurrentSolution();
            var referencedSymbols = (await SymbolFinder.FindReferencesAsync(
                symbol,
                solution,
                cancellationToken)).ToImmutableList();

            logger.LogInformation("Found {Count} references for {SymbolName}", referencedSymbols.Count, symbol.Name);

            return await BuildReferencesMarkdownAsync(symbol, referencedSymbols, maxReferencesPerFile, maxFilesPerProject, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing FindReferencesTool");
            return GetErrorHelpResponse($"Failed to find references: {ex.Message}");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Find References",
            message,
            "FindReferences(\n    filePath: \"path/to/File.cs\",\n    lineNumber: 42,  // Line near the symbol declaration\n    symbolName: \"MyMethod\"\n)",
            "- `FindReferences(filePath: \"C:/MyProject/MyClass.cs\", lineNumber: 15, symbolName: \"MyMethod\")`\n- `FindReferences(filePath: \"./Utils.cs\", lineNumber: 42, symbolName: \"Helper\")`"
        );
    }

    private static async Task<string> BuildReferencesMarkdownAsync(
        ISymbol symbol,
        IReadOnlyList<ReferencedSymbol> referencedSymbols,
        int maxReferencesPerFile,
        int maxFilesPerProject,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var displayName = symbol.GetDisplayName();

        sb.AppendLine($"# References: `{displayName}`");
        sb.AppendLine();

        // Collect all references with project info
        var allRefs = new List<(ReferenceLocation Location, Document Document, string FilePath, Project Project)>();

        foreach (var rs in referencedSymbols)
        {
            foreach (var loc in rs.Locations)
            {
                var doc = loc.Document;
                var project = doc.Project;
                allRefs.Add((loc, doc, doc.FilePath ?? "", project));
            }
        }

        var totalRefs = allRefs.Count;
        var totalFiles = allRefs.Select(r => r.FilePath).Distinct().Count();
        var totalProjects = allRefs.Select(r => r.Project).Distinct().Count();

        // Summary
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"**{totalRefs}** references in **{totalFiles}** files across **{totalProjects}** project{(totalProjects != 1 ? "s" : "")}");
        sb.AppendLine();

        if (totalRefs == 0)
        {
            sb.AppendLine("No references found.");
            return sb.ToString();
        }

        // Group by project
        var byProject = allRefs
            .GroupBy(r => r.Project)
            .OrderByDescending(g => g.Count());

        foreach (var projectGroup in byProject)
        {
            var project = projectGroup.Key;
            var projectRefs = projectGroup.Count();
            var projectFiles = projectGroup.Select(r => r.FilePath).Distinct().Count();

            sb.AppendLine($"## {project.Name}");
            sb.AppendLine();
            sb.AppendLine($"**{projectRefs}** references in **{projectFiles}** file{(projectFiles != 1 ? "s" : "")}");
            sb.AppendLine();

            // Group by file within project
            var byFile = projectGroup
                .GroupBy(r => r.FilePath)
                .OrderByDescending(g => g.Count())
                .Take(maxFilesPerProject);

            foreach (var fileGroup in byFile)
            {
                var relativePath = GetRelativePath(fileGroup.Key);
                var fileName = System.IO.Path.GetFileName(relativePath);
                var fileRefs = fileGroup.Count();

                sb.AppendLine($"### {fileName}");
                sb.AppendLine($"`{relativePath}` ({fileRefs} reference{(fileRefs != 1 ? "s" : "")})");
                sb.AppendLine();

                var refsInFile = fileGroup
                    .OrderBy(r => GetLineNumber(r.Location))
                    .Take(maxReferencesPerFile);

                foreach (var refLoc in refsInFile)
                {
                    var location = refLoc.Location.Location;
                    var lineSpan = location.GetLineSpan();
                    var startLine = lineSpan.StartLinePosition.Line + 1;
                    var endLine = lineSpan.EndLinePosition.Line + 1;

                    var lineText = await MarkdownHelper.ExtractLineTextAsync(refLoc.Document, startLine, cancellationToken);

                    var lineRange = endLine > startLine ? $"L{startLine}-L{endLine}" : $"L{startLine}";
                    sb.AppendLine($"- **{lineRange}**: `{lineText?.Trim() ?? ""}`");
                }

                if (fileGroup.Count() > maxReferencesPerFile)
                {
                    sb.AppendLine($"- _... and {fileGroup.Count() - maxReferencesPerFile} more references_");
                }

                sb.AppendLine();
            }

            var totalFilesInProject = projectGroup.Select(r => r.FilePath).Distinct().Count();
            if (totalFilesInProject > maxFilesPerProject)
            {
                sb.AppendLine($"_... and {totalFilesInProject - maxFilesPerProject} more files in this project_");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static string GetRelativePath(string absolutePath)
    {
        try
        {
            var currentDir = System.IO.Directory.GetCurrentDirectory();
            var relativePath = System.IO.Path.GetRelativePath(currentDir, absolutePath);
            return string.IsNullOrEmpty(relativePath) ? absolutePath.Replace('\\', '/') : relativePath.Replace('\\', '/');
        }
        catch
        {
            return absolutePath.Replace('\\', '/');
        }
    }

    private static int GetLineNumber(ReferenceLocation location)
    {
        return location.Location.GetLineSpan().StartLinePosition.Line + 1;
    }
}
