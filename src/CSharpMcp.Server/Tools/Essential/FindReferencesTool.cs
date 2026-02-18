using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.Essential;

/// <summary>
/// find_references 工具 - 查找符号的所有引用
/// </summary>
[McpServerToolType]
public class FindReferencesTool
{
    /// <summary>
    /// Find all references to a symbol across the workspace
    /// </summary>
    [McpServerTool, Description("Find all references to a symbol across the workspace")]
    public static async Task<string> FindReferences(
        [Description("Path to the file containing the symbol")] string filePath,
        IWorkspaceManager workspaceManager,
        ILogger<FindReferencesTool> logger,
        CancellationToken cancellationToken,
        [Description("1-based line number near the symbol declaration")] int lineNumber = 0,
        [Description("The name of the symbol to find references for")] string? symbolName = null,
        [Description("Whether to include source code context around each reference")] bool includeContext = true,
        [Description("Number of lines to show before and after each reference")] int contextLines = 3,
        [Description("Show only file names and reference counts, not detailed code context")] bool compact = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required", nameof(filePath));
            }

            // Check workspace state
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Find References");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            logger.LogInformation("Finding references: {FilePath}:{LineNumber} - {SymbolName}",
                filePath, lineNumber, symbolName);

            // First, resolve the symbol
            var symbol = await ResolveSymbolAsync(filePath, lineNumber, symbolName ?? "", workspaceManager, cancellationToken);
            if (symbol == null)
            {
                var errorDetails = await BuildErrorDetailsAsync(filePath, lineNumber, symbolName ?? "Not specified", workspaceManager, cancellationToken);
                logger.LogWarning("Symbol not found: {Details}", errorDetails);
                return GetErrorHelpResponse(errorDetails);
            }

            // Get the solution
            var solution = workspaceManager.GetCurrentSolution();

            // Find references
            var referencedSymbols = (await SymbolFinder.FindReferencesAsync(
                symbol,
                solution,
                cancellationToken)).ToImmutableList();

            logger.LogInformation("Found {Count} references for {SymbolName}", referencedSymbols.Count, symbol.Name);

            // Build Markdown directly
            return await BuildReferencesMarkdownAsync(symbol, referencedSymbols, compact, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing FindReferencesTool");
            return GetErrorHelpResponse($"Failed to find references: {ex.Message}\n\nStack Trace:\n```\n{ex.StackTrace}\n```\n\nCommon issues:\n- Symbol not found in workspace\n- Workspace is not loaded (call LoadWorkspace first)\n- Symbol is from an external library");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Find References",
            message,
            "FindReferences(\n    filePath: \"path/to/File.cs\",\n    lineNumber: 42,  // Line near the symbol declaration\n    symbolName: \"MyMethod\"\n)",
            "- `FindReferences(filePath: \"C:/MyProject/MyClass.cs\", lineNumber: 15, symbolName: \"MyMethod\")`\n- `FindReferences(filePath: \"./Utils.cs\", lineNumber: 42, symbolName: \"Helper\", compact: true)`"
        );
    }

    private static async Task<string> BuildReferencesMarkdownAsync(
        ISymbol symbol,
        IReadOnlyList<ReferencedSymbol> referencedSymbols,
        bool compact,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var displayName = symbol.GetDisplayName();

        sb.AppendLine($"## References: `{displayName}`");
        sb.AppendLine();

        // Group by file
        var groupedByFile = referencedSymbols
            .SelectMany(rs => rs.Locations.Select(loc => new
            {
                ReferenceLocation = loc,
                Definition = rs.Definition
            }))
            .GroupBy(r => r.ReferenceLocation.Document.FilePath);

        // Calculate total references correctly
        long totalRefs = groupedByFile.Sum(g => g.Count());

        if (compact)
        {
            sb.AppendLine($"**Total**: {totalRefs} references across {groupedByFile.Count()} file{(groupedByFile.Count() != 1 ? "s" : "")}");
            sb.AppendLine();

            foreach (var fileGroup in groupedByFile.OrderBy(g => g.Key))
            {
                var fileName = Path.GetFileName(fileGroup.Key);
                var count = fileGroup.Count();
                sb.AppendLine($"- `{fileName}`: {count} reference{(count != 1 ? "s" : "")}");
            }
            return sb.ToString();
        }

        sb.AppendLine($"**Total References**: {totalRefs} in {referencedSymbols.Count} location{(referencedSymbols.Count != 1 ? "s" : "")}**");
        sb.AppendLine();

        foreach (var fileGroup in groupedByFile.OrderBy(g => g.Key))
        {
            var fileName = Path.GetFileName(fileGroup.Key);
            sb.AppendLine($"### {fileName}");
            sb.AppendLine();

            foreach (var refLoc in fileGroup.OrderBy(r => GetLineNumber(r.ReferenceLocation)))
            {
                var location = refLoc.ReferenceLocation.Location;
                var lineSpan = location.GetLineSpan();
                var startLine = lineSpan.StartLinePosition.Line + 1;
                var endLine = lineSpan.EndLinePosition.Line + 1;

                // Extract line text
                var lineText = await MarkdownHelper.ExtractLineTextAsync(refLoc.ReferenceLocation.Document, startLine, cancellationToken);

                var lineRange = endLine > startLine ? $"L{startLine}-{endLine}" : $"L{startLine}";
                sb.AppendLine($"- {lineRange}: {lineText?.Trim() ?? ""}");
            }
            sb.AppendLine();
        }

        // Summary
        sb.AppendLine("**Summary**:");
        var filesAffected = groupedByFile.Count();

        sb.AppendLine($"- **Total References**: {totalRefs}");
        sb.AppendLine($"- **Files Affected**: {filesAffected}");

        return sb.ToString();
    }

    private static int GetLineNumber(ReferenceLocation location)
    {
        return location.Location.GetLineSpan().StartLinePosition.Line + 1;
    }

    private static async Task<string> BuildErrorDetailsAsync(
        string filePath,
        int lineNumber,
        string symbolName,
        IWorkspaceManager workspaceManager,
        CancellationToken cancellationToken)
    {
        var details = await MarkdownHelper.BuildSymbolNotFoundErrorDetailsAsync(
            filePath,
            lineNumber,
            symbolName,
            workspaceManager.GetCurrentSolution(),
            cancellationToken);

        return details.ToString();
    }

    /// <summary>
    /// Resolve a single symbol from file location info
    /// </summary>
    private static async Task<ISymbol?> ResolveSymbolAsync(
        string filePath,
        int lineNumber,
        string symbolName,
        IWorkspaceManager workspaceManager,
        CancellationToken cancellationToken)
    {
        var symbols = await workspaceManager.SearchSymbolsAsync(symbolName, SymbolFilter.TypeAndMember, cancellationToken);
        if (!symbols.Any())
        {
            symbols = await workspaceManager.SearchSymbolsWithPatternAsync(symbolName, SymbolFilter.TypeAndMember, cancellationToken);
        }

        if (string.IsNullOrEmpty(filePath))
        {
            return symbols.FirstOrDefault();
        }

        return OrderSymbolsByProximity(symbols, filePath, lineNumber).FirstOrDefault();
    }

    private static IEnumerable<ISymbol> OrderSymbolsByProximity(
        IEnumerable<ISymbol> symbols,
        string filePath,
        int lineNumber)
    {
        var filename = Path.GetFileName(filePath)?.ToLowerInvariant();
        return symbols.OrderBy(s => s.Locations.Sum(loc =>
            (loc.GetLineSpan().Path.ToLowerInvariant().Contains(filename, StringComparison.InvariantCultureIgnoreCase) == true ? 0 : 10000) +
            Math.Abs(loc.GetLineSpan().StartLinePosition.Line - lineNumber)));
    }
}
