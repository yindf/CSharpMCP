using System;
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
        GetInheritanceHierarchyParams parameters,
        IWorkspaceManager workspaceManager,
        IInheritanceAnalyzer inheritanceAnalyzer,
        ILogger<GetInheritanceHierarchyTool> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            // Check workspace state
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Inheritance Hierarchy");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            logger.LogInformation("Getting inheritance hierarchy: {FilePath}:{LineNumber} - {SymbolName}",
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

            // Get the solution
            var solution = workspaceManager.GetCurrentSolution();

            // Use default max depth of 3 if not specified (0 means not specified in JSON)
            var maxDepth = parameters.MaxDerivedDepth > 0 ? parameters.MaxDerivedDepth : 3;

            // Get inheritance tree
            var tree = await inheritanceAnalyzer.GetInheritanceTreeAsync(
                type,
                solution,
                parameters.IncludeDerived,
                maxDepth,
                cancellationToken);

            logger.LogInformation("Retrieved inheritance hierarchy for: {TypeName}", type.Name);

            // Build Markdown directly
            return BuildHierarchyMarkdown(type, tree, parameters);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetInheritanceHierarchyTool");
            return GetErrorHelpResponse($"Failed to get inheritance hierarchy: {ex.Message}\n\nStack Trace:\n```\n{ex.StackTrace}\n```\n\nCommon issues:\n- Symbol is not a type (use GetSymbols to find types)\n- Symbol is from an external library\n- Workspace is not loaded (call LoadWorkspace first)");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Get Inheritance Hierarchy - Failed");
        sb.AppendLine();
        sb.AppendLine(message);
        sb.AppendLine();
        sb.AppendLine("**Usage:**");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("GetInheritanceHierarchy(");
        sb.AppendLine("    filePath: \"path/to/File.cs\",");
        sb.AppendLine("    lineNumber: 7,  // Line where class is declared");
        sb.AppendLine("    symbolName: \"MyClass\"");
        sb.AppendLine(")");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("**Examples:**");
        sb.AppendLine("- `GetInheritanceHierarchy(filePath: \"C:/MyProject/Models.cs\", lineNumber: 15, symbolName: \"User\")`");
        sb.AppendLine("- `GetInheritanceHierarchy(filePath: \"./Controllers.cs\", lineNumber: 42, symbolName: \"BaseController\", includeDerived: true)`");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string BuildHierarchyMarkdown(
        INamedTypeSymbol type,
        InheritanceTree tree,
        GetInheritanceHierarchyParams parameters)
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
                var (startLine, endLine) = baseType.GetLineRange();
                var filePath = baseType.GetFilePath();
                sb.AppendLine($"- **{baseType.ToDisplayString()}**");
                // 只在有有效文件路径时才显示位置信息
                if (startLine > 0 && !string.IsNullOrEmpty(filePath))
                {
                    var fileName = System.IO.Path.GetFileName(filePath);
                    sb.AppendLine($"  - `{fileName}:{startLine}`");
                }
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
                var (startLine, endLine) = iface.GetLineRange();
                var filePath = iface.GetFilePath();
                sb.AppendLine($"- **{iface.ToDisplayString()}**");
                // 只在有有效文件路径时才显示位置信息
                if (startLine > 0 && !string.IsNullOrEmpty(filePath))
                {
                    var fileName = System.IO.Path.GetFileName(filePath);
                    sb.AppendLine($"  - `{fileName}:{startLine}`");
                }
            }
            sb.AppendLine();
        }

        // Derived types
        if (parameters.IncludeDerived && tree.DerivedTypes.Count > 0)
        {
            sb.AppendLine($"### Derived Types ({tree.DerivedTypes.Count}, depth: {tree.Depth})");
            sb.AppendLine();
            foreach (var derived in tree.DerivedTypes)
            {
                var (startLine, endLine) = derived.GetLineRange();
                var filePath = derived.GetFilePath();
                sb.AppendLine($"- **{derived.ToDisplayString()}**");
                // 只在有有效文件路径时才显示位置信息
                if (startLine > 0 && !string.IsNullOrEmpty(filePath))
                {
                    var fileName = System.IO.Path.GetFileName(filePath);
                    sb.AppendLine($"  - `{fileName}:{startLine}`");
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
}
