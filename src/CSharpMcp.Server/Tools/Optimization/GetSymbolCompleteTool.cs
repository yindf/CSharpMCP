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

namespace CSharpMcp.Server.Tools.Optimization;

/// <summary>
/// get_symbol_complete 工具 - 整合多个信息源获取完整符号信息
/// 减少API调用次数，一次性返回所有需要的信息
/// </summary>
[McpServerToolType]
public class GetSymbolCompleteTool
{
    /// <summary>
    /// Get complete symbol information from multiple sources in one call
    /// </summary>
    [McpServerTool, Description("Get complete symbol information combining signature, documentation, references, inheritance, and call graph")]
    public static async Task<string> GetSymbolComplete(
        GetSymbolCompleteParams parameters,
        IWorkspaceManager workspaceManager,
        IInheritanceAnalyzer inheritanceAnalyzer,
        ILogger<GetSymbolCompleteTool> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            // Check workspace state
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Symbol Complete");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            logger.LogInformation("Getting complete symbol info: {FilePath}:{LineNumber} - {SymbolName}",
                parameters.FilePath, parameters.LineNumber, parameters.SymbolName);

            // Resolve the symbol
            var symbol = await parameters.FindSymbolAsync(workspaceManager, cancellationToken: cancellationToken);
            if (symbol == null)
            {
                logger.LogWarning("Symbol not found: {SymbolName}", parameters.SymbolName ?? "at specified location");
                return GetErrorHelpResponse($"Symbol not found: `{parameters.SymbolName ?? "at specified location"}`\n\nCommon issues:\n- Symbol name is incorrect\n- File path or line number is wrong\n- Symbol is from an external library");
            }

            // Build complete Markdown
            var result = await BuildCompleteMarkdownAsync(
                symbol,
                parameters,
                workspaceManager.GetCurrentSolution(),
                inheritanceAnalyzer,
                logger,
                cancellationToken);

