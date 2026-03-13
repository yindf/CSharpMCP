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
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.Essential;

[McpServerToolType]
public class GetDefinitionTool
{
    [McpServerTool, Description("Get comprehensive information about a symbol including signature, documentation, and location")]
    public static async Task<string> GetDefinition(
        [Description("The name of the symbol to locate")] string symbolName,
        IWorkspaceManager workspaceManager,
        ILogger<GetDefinitionTool> logger,
        CancellationToken cancellationToken,
        [Description("Path to the file containing the symbol")] string filePath = "",
        [Description("1-based line number near the symbol declaration (used for fuzzy matching)")] int lineNumber = 0,
        [Description("Whether to include method/property implementation in output")] bool includeBody = true,
        [Description("Maximum number of lines to include for implementation code")] int maxBodyLines = 50,
        [Description("Only return primary symbol definitions (classes, interfaces, methods), excluding fields/properties")] bool definitionsOnly = false)
    {
        try
        {
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Definition");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            // 确保工作区是最新的（如果需要会重新加载整个工作区）
            await workspaceManager.EnsureRefreshAsync(cancellationToken);

            logger.LogInformation("Resolving symbol: {FilePath}:{LineNumber} - {SymbolName}",
                filePath, lineNumber, symbolName);

            // Use the new ResolveSymbolsAsync that returns ResolvedSymbols with best locations
            var resolvedSymbols = await SymbolResolver.ResolveSymbolsAsync(filePath, lineNumber, symbolName ?? "", workspaceManager, logger, cancellationToken);

            // Filter to types and methods if requested
            if (definitionsOnly)
            {
                resolvedSymbols = resolvedSymbols.Where(rs => rs.Symbol is INamedTypeSymbol or IMethodSymbol).ToList();
            }

            if (resolvedSymbols.Count == 0)
            {
                var errorDetails = await MarkdownHelper.BuildSymbolNotFoundErrorDetailsAsync(
                    filePath, lineNumber, symbolName ?? "Not specified", workspaceManager.GetCurrentSolution(), cancellationToken);
                logger.LogWarning("Symbol not found: {Details}", errorDetails);
                return GetErrorHelpResponse(errorDetails.ToString());
            }

            // Debug: Log the order of symbols received
            logger.LogInformation("GetDefinition: Received {Count} resolved symbols for '{SymbolName}'",
                resolvedSymbols.Count, symbolName);
            foreach (var rs in resolvedSymbols.Take(5))
            {
                logger.LogInformation("  - {SymbolName} at {Path}:{Line} (score={Score})",
                    rs.Symbol.Name, rs.FilePath, rs.StartLine, rs.Score);
            }

            // If filePath AND lineNumber are provided, check if first symbol is from target file
            // The symbols are already ordered by proximity by SymbolResolver
            if (!string.IsNullOrEmpty(filePath) && lineNumber > 0)
            {
                var targetFileName = Path.GetFileName(filePath).ToLowerInvariant();
                var firstResolved = resolvedSymbols.FirstOrDefault();

                // Check if the first symbol's best location is in the target file
                if (firstResolved != null && firstResolved.FilePath.Contains(targetFileName, StringComparison.OrdinalIgnoreCase))
                {
                    // First symbol is from target file - return it (it's the closest match)
                    logger.LogInformation("Resolved symbol from target file: {SymbolName} in {FilePath}",
                        firstResolved.Symbol.Name, firstResolved.FilePath);
                    return await BuildSymbolMarkdownAsync(firstResolved, includeBody, maxBodyLines, workspaceManager.GetCurrentSolution(), cancellationToken);
                }
            }
            // If only filePath is provided (no lineNumber), find symbol from that file
            else if (!string.IsNullOrEmpty(filePath))
            {
                var targetFileName = Path.GetFileName(filePath).ToLowerInvariant();

                var fileMatch = resolvedSymbols.FirstOrDefault(rs =>
                    rs.FilePath.Contains(targetFileName, StringComparison.OrdinalIgnoreCase));

                if (fileMatch != null)
                {
                    logger.LogInformation("Resolved symbol from target file: {SymbolName} in {FilePath}",
                        fileMatch.Symbol.Name, filePath);
                    return await BuildSymbolMarkdownAsync(fileMatch, includeBody, maxBodyLines, workspaceManager.GetCurrentSolution(), cancellationToken);
                }
            }

            // Multiple ambiguous matches - show disambiguation list
            if (resolvedSymbols.Count > 1)
            {
                logger.LogInformation("Multiple symbols found for '{SymbolName}', showing disambiguation list", symbolName);
                return BuildDisambiguationResponse(resolvedSymbols, symbolName ?? "symbol");
            }

            // Single match - return it
            var sb = new StringBuilder();
            foreach (var resolved in resolvedSymbols)
            {
                var result = await BuildSymbolMarkdownAsync(resolved, includeBody, maxBodyLines, workspaceManager.GetCurrentSolution(), cancellationToken);
                sb.AppendLine(result);
                logger.LogInformation("Resolved symbol: {SymbolName}", resolved.Symbol.Name);
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetDefinitionTool");
            return GetErrorHelpResponse($"Failed to resolve symbol: {ex.Message}");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Get Definition",
            message,
            "GetDefinition(\n    filePath: \"path/to/File.cs\",\n    lineNumber: 42,  // Line near the symbol reference\n    symbolName: \"MyMethod\"\n)",
            "- `GetDefinition(filePath: \"C:/MyProject/Program.cs\", lineNumber: 15, symbolName: \"MyMethod\")`\n- `GetDefinition(filePath: \"./Utils.cs\", lineNumber: 42, symbolName: \"Helper\", includeBody: true)`"
        );
    }

    private static async Task<string> BuildSymbolMarkdownAsync(
        ResolvedSymbol resolved,
        bool includeBody,
        int maxBodyLines,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var symbol = resolved.Symbol;
        var sb = new StringBuilder();
        var displayName = symbol.GetDisplayName();
        var kind = symbol.GetDisplayKind();

        sb.AppendLine($"## Symbol: `{displayName}`");
        sb.AppendLine();

        sb.AppendLine("**Basic Info**:");
        sb.AppendLine($"- **Kind**: {kind}");
        sb.AppendLine($"- **Accessibility**: {symbol.GetAccessibilityString()}");

        var containingType = symbol.GetContainingTypeName();
        if (!string.IsNullOrEmpty(containingType))
            sb.AppendLine($"- **Containing Type**: {containingType}");

        var ns = symbol.GetNamespace();
        if (!string.IsNullOrEmpty(ns))
            sb.AppendLine($"- **Namespace**: {ns}");

        if (resolved.StartLine > 0)
        {
            var fileName = Path.GetFileName(resolved.FilePath);
            sb.AppendLine($"- **Location**: `{fileName}:{resolved.StartLine}-{resolved.EndLine}`");
        }
        sb.AppendLine();

        var signature = symbol.GetSignature();
        if (!string.IsNullOrEmpty(signature))
        {
            sb.AppendLine("**Signature**:");
            sb.AppendLine($"```csharp");
            sb.AppendLine($"{symbol.GetFullDeclaration()}");
            sb.AppendLine($"```");
            sb.AppendLine();
        }

        var summary = symbol.GetSummaryComment();
        if (!string.IsNullOrEmpty(summary))
        {
            sb.AppendLine("**Documentation**:");
            sb.AppendLine(summary);
            sb.AppendLine();
        }

        if (includeBody)
        {
            var implementation = await symbol.GetFullImplementationAsync(maxBodyLines, cancellationToken);
            if (!string.IsNullOrEmpty(implementation))
            {
                sb.AppendLine("**Implementation**:");
                sb.AppendLine("```csharp");
                sb.AppendLine(implementation);
                sb.AppendLine("```");

                var totalLines = resolved.EndLine - resolved.StartLine + 1;
                if (maxBodyLines < totalLines)
                {
                    sb.AppendLine($"*... {totalLines - maxBodyLines} more lines hidden*");
                }
                sb.AppendLine();
            }
        }

        // Show other partial definitions if any
        MarkdownHelper.AppendOtherPartialDefinitions(sb, resolved);

        try
        {
            var referencedSymbols = (await SymbolFinder.FindReferencesAsync(
                symbol,
                solution,
                cancellationToken)).ToImmutableList();

            if (referencedSymbols.Count > 0)
            {
                sb.AppendLine($"**References** (showing first {Math.Min(5, referencedSymbols.Count)} of {referencedSymbols.Count}):");
                sb.AppendLine();

                int shownRefs = 0;
                foreach (var refSym in referencedSymbols.Take(5))
                {
                    foreach (var loc in refSym.Locations.Take(2))
                    {
                        var refFilePath = loc.Document.FilePath;
                        var refFileName = Path.GetFileName(refFilePath);
                        var refLineSpan = loc.Location.GetLineSpan();
                        var refLine = refLineSpan.StartLinePosition.Line + 1;

                        var lineText = await MarkdownHelper.ExtractLineTextAsync(loc.Document, refLine, cancellationToken);

                        sb.AppendLine($"- `{refFileName}:{refLine}`");
                        if (!string.IsNullOrEmpty(lineText))
                        {
                            sb.AppendLine($"  - {lineText.Trim()}");
                        }

                        shownRefs++;
                    }
                }
                sb.AppendLine();
            }
        }
        catch
        {
        }

        return sb.ToString();
    }

    private static string BuildDisambiguationResponse(IReadOnlyList<ResolvedSymbol> resolvedSymbols, string symbolName)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## Multiple Symbols Found: `{symbolName}`");
        sb.AppendLine();
        sb.AppendLine($"Found {resolvedSymbols.Count} symbols with this name. Please specify the file path and line number to disambiguate:");
        sb.AppendLine();

        // Group by file but maintain proximity order
        var seenFiles = new HashSet<string>();
        var orderedGroups = new List<(string FilePath, List<(ResolvedSymbol Resolved, int Line)> Symbols)>();

        foreach (var resolved in resolvedSymbols)
        {
            var filePath = resolved.FilePath;
            if (string.IsNullOrEmpty(filePath)) continue;

            var line = resolved.StartLine;

            if (!seenFiles.Contains(filePath))
            {
                seenFiles.Add(filePath);
                orderedGroups.Add((filePath, new List<(ResolvedSymbol, int)>()));
            }

            var group = orderedGroups.First(g => g.FilePath == filePath);
            group.Symbols.Add((resolved, line));
        }

        foreach (var (filePath, fileSymbols) in orderedGroups.Take(15))
        {
            var displayPath = MarkdownHelper.GetDisplayPath(filePath, null);
            sb.AppendLine($"### `{displayPath}`");

            foreach (var (resolved, line) in fileSymbols.Take(3))
            {
                var symbol = resolved.Symbol;
                var kind = symbol.GetDisplayKind();
                var containingType = symbol.GetContainingTypeName();

                var description = !string.IsNullOrEmpty(containingType)
                    ? $"{kind} in `{containingType}`"
                    : kind;

                sb.AppendLine($"- L{line}: `{symbol.Name}` ({description})");
            }

            sb.AppendLine();
        }

        if (orderedGroups.Count > 15)
        {
            sb.AppendLine($"*... and {orderedGroups.Count - 15} more files*");
            sb.AppendLine();
        }

        sb.AppendLine("**Tip**: Use `filePath` and `lineNumber` parameters to get a specific symbol:");
        sb.AppendLine("```");
        sb.AppendLine($"GetDefinition(symbolName: \"{symbolName}\", filePath: \"path/to/File.cs\", lineNumber: 42)");
        sb.AppendLine("```");

        return sb.ToString();
    }
}
