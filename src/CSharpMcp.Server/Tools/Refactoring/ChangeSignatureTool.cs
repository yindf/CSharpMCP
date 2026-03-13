using System;
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
            var resolved = await SymbolResolver.ResolveSymbolAsync(
                filePath, lineNumber, methodName,
                workspaceManager,
                SymbolFilter.Member,
                cancellationToken);

            if (resolved == null)
            {
                return GetErrorHelpResponse($"Method not found: `{methodName}`");
            }

            var symbol = resolved.Symbol;
            if (symbol is not IMethodSymbol methodSymbol)
            {
                return GetErrorHelpResponse($"Symbol `{methodName}` is not a method (found: {symbol.Kind})");
            }

            if (methodSymbol.Locations.Length > 0 && methodSymbol.Locations[0].IsInMetadata)
            {
                return GetErrorHelpResponse($"Cannot change signature of `{methodName}`: it is defined in referenced metadata (external assembly).");
            }

            // Parse the mapping if provided, otherwise auto-detect from parameter names
            Dictionary<int, int>? mapping = null;
            if (!string.IsNullOrEmpty(parameterMapping))
            {
                mapping = ParseParameterMapping(parameterMapping);
            }
            else
            {
                // Auto-detect parameter reordering by matching parameter names
                mapping = AutoDetectParameterMapping(methodSymbol.Parameters, newParameters);
                if (mapping != null && mapping.Count > 0)
                {
                    logger.LogInformation("Auto-detected parameter mapping: {Mapping}",
                        string.Join(", ", mapping.Select(kv => $"{kv.Key}->{kv.Value}")));
                }
            }

            // Get all references (call sites) in ORIGINAL solution BEFORE any modifications
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

            // Analyze all references in ORIGINAL solution to detect delegate usages FIRST
            var delegateUsages = new List<(string FilePath, int Line)>();
            var callSiteUpdates = new List<(DocumentId DocId, ReferenceLocation RefLocation, string FilePath, int Line)>();

            foreach (var refLocation in refList.SelectMany(r => r.Locations))
            {
                var loc = refLocation.Location;
                if (!loc.IsInSource || loc.SourceTree == null) continue;

                var line = loc.GetLineSpan().StartLinePosition.Line + 1;
                var doc = solution.GetDocument(refLocation.Document.Id);
                if (doc == null) continue;

                var path = doc.FilePath ?? doc.Name;

                // Check if this is a delegate usage in ORIGINAL solution (symbol matching works here)
                var isDelegateUsage = await IsDelegateUsageAsync(
                    solution, doc.Id, refLocation, methodSymbol, cancellationToken);

                if (isDelegateUsage)
                {
                    delegateUsages.Add((path, line));
                }
                else
                {
                    callSiteUpdates.Add((doc.Id, refLocation, path, line));
                }
            }

            // Preview mode - return without applying changes
            if (previewOnly)
            {
                logger.LogInformation("Preview signature change for method: {MethodName}", methodName);
                var callSiteLocations = callSiteUpdates.Select(u => (u.FilePath, u.Line)).ToList();

                // Get sample transformations
                var samples = await GetSampleTransformationsAsync(
                    solution, callSiteUpdates.Take(3).ToList(), methodSymbol, mapping, cancellationToken);

                return BuildPreviewResponse(methodSymbol, newParameters, callSiteLocations, callSiteUpdates.Count + 1, delegateUsages, workspaceManager.WorkspacePath, samples);
            }

            // Apply changes to working solution
            var workingSolution = solution;
            var changedFiles = new HashSet<string>();
            int updatedCalls = 0;

            // 1. FIRST: Update all NON-DELEGATE call sites (in original solution context)
            foreach (var (docId, refLocation, path, line) in callSiteUpdates)
            {
                var updatedSolution = await UpdateCallSiteAsync(
                    workingSolution, docId, refLocation, methodSymbol, mapping, logger, cancellationToken);

                if (updatedSolution != workingSolution)
                {
                    workingSolution = updatedSolution;
                    changedFiles.Add(path);
                    updatedCalls++;
                }
            }

            // 2. SECOND: Update the method declaration
            // Need to re-get the document from working solution after call site updates
            var updatedDeclarationDoc = workingSolution.GetDocument(declarationDoc.Id);
            if (updatedDeclarationDoc == null)
            {
                return GetErrorHelpResponse("Could not find declaration document after call site updates.");
            }

            workingSolution = await UpdateMethodDeclarationAsync(
                workingSolution, updatedDeclarationDoc.Id, methodSymbol, newParameters, logger, cancellationToken);

            if (workingSolution != solution)
            {
                changedFiles.Add(declarationDoc.FilePath ?? declarationDoc.Name);
            }

            // Apply changes to workspace and persist to disk
            if (changedFiles.Any())
            {
                var result = await workspaceManager.ApplyChangesAsync(workingSolution, cancellationToken);
                if (!result.Success)
                {
                    return GetErrorHelpResponse(result.ErrorMessage ?? "Failed to apply changes.");
                }

                logger.LogInformation("Changed signature of '{MethodName}': {Files} files, {Calls} call sites updated",
                    methodSymbol.Name, result.ChangedFiles.Count, updatedCalls);
            }

            return BuildResponse(methodSymbol, newParameters, changedFiles.Count, updatedCalls, delegateUsages, workspaceManager.WorkspacePath);
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

    /// <summary>
    /// Auto-detect parameter reordering by matching parameter names
    /// </summary>
    private static Dictionary<int, int>? AutoDetectParameterMapping(
        IReadOnlyList<IParameterSymbol> oldParameters,
        string newParametersString)
    {
        if (oldParameters.Count == 0)
            return null;

        // Parse new parameter names from the string
        var newParamNames = ParseParameterNames(newParametersString);
        if (newParamNames.Count == 0)
            return null;

        var mapping = new Dictionary<int, int>();
        var oldParamNames = oldParameters.Select(p => p.Name).ToList();

        // For each new position, find the old position of that parameter
        for (int newPos = 0; newPos < newParamNames.Count && newPos < oldParameters.Count; newPos++)
        {
            var newName = newParamNames[newPos];
            var oldPos = oldParamNames.IndexOf(newName);

            if (oldPos >= 0 && oldPos != newPos)
            {
                // Parameter moved from oldPos to newPos
                mapping[oldPos] = newPos;
            }
        }

        // If no reordering detected, return null
        return mapping.Count > 0 ? mapping : null;
    }

    /// <summary>
    /// Parse parameter names from a parameter list string
    /// </summary>
    private static List<string> ParseParameterNames(string parametersString)
    {
        var names = new List<string>();

        // Handle empty parameter list
        if (string.IsNullOrWhiteSpace(parametersString.Trim().Trim('(', ')')))
            return names;

        // Split by comma, handling nested generics and default values
        var parts = SplitParameters(parametersString);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Extract the parameter name (last identifier before any default value)
            var name = ExtractParameterName(trimmed);
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// Split parameter string by commas, respecting nested brackets
    /// </summary>
    private static List<string> SplitParameters(string parametersString)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        int depth = 0;

        foreach (char c in parametersString)
        {
            if (c == ',' && depth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                if (c == '<' || c == '(' || c == '[') depth++;
                if (c == '>' || c == ')' || c == ']') depth--;
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    /// <summary>
    /// Extract parameter name from a parameter declaration
    /// e.g., "string url" -> "url", "Action<ImageDownloadEvent> callback = null" -> "callback"
    /// </summary>
    private static string ExtractParameterName(string paramDecl)
    {
        // Remove default value part
        var withoutDefault = paramDecl.Split('=')[0].Trim();

        // Handle ref/out/in modifiers
        var modifiers = new[] { "ref ", "out ", "in ", "params " };
        foreach (var mod in modifiers)
        {
            if (withoutDefault.StartsWith(mod, StringComparison.OrdinalIgnoreCase))
            {
                withoutDefault = withoutDefault.Substring(mod.Length);
                break;
            }
        }

        // The parameter name is the last word
        var parts = withoutDefault.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return string.Empty;

        // Last part is the name
        return parts[^1].Trim();
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

        // Find the identifier name for this method reference
        // The node itself might be the identifier, or we need to find it in descendants
        var identifierName = node as Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax
            ?? node.DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>()
                .FirstOrDefault();

        if (identifierName == null)
        {
            // Could not find identifier - assume not delegate usage to be safe
            return false;
        }

        // Verify this identifier refers to our method
        var symbolInfo = semanticModel.GetSymbolInfo(identifierName);
        if (!SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, methodSymbol))
        {
            // Symbol doesn't match - this might be a different overload or unrelated
            // Check candidate symbols as well
            if (!symbolInfo.CandidateSymbols.Any(s => SymbolEqualityComparer.Default.Equals(s, methodSymbol)))
            {
                return false;
            }
        }

        // Now check: is this identifier being directly invoked, or used as a delegate?
        // Walk up the parent chain to find if we're inside an invocation expression
        var current = identifierName.Parent;
        while (current != null)
        {
            // If we find an InvocationExpression where our identifier IS the expression being invoked
            if (current is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression == identifierName)
                {
                    return false; // Direct invocation - not a delegate usage
                }
            }

            // Stop at statement level - we've checked enough context
            if (current is Microsoft.CodeAnalysis.CSharp.Syntax.StatementSyntax)
            {
                break;
            }

            current = current.Parent;
        }

        // If we get here, the method reference is NOT a direct invocation
        // It could be:
        // - Passed as argument (method group)
        // - Assigned to delegate variable
        // - Used in += or -= for event subscription
        return true;
    }

    private static async Task<List<(string FilePath, int Line, string Before, string After)>> GetSampleTransformationsAsync(
        Solution solution,
        List<(DocumentId DocId, ReferenceLocation RefLocation, string FilePath, int Line)> callSites,
        IMethodSymbol methodSymbol,
        Dictionary<int, int>? mapping,
        CancellationToken cancellationToken)
    {
        var samples = new List<(string FilePath, int Line, string Before, string After)>();

        foreach (var (docId, refLocation, filePath, line) in callSites)
        {
            var document = solution.GetDocument(docId);
            if (document == null) continue;

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            if (root == null) continue;

            // Find the invocation expression at this location
            var node = root.FindNode(refLocation.Location.SourceSpan);
            var invocation = node.AncestorsAndSelf()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .FirstOrDefault();

            if (invocation == null) continue;

            var before = invocation.ToString();

            // Generate the transformed version
            string after;
            if (mapping != null && mapping.Any())
            {
                // Reorder arguments
                var currentArgs = invocation.ArgumentList.Arguments;
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

                // Add any remaining arguments
                for (int i = newArgs.Count; i < argsList.Count; i++)
                {
                    newArgs.Add(argsList[i]);
                }

                var newArgList = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ArgumentList(
                    Microsoft.CodeAnalysis.CSharp.SyntaxFactory.SeparatedList(newArgs));
                var newInvocation = invocation.WithArgumentList(newArgList);
                after = newInvocation.ToString();
            }
            else
            {
                // No mapping - the transformation is just signature change, arguments stay same
                after = before;
            }

            samples.Add((filePath, line, before, after));
        }

        return samples;
    }

    private static string BuildResponse(IMethodSymbol method, string newParameters, int filesChanged, int callsUpdated, List<(string FilePath, int Line)> delegateUsages, string? workspacePath)
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
                sb.AppendLine($"- `{GetDisplayPath(filePath, workspacePath)}`: L{line}");
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

    private static string BuildPreviewResponse(
        IMethodSymbol method,
        string newParameters,
        List<(string FilePath, int Line)> callSites,
        int filesAffected,
        List<(string FilePath, int Line)> delegateUsages,
        string? workspacePath,
        List<(string FilePath, int Line, string Before, string After)> samples)
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

        // Show sample transformations first
        if (samples.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Sample Transformations");
            sb.AppendLine();

            foreach (var (filePath, line, before, after) in samples)
            {
                var displayPath = GetDisplayPath(filePath, workspacePath);
                sb.AppendLine($"**`{displayPath}:{line}`**");
                sb.AppendLine();
                sb.AppendLine("Before:");
                sb.AppendLine("```csharp");
                sb.AppendLine(before);
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("After:");
                sb.AppendLine("```csharp");
                sb.AppendLine(after);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        if (callSites.Count > 0)
        {
            sb.AppendLine("### All Call Sites");
            sb.AppendLine();

            var groupedByFile = callSites
                .GroupBy(c => c.FilePath)
                .OrderBy(g => g.Key)
                .Take(10);

            foreach (var group in groupedByFile)
            {
                var displayPath = GetDisplayPath(group.Key, workspacePath);
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
                sb.AppendLine($"- `{GetDisplayPath(filePath, workspacePath)}`: L{line}");
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

    private static string GetDisplayPath(string fullPath, string? workspacePath)
        => MarkdownHelper.GetDisplayPath(fullPath, workspacePath);

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
