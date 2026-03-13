using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSharpMcp.Server.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CSharpMcp.Server.Tools.Refactoring;

[McpServerToolType]
public class ChangeSignatureTool
{
    [McpServerTool, Description("Change method signature (add/remove/reorder parameters) and update all call sites across the workspace.")]
    public static async Task<string> ChangeSignature(
        IWorkspaceManager workspaceManager,
        ILogger<ChangeSignatureTool> logger,
        CancellationToken cancellationToken,
        [Description("New parameter list in C# syntax, e.g. 'string name, int age = 0'")] string newParameters,
        [Description("The name of the method to change")] string methodName = "",
        [Description("Path to the file containing the method")] string filePath = "",
        [Description("1-based line number near the method declaration")] int lineNumber = 0,
        [Description("Mapping of old parameter positions to new positions (e.g. '0:1,1:0' swaps first two params). Only needed for reordering.")] string? parameterMapping = null,
        [Description("If true, only preview changes without applying them")] bool previewOnly = false)
    {
        try
        {
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Change Signature");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            if (string.IsNullOrWhiteSpace(newParameters))
            {
                return GetErrorHelpResponse("newParameters cannot be empty. Use empty parentheses '()' to remove all parameters.");
            }

            var solution = workspaceManager.GetCurrentSolution();
            if (solution == null)
            {
                return GetErrorHelpResponse("Solution not available.");
            }

            logger.LogInformation("Changing signature for method: {MethodName}", methodName);

            // Find the method symbol
            var symbol = await SymbolResolver.ResolveSymbolAsync(
                filePath, lineNumber, methodName,
                workspaceManager,
                SymbolFilter.Member,
                cancellationToken);

            if (symbol == null)
            {
                return GetErrorHelpResponse($"Method not found: `{methodName}`");
            }

            if (symbol is not IMethodSymbol methodSymbol)
            {
                return GetErrorHelpResponse($"Symbol `{methodName}` is not a method (found: {symbol.Kind})");
            }

            if (methodSymbol.Locations.Length > 0 && methodSymbol.Locations[0].IsInMetadata)
            {
                return GetErrorHelpResponse($"Cannot change signature of `{methodName}`: it is defined in referenced metadata (external assembly).");
            }

            // Parse the mapping if provided
            Dictionary<int, int>? mapping = null;
            if (!string.IsNullOrEmpty(parameterMapping))
            {
                mapping = ParseParameterMapping(parameterMapping);
            }

            // Get all references (call sites)
            var references = await SymbolFinder.FindReferencesAsync(methodSymbol, solution, cancellationToken);
            var refList = references.ToList();

            // Get the declaration location
            var declarationLocation = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (declarationLocation == null)
            {
                return GetErrorHelpResponse("Could not find method declaration in source.");
            }

            var declarationDoc = solution.GetDocument(declarationLocation.SourceTree);
            if (declarationDoc == null)
            {
                return GetErrorHelpResponse("Could not find document containing method declaration.");
            }

            // Apply changes
            var newSolution = solution;
            var changedFiles = new HashSet<string>();

            // 1. Update method declaration
            newSolution = await UpdateMethodDeclarationAsync(
                newSolution, declarationDoc.Id, methodSymbol, newParameters, logger, cancellationToken);

            if (newSolution != solution)
            {
                changedFiles.Add(declarationDoc.FilePath ?? declarationDoc.Name);
            }

            // 2. Update all call sites and detect delegate usage
            int updatedCalls = 0;
            var callSiteLocations = new List<(string FilePath, int Line)>();
            var delegateUsages = new List<(string FilePath, int Line)>();

            foreach (var refLocation in refList.SelectMany(r => r.Locations))
            {
                var doc = newSolution.GetDocument(refLocation.Document.Id);
                if (doc == null) continue;

                // Collect call site locations for preview
                var loc = refLocation.Location;
                if (loc.IsInSource && loc.SourceTree != null)
                {
                    var line = loc.GetLineSpan().StartLinePosition.Line + 1;
                    var path = doc.FilePath ?? doc.Name;
                    callSiteLocations.Add((path, line));
                }

                // Check if this is a delegate usage (method group conversion)
                var isDelegateUsage = await IsDelegateUsageAsync(
                    newSolution, doc.Id, refLocation, methodSymbol, cancellationToken);

                if (isDelegateUsage)
                {
                    if (loc.IsInSource && loc.SourceTree != null)
                    {
                        var line = loc.GetLineSpan().StartLinePosition.Line + 1;
                        var path = doc.FilePath ?? doc.Name;
                        delegateUsages.Add((path, line));
                    }
                    continue; // Skip delegate usages - they can't be auto-updated
                }

                var updatedSolution = await UpdateCallSiteAsync(
                    newSolution, doc.Id, refLocation, methodSymbol, mapping, logger, cancellationToken);

                if (updatedSolution != newSolution)
                {
                    newSolution = updatedSolution;
                    changedFiles.Add(doc.FilePath ?? doc.Name);
                    updatedCalls++;
                }
            }

            // Preview mode - return without applying changes
            if (previewOnly)
            {
                logger.LogInformation("Preview signature change for method: {MethodName}", methodName);
                return BuildPreviewResponse(methodSymbol, newParameters, callSiteLocations, changedFiles.Count, delegateUsages);
            }

            // Apply changes to workspace and persist to disk
            if (changedFiles.Any())
            {
                var result = await workspaceManager.ApplyChangesAsync(newSolution, cancellationToken);
                if (!result.Success)
                {
                    return GetErrorHelpResponse(result.ErrorMessage ?? "Failed to apply changes.");
                }

                logger.LogInformation("Changed signature of '{MethodName}': {Files} files, {Calls} call sites updated",
                    methodSymbol.Name, result.ChangedFiles.Count, updatedCalls);
            }

            return BuildResponse(methodSymbol, newParameters, changedFiles.Count, updatedCalls, delegateUsages);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error executing ChangeSignatureTool");
            return GetErrorHelpResponse($"Failed to change signature: {ex.Message}");
        }
    }

