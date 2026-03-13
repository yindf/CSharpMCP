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

namespace CSharpMcp.Server.Tools.HighValue;

[McpServerToolType]
public class GetTypeMembersTool
{
    [McpServerTool, Description("Get all members (methods, properties, fields, events) of a type with optional filtering")]
    public static async Task<string> GetTypeMembers(
        [Description("The name of the type to get members for")] string symbolName,
        IWorkspaceManager workspaceManager,
        ILogger<GetTypeMembersTool> logger,
        CancellationToken cancellationToken,
        [Description("Path to the file containing the type")] string filePath = "",
        [Description("1-based line number near the type declaration")] int lineNumber = 0,
        [Description("Whether to include inherited members")] bool includeInherited = false,
        [Description("Filter by access modifier: public, private, protected, internal, or empty for all")] string accessModifier = "",
        [Description("Filter by member name pattern (supports * wildcard, e.g. 'Get*' or '*Async')")] string pattern = "",
        [Description("Filter by member kind: Method, Property, Field, Event, or empty for all")] string memberKind = "")
    {
        try
        {
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Type Members");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            // 确保工作区是最新的（如果需要会重新加载整个工作区）
            await workspaceManager.EnsureRefreshAsync(cancellationToken);

            logger.LogInformation("Getting type members: {FilePath}:{LineNumber} - {SymbolName}",
                filePath, lineNumber, symbolName);

            var resolved = await SymbolResolver.ResolveSymbolAsync(filePath, lineNumber, symbolName ?? "", workspaceManager, SymbolFilter.Type, cancellationToken);
            if (resolved == null)
            {
                logger.LogWarning("Type not found: {SymbolName}", symbolName ?? "at specified location");
                return MarkdownHelper.BuildSymbolNotFoundResponse(
                    filePath,
                    lineNumber,
                    symbolName,
                    "- Line numbers should point to a class, struct, interface, or enum declaration\n- Use `GetSymbols` first to find valid line numbers for types\n- Or provide a valid `symbolName` parameter");
            }

            var symbol = resolved.Symbol;
            if (symbol is not INamedTypeSymbol type)
            {
                logger.LogWarning("Symbol is not a type: {SymbolName}", symbol.Name);
                return MarkdownHelper.BuildNotATypeResponse(symbol.Name, symbol.Kind.ToString());
            }

            var (members, totalBeforeFilter) = GetMembers(type, includeInherited, accessModifier, pattern, memberKind);

            logger.LogInformation("Retrieved {Count} members for: {TypeName} (filtered from {Total})", members.Count, type.Name, totalBeforeFilter);

            return BuildMembersMarkdown(type, members, totalBeforeFilter, accessModifier, pattern, memberKind);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetTypeMembersTool");
            return GetErrorHelpResponse($"Failed to get type members: {ex.Message}");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Get Type Members",
            message,
            "GetTypeMembers(\n    filePath: \"path/to/File.cs\",\n    lineNumber: 7,  // Line where class is declared\n    symbolName: \"MyClass\"\n)",
            "- `GetTypeMembers(filePath: \"C:/MyProject/Models.cs\", lineNumber: 15, symbolName: \"User\")`\n- `GetTypeMembers(filePath: \"./Controllers.cs\", lineNumber: 42, symbolName: \"BaseController\", includeInherited: true)`"
        );
    }

