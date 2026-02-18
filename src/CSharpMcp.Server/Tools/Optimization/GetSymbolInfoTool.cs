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
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Roslyn;
using Microsoft.CodeAnalysis.FindSymbols;

namespace CSharpMcp.Server.Tools.Optimization;

/// <summary>
/// get_symbol_info 工具 - 整合多个信息源获取完整符号信息
/// 减少API调用次数，一次性返回所有需要的信息
/// </summary>
[McpServerToolType]
public class GetSymbolInfoTool
{
    /// <summary>
    /// Get complete symbol information from multiple sources in one call
    /// </summary>
    [McpServerTool, Description("Get complete symbol information combining signature, documentation, references, inheritance, and call graph")]
    public static async Task<string> GetSymbolInfo(
        [Description("Path to the file containing the symbol")] string filePath,
        IWorkspaceManager workspaceManager,
        IInheritanceAnalyzer inheritanceAnalyzer,
        ILogger<GetSymbolInfoTool> logger,
        CancellationToken cancellationToken,
        [Description("1-based line number near the symbol declaration")] int lineNumber = 0,
        [Description("The name of the symbol to analyze")] string? symbolName = null,
        [Description("Whether to include method/property implementation")] bool includeBody = true,
        [Description("Maximum lines of implementation code to include")] int maxBodyLines = 100,
        [Description("Whether to include references to the symbol")] bool includeReferences = false,
        [Description("Maximum number of references to show")] int maxReferences = 20,
        [Description("Whether to include inheritance hierarchy")] bool includeInheritance = false,
        [Description("Whether to include call graph (for methods only)")] bool includeCallGraph = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required", nameof(filePath));
            }