    private static Dictionary<int, int> ParseParameterMapping(string mapping)
    {
        var result = new Dictionary<int, int>();
        var parts = mapping.Split(',');

        foreach (var part in parts)
        {
            var indices = part.Split(':');
            if (indices.Length == 2 &&
                int.TryParse(indices[0].Trim(), out var oldIndex) &&
                int.TryParse(indices[1].Trim(), out var newIndex))
            {
                result[oldIndex] = newIndex;
            }
        }

        return result;
    }

    private static async Task<Solution> UpdateMethodDeclarationAsync(
        Solution solution,
        DocumentId documentId,
        IMethodSymbol methodSymbol,
        string newParameters,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(documentId);
        if (document == null) return solution;

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) return solution;

        // Find the method declaration node
        var methodNode = root.FindNode(methodSymbol.Locations[0].SourceSpan)
            .AncestorsAndSelf()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (methodNode == null)
        {
            logger.LogWarning("Could not find method declaration node");
            return solution;
        }

        // Parse new parameter list
        var newParamList = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseParameterList(
            $"({newParameters})");

        // Create new method with updated parameter list
        var newMethodNode = methodNode.WithParameterList(newParamList);

        // Replace in tree
        var newRoot = root.ReplaceNode(methodNode, newMethodNode);
        var newDocument = document.WithSyntaxRoot(newRoot);

