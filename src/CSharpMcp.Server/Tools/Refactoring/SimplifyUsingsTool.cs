using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.Refactoring;

[McpServerToolType]
public class SimplifyUsingsTool
{
    [McpServerTool, Description("Remove unused using directives and sort remaining ones. Modifies files in place.")]
    public static async Task<string> SimplifyUsings(
        IWorkspaceManager workspaceManager,
        ILogger<SimplifyUsingsTool> logger,
        CancellationToken cancellationToken,
        [Description("Path to specific file to process (null = entire workspace)")] string filePath = null,
        [Description("Whether to sort usings after removing unused ones")] bool sortUsings = true,
        [Description("Whether to place System usings first when sorting")] bool systemUsingsFirst = true)
    {
        try
        {
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Simplify Usings");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            var solution = workspaceManager.GetCurrentSolution();
            if (solution == null)
            {
                return GetErrorHelpResponse("Solution not available.");
            }

            logger.LogInformation("Simplifying usings for: {FilePath}", filePath ?? "entire workspace");

            var results = new List<FileUsingsResult>();
            var newSolution = solution;

            if (!string.IsNullOrEmpty(filePath))
            {
                // Single file mode
                var document = await workspaceManager.GetDocumentAsync(filePath, cancellationToken);
                if (document == null)
                {
                    return GetErrorHelpResponse($"File not found: `{filePath}`");
                }

                var result = await ProcessDocumentAsync(document, sortUsings, systemUsingsFirst, logger, cancellationToken);
                if (result != null)
                {
                    results.Add(result);
                    if (result.Modified)
                    {
                        newSolution = await ApplyUsingsChangesAsync(newSolution, document.Id, result, cancellationToken);
                    }
                }
            }
            else
            {
                // Entire workspace mode
                foreach (var project in workspaceManager.GetProjects())
                {
                    foreach (var document in project.Documents)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var result = await ProcessDocumentAsync(document, sortUsings, systemUsingsFirst, logger, cancellationToken);
                        if (result != null)
                        {
                            results.Add(result);
                            if (result.Modified)
                            {
                                newSolution = await ApplyUsingsChangesAsync(newSolution, document.Id, result, cancellationToken);
                            }
                        }
                    }
                }
            }

            // Apply changes to workspace and persist to disk
            if (results.Any(r => r.Modified))
            {
                var applyResult = await workspaceManager.ApplyChangesAsync(newSolution, cancellationToken);
                if (!applyResult.Success)
                {
                    return GetErrorHelpResponse(applyResult.ErrorMessage ?? "Failed to apply changes.");
                }

                logger.LogInformation("Simplified usings in {Count} files", applyResult.ChangedFiles.Count);
            }

            return BuildResponse(results);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error executing SimplifyUsingsTool");
            return GetErrorHelpResponse($"Failed to simplify usings: {ex.Message}");
        }
    }

    private static async Task<FileUsingsResult?> ProcessDocumentAsync(
        Document document,
        bool sortUsings,
        bool systemUsingsFirst,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) return null;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null) return null;

        // Find all using directives
        var usingDirectives = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
            .ToList();

        if (!usingDirectives.Any())
            return null;

        var unusedUsings = new List<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>();

        // Check each using directive
        foreach (var usingDirective in usingDirectives)
        {
            if (IsUsingUsed(usingDirective, root, semanticModel))
            {
                continue;
            }
            unusedUsings.Add(usingDirective);
        }

        // Build sorted list if needed
        List<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>? sortedUsings = null;
        if (sortUsings && usingDirectives.Count > unusedUsings.Count)
        {
            var remainingUsings = usingDirectives.Except(unusedUsings).ToList();
            sortedUsings = systemUsingsFirst
                ? remainingUsings.OrderBy(u => u.Name?.ToString() ?? "", new SystemFirstComparer()).ToList()
                : remainingUsings.OrderBy(u => u.Name?.ToString() ?? "").ToList();
        }

        return new FileUsingsResult(
            document.FilePath ?? document.Name,
            usingDirectives.Count,
            unusedUsings.Count,
            unusedUsings.Count > 0 || sortedUsings != null,
            unusedUsings.Select(u => u.Name?.ToString() ?? "").ToList(),
            sortedUsings?.Select(u => u.Name?.ToString() ?? "").ToList()
        );
    }

    private static bool IsUsingUsed(
        Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax usingDirective,
        Microsoft.CodeAnalysis.SyntaxNode root,
        SemanticModel semanticModel)
    {
        var namespaceName = usingDirective.Name?.ToString();
        if (string.IsNullOrEmpty(namespaceName))
            return true; // Keep global usings or aliases

        // Get all identifiers in the file
        var identifiers = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>();

        foreach (var identifier in identifiers)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(identifier);
            var symbol = symbolInfo.Symbol;

            if (symbol != null && IsSymbolFromNamespace(symbol, namespaceName))
            {
                return true;
            }

            // Also check candidate symbols (for ambiguous cases)
            foreach (var candidate in symbolInfo.CandidateSymbols)
            {
                if (IsSymbolFromNamespace(candidate, namespaceName))
                {
                    return true;
                }
            }
        }

        // Check for extension methods
        var memberAccesses = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax>();

        foreach (var memberAccess in memberAccesses)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
            var symbol = symbolInfo.Symbol;

            if (symbol is IMethodSymbol method && method.IsExtensionMethod)
            {
                if (IsSymbolFromNamespace(method, namespaceName))
                {
                    return true;
                }
            }
        }

        // Check generic type names in type syntax (e.g., Task in Task<string>)
        var genericNames = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax>();

        foreach (var genericName in genericNames)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(genericName);
            var symbol = symbolInfo.Symbol;

            if (symbol != null && IsSymbolFromNamespace(symbol, namespaceName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a symbol is from the specified namespace (directly or indirectly)
    /// </summary>
    private static bool IsSymbolFromNamespace(ISymbol symbol, string namespaceName)
    {
        // Check the symbol's containing namespace
        var containingNamespace = symbol.ContainingNamespace?.ToDisplayString();
        if (containingNamespace == namespaceName ||
            (containingNamespace?.StartsWith(namespaceName + ".") ?? false))
        {
            return true;
        }

        // For generic types, check the original definition
        if (symbol is INamedTypeSymbol namedType)
        {
            // Check constructed from (e.g., Task<string> -> Task<>)
            if (namedType.IsGenericType && namedType.ConstructedFrom != null)
            {
                var originalNamespace = namedType.ConstructedFrom.ContainingNamespace?.ToDisplayString();
                if (originalNamespace == namespaceName ||
                    (originalNamespace?.StartsWith(namespaceName + ".") ?? false))
                {
                    return true;
                }
            }
        }

        // For methods, check the containing type's namespace
        if (symbol is IMethodSymbol methodSymbol)
        {
            var typeNamespace = methodSymbol.ContainingType?.ContainingNamespace?.ToDisplayString();
            if (typeNamespace == namespaceName ||
                (typeNamespace?.StartsWith(namespaceName + ".") ?? false))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<Solution> ApplyUsingsChangesAsync(
        Solution solution,
        DocumentId documentId,
        FileUsingsResult result,
        CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(documentId);
        if (document == null) return solution;

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) return solution;

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken);

        // Remove unused usings
        var usingDirectives = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
            .ToList();

        foreach (var unused in result.UnusedUsings)
        {
            var nodeToRemove = usingDirectives.FirstOrDefault(u => u.Name?.ToString() == unused);
            if (nodeToRemove != null)
            {
                editor.RemoveNode(nodeToRemove);
            }
        }

        return editor.GetChangedDocument().Project.Solution;
    }

    private static string BuildResponse(List<FileUsingsResult> results)
    {
        var sb = new StringBuilder();

        var modified = results.Where(r => r.Modified).ToList();
        var unchanged = results.Where(r => !r.Modified).ToList();

        sb.AppendLine("## Usings Simplified");
        sb.AppendLine();

        if (modified.Any())
        {
            sb.AppendLine($"**Modified Files**: {modified.Count}");
            sb.AppendLine();

            foreach (var file in modified.Take(20))
            {
                sb.AppendLine($"- `{file.FileName}`: Removed {file.UnusedCount} unused usings");
                if (file.UnusedUsings.Any())
                {
                    sb.AppendLine($"  - Removed: {string.Join(", ", file.UnusedUsings.Take(5))}{(file.UnusedUsings.Count > 5 ? "..." : "")}");
                }
            }

            if (modified.Count > 20)
            {
                sb.AppendLine($"- ... and {modified.Count - 20} more files");
            }
        }
        else
        {
            sb.AppendLine("**No files modified** - all using directives are in use.");
        }

        sb.AppendLine();
        sb.AppendLine($"**Files Scanned**: {results.Count}");
        sb.AppendLine($"**Total Usings Removed**: {results.Sum(r => r.UnusedCount)}");

        return sb.ToString();
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Simplify Usings",
            message,
            "SimplifyUsings()\nSimplifyUsings(filePath: \"path/to/File.cs\")",
            "- `SimplifyUsings()` - Process entire workspace\n- `SimplifyUsings(filePath: \"MyClass.cs\", sortUsings: true)`"
        );
    }

    private record FileUsingsResult(
        string FileName,
        int TotalUsings,
        int UnusedCount,
        bool Modified,
        List<string> UnusedUsings,
        List<string>? SortedUsings
    );

    /// <summary>
    /// Comparer that puts System namespaces first, then alphabetically
    /// </summary>
    private class SystemFirstComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return 1;
            if (y == null) return -1;

            bool xIsSystem = x.StartsWith("System") || x == "System";
            bool yIsSystem = y.StartsWith("System") || y == "System";

            if (xIsSystem && !yIsSystem) return -1;
            if (!xIsSystem && yIsSystem) return 1;

            return string.Compare(x, y, System.StringComparison.Ordinal);
        }
    }
}
