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
    [McpServerTool, Description("Search for symbols across the entire workspace by name with wildcard support")]
    public static async Task<string> SearchSymbols(
        [Description("Search query with optional wildcards (e.g., 'MyClass.*', '*.Controller', 'MyMethod')")] string query,
        IWorkspaceManager workspaceManager,
        ILogger<SearchSymbolsTool> logger,
        CancellationToken cancellationToken,
        [Description("Maximum number of results to return")] int maxResults = 100,
        [Description("Sort order: relevance (type>field, exact>wildcard), name, or kind")] string sortBy = "relevance")
    {
        try
        {
            // Validate parameters
            if (string.IsNullOrWhiteSpace(query))
            {
                return GetErrorHelpResponse("Search query cannot be empty. Please provide a search term.");
            }

            // Check workspace state
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Search Symbols");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            logger.LogInformation("=== SearchSymbolsTool START === Query: {Query}, maxResults: {MaxResults}", query, maxResults);

            var skippedCount = 0;
            var errorCount = 0;
            var results = new List<ISymbol>();

            // Try exact search first, then pattern search if no results
            var symbols = await workspaceManager.SearchSymbolsAsync(query,
                SymbolFilter.TypeAndMember, cancellationToken);
            if (!symbols.Any())
            {
                symbols = await workspaceManager.SearchSymbolsWithPatternAsync(query,
                    SymbolFilter.TypeAndMember, cancellationToken);
            }

            foreach (var symbol in symbols.Take(maxResults))
            {
                try
                {
                    // Check if symbol has source location
                    var locations = symbol.Locations.Where(l => l.IsInSource).ToList();
                    if (locations.Count == 0)
                    {
                        skippedCount++;
                        continue;
                    }

                    var filePath = locations[0].SourceTree?.FilePath ?? "";
                    if (string.IsNullOrEmpty(filePath))
                    {
                        skippedCount++;
                        continue;
                    }

                    results.Add(symbol);

                    if (results.Count >= maxResults)
                        break;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    logger.LogInformation(ex, "Error processing symbol during search");
                }
            }

            logger.LogInformation("Found {Count} symbols matching: {Query}, skipped: {Skipped}, errors: {Errors}",
                results.Count, query, skippedCount, errorCount);

            // If no results found, provide helpful guidance
            if (results.Count == 0)
            {
                return GetNoResultsHelpResponse(query, query, errorCount > 0);
            }

            // Apply sorting
            results = SortResults(results, query, sortBy).ToList();

            // Build Markdown directly
            return BuildSearchResultsMarkdown(query, results);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "=== SearchSymbolsTool ERROR ===: {Message}", ex.Message);
            logger.LogError(ex, "Stack trace: {StackTrace}", ex.StackTrace);
            return GetErrorHelpResponse($"Failed to search symbols: {ex.Message}\n\nStack Trace:\n```\n{ex.StackTrace}\n```\n\nCommon issues:\n- Workspace is not loaded (call LoadWorkspace first)\n- Search query is too complex\n- Workspace has compilation errors");
        }
    }

    private static string BuildSearchResultsMarkdown(string query, List<ISymbol> results)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## Search Results: \"{query}\"");
        sb.AppendLine();
        sb.AppendLine($"**Found {results.Count} result{(results.Count != 1 ? "s" : "")}**");
        sb.AppendLine();

        // Group by kind
        var groupedByKind = results.GroupBy(r => r.Kind);

        foreach (var kindGroup in groupedByKind.OrderBy(g => g.Key))
        {
            // 对于 NamedType，使用第一个符号的实际类型名称作为分组标题
            string groupTitle;
            if (kindGroup.Key == SymbolKind.NamedType && kindGroup.Any())
            {
                var firstSymbol = kindGroup.First();
                var displayKind = firstSymbol.GetDisplayKind();
                groupTitle = SymbolExtensions.PluralizeKind(displayKind);
            }
            else
            {
                var kind = kindGroup.Key.ToString().ToLower();
                groupTitle = SymbolExtensions.PluralizeKind(kind);
            }
            sb.AppendLine($"### {groupTitle}");
            sb.AppendLine();

            foreach (var symbol in kindGroup.OrderBy(s => s.GetDisplayName()))
            {
                var displayName = symbol.GetDisplayName();
                var (startLine, endLine) = symbol.GetLineRange();
                var filePath = symbol.GetFilePath();
                var signature = symbol.GetSignature();
                var accessibility = symbol.GetAccessibilityString();

                var fileName = System.IO.Path.GetFileName(filePath);
                var containingType = symbol.GetContainingTypeName();

                sb.Append($"- **{displayName}**");
                if (!string.IsNullOrEmpty(containingType))
                {
                    sb.Append($" (in {containingType})");
                }
                sb.AppendLine($" `{accessibility} {symbol.GetDisplayKind()}`");

                if (startLine > 0)
                {
                    sb.AppendLine($"  - `{fileName}:{startLine}`");
                }

                if (!string.IsNullOrEmpty(signature))
                {
                    sb.AppendLine($"  - `{signature}`");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Sort results by specified criteria
    /// </summary>
    private static List<ISymbol> SortResults(List<ISymbol> results, string query, string sortBy)
    {
        var sortByLower = sortBy.ToLowerInvariant();

        if (sortByLower == "name")
        {
            return results.OrderBy(s => s.GetDisplayName()).ToList();
        }

        if (sortByLower == "kind")
        {
            return results
                .OrderBy(s => s.Kind.ToString())
                .ThenBy(s => s.GetDisplayName())
                .ToList();
        }

        // Default to relevance sorting
        return SortByRelevance(results, query).ToList();
    }

    /// <summary>
    /// Sort by relevance - prioritize types, exact matches, and prefix matches
    /// </summary>
    private static IEnumerable<ISymbol> SortByRelevance(List<ISymbol> symbols, string query)
    {
        bool hasWildcard = query.Contains('*');
        bool isExactMatch = !hasWildcard;

        return symbols.OrderByDescending(s =>
        {
            int score = 0;

            // Type symbols rank higher
            if (s is INamedTypeSymbol) score += 1000;
            else if (s is IMethodSymbol) score += 500;
            else if (s is IPropertySymbol) score += 200;
            // Fields/Events rank lower

            // Exact name match ranks higher
            if (isExactMatch && string.Equals(s.Name, query, StringComparison.OrdinalIgnoreCase))
                score += 500;

            // Name starts with query ranks higher
            if (s.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                score += 200;

            return score;
        }).ThenBy(s => s.Name);
    }

    /// <summary>
    /// Generate helpful error response when parameters are invalid
    /// </summary>
    private static string GetErrorHelpResponse(string message)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Invalid Input");
        sb.AppendLine();
        sb.AppendLine($"**Error**: {message}");
        sb.AppendLine();
        sb.AppendLine("**Usage**:");
        sb.AppendLine("```");
        sb.AppendLine("SearchSymbols(query: \"SymbolName\")");
        sb.AppendLine("SearchSymbols(query: \"MyClass*\", maxResults: 50)");
        sb.AppendLine("```");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Generate helpful response when no workspace is loaded
    /// </summary>
    private static string GetNoWorkspaceHelpResponse()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## No Workspace Loaded");
        sb.AppendLine();
        sb.AppendLine("**Action Required**: Call `LoadWorkspace` first to load a C# solution or project.");
        sb.AppendLine();
        sb.AppendLine("**Usage**:");
        sb.AppendLine("```");
        sb.AppendLine("LoadWorkspace(path: \"path/to/MySolution.sln\")");
        sb.AppendLine("LoadWorkspace(path: \"path/to/MyProject.csproj\")");
        sb.AppendLine("LoadWorkspace(path: \".\")  // Auto-detect in current directory");
        sb.AppendLine("```");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Generate helpful response when no results are found
    /// </summary>
    private static string GetNoResultsHelpResponse(string originalQuery, string searchTerm, bool hadErrors)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## No Results Found");
        sb.AppendLine();
        sb.AppendLine($"**Query**: \"{originalQuery}\"");
        sb.AppendLine();
        sb.AppendLine("**Try**:");
        sb.AppendLine("1. Shorter search term");
        sb.AppendLine("2. Different part of the name");
        sb.AppendLine("3. Wildcards: `*MyTerm*`, `MyClass*`, `*Manager`");
        sb.AppendLine("4. `GetDiagnostics` to check for workspace errors");
        sb.AppendLine();

        return sb.ToString();
    }
}