        return newDocument.Project.Solution;
    }

    private static async Task<Solution> UpdateCallSiteAsync(
        Solution solution,
        DocumentId documentId,
        ReferenceLocation refLocation,
        IMethodSymbol methodSymbol,
        Dictionary<int, int>? mapping,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(documentId);
        if (document == null) return solution;

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) return solution;

        // Find the invocation expression at this location
        var node = root.FindNode(refLocation.Location.SourceSpan);

        var invocation = node.AncestorsAndSelf()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation == null)
        {
            // Could be a constructor call or other usage
            return solution;
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null) return solution;

        // Verify this is a call to our method
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol == null || !SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, methodSymbol))
        {
            // Check if it's the original method (before changes)
            if (symbolInfo.CandidateSymbols.Any(s => SymbolEqualityComparer.Default.Equals(s, methodSymbol)))
            {
                // This is a candidate, proceed with update
            }
            else
            {
                return solution;
            }
        }

        // Get current arguments
        var currentArgs = invocation.ArgumentList.Arguments;

        // If mapping is provided, reorder arguments
        if (mapping != null && mapping.Any())
        {
            var newArgs = new List<Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentSyntax>();
            var argsList = currentArgs.ToList();

            for (int i = 0; i < methodSymbol.Parameters.Length; i++)
            {
                if (mapping.TryGetValue(i, out var sourceIndex) && sourceIndex < argsList.Count)
                {
                    newArgs.Add(argsList[sourceIndex]);
                }
                else if (i < argsList.Count)
                {
                    newArgs.Add(argsList[i]);
                }
            }

            // Add any remaining arguments (for new optional params that might be added)
            for (int i = newArgs.Count; i < argsList.Count; i++)
            {
                newArgs.Add(argsList[i]);
            }

            var newArgList = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ArgumentList(
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.SeparatedList(newArgs));

            var newInvocation = invocation.WithArgumentList(newArgList);
            var newRoot = root.ReplaceNode(invocation, newInvocation);
            var newDocument = document.WithSyntaxRoot(newRoot);

            return newDocument.Project.Solution;
        }

        // No mapping - just return as is (signature change without reordering)
        return solution;
    }

    private static async Task<bool> IsDelegateUsageAsync(
        Solution solution,
        DocumentId documentId,
        ReferenceLocation refLocation,
        IMethodSymbol methodSymbol,
        CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(documentId);
        if (document == null) return false;

        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null) return false;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null) return false;

        // Find the node at this location
        var node = root.FindNode(refLocation.Location.SourceSpan);

        // Check if this is an invocation expression (direct call)
        var invocation = node.AncestorsAndSelf()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation != null)
        {
            // Check if the method is actually being invoked (not just referenced as identifier)
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, methodSymbol))
            {
                return false; // Direct invocation, not a delegate usage
            }
        }

        // Check for method group conversion (delegate usage)
        // This happens when a method is assigned to a delegate variable, passed as argument, etc.
        var identifierName = node as Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax
            ?? node.AncestorsAndSelf()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>()
                .FirstOrDefault();

        if (identifierName != null)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(identifierName);
            if (SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, methodSymbol))
            {
                // Check if parent is NOT an invocation (then it's a delegate usage)
                var parent = identifierName.Parent;
                if (parent is not Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax)
                {
                    // Could be: assignment to delegate, method argument, etc.
                    return true;
                }
            }
        }

        return false;
    }

    private static string BuildResponse(IMethodSymbol method, string newParameters, int filesChanged, int callsUpdated, List<(string FilePath, int Line)> delegateUsages)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Signature Changed");
        sb.AppendLine();

        sb.AppendLine($"**Method**: `{method.Name}`");
        sb.AppendLine($"**Old Parameters**: `{FormatParameters(method.Parameters)}`");
        sb.AppendLine($"**New Parameters**: `({newParameters})`");
        sb.AppendLine();
        sb.AppendLine($"- **Files Modified**: {filesChanged}");
        sb.AppendLine($"- **Call Sites Updated**: {callsUpdated}");

        // Show delegate usage warnings
        if (delegateUsages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"⚠️ **Warning: {delegateUsages.Count} Delegate Usage(s) Detected**");
            sb.AppendLine();
            sb.AppendLine("This method is used as a delegate (e.g., passed as `Action<T>` or `Func<T>`). These usages cannot be auto-updated and may cause compilation errors:");
            sb.AppendLine();

            foreach (var (filePath, line) in delegateUsages.Take(5))
            {
                sb.AppendLine($"- `{GetDisplayPath(filePath)}`: L{line}");
            }

            if (delegateUsages.Count > 5)
            {
                sb.AppendLine($"- ... and {delegateUsages.Count - 5} more");
            }

            sb.AppendLine();
            sb.AppendLine("> **Action Required**: Manually update delegate usages to match the new signature.");
        }

        sb.AppendLine();
        sb.AppendLine("> **Note**: If you removed parameters, call sites now use default values if available, or may have compilation errors if not.");

        return sb.ToString();
    }

    private static string BuildPreviewResponse(IMethodSymbol method, string newParameters, List<(string FilePath, int Line)> callSites, int filesAffected, List<(string FilePath, int Line)> delegateUsages)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Preview: Change Signature");
        sb.AppendLine();
        sb.AppendLine("> **Preview Mode**: No changes will be applied. Use `previewOnly: false` to apply changes.");
        sb.AppendLine();

        sb.AppendLine($"**Method**: `{method.Name}`");
        sb.AppendLine($"**Old Parameters**: `{FormatParameters(method.Parameters)}`");
        sb.AppendLine($"**New Parameters**: `({newParameters})`");
        sb.AppendLine();
        sb.AppendLine($"- **Files to Modify**: {filesAffected}");
        sb.AppendLine($"- **Call Sites**: {callSites.Count}");

        // Show delegate usage warnings
        if (delegateUsages.Count > 0)
        {
            sb.AppendLine($"- ⚠️ **Delegate Usages**: {delegateUsages.Count} (cannot auto-update)");
        }

        if (callSites.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Call Sites**:");

            var groupedByFile = callSites
                .GroupBy(c => c.FilePath)
                .OrderBy(g => g.Key)
                .Take(10);

            foreach (var group in groupedByFile)
            {
                var displayPath = GetDisplayPath(group.Key);
                var lines = group.Select(c => c.Line).OrderBy(l => l).Take(5);
                var lineStr = string.Join(", ", lines);
                if (group.Count() > 5) lineStr += $" (+{group.Count() - 5} more)";
                sb.AppendLine($"- `{displayPath}`: L{lineStr}");
            }

            var distinctFiles = callSites.Select(c => c.FilePath).Distinct().Count();
            if (distinctFiles > 10)
            {
                sb.AppendLine($"- ... and {distinctFiles - 10} more files");
            }
        }

        // Show delegate usage details
        if (delegateUsages.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("⚠️ **Delegate Usages (Require Manual Update)**:");
            sb.AppendLine();
            sb.AppendLine("This method is used as a delegate. These cannot be auto-updated:");

            foreach (var (filePath, line) in delegateUsages.Take(5))
            {
                sb.AppendLine($"- `{GetDisplayPath(filePath)}`: L{line}");
            }

            if (delegateUsages.Count > 5)
            {
                sb.AppendLine($"- ... and {delegateUsages.Count - 5} more");
            }
        }

        sb.AppendLine();
        sb.AppendLine("> **Warning**: If you remove required parameters, call sites may have compilation errors.");

        return sb.ToString();
    }

    private static string GetDisplayPath(string fullPath)
    {
        if (fullPath.Contains("src/"))
            return fullPath.Substring(fullPath.IndexOf("src/") + 4);
        if (fullPath.Contains("tests/"))
            return fullPath.Substring(fullPath.IndexOf("tests/") + 6);
        return fullPath;
    }

    private static string FormatParameters(IReadOnlyList<IParameterSymbol> parameters)
    {
        if (!parameters.Any()) return "()";

        var parts = parameters.Select(p =>
        {
            var modifier = p.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => ""
            };

            var defaultValue = p.HasExplicitDefaultValue
                ? $" = {FormatDefaultValue(p.ExplicitDefaultValue)}"
                : "";

            return $"{modifier}{p.Type.ToDisplayString()} {p.Name}{defaultValue}";
        });

        return $"({string.Join(", ", parts)})";
    }

    private static string FormatDefaultValue(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? "null"
        };
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Change Signature",
            message,
            "ChangeSignature(\n    newParameters: \"string name, int age = 0\",\n    methodName: \"MyMethod\",\n    filePath: \"path/to/File.cs\",\n    lineNumber: 42\n)",
            "- `ChangeSignature(newParameters: \"int x, int y\", methodName: \"Calculate\", filePath: \"Math.cs\", lineNumber: 15)`\n" +
            "- `ChangeSignature(newParameters: \"string s\", methodName: \"Process\", parameterMapping: \"1:0,0:1\")` // Swap params"
        );
    }
}
