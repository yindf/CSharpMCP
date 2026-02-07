using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Models.Output;
using CSharpMcp.Server.Models.Tools;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.Essential;

/// <summary>
/// search_symbols 工具 - 搜索跨工作区的符号
/// </summary>
[McpServerToolType]
public class SearchSymbolsTool
{
    /// <summary>
    /// Search for symbols across the entire workspace by name pattern
    /// </summary>
    [McpServerTool]
    public static async Task<SearchSymbolsResponse> SearchSymbols(
        SearchSymbolsParams parameters,
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger<SearchSymbolsTool> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            logger.LogDebug("Searching symbols: {Query}", parameters.Query);

            var compilation = await workspaceManager.GetCompilationAsync(cancellationToken: cancellationToken);
            if (compilation == null)
            {
                logger.LogWarning("No compilation loaded");
                throw new InvalidOperationException("No workspace loaded");
            }

            // Parse query for wildcards
            var searchTerm = parameters.Query.Replace("*", "").Replace(".", "").Trim();

            // Get all symbols with matching name
            var allSymbols = compilation.GetSymbolsWithName(
                n => n.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                     n.Equals(searchTerm, StringComparison.OrdinalIgnoreCase),
                SymbolFilter.All);

            var results = new List<Models.SymbolInfo>();
            foreach (var symbol in allSymbols)
            {
                // Check if symbol has source location
                var locations = symbol.Locations.Where(l => l.IsInSource).ToList();
                if (locations.Count == 0)
                    continue;

                // Get the first source location
                var location = locations[0];
                var lineSpan = location.GetLineSpan();

                var symbolLocation = new Models.SymbolLocation(
                    location.SourceTree?.FilePath ?? "",
                    lineSpan.StartLinePosition.Line + 1,
                    lineSpan.EndLinePosition.Line + 1,
                    lineSpan.StartLinePosition.Character + 1,
                    lineSpan.EndLinePosition.Character + 1
                );

                // Create minimal symbol info
                var info = new Models.SymbolInfo
                {
                    Name = symbol.Name,
                    Kind = MapSymbolKind(symbol.Kind),
                    Location = symbolLocation,
                    ContainingType = symbol.ContainingType?.Name ?? "",
                    Namespace = symbol.ContainingNamespace?.ToString() ?? "",
                    IsStatic = symbol.IsStatic,
                    IsVirtual = symbol.IsVirtual,
                    IsOverride = symbol.IsOverride,
                    IsAbstract = symbol.IsAbstract,
                    Accessibility = MapAccessibility(symbol.DeclaredAccessibility)
                };

                results.Add(info);

                if (results.Count >= parameters.MaxResults)
                    break;
            }

            logger.LogDebug("Found {Count} symbols matching: {Query}", results.Count, parameters.Query);

            return new SearchSymbolsResponse(parameters.Query, results);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing SearchSymbolsTool");
            throw;
        }
    }

    private static Models.SymbolKind MapSymbolKind(SymbolKind kind)
    {
        return kind switch
        {
            SymbolKind.NamedType => Models.SymbolKind.Class,
            SymbolKind.Method => Models.SymbolKind.Method,
            SymbolKind.Property => Models.SymbolKind.Property,
            SymbolKind.Field => Models.SymbolKind.Field,
            SymbolKind.Event => Models.SymbolKind.Event,
            _ => Models.SymbolKind.Method
        };
    }

    private static Models.Accessibility MapAccessibility(Microsoft.CodeAnalysis.Accessibility accessibility)
    {
        return accessibility switch
        {
            Microsoft.CodeAnalysis.Accessibility.Public => Models.Accessibility.Public,
            Microsoft.CodeAnalysis.Accessibility.Internal => Models.Accessibility.Internal,
            Microsoft.CodeAnalysis.Accessibility.Protected => Models.Accessibility.Protected,
            Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => Models.Accessibility.ProtectedInternal,
            Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => Models.Accessibility.PrivateProtected,
            Microsoft.CodeAnalysis.Accessibility.Private => Models.Accessibility.Private,
            _ => Models.Accessibility.Private
        };
    }
}
