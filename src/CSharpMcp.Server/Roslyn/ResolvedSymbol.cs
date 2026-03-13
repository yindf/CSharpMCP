using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// Represents a resolved symbol with its best matching location.
/// For partial classes with multiple locations, this contains the location
/// that best matches the target file path and line number.
/// </summary>
public sealed class ResolvedSymbol
{
    /// <summary>
    /// The resolved symbol.
    /// </summary>
    public ISymbol Symbol { get; }

    /// <summary>
    /// The best matching location for this symbol based on the target file path.
    /// For partial classes, this is the location in the file that best matches the target.
    /// </summary>
    public Location BestLocation { get; }

    /// <summary>
    /// The proximity score (lower is better).
    /// Score = 0 if in matching file, 10000+ if in different file, plus line distance.
    /// </summary>
    internal int Score { get; }

    /// <summary>
    /// The file path of the best location.
    /// </summary>
    public string FilePath => BestLocation.SourceTree?.FilePath ?? "";

    /// <summary>
    /// The line number (1-based) of the best location start.
    /// </summary>
    public int StartLine
    {
        get
        {
            var lineSpan = BestLocation.GetLineSpan();
            return lineSpan.StartLinePosition.Line + 1;
        }
    }

    /// <summary>
    /// The end line number (1-based) of the best location.
    /// </summary>
    public int EndLine
    {
        get
        {
            var lineSpan = BestLocation.GetLineSpan();
            return lineSpan.EndLinePosition.Line + 1;
        }
    }

    private ResolvedSymbol(ISymbol symbol, Location bestLocation, int score)
    {
        Symbol = symbol;
        BestLocation = bestLocation;
        Score = score;
    }

    /// <summary>
    /// Create a ResolvedSymbol with the best matching location.
    /// </summary>
    internal static ResolvedSymbol Create(ISymbol symbol, string? targetFilename, int targetLineNumber)
    {
        var bestLocation = symbol.Locations.FirstOrDefault(l => l.IsInSource) ?? symbol.Locations[0];
        var bestScore = int.MaxValue;

        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource || location.SourceTree == null)
                continue;

            var lineSpan = location.GetLineSpan();
            var path = lineSpan.Path.ToLowerInvariant();

            // Calculate score
            var fileMatch = !string.IsNullOrEmpty(targetFilename) &&
                            path.Contains(targetFilename, StringComparison.InvariantCultureIgnoreCase);
            var fileScore = fileMatch ? 0 : 10000;
            var lineScore = Math.Abs((lineSpan.StartLinePosition.Line + 1) - targetLineNumber);
            var score = fileScore + lineScore;

            if (score < bestScore)
            {
                bestScore = score;
                bestLocation = location;
            }

            // Early exit if perfect match found
            if (bestScore == 0)
                break;
        }

        return new ResolvedSymbol(symbol, bestLocation, bestScore);
    }
}
