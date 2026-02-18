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

/// <summary>
/// get_inheritance_hierarchy 工具 - 获取类型的继承层次结构
/// </summary>
[McpServerToolType]
public class GetInheritanceHierarchyTool
{
    /// <summary>
    /// Get the complete inheritance hierarchy for a type
    /// </summary>
    [McpServerTool, Description("Get the complete inheritance hierarchy for a type including base types, interfaces, and derived types")]
    public static async Task<string> GetInheritanceHierarchy(
        [Description("Path to the file containing the type")] string filePath,
        IWorkspaceManager workspaceManager,
        IInheritanceAnalyzer inheritanceAnalyzer,
        ILogger<GetInheritanceHierarchyTool> logger,
        CancellationToken cancellationToken,
        [Description("1-based line number near the type declaration")] int lineNumber = 0,
        [Description("The name of the type to analyze")] string? symbolName = null,
        [Description("Whether to include derived types in the hierarchy")] bool includeDerived = true,
        [Description("Maximum depth for derived type search (0 = unlimited, default 3)")] int maxDerivedDepth = 3)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required", nameof(filePath));
            }

            // Check workspace state
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Inheritance Hierarchy");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            logger.LogInformation("Getting inheritance hierarchy: {FilePath}:{LineNumber} - {SymbolName}",
                filePath, lineNumber, symbolName);

            // Resolve the type symbol
            var symbol = await ResolveSymbolAsync(filePath, lineNumber, symbolName ?? "", workspaceManager, SymbolFilter.Type, cancellationToken);
            if (symbol == null)
            {
                logger.LogWarning("Type not found: {SymbolName}", symbolName ?? "at specified location");
                return GetNoSymbolFoundHelpResponse(filePath, lineNumber, symbolName);
            }

            if (symbol is not INamedTypeSymbol type)
            {
                logger.LogWarning("Symbol is not a type: {SymbolName}", symbol.Name);
                return GetNotATypeHelpResponse(symbol.Name, symbol.Kind.ToString(), filePath, lineNumber);
            }

            // Get the solution
            var solution = workspaceManager.GetCurrentSolution();

            // Use default max depth of 3 if not specified (0 means not specified in JSON)
            var maxDepth = maxDerivedDepth > 0 ? maxDerivedDepth : 3;

            // Get inheritance tree
            var tree = await inheritanceAnalyzer.GetInheritanceTreeAsync(
                type,
                solution,
                includeDerived,
                maxDepth,
                cancellationToken);

            logger.LogInformation("Retrieved inheritance hierarchy for: {TypeName}", type.Name);

            // Build Markdown directly
            return BuildHierarchyMarkdown(type, tree, includeDerived);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetInheritanceHierarchyTool");
            return GetErrorHelpResponse($"Failed to get inheritance hierarchy: {ex.Message}\n\nStack Trace:\n```\n{ex.StackTrace}\n```\n\nCommon issues:\n- Symbol is not a type (use GetSymbols to find types)\n- Symbol is from an external library\n- Workspace is not loaded (call LoadWorkspace first)");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Get Inheritance Hierarchy",
            message,
            "GetInheritanceHierarchy(\n    filePath: \"path/to/File.cs\",\n    lineNumber: 7,  // Line where class is declared\n    symbolName: \"MyClass\"\n)",
            "- `GetInheritanceHierarchy(filePath: \"C:/MyProject/Models.cs\", lineNumber: 15, symbolName: \"User\")`\n- `GetInheritanceHierarchy(filePath: \"./Controllers.cs\", lineNumber: 42, symbolName: \"BaseController\", includeDerived: true)`"
        );
    }

    private static string BuildHierarchyMarkdown(
        INamedTypeSymbol type,
        InheritanceTree tree,
        bool includeDerived)
    {
        var sb = new StringBuilder();
        var typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        sb.AppendLine($"## Inheritance Hierarchy: `{typeName}`");
        sb.AppendLine();

        // Type kind (use display kind to show record, class, struct, etc.)
        var kind = type.GetNamedTypeKindDisplay();
        sb.AppendLine($"**Kind**: {kind}");
        sb.AppendLine();

        // Base types
        if (tree.BaseTypes.Count > 0)
        {
            sb.AppendLine($"### Base Types ({tree.BaseTypes.Count})");
            sb.AppendLine();
            foreach (var baseType in tree.BaseTypes)
            {
                sb.AppendLine($"- **{baseType.ToDisplayString()}**");
                AppendLocationIfExists(sb, baseType);
            }
            sb.AppendLine();
        }

        // Interfaces
        if (tree.Interfaces.Count > 0)
        {
            sb.AppendLine($"### Interfaces ({tree.Interfaces.Count})");
            sb.AppendLine();
            foreach (var iface in tree.Interfaces)
            {
                sb.AppendLine($"- **{iface.ToDisplayString()}**");
                AppendLocationIfExists(sb, iface);
            }
            sb.AppendLine();
        }

        // Derived types
        if (includeDerived && tree.DerivedTypes.Count > 0)
        {
            sb.AppendLine($"### Derived Types ({tree.DerivedTypes.Count}, depth: {tree.Depth})");
            sb.AppendLine();
            foreach (var derived in tree.DerivedTypes)
            {
                sb.AppendLine($"- **{derived.ToDisplayString()}**");
                AppendLocationIfExists(sb, derived);
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
        sb.AppendLine("GetInheritanceHierarchy(");
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

    private static void AppendLocationIfExists(StringBuilder sb, ISymbol symbol)
    {
        var (startLine, _) = symbol.GetLineRange();
        var filePath = symbol.GetFilePath();
        if (startLine > 0 && !string.IsNullOrEmpty(filePath))
        {
            var fileName = System.IO.Path.GetFileName(filePath);
            sb.AppendLine($"  - `{fileName}:{startLine}`");
        }
    }

    /// <summary>
    /// Resolve a single symbol from file location info
    /// </summary>
    private static async Task<ISymbol?> ResolveSymbolAsync(
        string filePath,
        int lineNumber,
        string symbolName,
        IWorkspaceManager workspaceManager,
        SymbolFilter filter,
        CancellationToken cancellationToken)
    {
        var symbols = await workspaceManager.SearchSymbolsAsync(symbolName, filter, cancellationToken);
        if (!symbols.Any())
        {
            symbols = await workspaceManager.SearchSymbolsWithPatternAsync(symbolName, filter, cancellationToken);
        }

        if (string.IsNullOrEmpty(filePath))
        {
            return symbols.FirstOrDefault();
        }

        return OrderSymbolsByProximity(symbols, filePath, lineNumber).FirstOrDefault();
    }

    private static IEnumerable<ISymbol> OrderSymbolsByProximity(
        IEnumerable<ISymbol> symbols,
        string filePath,
        int lineNumber)
    {
        var filename = System.IO.Path.GetFileName(filePath)?.ToLowerInvariant();
        return symbols.OrderBy(s => s.Locations.Sum(loc =>
            (loc.GetLineSpan().Path.ToLowerInvariant().Contains(filename, StringComparison.InvariantCultureIgnoreCase) == true ? 0 : 10000) +
            Math.Abs(loc.GetLineSpan().StartLinePosition.Line - lineNumber)));
    }
}
