using System;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.HighValue;

[McpServerToolType]
public class GetCallGraphTool
{
    [McpServerTool, Description("Get call graph for a method showing its callers and callees")]
    public static async Task<string> GetCallGraph(
        [Description("The name of the method to analyze")] string symbolName,
        IWorkspaceManager workspaceManager,
        ILogger<GetCallGraphTool> logger,
        CancellationToken cancellationToken,
        [Description("Path to the file containing the method")] string filePath = "",
        [Description("1-based line number near the method declaration")] int lineNumber = 0,
        [Description("Maximum number of callers to display")] int maxCallers = 20,
        [Description("Maximum number of callees to display")] int maxCallees = 10)
    {
        try
        {
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Call Graph");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            // 确保工作区是最新的（如果需要会重新加载整个工作区）
            await workspaceManager.EnsureRefreshAsync(cancellationToken);

            logger.LogInformation("Getting call graph: {FilePath}:{LineNumber} - {SymbolName}",
                filePath, lineNumber, symbolName);

            var resolved = await SymbolResolver.ResolveSymbolAsync(filePath, lineNumber, symbolName ?? "", workspaceManager, SymbolFilter.Member, cancellationToken);
            if (resolved == null)
            {
                var errorDetails = await MarkdownHelper.BuildSymbolNotFoundErrorDetailsAsync(
                    filePath, lineNumber, symbolName ?? "Not specified", workspaceManager.GetCurrentSolution(), cancellationToken);
                logger.LogWarning("Symbol not found: {Details}", errorDetails);
                return GetErrorHelpResponse(errorDetails.ToString());
            }

            var symbol = resolved.Symbol;
            if (symbol is not IMethodSymbol method)
            {
                logger.LogWarning("Symbol is not a method: {SymbolName}", symbol.Name);
                return MarkdownHelper.BuildNotAMethodResponse(symbol.Name, symbol.Kind.ToString());
            }

            var solution = workspaceManager.GetCurrentSolution();
            return await method.GetCallGraphMarkdown(solution, maxCallers, maxCallees, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetCallGraphTool");
            return GetErrorHelpResponse($"Failed to get call graph: {ex.Message}");
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
}
