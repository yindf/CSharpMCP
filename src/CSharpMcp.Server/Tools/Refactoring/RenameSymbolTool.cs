using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSharpMcp.Server.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CSharpMcp.Server.Tools.Refactoring;

[McpServerToolType]
public class RenameSymbolTool
{
    [McpServerTool, Description("Rename a symbol (variable, method, class, etc.) across the entire workspace. Returns the number of files and references affected.")]
    public static async Task<string> RenameSymbol(
        IWorkspaceManager workspaceManager,
        ILogger<RenameSymbolTool> logger,
        CancellationToken cancellationToken,
        [Description("The new name for the symbol")] string newName,
        [Description("The name of the symbol to rename")] string symbolName = "",
        [Description("Path to the file containing the symbol")] string filePath = "",
        [Description("1-based line number near the symbol declaration")] int lineNumber = 0,
        [Description("If true, only preview changes without applying them")] bool previewOnly = false)
    {
        try
        {
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Rename Symbol");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            // Validate new name
            if (string.IsNullOrWhiteSpace(newName))
            {
                return GetErrorHelpResponse("New name cannot be empty.");
            }

            // 确保工作区是最新的（如果需要会重新加载整个工作区）
            await workspaceManager.EnsureRefreshAsync(cancellationToken);

            logger.LogInformation("Renaming symbol: {FilePath}:{LineNumber} - {SymbolName} -> {NewName}",
                filePath, lineNumber, symbolName, newName);

            // Resolve the symbol
            var symbol = await SymbolResolver.ResolveSymbolAsync(
                filePath, lineNumber, symbolName ?? "",
                workspaceManager,
                SymbolFilter.TypeAndMember | SymbolFilter.Namespace,
                cancellationToken);

            if (symbol == null)
            {
                var errorDetails = await MarkdownHelper.BuildSymbolNotFoundErrorDetailsAsync(
                    filePath, lineNumber, symbolName ?? "Not specified",
                    workspaceManager.GetCurrentSolution(), cancellationToken);
                return GetErrorHelpResponse($"Symbol not found.\n\n{errorDetails}");
            }

            // Check if the symbol can be renamed
            if (symbol.Locations.Length > 0 && symbol.Locations[0].IsInMetadata)
            {
                return GetErrorHelpResponse($"Cannot rename symbol '{symbol.Name}': it is defined in referenced metadata (external assembly).");
            }

            var solution = workspaceManager.GetCurrentSolution();
            if (solution == null)
            {
                return GetErrorHelpResponse("Solution not available.");
            }

            // Get all references before rename
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);
            var refList = references.ToList();
            var totalRefs = refList.Sum(r => r.Locations.Count());
            var affectedFiles = refList
                .SelectMany(r => r.Locations)
                .Select(l => l.Document.FilePath)
                .Distinct()
                .Count();

            // Perform the rename
            var newSolution = await Renamer.RenameSymbolAsync(
                solution,
                symbol,
                newName,
                solution.Options,
                cancellationToken);

            // Get reference locations with line numbers for preview display
            var refLocationsWithLines = new List<(string FilePath, int Line)>();
            foreach (var refLoc in refList.SelectMany(r => r.Locations))
            {
                var loc = refLoc.Location;
                if (loc.IsInSource && loc.SourceTree != null)
                {
                    var line = loc.GetLineSpan().StartLinePosition.Line + 1;
                    var path = refLoc.Document.FilePath ?? refLoc.Document.Name;
                    refLocationsWithLines.Add((path, line));
                }
            }

            if (previewOnly)
            {
                logger.LogInformation("Preview rename: '{OldName}' -> '{NewName}' across {Count} files",
                    symbol.Name, newName, affectedFiles);
                return BuildPreviewResponse(symbol, newName, refLocationsWithLines, affectedFiles, workspaceManager.WorkspacePath);
            }

            // Apply changes to workspace and persist to disk
            var result = await workspaceManager.ApplyChangesAsync(newSolution, cancellationToken);
            if (!result.Success)
            {
                return GetErrorHelpResponse(result.ErrorMessage ?? "Failed to apply rename changes.");
            }

            logger.LogInformation("Successfully renamed '{OldName}' to '{NewName}' across {Count} files",
                symbol.Name, newName, result.ChangedFiles.Count);

            return BuildRenameResult(symbol, newName, result.ChangedFiles, totalRefs, workspaceManager.WorkspacePath);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error executing RenameSymbolTool");
            return GetErrorHelpResponse($"Failed to rename symbol: {ex.Message}");
        }
    }

    private static string BuildRenameResult(ISymbol symbol, string newName, IReadOnlyList<string> changedFiles, int totalRefs, string? workspacePath)
    {
        const int maxFilesToShow = 10;
        var sb = new StringBuilder();

        sb.AppendLine($"## Renamed: `{symbol.Name}` → `{newName}`");
        sb.AppendLine();

        sb.AppendLine($"- **Symbol**: `{symbol.GetDisplayName()}`");
        sb.AppendLine($"- **Kind**: {symbol.Kind}");
        sb.AppendLine($"- **References Updated**: {totalRefs}");
        sb.AppendLine($"- **Files Modified**: {changedFiles.Count}");

        if (changedFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Modified Files**:");

            var filesToShow = changedFiles.Take(maxFilesToShow).ToList();
            foreach (var file in filesToShow)
            {
                var displayPath = GetDisplayPath(file, workspacePath);
                sb.AppendLine($"- `{displayPath}`");
            }

            if (changedFiles.Count > maxFilesToShow)
            {
                sb.AppendLine($"- ... and {changedFiles.Count - maxFilesToShow} more files");
            }
        }

        return sb.ToString();
    }

    private static string BuildPreviewResponse(ISymbol symbol, string newName, List<(string FilePath, int Line)> locations, int affectedFiles, string? workspacePath)
    {
        const int maxFilesToShow = 10;
        var sb = new StringBuilder();

        sb.AppendLine($"## Preview: Rename `{symbol.Name}` → `{newName}`");
        sb.AppendLine();
        sb.AppendLine("> **Preview Mode**: No changes will be applied. Use `previewOnly: false` to apply changes.");
        sb.AppendLine();

        sb.AppendLine($"- **Symbol**: `{symbol.GetDisplayName()}`");
        sb.AppendLine($"- **Kind**: {symbol.Kind}");
        sb.AppendLine($"- **References to Update**: {locations.Count}");
        sb.AppendLine($"- **Files Affected**: {affectedFiles}");

        if (locations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Locations**:");

            var groupedByFile = locations
                .GroupBy(l => l.FilePath)
                .OrderBy(g => g.Key)
                .Take(maxFilesToShow);

            foreach (var group in groupedByFile)
            {
                var displayPath = GetDisplayPath(group.Key, workspacePath);
                var lines = group.Select(l => l.Line).OrderBy(l => l).Take(5);
                var lineStr = string.Join(", ", lines);
                if (group.Count() > 5) lineStr += $" (+{group.Count() - 5} more)";
                sb.AppendLine($"- `{displayPath}`: L{lineStr}");
            }

            if (locations.Select(l => l.FilePath).Distinct().Count() > maxFilesToShow)
            {
                sb.AppendLine($"- ... and {affectedFiles - maxFilesToShow} more files");
            }
        }

        return sb.ToString();
    }

    private static string GetDisplayPath(string fullPath, string? workspacePath)
        => MarkdownHelper.GetDisplayPath(fullPath, workspacePath);

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Rename Symbol",
            message,
            "RenameSymbol(\n    newName: \"NewMethodName\",\n    filePath: \"path/to/File.cs\",\n    lineNumber: 42,\n    symbolName: \"OldMethodName\"\n)",
            "- `RenameSymbol(newName: \"ProcessData\", filePath: \"C:/MyProject/Service.cs\", lineNumber: 15, symbolName: \"DoWork\")`\n- `RenameSymbol(newName: \"_logger\", filePath: \"./MyClass.cs\", lineNumber: 10, symbolName: \"Logger\", previewOnly: true)`"
        );
    }
}
