using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// Helper methods for resolving symbols from file location information
/// </summary>
public static class SymbolResolver
{
    /// <summary>
    /// Resolve a single symbol from file location info with fuzzy matching.
    /// Returns the ResolvedSymbol with the best matching location.
    /// </summary>
    public static async Task<ResolvedSymbol?> ResolveSymbolAsync(
        string filePath,
        int lineNumber,
        string symbolName,
        IWorkspaceManager workspaceManager,
        SymbolFilter filter,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAllSymbolsAsync(filePath, lineNumber, symbolName, workspaceManager, filter, null, cancellationToken);
        return resolved.FirstOrDefault();
    }

    /// <summary>
    /// Resolve a single symbol from file location info with fuzzy matching and SymbolKind filtering.
    /// Returns a ResolvedSymbol with the best matching location.
    /// </summary>
    public static async Task<ResolvedSymbol?> ResolveSymbolAsync(
        string filePath,
        int lineNumber,
        string symbolName,
        IWorkspaceManager workspaceManager,
        SymbolFilter filter,
        SymbolKind? symbolKind,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAllSymbolsAsync(filePath, lineNumber, symbolName, workspaceManager, filter, symbolKind, cancellationToken);
        return resolved.FirstOrDefault();
    }

    /// <summary>
    /// Resolve all matching symbols and return them ordered by proximity.
    /// Returns ResolvedSymbols with their best matching locations.
    /// </summary>
    public static async Task<IReadOnlyList<ResolvedSymbol>> ResolveAllSymbolsAsync(
        string filePath,
        int lineNumber,
        string symbolName,
        IWorkspaceManager workspaceManager,
        SymbolFilter filter,
        SymbolKind? symbolKind,
        CancellationToken cancellationToken)
    {
        var symbols = await workspaceManager.SearchSymbolsAsync(symbolName, filter, cancellationToken);

        if (!symbols.Any())
        {
            symbols = await workspaceManager.SearchSymbolsWithPatternAsync(symbolName, filter, cancellationToken);
        }

        // Filter by SymbolKind if specified
        if (symbolKind.HasValue)
        {
            symbols = FilterBySymbolKind(symbols, symbolKind.Value);
        }

        if (string.IsNullOrEmpty(filePath))
        {
            // No file path - return with first location
            return symbols.Select(s => ResolvedSymbol.Create(s, null, 0)).ToList();
        }

        return OrderSymbolsByProximity(symbols, filePath, lineNumber);
    }

    /// <summary>
    /// Resolve multiple symbols from file location info with fuzzy matching.
    /// Returns ResolvedSymbols with their best matching locations.
    /// </summary>
    public static async Task<IReadOnlyList<ResolvedSymbol>> ResolveSymbolsAsync(
        string filePath,
        int lineNumber,
        string symbolName,
        IWorkspaceManager workspaceManager,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ResolveSymbolsAsync: symbols for '{SymbolName}' at {FilePath}:{Line}", symbolName, filePath, lineNumber);

        var symbols = await workspaceManager.SearchSymbolsAsync(symbolName, SymbolFilter.TypeAndMember, cancellationToken);

        if (!symbols.Any())
        {
            symbols = await workspaceManager.SearchSymbolsWithPatternAsync(symbolName, SymbolFilter.TypeAndMember, cancellationToken);
        }

        if (string.IsNullOrEmpty(filePath))
        {
            return symbols.Select(s => ResolvedSymbol.Create(s, null, 0)).ToList();
        }

        return OrderSymbolsByProximity(symbols, filePath, lineNumber, logger);
    }

    /// <summary>
    /// Filter symbols by SymbolKind, handling common mappings
    /// </summary>
    private static IEnumerable<ISymbol> FilterBySymbolKind(IEnumerable<ISymbol> symbols, SymbolKind kind)
    {
        return symbols.Where(s => s.Kind == kind);
    }

    /// <summary>
    /// Order symbols by proximity to a given file and line number.
    /// Symbols in the matching file and closest to the line number are ranked highest.
    /// Returns ResolvedSymbols with their best matching locations.
    /// </summary>
    private static IReadOnlyList<ResolvedSymbol> OrderSymbolsByProximity(
        IEnumerable<ISymbol> symbols,
        string filePath,
        int lineNumber,
        ILogger logger = null)
    {
        var filename = Path.GetFileName(filePath)?.ToLowerInvariant();
        var symbolList = symbols.ToList();

        // Calculate scores and create ResolvedSymbols
        var resolvedSymbols = new List<ResolvedSymbol>();

        foreach (var s in symbolList)
        {
            var resolved = ResolvedSymbol.Create(s, filename, lineNumber);
            resolvedSymbols.Add(resolved);
        }

        // Sort by score (ascending - lower is better)
        resolvedSymbols.Sort((a, b) => a.Score.CompareTo(b.Score));

        // Log for debugging
        if (logger != null)
        {
            logger.LogInformation("OrderSymbolsByProximity: target={Filename}, line={LineNumber}, total={Count}",
                filename, lineNumber, resolvedSymbols.Count);
            foreach (var x in resolvedSymbols.Take(10))
            {
                logger.LogInformation("  {SymbolName} score={Score} path={Path}",
                    x.Symbol.Name, x.Score, x.FilePath);
            }
        }

        return resolvedSymbols;
    }
}
