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
/// resolve_symbol 工具 - 获取符号的完整信息
/// </summary>
[McpServerToolType]
public class GoToDefinitionTool
{
    /// <summary>
    /// Get comprehensive symbol information including documentation, comments, and context
    /// </summary>
    [McpServerTool, Description("Get comprehensive information about a symbol including signature, documentation, and location")]
    public static async Task<string> GoToDefinition(
        ResolveSymbolParams parameters,
        IWorkspaceManager workspaceManager,
        ILogger<GoToDefinitionTool> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            // Check workspace state
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Go To Definition");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            logger.LogInformation("Resolving symbol: {FilePath}:{LineNumber} - {SymbolName}",
                parameters.FilePath, parameters.LineNumber, parameters.SymbolName);

            // Resolve the symbol
            var symbols = await parameters.ResolveSymbolAsync(workspaceManager, cancellationToken: cancellationToken);
            if (symbols == null || !symbols.Any())
            {
                var errorDetails = await BuildErrorDetails(parameters, workspaceManager, cancellationToken);
                logger.LogWarning("Symbol not found: {Details}", errorDetails);
                return GetErrorHelpResponse(errorDetails);
            }

            // Build Markdown directly
            StringBuilder sb = new StringBuilder();
            foreach (var symbol in symbols)
            {
                var result = await BuildSymbolMarkdownAsync(symbol, parameters, workspaceManager.GetCurrentSolution(), cancellationToken);
                sb.AppendLine(result);

                logger.LogInformation("Resolved symbol: {SymbolName}", symbol.Name);
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing ResolveSymbolTool");
            return GetErrorHelpResponse($"Failed to resolve symbol: {ex.Message}\n\nStack Trace:\n```\n{ex.StackTrace}\n```\n\nCommon issues:\n- Symbol not found in workspace\n- Workspace is not loaded (call LoadWorkspace first)\n- Symbol is from an external library");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Go To Definition",
            message,
            "GoToDefinition(\n    filePath: \"path/to/File.cs\",\n    lineNumber: 42,  // Line near the symbol reference\n    symbolName: \"MyMethod\"\n)",
            "- `GoToDefinition(filePath: \"C:/MyProject/Program.cs\", lineNumber: 15, symbolName: \"MyMethod\")`\n- `GoToDefinition(filePath: \"./Utils.cs\", lineNumber: 42, symbolName: \"Helper\", includeBody: true)`"
        );
    }

    private static async Task<string> BuildSymbolMarkdownAsync(
        ISymbol symbol,
        ResolveSymbolParams parameters,
        Solution solution,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var displayName = symbol.GetDisplayName();
        var (startLine, endLine) = symbol.GetLineRange();
        var filePath = symbol.GetFilePath();
        var kind = symbol.GetDisplayKind();

        sb.AppendLine($"## Symbol: `{displayName}`");
        sb.AppendLine();

        // Basic info
        sb.AppendLine("**Basic Info**:");
        sb.AppendLine($"- **Kind**: {kind}");
        sb.AppendLine($"- **Accessibility**: {symbol.GetAccessibilityString()}");

        var containingType = symbol.GetContainingTypeName();
        if (!string.IsNullOrEmpty(containingType))
            sb.AppendLine($"- **Containing Type**: {containingType}");

        var ns = symbol.GetNamespace();
        if (!string.IsNullOrEmpty(ns))
            sb.AppendLine($"- **Namespace**: {ns}");

        if (startLine > 0)
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            sb.AppendLine($"- **Location**: `{fileName}:{startLine}-{endLine}`");
        }
        sb.AppendLine();

        // Signature
        var signature = symbol.GetSignature();
        if (!string.IsNullOrEmpty(signature))
        {
            sb.AppendLine("**Signature**:");
            sb.AppendLine($"```csharp");
            sb.AppendLine($"{symbol.GetFullDeclaration()}");
            sb.AppendLine($"```");
            sb.AppendLine();
        }

        // Documentation
        var summary = symbol.GetSummaryComment();
        if (!string.IsNullOrEmpty(summary))
        {
            sb.AppendLine("**Documentation**:");
            sb.AppendLine(summary);
            sb.AppendLine();
        }

        // Full implementation
        if (parameters.IncludeBody)
        {
            var maxLines = parameters.GetBodyMaxLines();
            var implementation = await symbol.GetFullImplementationAsync(maxLines, cancellationToken);
            if (!string.IsNullOrEmpty(implementation))
            {
                sb.AppendLine("**Implementation**:");
                sb.AppendLine("```csharp");
                sb.AppendLine(implementation);
                sb.AppendLine("```");

                var totalLines = endLine - startLine + 1;
                if (maxLines < totalLines)
                {
                    sb.AppendLine($"*... {totalLines - maxLines} more lines hidden*");
                }
                sb.AppendLine();
            }
        }

        // References (limited)
        try
        {
            var referencedSymbols = (await SymbolFinder.FindReferencesAsync(
                symbol,
                solution,
                cancellationToken)).ToImmutableList();

            if (referencedSymbols.Count > 0)
            {
                sb.AppendLine($"**References** (showing first {Math.Min(5, referencedSymbols.Count)} of {referencedSymbols.Count}):");
                sb.AppendLine();

                int shownRefs = 0;
                foreach (var refSym in referencedSymbols.Take(5))
                {
                    foreach (var loc in refSym.Locations.Take(2))
                    {
                        var refFilePath = loc.Document.FilePath;
                        var refFileName = System.IO.Path.GetFileName(refFilePath);
                        var refLineSpan = loc.Location.GetLineSpan();
                        var refLine = refLineSpan.StartLinePosition.Line + 1;

                        // Extract line text
                        var lineText = await MarkdownHelper.ExtractLineTextAsync(loc.Document, refLine, cancellationToken);

                        sb.AppendLine($"- `{refFileName}:{refLine}`");
                        if (!string.IsNullOrEmpty(lineText))
                        {
                            sb.AppendLine($"  - {lineText.Trim()}");
                        }
                        shownRefs++;
                    }
                }
                sb.AppendLine();
            }
        }
        catch
        {
            // Ignore reference errors
        }

        return sb.ToString();
    }


    private static async Task<string> BuildErrorDetails(
        ResolveSymbolParams parameters,
        IWorkspaceManager workspaceManager,
        CancellationToken cancellationToken)
    {
        var details = new StringBuilder();
        details.AppendLine("## Symbol Not Found");
        details.AppendLine();
        details.AppendLine($"**File**: `{parameters.FilePath}`");
        details.AppendLine($"**Line Number**: {parameters.LineNumber.ToString() ?? "Not specified"}");
        details.AppendLine($"**Symbol Name**: `{parameters.SymbolName ?? "Not specified"}`");
        details.AppendLine();

        // 尝试读取文件内容显示该行
        try
        {
            var document = workspaceManager.GetCurrentSolution()?.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == parameters.FilePath);

            if (document != null)
            {
                var sourceText = await document.GetTextAsync(cancellationToken);
                if (sourceText != null)
                {
                    var line = sourceText.Lines.FirstOrDefault(l => l.LineNumber == parameters.LineNumber - 1);
                    if (parameters.LineNumber >= 0)
                    {
                        details.AppendLine("**Line Content**:");
                        details.AppendLine("```csharp");
                        details.AppendLine(line.ToString().Trim());
                        details.AppendLine("```");
                        details.AppendLine();
                    }
                }
            }
        }
        catch
        {
            details.AppendLine("**Line Content**: Unable to read file content");
            details.AppendLine();
        }

        details.AppendLine("**Possible Reasons**:");
        details.AppendLine("1. The symbol is defined in an external library (not in this workspace)");
        details.AppendLine("2. The symbol is a built-in C# type or keyword");
        details.AppendLine("3. The file path or line number is incorrect");
        details.AppendLine("4. The workspace needs to be reloaded (try LoadWorkspace again)");

        return details.ToString();
    }
}
