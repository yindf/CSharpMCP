using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Models.Output;
using CSharpMcp.Server.Models.Tools;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.Essential;

/// <summary>
/// go_to_definition 工具 - 跳转到符号定义
/// </summary>
[McpServerToolType]
public class GoToDefinitionTool
{
    /// <summary>
    /// Navigate to the definition of a symbol
    /// </summary>
    [McpServerTool]
    public static async Task<string> GoToDefinition(
        GoToDefinitionParams parameters,
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger<GoToDefinitionTool> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            logger.LogDebug("Going to definition: {FilePath}:{LineNumber} - {SymbolName}",
                parameters.FilePath, parameters.LineNumber, parameters.SymbolName);

            // Try by position first
            if (parameters.LineNumber.HasValue)
            {
                var result = await TryFindByPositionAsync(parameters, workspaceManager, symbolAnalyzer, logger, cancellationToken);
                if (result != null)
                {
                    return result.ToMarkdown();
                }
            }

            // Try by name
            if (!string.IsNullOrEmpty(parameters.SymbolName))
            {
                var result = await TryFindByNameAsync(parameters, workspaceManager, symbolAnalyzer, logger, cancellationToken);
                if (result != null)
                {
                    return result.ToMarkdown();
                }
            }

            logger.LogWarning("Symbol not found: {SymbolName} in {FilePath}",
                parameters.SymbolName ?? "at specified location", parameters.FilePath);

            throw new FileNotFoundException($"Symbol not found: {parameters.SymbolName ?? "at specified location"}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GoToDefinitionTool");
            throw;
        }
    }

    private static async Task<GoToDefinitionResponse?> TryFindByPositionAsync(
        GoToDefinitionParams parameters,
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger<GoToDefinitionTool> logger,
        CancellationToken cancellationToken)
    {
        var document = await workspaceManager.GetDocumentAsync(parameters.FilePath, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var lineNumber = parameters.LineNumber!.Value;
        var symbol = await symbolAnalyzer.ResolveSymbolAtPositionAsync(
            document,
            lineNumber,
            1,
            cancellationToken);

        if (symbol != null)
        {
            return await CreateResponseAsync(symbol, parameters, symbolAnalyzer, logger, cancellationToken);
        }

        return null;
    }

    private static async Task<GoToDefinitionResponse?> TryFindByNameAsync(
        GoToDefinitionParams parameters,
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger<GoToDefinitionTool> logger,
        CancellationToken cancellationToken)
    {
        var document = await workspaceManager.GetDocumentAsync(parameters.FilePath, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var symbols = await symbolAnalyzer.FindSymbolsByNameAsync(
            document,
            parameters.SymbolName!,
            parameters.LineNumber,
            cancellationToken);

        if (symbols.Count > 0)
        {
            return await CreateResponseAsync(symbols[0], parameters, symbolAnalyzer, logger, cancellationToken);
        }

        return null;
    }

    private static async Task<GoToDefinitionResponse> CreateResponseAsync(
        Microsoft.CodeAnalysis.ISymbol symbol,
        GoToDefinitionParams parameters,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger<GoToDefinitionTool> logger,
        CancellationToken cancellationToken)
    {
        var info = await symbolAnalyzer.ToSymbolInfoAsync(
            symbol,
            parameters.DetailLevel,
            parameters.IncludeBody ? parameters.GetBodyMaxLines() : null,
            cancellationToken);

        // Calculate total lines in the full method span
        var totalLines = info.Location.EndLine - info.Location.StartLine + 1;
        // Calculate actual lines returned (may be truncated)
        var sourceLines = info.SourceCode?.Split('\n').Length ?? 0;
        var isTruncated = parameters.IncludeBody && parameters.GetBodyMaxLines() < totalLines;

        logger.LogDebug("Found definition: {SymbolName} at {FilePath}:{LineNumber}",
            symbol.Name, info.Location.FilePath, info.Location.StartLine);

        // Pass totalLines (full span) not sourceLines (truncated count)
        return new GoToDefinitionResponse(info, isTruncated, totalLines);
    }
}