    private static (List<ISymbol> Members, int TotalBeforeFilter) GetMembers(
        INamedTypeSymbol type,
        bool includeInherited,
        string accessModifier,
        string pattern,
        string memberKind)
    {
        var allMembers = includeInherited
            ? type.AllInterfaces
                .Concat(new[] { type })
                .SelectMany(t => t.GetMembers())
                .Distinct(SymbolEqualityComparer.Default)
            : type.GetMembers();

        var filtered = allMembers
            .Where(m => m.ShouldDisplay())  // Filter property accessors, event accessors, backing fields
            .Where(m => m.Locations.FirstOrDefault()?.IsInMetadata != true);

        // Count total before filtering
        var totalBeforeFilter = filtered.Count();

        // Apply access modifier filter
        if (!string.IsNullOrEmpty(accessModifier))
        {
            var targetAccessibility = ParseAccessibility(accessModifier);
            if (targetAccessibility.HasValue)
            {
                filtered = filtered.Where(m => m.DeclaredAccessibility == targetAccessibility.Value);
            }
        }

        // Apply pattern filter (supports * wildcard)
        if (!string.IsNullOrEmpty(pattern))
        {
            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*") + "$";
            var regex = new System.Text.RegularExpressions.Regex(regexPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            filtered = filtered.Where(m => regex.IsMatch(m.Name));
        }

        // Apply member kind filter
        if (!string.IsNullOrEmpty(memberKind))
        {
            var targetKind = ParseMemberKind(memberKind);
            if (targetKind.HasValue)
            {
                filtered = filtered.Where(m => m.Kind == targetKind.Value);
            }
        }

        return (filtered.ToList(), totalBeforeFilter);
    }

    private static Accessibility? ParseAccessibility(string accessModifier)
    {
        return accessModifier.ToLowerInvariant() switch
        {
            "public" => Accessibility.Public,
            "private" => Accessibility.Private,
            "protected" => Accessibility.Protected,
            "internal" => Accessibility.Internal,
            "protected internal" => Accessibility.ProtectedOrInternal,
            "private protected" => Accessibility.ProtectedAndInternal,
            _ => null
        };
    }

    private static SymbolKind? ParseMemberKind(string memberKind)
    {
        return memberKind.ToLowerInvariant() switch
        {
            "method" => SymbolKind.Method,
            "property" => SymbolKind.Property,
            "field" => SymbolKind.Field,
            "event" => SymbolKind.Event,
            _ => null
        };
    }

    private static string BuildMembersMarkdown(
        INamedTypeSymbol type,
        List<ISymbol> members,
        int totalBeforeFilter,
        string accessModifier,
        string pattern,
        string memberKind)
    {
        var sb = new StringBuilder();
        var typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        sb.AppendLine($"## Type Members: `{typeName}`");
        sb.AppendLine();

        // Show filter info if filters were applied
        var hasFilters = !string.IsNullOrEmpty(accessModifier) || !string.IsNullOrEmpty(pattern) || !string.IsNullOrEmpty(memberKind);
        if (hasFilters)
        {
            var filterParts = new List<string>();
            if (!string.IsNullOrEmpty(accessModifier))
                filterParts.Add($"access: `{accessModifier}`");
            if (!string.IsNullOrEmpty(pattern))
                filterParts.Add($"pattern: `{pattern}`");
            if (!string.IsNullOrEmpty(memberKind))
                filterParts.Add($"kind: `{memberKind}`");

            sb.AppendLine($"**Filters**: {string.Join(", ", filterParts)}");
            sb.AppendLine($"**Showing**: {members.Count} of {totalBeforeFilter} members");
        }
        else
        {
            sb.AppendLine($"**Total**: {members.Count} member{(members.Count != 1 ? "s" : "")}");
        }
        sb.AppendLine();

        // Show filter hint if no results but filters were applied
        if (members.Count == 0 && hasFilters)
        {
            sb.AppendLine("_No members match the specified filters._");
            sb.AppendLine();
            sb.AppendLine("**Tips**:");
            sb.AppendLine("- Try removing or adjusting filter criteria");
            sb.AppendLine("- Use `accessModifier: \"\"` to show all access levels");
            sb.AppendLine("- Use `pattern: \"\"` to show all members");
            return sb.ToString();
        }

        var groupedByKind = members.GroupBy(m => m.GetDisplayKind());

        foreach (var kindGroup in groupedByKind.OrderBy(g => g.Key))
        {
            var title = SymbolExtensions.PluralizeKind(kindGroup.Key);
            sb.AppendLine($"### {title}");
            sb.AppendLine();

            foreach (var member in kindGroup.OrderBy(m => m.GetDisplayName()))
            {
                var displayName = member.GetDisplayName();
                var (startLine, endLine) = member.GetLineRange();
                var displayKind = member.GetDisplayKind();
                var accessibility = member.GetAccessibilityString();
                var signature = member.GetSignature();

                sb.Append($"- **{displayName}**");
                sb.Append($" ({accessibility} {displayKind})");
                if (startLine > 0)
                {
                    sb.Append($" L{startLine}-{endLine}");
                }

                if (!string.IsNullOrEmpty(signature))
                {
                    sb.AppendLine($" - `{signature}`");
                }
                else
                {
                    sb.AppendLine();
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
