using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Models.Tools;
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
        GetCallGraphParams parameters,
        IWorkspaceManager workspaceManager,
        ILogger<GetCallGraphTool> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            logger.LogInformation("Getting call graph: {FilePath}:{LineNumber} - {SymbolName}",
                parameters.FilePath, parameters.LineNumber, parameters.SymbolName);

            // Resolve the method symbol
            var symbol = await parameters.FindSymbolAsync(
                workspaceManager,
                SymbolFilter.Member,
                cancellationToken);
            if (symbol == null)
            {
                logger.LogWarning("Method not found: {SymbolName}", parameters.SymbolName ?? "at specified location");
                return GetNoSymbolFoundHelpResponse(parameters.FilePath, parameters.LineNumber, parameters.SymbolName);
            }

            if (symbol is not IMethodSymbol method)
            {
                logger.LogWarning("Symbol is not a method: {SymbolName}", symbol.Name);
                return GetNotAMethodHelpResponse(symbol.Name, symbol.Kind.ToString(), parameters.FilePath, parameters.LineNumber);
            }

            var solution = workspaceManager.GetCurrentSolution();

            // Build Markdown directly
            return await method.GetCallGraphMarkdown(solution, parameters.MaxCaller, parameters.MaxCallee, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetCallGraphTool");
            return GetErrorHelpResponse($"Failed to get call graph: {ex.Message}\n\nStack Trace:\n```\n{ex.StackTrace}\n```\n\nCommon issues:\n- Symbol is not a method (use GetSymbols to find methods)\n- Symbol is from an external library\n- Workspace is not loaded (call LoadWorkspace first)");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Get Call Graph - Failed");
        sb.AppendLine();
        sb.AppendLine(message);
        sb.AppendLine();
        sb.AppendLine("**Usage:**");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("GetCallGraph(");
        sb.AppendLine("    filePath: \"path/to/File.cs\",");
        sb.AppendLine("    lineNumber: 42,  // Line where method is declared");
        sb.AppendLine("    symbolName: \"MyMethod\"");
        sb.AppendLine(")");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("**Examples:**");
        sb.AppendLine("- `GetCallGraph(filePath: \"C:/MyProject/Service.cs\", lineNumber: 15, symbolName: \"ProcessData\")`");
        sb.AppendLine("- `GetCallGraph(filePath: \"./Utils.cs\", lineNumber: 42, symbolName: \"Helper\", maxCaller: 50, maxCallee: 20)`");
        sb.AppendLine();
        return sb.ToString();
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
        sb.AppendLine("    lineNumber: 42,  // Line where method is declared");
        sb.AppendLine("    direction: 1  // 1=callers, 2=callees, 0=both");
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
        sb.AppendLine("**Direction Values**:");
        sb.AppendLine("- `0` = Both (callers and callees)");
        sb.AppendLine("- `1` = In (callers only)");
        sb.AppendLine("- `2` = Out (callees only)");
        sb.AppendLine();
        return sb.ToString();
    }
}
