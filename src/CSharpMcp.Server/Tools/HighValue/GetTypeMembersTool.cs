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
using CSharpMcp.Server.Models.Tools;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.HighValue;

/// <summary>
/// get_type_members 工具 - 获取类型的成员
/// </summary>
[McpServerToolType]
public class GetTypeMembersTool
{
    /// <summary>
    /// Get all members of a type
    /// </summary>
    [McpServerTool, Description("Get all members (methods, properties, fields, events) of a type")]
    public static async Task<string> GetTypeMembers(
        GetTypeMembersParams parameters,
        IWorkspaceManager workspaceManager,
        ILogger<GetTypeMembersTool> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            // Check workspace state
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Type Members");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            logger.LogInformation("Getting type members: {FilePath}:{LineNumber} - {SymbolName}",
                parameters.FilePath, parameters.LineNumber, parameters.SymbolName);

            // Resolve the type symbol
            var symbol = await parameters.FindSymbolAsync(
                workspaceManager,
                SymbolFilter.Type,
                cancellationToken);
            if (symbol == null)
            {
                logger.LogWarning("Type not found: {SymbolName}", parameters.SymbolName ?? "at specified location");
                return GetNoSymbolFoundHelpResponse(parameters.FilePath, parameters.LineNumber, parameters.SymbolName);
            }

            if (symbol is not INamedTypeSymbol type)
            {
                logger.LogWarning("Symbol is not a type: {SymbolName}", symbol.Name);
                return GetNotATypeHelpResponse(symbol.Name, symbol.Kind.ToString(), parameters.FilePath, parameters.LineNumber);
            }

            // Get all members as ISymbol
            var members = GetMembers(type, parameters.IncludeInherited);

            logger.LogInformation("Retrieved {Count} members for: {TypeName}", members.Count, type.Name);

            // Build Markdown directly
            return BuildMembersMarkdown(type, members, parameters);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetTypeMembersTool");
            return GetErrorHelpResponse($"Failed to get type members: {ex.Message}\n\nStack Trace:\n```\n{ex.StackTrace}\n```\n\nCommon issues:\n- Symbol is not a type (use GetSymbols to find types)\n- Symbol is from an external library\n- Workspace is not loaded (call LoadWorkspace first)");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Get Type Members - Failed");
        sb.AppendLine();
        sb.AppendLine(message);
        sb.AppendLine();
        sb.AppendLine("**Usage:**");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("GetTypeMembers(");
        sb.AppendLine("    filePath: \"path/to/File.cs\",");
        sb.AppendLine("    lineNumber: 7,  // Line where class is declared");
        sb.AppendLine("    symbolName: \"MyClass\"");
        sb.AppendLine(")");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("**Examples:**");
        sb.AppendLine("- `GetTypeMembers(filePath: \"C:/MyProject/Models.cs\", lineNumber: 15, symbolName: \"User\")`");
        sb.AppendLine("- `GetTypeMembers(filePath: \"./Controllers.cs\", lineNumber: 42, symbolName: \"BaseController\", includeInherited: true)`");
        sb.AppendLine();
        return sb.ToString();
    }

    private static List<ISymbol> GetMembers(
        INamedTypeSymbol type,
        bool includeInherited)
    {
        var members = new List<ISymbol>();

        // Get all members
        var allMembers = includeInherited
            ? type.AllInterfaces
                .Concat(new[] { type })
                .SelectMany(t => t.GetMembers())
                .Distinct(SymbolEqualityComparer.Default)
            : type.GetMembers();

        foreach (var member in allMembers)
        {
            // Skip implicitly declared members
            if (member.IsImplicitlyDeclared)
            {
                continue;
            }

            // Skip metadata members
            var location = member.Locations.FirstOrDefault();
            if (location?.IsInMetadata == true)
            {
                continue;
            }

            members.Add(member);
        }

        return members;
    }

    private static string BuildMembersMarkdown(
        INamedTypeSymbol type,
        List<ISymbol> members,
        GetTypeMembersParams parameters)
    {
        var sb = new StringBuilder();
        var typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        sb.AppendLine($"## Type Members: `{typeName}`");
        sb.AppendLine();
        sb.AppendLine($"**Total: {members.Count} member{(members.Count != 1 ? "s" : "")}**");
        sb.AppendLine();

        // Group by kind (use display kind for proper grouping of NamedTypes)
        var groupedByKind = members.GroupBy(m => m.GetDisplayKind());

        foreach (var kindGroup in groupedByKind.OrderBy(g => g.Key))
        {
            var title = SymbolExtensions.PluralizeKind(kindGroup.Key);
            sb.AppendLine($"### {title}");
            sb.AppendLine();

            foreach (var member in kindGroup.OrderBy(m => m.GetDisplayName()))
            {
                if (member.IsImplicitlyDeclared)
                    continue;

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
        sb.AppendLine("- Line numbers should point to a class, struct, interface, or enum declaration");
        sb.AppendLine("- Use `GetSymbols` first to find valid line numbers for types");
        sb.AppendLine("- Or provide a valid `symbolName` parameter");
        sb.AppendLine();
        sb.AppendLine("**Usage**:");
        sb.AppendLine("```");
        sb.AppendLine("GetTypeMembers(");
        sb.AppendLine("    filePath: \"path/to/File.cs\",");
        sb.AppendLine("    lineNumber: 7  // Line where class is declared");
        sb.AppendLine(")");
        sb.AppendLine("```");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Generate helpful error response when symbol is not a type
    /// </summary>
    private static string GetNotATypeHelpResponse(string symbolName, string symbolKind, string filePath, int? lineNumber)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Not a Type");
        sb.AppendLine();
        sb.AppendLine($"**Symbol**: `{symbolName}`");
        sb.AppendLine($"**Kind**: {symbolKind}");
        sb.AppendLine();
        sb.AppendLine("The symbol at the specified location is not a class, struct, interface, or enum.");
        sb.AppendLine();
        sb.AppendLine("**Tips**:");
        sb.AppendLine("- Use `GetSymbols` first to find valid type declarations");
        sb.AppendLine("- Ensure the line number points to a type declaration (not a method, property, etc.)");
        sb.AppendLine();
        return sb.ToString();
    }
}