            // Check workspace state
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Symbol Info");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            logger.LogInformation("Getting complete symbol info: {FilePath}:{LineNumber} - {SymbolName}",
                filePath, lineNumber, symbolName);

            // Resolve the symbol
            var symbol = await ResolveSymbolAsync(filePath, lineNumber, symbolName ?? "", workspaceManager, cancellationToken);
            if (symbol == null)
            {
                logger.LogWarning("Symbol not found: {SymbolName}", symbolName ?? "at specified location");
                return GetErrorHelpResponse($"Symbol not found: `{symbolName ?? "at specified location"}`\n\nCommon issues:\n- Symbol name is incorrect\n- File path or line number is wrong\n- Symbol is from an external library");
            }

            // Build complete Markdown
            var result = await BuildCompleteMarkdownAsync(
                symbol,
                includeBody,
                maxBodyLines,
                includeReferences,
                maxReferences,
                includeInheritance,
                includeCallGraph,
                workspaceManager.GetCurrentSolution(),
                inheritanceAnalyzer,
                logger,
                cancellationToken);

            logger.LogInformation("Retrieved complete symbol info for: {SymbolName}", symbol.Name);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetSymbolInfoTool");
            return GetErrorHelpResponse($"Failed to get complete symbol information: {ex.Message}\n\nStack Trace:\n```\n{ex.StackTrace}\n```\n\nCommon issues:\n- Symbol not found in workspace\n- Workspace is not loaded (call LoadWorkspace first)\n- Symbol is from an external library");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Get Symbol Info",
            message,
            "GetSymbolInfo(\n    filePath: \"path/to/File.cs\",\n    lineNumber: 42,\n    symbolName: \"MyMethod\",\n    includeReferences: true,\n    includeInheritance: false,\n    includeCallGraph: false\n)",
            "- `GetSymbolInfo(filePath: \"C:/MyProject/Service.cs\", lineNumber: 15, symbolName: \"ProcessData\")`\n- `GetSymbolInfo(filePath: \"./Models.cs\", lineNumber: 42, symbolName: \"User\", includeReferences: true, includeInheritance: true)`"
        );
    }

    private static async Task<string> BuildCompleteMarkdownAsync(
        ISymbol symbol,
        bool includeBody,
        int maxBodyLines,
        bool includeReferences,
        int maxReferences,
        bool includeInheritance,
        bool includeCallGraph,
        Solution solution,
        IInheritanceAnalyzer inheritanceAnalyzer,
        ILogger<GetSymbolInfoTool> logger,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var displayName = symbol.GetDisplayName();
        var (startLine, endLine) = symbol.GetLineRange();
        var filePath = symbol.GetFilePath();
        var kind = symbol.GetDisplayKind();

        sb.AppendLine($"# Complete Symbol Information: `{displayName}`");
        sb.AppendLine();

        // ========== Basic Info Section ==========
        sb.AppendLine("## Basic Information");
        sb.AppendLine();
        sb.AppendLine($"- **Name**: `{displayName}`");
        sb.AppendLine($"- **Kind**: {kind}");
        sb.AppendLine($"- **Accessibility**: {symbol.GetAccessibilityString()}");

        var containingType = symbol.GetContainingTypeName();
        if (!string.IsNullOrEmpty(containingType))
            sb.AppendLine($"- **Containing Type**: {containingType}");

        var ns = symbol.GetNamespace();
        if (!string.IsNullOrEmpty(ns))
            sb.AppendLine($"- **Namespace**: {ns}");

        if (startLine > 0)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            sb.AppendLine($"- **Location**: `{fileName}:{startLine}-{endLine}`");
        }

        var signature = symbol.GetSignature();
        if (!string.IsNullOrEmpty(signature))
        {
            sb.AppendLine($"- **Signature**: `{signature}`");
        }

        var modifiers = new List<string>();
        if (symbol.IsStatic) modifiers.Add("static");
        if (symbol.IsVirtual) modifiers.Add("virtual");
        if (symbol.IsOverride) modifiers.Add("override");
        if (symbol.IsAbstract) modifiers.Add("abstract");
        if (modifiers.Count > 0)
        {
            sb.AppendLine($"- **Modifiers**: {string.Join(", ", modifiers)}");
        }

        sb.AppendLine();

        // ========== Documentation Section ==========
        var summary = symbol.GetSummaryComment();
        var fullComment = symbol.GetFullComment();

        if (!string.IsNullOrEmpty(summary) || !string.IsNullOrEmpty(fullComment))
        {
            sb.AppendLine("## Documentation");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(fullComment))
            {
                sb.AppendLine(fullComment);
            }
            else if (!string.IsNullOrEmpty(summary))
            {
                sb.AppendLine(summary);
            }

            sb.AppendLine();
        }

        // ========== Source Code Section ==========
        var isMethod = symbol.Kind == SymbolKind.Method;

        if (isMethod)
        {
            var implementation = await symbol.GetFullImplementationAsync(maxBodyLines, cancellationToken);
            if (!string.IsNullOrEmpty(implementation))
            {
                sb.AppendLine("## Source Code");
                sb.AppendLine();

                var totalLines = endLine - startLine + 1;
                if (maxBodyLines < totalLines)
                {
                    sb.AppendLine($"(showing {maxBodyLines} of {totalLines} total lines)");
                    sb.AppendLine();
                }

                sb.AppendLine("```csharp");
                sb.AppendLine(implementation);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        // ========== References Section ==========
        if (includeReferences)
        {
            try
            {
                var referencedSymbols = (await SymbolFinder.FindReferencesAsync(
                    symbol,
                    solution,
                    cancellationToken)).ToImmutableList();

                if (referencedSymbols.Count > 0)
                {
                    sb.AppendLine($"## References (showing first {Math.Min(maxReferences, referencedSymbols.Count)} locations)");
                    sb.AppendLine();

                    int shownLocations = 0;
                    foreach (var refSym in referencedSymbols)
                    {
                        if (shownLocations >= maxReferences)
                            break;

                        foreach (var loc in refSym.Locations)
                        {
                            if (shownLocations >= maxReferences)
                                break;

                            var refFilePath = loc.Document.FilePath;
                            var refFileName = System.IO.Path.GetFileName(refFilePath);
                            var refLineSpan = loc.Location.GetLineSpan();
                            var refLine = refLineSpan.StartLinePosition.Line + 1;

                            // Extract line text for context
                            var lineText = await MarkdownHelper.ExtractLineTextAsync(loc.Document, refLine, cancellationToken);

                            sb.AppendLine($"- `{refFileName}:{refLine}`");
                            if (!string.IsNullOrEmpty(lineText))
                            {
                                sb.AppendLine($"  - {lineText.Trim()}");
                            }

                            shownLocations++;
                        }
                    }

                    if (shownLocations < referencedSymbols.Sum(r => r.Locations.Count()))
                    {
                        sb.AppendLine($"- ... ({referencedSymbols.Sum(r => r.Locations.Count()) - shownLocations} more references)");
                    }

                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get references for symbol: {SymbolName}", symbol.Name);
            }
        }

        // ========== Inheritance Section ==========
        if (includeInheritance)
        {
            if (symbol is INamedTypeSymbol type)
            {
                try
                {
                    var tree = await inheritanceAnalyzer.GetInheritanceTreeAsync(
                        type,
                        solution,
                        includeDerived: false,
                        maxDerivedDepth: 0,
                        cancellationToken);

                    if (tree.BaseTypes.Count > 0 || tree.Interfaces.Count > 0)
                    {
                        sb.AppendLine("## Inheritance");
                        sb.AppendLine();

                        if (tree.BaseTypes.Count > 0)
                        {
                            sb.AppendLine("### Base Types");
                            foreach (var baseType in tree.BaseTypes)
                            {
                                sb.AppendLine($"- {baseType.ToDisplayString()}");
                            }
                            sb.AppendLine();
                        }

                        if (tree.Interfaces.Count > 0)
                        {
                            sb.AppendLine("### Interfaces");
                            foreach (var iface in tree.Interfaces)
                            {
                                sb.AppendLine($"- {iface.ToDisplayString()}");
                            }
                            sb.AppendLine();
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to get inheritance for symbol: {SymbolName}", symbol.Name);
                }
            }
        }

        // ========== Call Graph Section ==========
        if (includeCallGraph)
        {
            if (symbol is IMethodSymbol method)
            {
                try
                {
                    sb.Append(await method.GetCallGraphMarkdown(solution, 10, 10, cancellationToken));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to get call graph for symbol: {SymbolName}", symbol.Name);
                }
            }
        }

        return sb.ToString();
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
        var filename = System.IO.Path.GetFileName(filePath)?.ToLowerInvariant();
        return symbols.OrderBy(s => s.Locations.Sum(loc =>
            (loc.GetLineSpan().Path.ToLowerInvariant().Contains(filename, StringComparison.InvariantCultureIgnoreCase) == true ? 0 : 10000) +
            Math.Abs(loc.GetLineSpan().StartLinePosition.Line - lineNumber)));
    }
}
