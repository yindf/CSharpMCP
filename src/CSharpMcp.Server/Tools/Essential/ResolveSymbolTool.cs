using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Models.Output;
using CSharpMcp.Server.Models.Tools;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.Essential;

/// <summary>
/// resolve_symbol 工具 - 获取符号的完整信息
/// </summary>
[McpServerToolType]
public class ResolveSymbolTool
{
    /// <summary>
    /// Get comprehensive symbol information including documentation, comments, and context
    /// </summary>
    [McpServerTool]
    public static async Task<ResolveSymbolResponse> ResolveSymbol(
        ResolveSymbolParams parameters,
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger<ResolveSymbolTool> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            logger.LogDebug("Resolving symbol: {FilePath}:{LineNumber} - {SymbolName}",
                parameters.FilePath, parameters.LineNumber, parameters.SymbolName);

            // Resolve the symbol
            var (symbol, document) = await ResolveSymbolAsync(parameters, workspaceManager, symbolAnalyzer, cancellationToken);
            if (symbol == null)
            {
                logger.LogWarning("Symbol not found: {SymbolName}", parameters.SymbolName ?? "at specified location");
                throw new FileNotFoundException($"Symbol not found: {parameters.SymbolName ?? "at specified location"}");
            }

            // Get symbol info
            var info = await symbolAnalyzer.ToSymbolInfoAsync(
                symbol,
                parameters.DetailLevel,
                parameters.IncludeBody ? parameters.BodyMaxLines : null,
                cancellationToken);

            // Get definition source
            string? definition = null;
            if (parameters.IncludeBody)
            {
                definition = await symbolAnalyzer.ExtractSourceCodeAsync(
                    symbol,
                    true,
                    parameters.BodyMaxLines,
                    cancellationToken);
            }

            // Get references (limited)
            List<Models.SymbolReference>? references = null;
            try
            {
                var solution = document.Project.Solution;
                var referencedSymbols = await symbolAnalyzer.FindReferencesAsync(
                    symbol,
                    solution,
                    cancellationToken);

                references = new List<Models.SymbolReference>();
                foreach (var refSym in referencedSymbols.Take(20))
                {
                    foreach (var loc in refSym.Locations.Take(3))
                    {
                        var location = new Models.SymbolLocation(
                            loc.Document.FilePath,
                            loc.Location.GetLineSpan().StartLinePosition.Line + 1,
                            loc.Location.GetLineSpan().EndLinePosition.Line + 1,
                            loc.Location.GetLineSpan().StartLinePosition.Character + 1,
                            loc.Location.GetLineSpan().EndLinePosition.Character + 1
                        );

                        references.Add(new Models.SymbolReference(
                            location,
                            refSym.Definition?.Name ?? "Unknown",
                            null
                        ));
                    }
                }
            }
            catch
            {
                // Ignore reference errors
            }

            logger.LogDebug("Resolved symbol: {SymbolName}", symbol.Name);

            return new ResolveSymbolResponse(info, definition, references);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing ResolveSymbolTool");
            throw;
        }
    }

    private static async Task<(Microsoft.CodeAnalysis.ISymbol? symbol, Document document)> ResolveSymbolAsync(
        ResolveSymbolParams parameters,
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        CancellationToken cancellationToken)
    {
        var document = await workspaceManager.GetDocumentAsync(parameters.FilePath, cancellationToken);
        if (document == null)
        {
            return (null, null!);
        }

        Microsoft.CodeAnalysis.ISymbol? symbol = null;

        // Try by position
        if (parameters.LineNumber.HasValue)
        {
            symbol = await symbolAnalyzer.ResolveSymbolAtPositionAsync(
                document,
                parameters.LineNumber.Value,
                1,
                cancellationToken);
        }

        // Try by name
        if (symbol == null && !string.IsNullOrEmpty(parameters.SymbolName))
        {
            var symbols = await symbolAnalyzer.FindSymbolsByNameAsync(
                document,
                parameters.SymbolName,
                parameters.LineNumber,
                cancellationToken);

            symbol = symbols.FirstOrDefault();
        }

        return (symbol, document);
    }
}
