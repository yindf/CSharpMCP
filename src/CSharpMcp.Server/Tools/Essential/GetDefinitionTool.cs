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

namespace CSharpMcp.Server.Tools.Essential;

/// <summary>
/// get_definition 工具 - 获取符号的完整信息
/// </summary>
[McpServerToolType]
public class GetDefinitionTool
{
    /// <summary>
    /// Get comprehensive symbol information including documentation, comments, and context
    /// </summary>
    [McpServerTool, Description("Get comprehensive information about a symbol including signature, documentation, and location")]
    public static async Task<string> GetDefinition(
        [Description("Path to the file containing the symbol")] string filePath,
        IWorkspaceManager workspaceManager,
        ILogger<GetDefinitionTool> logger,
        CancellationToken cancellationToken,
        [Description("1-based line number near the symbol declaration (used for fuzzy matching)")] int lineNumber = 0,
        [Description("The name of the symbol to locate")] string? symbolName = null,
        [Description("Whether to include method/property implementation in output")] bool includeBody = true,
        [Description("Maximum number of lines to include for implementation code")] int maxBodyLines = 50,
        [Description("Only return primary symbol definitions (classes, interfaces, methods), excluding fields/properties")] bool definitionsOnly = true)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required", nameof(filePath));
            }

            // Check workspace state
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Definition");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            logger.LogInformation("Resolving symbol: {FilePath}:{LineNumber} - {SymbolName}",
                filePath, lineNumber, symbolName);

            // Resolve the symbol
            var symbols = await ResolveSymbolAsync(filePath, lineNumber, symbolName ?? "", workspaceManager, cancellationToken);

            // Apply DefinitionsOnly filter if enabled
            if (definitionsOnly)
            {
                symbols = symbols.Where(s => s is INamedTypeSymbol or IMethodSymbol).ToImmutableList();
            }

            if (symbols == null || !symbols.Any())
            {
                var errorDetails = await BuildErrorDetailsAsync(filePath, lineNumber, symbolName ?? "Not specified", workspaceManager, cancellationToken);
                logger.LogWarning("Symbol not found: {Details}", errorDetails);
                return GetErrorHelpResponse(errorDetails);
            }

            // Build Markdown directly
            StringBuilder sb = new StringBuilder();
            foreach (var symbol in symbols)
            {
                var result = await BuildSymbolMarkdownAsync(symbol, includeBody, maxBodyLines, workspaceManager.GetCurrentSolution(), cancellationToken);
                sb.AppendLine(result);

                logger.LogInformation("Resolved symbol: {SymbolName}", symbol.Name);
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetDefinitionTool");
            return GetErrorHelpResponse($"Failed to resolve symbol: {ex.Message}\n\nStack Trace:\n```\n{ex.StackTrace}\n```\n\nCommon issues:\n- Symbol not found in workspace\n- Workspace is not loaded (call LoadWorkspace first)\n- Symbol is from an external library");
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
        ISymbol symbol,
        bool includeBody,
        int maxBodyLines,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var displayName = symbol.GetDisplayName();
        var (startLine, endLine) = symbol.GetLineRange();
        var filePath = symbol.GetFilePath();
        var kind = symbol.GetDisplayKind();

        sb.AppendLine($"## Symbol: `{displayName}`");
        sb.AppendLine();

        // Basic info
        sb.AppendLine("**Basic Info**:");
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
        sb.AppendLine();

        // Signature
        var signature = symbol.GetSignature();
        if (!string.IsNullOrEmpty(signature))
        {
            sb.AppendLine("**Signature**:");
            sb.AppendLine($"```csharp");
            sb.AppendLine($"{symbol.GetFullDeclaration()}");
            sb.AppendLine($"```");
            sb.AppendLine();
        }

        // Documentation
        var summary = symbol.GetSummaryComment();
        if (!string.IsNullOrEmpty(summary))
        {
            sb.AppendLine("**Documentation**:");
            sb.AppendLine(summary);
            sb.AppendLine();
        }

        // Full implementation
        if (includeBody)
        {
            var implementation = await symbol.GetFullImplementationAsync(maxBodyLines, cancellationToken);
            if (!string.IsNullOrEmpty(implementation))
            {
                sb.AppendLine("**Implementation**:");
                sb.AppendLine("```csharp");
                sb.AppendLine(implementation);
                sb.AppendLine("```");

                var totalLines = endLine - startLine + 1;
                if (maxBodyLines < totalLines)
                {
                    sb.AppendLine($"*... {totalLines - maxBodyLines} more lines hidden*");
                }
                sb.AppendLine();
            }
        }

        // References (limited)
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
                        var refFileName = System.IO.Path.GetFileName(refFilePath);
                        var refLineSpan = loc.Location.GetLineSpan();
                        var refLine = refLineSpan.StartLinePosition.Line + 1;

                        // Extract line text
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
            // Ignore reference errors
        }

        return sb.ToString();
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
    /// Resolve symbols from file location info
    /// </summary>
    private static async Task<IEnumerable<ISymbol>> ResolveSymbolAsync(
        string filePath,
        int lineNumber,
        string symbolName,
        IWorkspaceManager workspaceManager,
        CancellationToken cancellationToken)
    {
        var symbols = await workspaceManager.SearchSymbolsAsync(symbolName, SymbolFilter.TypeAndMember, cancellationToken);

        if (string.IsNullOrEmpty(filePath))
        {
            return symbols;
        }

        return OrderSymbolsByProximity(symbols, filePath, lineNumber);
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
