using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Models.Tools;
using CSharpMcp.Server.Roslyn;
using Microsoft.CodeAnalysis.FindSymbols;

namespace CSharpMcp.Server.Tools.Essential;

/// <summary>
/// get_symbols 工具 - 获取文档中的所有符号
/// </summary>
[McpServerToolType]
public class GetSymbolsTool
{
    /// <summary>
    /// Get all symbols in a C# document with optional filtering
    /// </summary>
    /// <param name="parameters">Tool parameters including file path and filters</param>
    /// <param name="workspaceManager">Workspace manager service</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of symbols found in the document</returns>
    [McpServerTool, Description("Get all symbols (classes, methods, properties, etc.) declared in a C# file")]
    public static async Task<string> GetSymbols(
        GetSymbolsParams parameters,
        IWorkspaceManager workspaceManager,
        ILogger<GetSymbolsTool> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (parameters == null)
            {
                logger.LogError("Error executing GetSymbolsTool, parameters == null");
                return GetErrorHelpResponse($"Failed to get symbols: parameters == null");
            }

            // Check workspace state
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Symbols");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            logger.LogDebug("Getting symbols for: {FilePath}", parameters.FilePath);

            var document = await workspaceManager.GetDocumentAsync(parameters.FilePath, cancellationToken);
            if (document == null)
            {
                logger.LogWarning("Document not found: {FilePath}", parameters.FilePath);
                return GetErrorHelpResponse(
                    $"Document not found: `{parameters.FilePath}`\n\nMake sure the file path is correct and the workspace is loaded.");
            }

            var symbols = (await document.GetDeclaredSymbolsAsync(cancellationToken)).ToImmutableList();

            // Apply filters
            symbols = ApplyFilters(symbols, parameters);

            logger.LogDebug("Found {Count} symbols in {FilePath}", symbols.Count, parameters.FilePath);

            logger.LogDebug("Found {Count} symbols in {FilePath}", symbols.Count, parameters.FilePath);

            // Build Markdown directly
            return await BuildSymbolsMarkdownAsync(parameters.FilePath, symbols, parameters, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetSymbolsTool");
            return GetErrorHelpResponse($"Failed to get symbols: {ex.Message}\n\nStack Trace:\n```\n{ex.StackTrace}\n```\n\nCommon issues:\n- File path is incorrect\n- Workspace is not loaded (call LoadWorkspace first)\n- File has compilation errors");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Get Symbols - Failed");
        sb.AppendLine();
        sb.AppendLine(message);
        sb.AppendLine();
        sb.AppendLine("**Usage:**");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("GetSymbols(");
        sb.AppendLine("    filePath: \"path/to/File.cs\",");
        sb.AppendLine("    symbolName: \"ClassName\"");
        sb.AppendLine(")");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("**Examples:**");
        sb.AppendLine("- `GetSymbols(filePath: \"C:/MyProject/MyClass.cs\", symbolName: \"MyClass\")`");
        sb.AppendLine("- `GetSymbols(filePath: \"./Utils.cs\", symbolName: \"Helper\", includeBody: true)`");
        sb.AppendLine();
        return sb.ToString();
    }

    private static async Task<string> BuildSymbolsMarkdownAsync(
        string filePath,
        IReadOnlyList<ISymbol> symbols,
        GetSymbolsParams parameters,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var fileName = System.IO.Path.GetFileName(filePath);

        sb.AppendLine($"## Symbols: {fileName}");
        sb.AppendLine($"**Total: {symbols.Count} symbol{(symbols.Count != 1 ? "s" : "")}**");
        sb.AppendLine();

        foreach (var symbol in symbols.Where(s => !s.IsImplicitlyDeclared))
        {
            var displayName = symbol.GetDisplayName();
            var (startLine, endLine) = symbol.GetLineRange();
            var kind = symbol.GetDisplayKind();

            sb.Append($"- **{displayName}** ({kind}) L{startLine}-{endLine}");

            var signature = symbol.GetSignature();
            if (!string.IsNullOrEmpty(signature))
                sb.Append($" - `{signature}`");

            var summary = symbol.GetSummaryComment();
            if (!string.IsNullOrEmpty(summary))
                sb.Append($" // {summary}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Apply visibility, kind, and local filters to the symbol list
    /// </summary>
    private static ImmutableList<ISymbol> ApplyFilters(ImmutableList<ISymbol> symbols, GetSymbolsParams parameters)
    {
        // Filter out implicitly declared symbols first
        var filtered = symbols.Where(s => !s.IsImplicitlyDeclared);

        // Apply visibility filter
        if (parameters.MinVisibility > Accessibility.Private)
        {
            filtered = FilterByAccessibility(filtered, parameters.MinVisibility);
        }

        // Apply symbol kind filter
        if (parameters.SymbolKinds != null && parameters.SymbolKinds.Length > 0)
        {
            var kinds = parameters.SymbolKinds.Select(ParseSymbolKind).ToHashSet();
            filtered = filtered.Where(s => kinds.Contains(s.Kind));
        }

        // Exclude local variables and parameters
        if (parameters.ExcludeLocal)
        {
            filtered = filtered.Where(s => s.Kind != SymbolKind.Local && s.Kind != SymbolKind.Parameter);
        }

        return filtered.ToImmutableList();
    }

    /// <summary>
    /// Filter symbols by minimum accessibility level
    /// </summary>
    private static IEnumerable<ISymbol> FilterByAccessibility(IEnumerable<ISymbol> symbols, Accessibility minLevel)
    {
        var accessibilityOrder = new[] { Accessibility.Private, Accessibility.Protected, Accessibility.Internal, Accessibility.Public };
        var minIndex = Array.IndexOf(accessibilityOrder, minLevel);

        return symbols.Where(s =>
        {
            var accessibility = s.DeclaredAccessibility;
            var index = Array.IndexOf(accessibilityOrder, accessibility);
            return index >= minIndex || accessibility == Accessibility.ProtectedAndInternal;
        });
    }

    /// <summary>
    /// Parse string to SymbolKind
    /// </summary>
    private static SymbolKind ParseSymbolKind(string kind)
    {
        return Enum.TryParse<SymbolKind>(kind, ignoreCase: true, out var result) ? result : SymbolKind.Namespace;
    }
}