            logger.LogInformation("Retrieved complete symbol info for: {SymbolName}", symbol.Name);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetSymbolCompleteTool");
            return GetErrorHelpResponse($"Failed to get complete symbol information: {ex.Message}\n\nStack Trace:\n```\n{ex.StackTrace}\n```\n\nCommon issues:\n- Symbol not found in workspace\n- Workspace is not loaded (call LoadWorkspace first)\n- Symbol is from an external library");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Get Symbol Complete",
            message,
            "GetSymbolComplete(\n    filePath: \"path/to/File.cs\",\n    lineNumber: 42,  // Line near the symbol\n    symbolName: \"MyMethod\",\n    includeReferences: true,\n    includeInheritance: false,\n    includeCallGraph: false\n)",
            "- `GetSymbolComplete(filePath: \"C:/MyProject/Service.cs\", lineNumber: 15, symbolName: \"ProcessData\")`\n- `GetSymbolComplete(filePath: \"./Models.cs\", lineNumber: 42, symbolName: \"User\", includeReferences: true, includeInheritance: true)`"
        );
    }

    private static async Task<string> BuildCompleteMarkdownAsync(
        ISymbol symbol,
        GetSymbolCompleteParams parameters,
        Solution solution,
        IInheritanceAnalyzer inheritanceAnalyzer,
        ILogger<GetSymbolCompleteTool> logger,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var displayName = symbol.GetDisplayName();
        var (startLine, endLine) = symbol.GetLineRange();
        var filePath = symbol.GetFilePath();
        var kind = symbol.GetDisplayKind();

        sb.AppendLine($"# Complete Symbol Information: `{displayName}`");
        sb.AppendLine();

        // ========== Basic Info Section ==========
        sb.AppendLine("## Basic Information");
        sb.AppendLine();
        sb.AppendLine($"- **Name**: `{displayName}`");
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

        var signature = symbol.GetSignature();
        if (!string.IsNullOrEmpty(signature))
        {
            sb.AppendLine($"- **Signature**: `{signature}`");
        }

        var modifiers = new List<string>();
        if (symbol.IsStatic) modifiers.Add("static");
        if (symbol.IsVirtual) modifiers.Add("virtual");
        if (symbol.IsOverride) modifiers.Add("override");
        if (symbol.IsAbstract) modifiers.Add("abstract");
        if (modifiers.Count > 0)
        {
            sb.AppendLine($"- **Modifiers**: {string.Join(", ", modifiers)}");
        }

        sb.AppendLine();

        // ========== Documentation Section ==========
        var summary = symbol.GetSummaryComment();
        var fullComment = symbol.GetFullComment();

        if (!string.IsNullOrEmpty(summary) || !string.IsNullOrEmpty(fullComment))
        {
            sb.AppendLine("## Documentation");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(fullComment))
            {
                sb.AppendLine(fullComment);
            }
            else if (!string.IsNullOrEmpty(summary))
            {
                sb.AppendLine(summary);
            }

            sb.AppendLine();
        }

        // ========== Source Code Section ==========
        var isMethod = symbol.Kind == SymbolKind.Method;
        var maxLines = parameters.GetBodyMaxLines();

        if (isMethod)
        {
            var implementation = await symbol.GetFullImplementationAsync(maxLines, cancellationToken);
            if (!string.IsNullOrEmpty(implementation))
            {
                sb.AppendLine("## Source Code");
                sb.AppendLine();

                var totalLines = endLine - startLine + 1;
                if (maxLines < totalLines)
                {
                    sb.AppendLine($"(showing {maxLines} of {totalLines} total lines)");
                    sb.AppendLine();
                }

                sb.AppendLine("```csharp");
                sb.AppendLine(implementation);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        // ========== References Section ==========
        if (parameters.IncludeReferences)
        {
            try
            {
                var referencedSymbols = (await SymbolFinder.FindReferencesAsync(
                    symbol,
                    solution,
                    cancellationToken)).ToImmutableList();

                if (referencedSymbols.Count > 0)
                {
                    sb.AppendLine($"## References (showing first {Math.Min(parameters.GetMaxReferences(), referencedSymbols.Count)} locations)");
                    sb.AppendLine();

                    int shownLocations = 0;
                    foreach (var refSym in referencedSymbols)
                    {
                        if (shownLocations >= parameters.GetMaxReferences())
                            break;

                        foreach (var loc in refSym.Locations)
                        {
                            if (shownLocations >= parameters.GetMaxReferences())
                                break;

                            var refFilePath = loc.Document.FilePath;
                            var refFileName = System.IO.Path.GetFileName(refFilePath);
                            var refLineSpan = loc.Location.GetLineSpan();
                            var refLine = refLineSpan.StartLinePosition.Line + 1;

                            // Extract line text for context
                            var lineText = await MarkdownHelper.ExtractLineTextAsync(loc.Document, refLine, cancellationToken);

                            sb.AppendLine($"- `{refFileName}:{refLine}`");
                            if (!string.IsNullOrEmpty(lineText))
                            {
                                sb.AppendLine($"  - {lineText.Trim()}");
                            }

                            shownLocations++;
                        }
                    }

                    if (shownLocations < referencedSymbols.Sum(r => r.Locations.Count()))
                    {
                        sb.AppendLine($"- ... ({referencedSymbols.Sum(r => r.Locations.Count()) - shownLocations} more references)");
                    }

                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get references for symbol: {SymbolName}", symbol.Name);
            }
        }

        // ========== Inheritance Section ==========
        if (parameters.IncludeInheritance)
        {
            if (symbol is INamedTypeSymbol type)
            {
                try
                {
                    var tree = await inheritanceAnalyzer.GetInheritanceTreeAsync(
                        type,
                        solution,
                        includeDerived: false,
                        maxDerivedDepth: 0,
                        cancellationToken);

                    if (tree.BaseTypes.Count > 0 || tree.Interfaces.Count > 0)
                    {
                        sb.AppendLine("## Inheritance");
                        sb.AppendLine();

                        if (tree.BaseTypes.Count > 0)
                        {
                            sb.AppendLine("### Base Types");
                            foreach (var baseType in tree.BaseTypes)
                            {
                                sb.AppendLine($"- {baseType.ToDisplayString()}");
                            }
                            sb.AppendLine();
                        }

                        if (tree.Interfaces.Count > 0)
                        {
                            sb.AppendLine("### Interfaces");
                            foreach (var iface in tree.Interfaces)
                            {
                                sb.AppendLine($"- {iface.ToDisplayString()}");
                            }
                            sb.AppendLine();
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to get inheritance for symbol: {SymbolName}", symbol.Name);
                }
            }
        }

        // ========== Call Graph Section ==========
        if (parameters.IncludeCallGraph)
        {
            if (symbol is IMethodSymbol method)
            {
                try
                {
                    sb.Append(await method.GetCallGraphMarkdown(solution, 10, 10, cancellationToken));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to get call graph for symbol: {SymbolName}", symbol.Name);
                }
            }
        }

        return sb.ToString();
    }

}
