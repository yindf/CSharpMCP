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
}
