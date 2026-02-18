using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Roslyn;
using Microsoft.CodeAnalysis.FindSymbols;

namespace CSharpMcp.Server.Tools.HighValue;

/// <summary>
/// get_call_graph 工具 - 获取方法的调用图
/// </summary>
[McpServerToolType]
public class GetCallGraphTool
{
    /// <summary>
    /// Get the call graph for a method showing callers and callees
    /// </summary>
    [McpServerTool, Description("Get call graph for a method showing its callers and callees")]
    public static async Task<string> GetCallGraph(
        [Description("Path to the file containing the method")] string filePath,
        IWorkspaceManager workspaceManager,
        ILogger<GetCallGraphTool> logger,
        CancellationToken cancellationToken,
        [Description("1-based line number near the method declaration")] int lineNumber = 0,
        [Description("The name of the method to analyze")] string? symbolName = null,
        [Description("Maximum number of callers to display")] int maxCallers = 20,
        [Description("Maximum number of callees to display")] int maxCallees = 10)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required", nameof(filePath));
            }

            // Check workspace state
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Call Graph");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            logger.LogInformation("Getting call graph: {FilePath}:{LineNumber} - {SymbolName}",
                filePath, lineNumber, symbolName);

            // Resolve the method symbol
            var symbol = await ResolveSymbolAsync(filePath, lineNumber, symbolName ?? "", workspaceManager, SymbolFilter.Member, cancellationToken);
            if (symbol == null)
            {
                logger.LogWarning("Method not found: {SymbolName}", symbolName ?? "at specified location");
                return GetNoSymbolFoundHelpResponse(filePath, lineNumber, symbolName);
            }

            if (symbol is not IMethodSymbol method)
            {
                logger.LogWarning("Symbol is not a method: {SymbolName}", symbol.Name);
                return GetNotAMethodHelpResponse(symbol.Name, symbol.Kind.ToString(), filePath, lineNumber);
            }

            var solution = workspaceManager.GetCurrentSolution();

            // Build Markdown directly
            return await method.GetCallGraphMarkdown(solution, maxCallers, maxCallees, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetCallGraphTool");
            return GetErrorHelpResponse($"Failed to get call graph: {ex.Message}\n\nStack Trace:\n```\n{ex.StackTrace}\n```\n\nCommon issues:\n- Symbol is not a method (use GetSymbols to find methods)\n- Symbol is from an external library\n- Workspace is not loaded (call LoadWorkspace first)");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Get Call Graph",
            message,
            "GetCallGraph(\n    filePath: \"path/to/File.cs\",\n    lineNumber: 42,  // Line where method is declared\n    symbolName: \"MyMethod\"\n)",
            "- `GetCallGraph(filePath: \"C:/MyProject/Service.cs\", lineNumber: 15, symbolName: \"ProcessData\")`\n- `GetCallGraph(filePath: \"./Utils.cs\", lineNumber: 42, symbolName: \"Helper\", maxCallers: 50, maxCallees: 20)`"
        );
    }

    /// <summary>
    /// Generate helpful error response when no symbol is found
    /// </summary>
    private static string GetNoSymbolFoundHelpResponse(string filePath, int? lineNumber, string? symbolName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## No Symbol Found");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(symbolName))
        {
            sb.AppendLine($"**Symbol Name**: {symbolName}");
        }
        if (lineNumber.HasValue)
        {
            sb.AppendLine($"**Line Number**: {lineNumber.Value}");
        }
        sb.AppendLine($"**File**: {filePath}");
        sb.AppendLine();
        sb.AppendLine("**Tips**:");
        sb.AppendLine("- Line numbers should point to a method declaration");
        sb.AppendLine("- Use `GetSymbols` first to find valid line numbers for methods");
        sb.AppendLine("- Or provide a valid `symbolName` parameter");
        sb.AppendLine();
        sb.AppendLine("**Usage**:");
        sb.AppendLine("```");
        sb.AppendLine("GetCallGraph(");
        sb.AppendLine("    filePath: \"path/to/File.cs\",");
        sb.AppendLine("    lineNumber: 42  // Line where method is declared");
        sb.AppendLine(")");
        sb.AppendLine("```");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Generate helpful error response when symbol is not a method
    /// </summary>
    private static string GetNotAMethodHelpResponse(string symbolName, string symbolKind, string filePath, int? lineNumber)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Not a Method");
        sb.AppendLine();
        sb.AppendLine($"**Symbol**: `{symbolName}`");
        sb.AppendLine($"**Kind**: {symbolKind}");
        sb.AppendLine();
        sb.AppendLine("The symbol at the specified location is not a method.");
        sb.AppendLine();
        sb.AppendLine("**Tips**:");
        sb.AppendLine("- Use `GetSymbols` first to find valid method declarations");
        sb.AppendLine("- Ensure the line number points to a method declaration");
        sb.AppendLine("- Properties, fields, and other members don't have call graphs");
        sb.AppendLine();
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
        SymbolFilter filter,
        CancellationToken cancellationToken)
    {
        var symbols = await workspaceManager.SearchSymbolsAsync(symbolName, filter, cancellationToken);
        if (!symbols.Any())
        {
            symbols = await workspaceManager.SearchSymbolsWithPatternAsync(symbolName, filter, cancellationToken);
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
