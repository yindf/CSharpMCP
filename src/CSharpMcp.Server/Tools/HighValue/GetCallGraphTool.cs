using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Models.Output;
using CSharpMcp.Server.Models.Tools;
using CSharpMcp.Server.Roslyn;

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
    [McpServerTool]
    public static async Task<CallGraphResponse> GetCallGraph(
        GetCallGraphParams parameters,
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ICallGraphAnalyzer callGraphAnalyzer,
        ILogger<GetCallGraphTool> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            logger.LogDebug("Getting call graph: {FilePath}:{LineNumber} - {SymbolName}",
                parameters.FilePath, parameters.LineNumber, parameters.SymbolName);

            // Resolve the method symbol
            var (symbol, document) = await ResolveSymbolAsync(parameters, workspaceManager, symbolAnalyzer, cancellationToken);
            if (symbol == null)
            {
                logger.LogWarning("Method not found: {SymbolName}", parameters.SymbolName ?? "at specified location");
                throw new FileNotFoundException($"Method not found: {parameters.SymbolName ?? "at specified location"}");
            }

            if (symbol is not IMethodSymbol method)
            {
                logger.LogWarning("Symbol is not a method: {SymbolName}", symbol.Name);
                throw new ArgumentException($"Symbol '{symbol.Name}' is not a method");
            }

            // Get the solution
            var solution = document.Project.Solution;

            // Get call graph
            var graph = await callGraphAnalyzer.GetCallGraphAsync(
                method,
                solution,
                parameters.Direction,
                parameters.MaxDepth,
                cancellationToken);

            // Convert to response format
            var callers = graph.Callers.Select(c => new CallRelationshipItem(
                c.Symbol,
                c.CallLocations.Select(loc => new CallLocationItem(
                    loc.ContainingSymbol,
                    loc.Location
                )).ToList()
            )).ToList();

            var callees = graph.Callees.Select(c => new CallRelationshipItem(
                c.Symbol,
                c.CallLocations.Select(loc => new CallLocationItem(
                    loc.ContainingSymbol,
                    loc.Location
                )).ToList()
            )).ToList();

            var statistics = new CallStatisticsItem(
                graph.Statistics.TotalCallers,
                graph.Statistics.TotalCallees,
                graph.Statistics.CyclomaticComplexity
            );

            logger.LogDebug("Retrieved call graph for: {MethodName}", graph.MethodName);

            return new CallGraphResponse(graph.MethodName, callers, callees, statistics);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetCallGraphTool");
            throw;
        }
    }

    private static async Task<(ISymbol? symbol, Document document)> ResolveSymbolAsync(
        GetCallGraphParams parameters,
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        CancellationToken cancellationToken)
    {
        var document = await workspaceManager.GetDocumentAsync(parameters.FilePath, cancellationToken);
        if (document == null)
        {
            return (null, null!);
        }

        ISymbol? symbol = null;

        // Try by position
        if (parameters.LineNumber.HasValue)
        {
            symbol = await symbolAnalyzer.ResolveSymbolAtPositionAsync(
                document,
                parameters.LineNumber.Value,
                1,
                cancellationToken);

            // If we found a symbol but it's not a method, try to get the containing method
            if (symbol is not IMethodSymbol && symbol != null)
            {
                symbol = symbol.ContainingType?.GetMembers()
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m => m.Name == symbol.Name);
            }
        }

        // Try by name
        if (symbol == null && !string.IsNullOrEmpty(parameters.SymbolName))
        {
            var symbols = await symbolAnalyzer.FindSymbolsByNameAsync(
                document,
                parameters.SymbolName,
                parameters.LineNumber,
                cancellationToken);

            // Prefer method symbols
            symbol = symbols.OfType<IMethodSymbol>().FirstOrDefault()
                     ?? symbols.FirstOrDefault();
        }

        return (symbol, document);
    }
}
