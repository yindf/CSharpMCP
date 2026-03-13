using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.Optimization;

[McpServerToolType]
public class GetSymbolInfoTool
{
    [McpServerTool, Description("Get complete symbol information for LLM analysis. Returns signature, documentation, source code, type members, references summary, inheritance, and call graph in a single call. If multiple symbols match, returns disambiguation list.")]
    public static async Task<string> GetSymbolInfo(
        [Description("The name of the symbol to analyze")] string symbolName,
        IWorkspaceManager workspaceManager,
        IInheritanceAnalyzer inheritanceAnalyzer,
        ILogger<GetSymbolInfoTool> logger,
        CancellationToken cancellationToken,
        [Description("Path to the file containing the symbol")] string filePath = "",
        [Description("1-based line number near the symbol declaration")] int lineNumber = 0,
        [Description("Filter by symbol kind: NamedType (class/interface/struct/enum), Method, Property, Field, Event")] string symbolKind = "",
        [Description("Maximum lines of source code to include (0 = no limit)")] int maxBodyLines = 100,
        [Description("Maximum number of references to show with context")] int maxReferences = 10,
        [Description("Maximum number of callers/callees in call graph")] int maxCallGraph = 10,
        [Description("Maximum number of derived types to show")] int maxDerivedTypes = 5)
    {
        try
        {
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Symbol Info");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            await workspaceManager.EnsureRefreshAsync(cancellationToken);

            logger.LogInformation("Getting complete symbol info: {FilePath}:{LineNumber} - {SymbolName}",
                filePath, lineNumber, symbolName);

            // Parse symbolKind parameter
            SymbolKind? parsedKind = ParseSymbolKind(symbolKind);

            // Get all matching symbols for disambiguation
            var allSymbols = await SymbolResolver.ResolveAllSymbolsAsync(
                filePath, lineNumber, symbolName ?? "", workspaceManager,
                SymbolFilter.TypeAndMember, parsedKind, cancellationToken);

            if (allSymbols.Count == 0)
            {
                logger.LogWarning("Symbol not found: {SymbolName}", symbolName ?? "at specified location");
                return GetErrorHelpResponse($"Symbol not found: `{symbolName ?? "at specified location"}`");
            }

            // If multiple symbols found, check if we can auto-select based on kind preference
            ISymbol? symbol;
            if (allSymbols.Count > 1 && !parsedKind.HasValue)
            {
                // If filePath was provided, trust proximity ordering (first symbol is best match)
                // ResolvedSymbol is already sorted by file match + line distance
                if (!string.IsNullOrEmpty(filePath))
                {
                    symbol = allSymbols.First().Symbol;
                    logger.LogInformation("Auto-selected symbol by proximity: {SymbolName} in {FilePath}",
                        symbol.Name, allSymbols.First().FilePath);
                }
                else
                {
                    // No filePath - use kind preference (types over members)
                    symbol = allSymbols.FirstOrDefault(rs => rs.Symbol is INamedTypeSymbol)?.Symbol
                          ?? allSymbols.FirstOrDefault(rs => rs.Symbol is IMethodSymbol or IPropertySymbol)?.Symbol
                          ?? allSymbols.First().Symbol;

                    // If we had to make a choice, show disambiguation info
                    // Use SymbolEqualityComparer for proper symbol comparison
                    if (!SymbolEqualityComparer.Default.Equals(symbol, allSymbols.First().Symbol) || allSymbols.Count(rs => rs.Symbol.Kind == symbol.Kind) > 1)
                    {
                        logger.LogInformation("Multiple symbols found for {SymbolName}, showing disambiguation", symbolName);
                        return BuildDisambiguationResponse(symbolName, allSymbols);
                    }
                }
            }
            else
            {
                symbol = allSymbols.First().Symbol;
            }

            var result = await BuildCompleteMarkdownAsync(
                symbol,
                allSymbols.First(),  // Pass resolved symbol for correct location
                maxBodyLines,
                maxReferences,
                maxCallGraph,
                maxDerivedTypes,
                workspaceManager.GetCurrentSolution(),
                inheritanceAnalyzer,
                logger,
                cancellationToken);

            logger.LogInformation("Retrieved complete symbol info for: {SymbolName}", symbol.Name);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetSymbolInfoTool");
            return GetErrorHelpResponse($"Failed to get complete symbol information: {ex.Message}");
        }
    }

    private static SymbolKind? ParseSymbolKind(string? symbolKind)
    {
        if (string.IsNullOrEmpty(symbolKind))
            return null;

        return symbolKind.ToLowerInvariant() switch
        {
            "namedtype" or "class" or "interface" or "struct" or "enum" or "delegate" => SymbolKind.NamedType,
            "method" => SymbolKind.Method,
            "property" => SymbolKind.Property,
            "field" => SymbolKind.Field,
            "event" => SymbolKind.Event,
            _ => null
        };
    }

    private static string BuildDisambiguationResponse(string symbolName, IReadOnlyList<ResolvedSymbol> resolvedSymbols)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Multiple Symbols Found for `{symbolName}`");
        sb.AppendLine();
        sb.AppendLine($"Found {resolvedSymbols.Count} symbols matching this name. Please specify which one:");
        sb.AppendLine();

        // Group by kind
        var byKind = resolvedSymbols.GroupBy(rs => rs.Symbol.Kind).OrderByDescending(g => g.Count());

        foreach (var group in byKind)
        {
            sb.AppendLine($"## {group.Key}s ({group.Count()})");
            sb.AppendLine();

            foreach (var resolved in group.Take(10))
            {
                var sym = resolved.Symbol;
                var startLine = resolved.StartLine;
                var filePath = resolved.FilePath;
                var kind = sym.GetDisplayKind();
                var containingType = sym.GetContainingTypeName();

                sb.AppendLine($"- `{sym.Name}` | **{kind}** | {MarkdownHelper.FormatFileLocation(filePath, startLine)}");
                if (!string.IsNullOrEmpty(containingType))
                    sb.AppendLine($"  - Containing type: `{containingType}`");
            }

            if (group.Count() > 10)
                sb.AppendLine($"- ... ({group.Count() - 10} more)");

            sb.AppendLine();
        }

        sb.AppendLine("## How to Disambiguate");
        sb.AppendLine();
        sb.AppendLine("Use one of these approaches:");
        sb.AppendLine();
        sb.AppendLine("1. **Specify symbol kind**:");
        sb.AppendLine($"   - `GetSymbolInfo(symbolName: \"{symbolName}\", symbolKind: \"NamedType\")` - for classes/interfaces");
        sb.AppendLine($"   - `GetSymbolInfo(symbolName: \"{symbolName}\", symbolKind: \"Method\")` - for methods");
        sb.AppendLine($"   - `GetSymbolInfo(symbolName: \"{symbolName}\", symbolKind: \"Field\")` - for fields");
        sb.AppendLine($"   - `GetSymbolInfo(symbolName: \"{symbolName}\", symbolKind: \"Property\")` - for properties");
        sb.AppendLine();
        sb.AppendLine("2. **Specify file and line number**:");
        sb.AppendLine($"   - `GetSymbolInfo(symbolName: \"{symbolName}\", filePath: \"path/to/File.cs\", lineNumber: 42)`");
        sb.AppendLine();

        return sb.ToString();
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Get Symbol Info",
            message,
            "GetSymbolInfo(\n    symbolName: \"MyMethod\",\n    filePath: \"path/to/File.cs\",  // optional\n    lineNumber: 42  // optional\n)",
            "- `GetSymbolInfo(symbolName: \"ProcessData\")`\n- `GetSymbolInfo(symbolName: \"User\", filePath: \"./Models.cs\", lineNumber: 15)`"
        );
    }

    private static async Task<string> BuildCompleteMarkdownAsync(
        ISymbol symbol,
        ResolvedSymbol resolved,
        int maxBodyLines,
        int maxReferences,
        int maxCallGraph,
        int maxDerivedTypes,
        Solution solution,
        IInheritanceAnalyzer inheritanceAnalyzer,
        ILogger<GetSymbolInfoTool> logger,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var displayName = symbol.GetDisplayName();
        var startLine = resolved.StartLine;
        var endLine = resolved.EndLine;
        var filePath = resolved.FilePath;
        var kind = symbol.GetDisplayKind();

        // Header with concise location
        sb.AppendLine($"# `{displayName}`");
        sb.AppendLine();
        sb.AppendLine($"**{kind}** | `{symbol.GetAccessibilityString()}` | {MarkdownHelper.FormatFileLocation(filePath, startLine)}");
        sb.AppendLine();

        // Signature
        var signature = symbol.GetSignature();
        if (!string.IsNullOrEmpty(signature))
        {
            sb.AppendLine("## Signature");
            sb.AppendLine();
            sb.AppendLine("```csharp");
            sb.AppendLine(signature);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Modifiers
        var modifiers = new List<string>();
        if (symbol.IsStatic) modifiers.Add("static");
        if (symbol.IsVirtual) modifiers.Add("virtual");
        if (symbol.IsOverride) modifiers.Add("override");
        if (symbol.IsAbstract) modifiers.Add("abstract");
        if (symbol.IsSealed) modifiers.Add("sealed");
        if (modifiers.Count > 0)
        {
            sb.AppendLine($"**Modifiers**: `{string.Join("`, `", modifiers)}`");
            sb.AppendLine();
        }

        // Context
        var containingType = symbol.GetContainingTypeName();
        var ns = symbol.GetNamespace();
        if (!string.IsNullOrEmpty(containingType) || !string.IsNullOrEmpty(ns))
        {
            sb.AppendLine("## Context");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(ns))
                sb.AppendLine($"- **Namespace**: `{ns}`");
            if (!string.IsNullOrEmpty(containingType))
                sb.AppendLine($"- **Containing Type**: `{containingType}`");
            sb.AppendLine();
        }

        // Documentation
        var fullComment = symbol.GetFullComment();
        if (!string.IsNullOrEmpty(fullComment))
        {
            sb.AppendLine("## Documentation");
            sb.AppendLine();
            sb.AppendLine(fullComment);
            sb.AppendLine();
        }

        // Type-specific information
        if (symbol is INamedTypeSymbol typeSymbol)
        {
            // Type Members
            if (typeSymbol.TypeKind == TypeKind.Class || typeSymbol.TypeKind == TypeKind.Interface || typeSymbol.TypeKind == TypeKind.Struct)
            {
                sb.AppendLine("## Members");
                sb.AppendLine();
                sb.AppendLine(await GetTypeMembersSummaryAsync(typeSymbol, cancellationToken));
            }

            // Inheritance
            try
            {
                var tree = await inheritanceAnalyzer.GetInheritanceTreeAsync(
                    typeSymbol, solution, includeDerived: true, maxDerivedDepth: 2, cancellationToken);

                if (tree.BaseTypes.Count > 0 || tree.Interfaces.Count > 0 || tree.DerivedTypes.Count > 0)
                {
                    sb.AppendLine("## Inheritance");
                    sb.AppendLine();

                    if (tree.BaseTypes.Count > 0)
                    {
                        sb.AppendLine("**Base Types:**");
                        foreach (var baseType in tree.BaseTypes)
                            sb.AppendLine($"- `{baseType.ToDisplayString()}`");
                        sb.AppendLine();
                    }

                    if (tree.Interfaces.Count > 0)
                    {
                        sb.AppendLine("**Implements:**");
                        foreach (var iface in tree.Interfaces.Take(10))
                            sb.AppendLine($"- `{iface.ToDisplayString()}`");
                        if (tree.Interfaces.Count > 10)
                            sb.AppendLine($"- ... ({tree.Interfaces.Count - 10} more)");
                        sb.AppendLine();
                    }

                    if (tree.DerivedTypes.Count > 0)
                    {
                        sb.AppendLine($"**Derived Types** ({tree.DerivedTypes.Count} total):");
                        foreach (var derived in tree.DerivedTypes.Take(maxDerivedTypes))
                            sb.AppendLine($"- `{derived.ToDisplayString()}`");
                        if (tree.DerivedTypes.Count > maxDerivedTypes)
                            sb.AppendLine($"- ... ({tree.DerivedTypes.Count - maxDerivedTypes} more)");
                        sb.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get inheritance for symbol: {SymbolName}", symbol.Name);
            }
        }

        // Method-specific information
        if (symbol is IMethodSymbol method)
        {
            // Parameters
            if (method.Parameters.Length > 0)
            {
                sb.AppendLine("## Parameters");
                sb.AppendLine();
                foreach (var param in method.Parameters)
                {
                    var defaultValue = param.HasExplicitDefaultValue ? $" = {param.ExplicitDefaultValue?.ToString() ?? "null"}" : "";
                    var refKind = param.RefKind switch
                    {
                        RefKind.Ref => "ref ",
                        RefKind.Out => "out ",
                        RefKind.In => "in ",
                        _ => ""
                    };
                    sb.AppendLine($"- `{refKind}{param.Type.ToDisplayString()} {param.Name}{defaultValue}`");
                }
                sb.AppendLine();
            }

            // Return type
            sb.AppendLine("## Returns");
            sb.AppendLine();
            if (method.ReturnsVoid)
            {
                sb.AppendLine("`void`");
            }
            else
            {
                sb.AppendLine($"`{method.ReturnType.ToDisplayString()}`");
                var returnComment = symbol.GetReturnComment();
                if (!string.IsNullOrEmpty(returnComment))
                {
                    sb.AppendLine();
                    sb.AppendLine(returnComment);
                }
            }
            sb.AppendLine();

            // Call graph using existing method
            try
            {
                sb.Append(await method.GetCallGraphMarkdown(solution, maxCallGraph, maxCallGraph, cancellationToken));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get call graph for symbol: {SymbolName}", symbol.Name);
            }
        }

        // Property-specific information
        if (symbol is IPropertySymbol property)
        {
            sb.AppendLine("## Property Details");
            sb.AppendLine();
            sb.AppendLine($"- **Type**: `{property.Type.ToDisplayString()}`");
            sb.AppendLine($"- **Read**: {(property.GetMethod != null ? "Yes" : "No")}");
            sb.AppendLine($"- **Write**: {(property.SetMethod != null ? (property.SetMethod.IsInitOnly ? "init-only" : "Yes") : "No")}");
            sb.AppendLine();
        }

        // Field-specific information
        if (symbol is IFieldSymbol field)
        {
            sb.AppendLine("## Field Details");
            sb.AppendLine();
            sb.AppendLine($"- **Type**: `{field.Type.ToDisplayString()}`");
            if (field.HasConstantValue)
            {
                sb.AppendLine($"- **Constant Value**: `{field.ConstantValue?.ToString() ?? "null"}`");
            }
            sb.AppendLine();
        }

        // Source code
        var implementation = await symbol.GetFullImplementationAsync(maxBodyLines, cancellationToken);
        if (!string.IsNullOrEmpty(implementation))
        {
            sb.AppendLine("## Source Code");
            sb.AppendLine();

            var totalLines = endLine - startLine + 1;
            if (maxBodyLines > 0 && maxBodyLines < totalLines)
            {
                sb.AppendLine($"Showing {maxBodyLines} of {totalLines} lines:");
                sb.AppendLine();
            }

            sb.AppendLine("```csharp");
            sb.AppendLine(implementation);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Show other partial definitions if any
        MarkdownHelper.AppendOtherPartialDefinitions(sb, resolved);

        // References using existing helper
        try
        {
            var referencedSymbols = (await SymbolFinder.FindReferencesAsync(
                symbol, solution, cancellationToken)).ToImmutableList();

            var totalRefs = referencedSymbols.Sum(r => r.Locations.Count());
            var filesAffected = referencedSymbols
                .SelectMany(r => r.Locations)
                .Select(l => l.Document.FilePath)
                .Distinct()
                .Count();

            sb.AppendLine("## References");
            sb.AppendLine();
            sb.AppendLine($"**Total**: {totalRefs} references in {filesAffected} files");
            sb.AppendLine();

            if (totalRefs > 0)
            {
                // Top files
                var groupedByFile = referencedSymbols
                    .SelectMany(rs => rs.Locations.Select(loc => new { Location = loc, rs.Definition }))
                    .GroupBy(r => r.Location.Document.FilePath)
                    .OrderByDescending(g => g.Count())
                    .Take(5);

                sb.AppendLine("**Top files:**");
                foreach (var fileGroup in groupedByFile)
                {
                    var count = fileGroup.Count();
                    sb.AppendLine($"- {MarkdownHelper.FormatFileLocation(fileGroup.Key, 0)}: {count} ref{(count != 1 ? "s" : "")}");
                }

                if (filesAffected > 5)
                    sb.AppendLine($"- ... ({filesAffected - 5} more files)");

                sb.AppendLine();

                // Sample references with context
                if (maxReferences > 0)
                {
                    sb.AppendLine("**Sample references:**");
                    sb.AppendLine();

                    int shown = 0;
                    foreach (var refSym in referencedSymbols)
                    {
                        if (shown >= maxReferences) break;

                        foreach (var loc in refSym.Locations)
                        {
                            if (shown >= maxReferences) break;

                            var refLineSpan = loc.Location.GetLineSpan();
                            var refLine = refLineSpan.StartLinePosition.Line + 1;

                            var lineText = await MarkdownHelper.ExtractLineTextAsync(loc.Document, refLine, cancellationToken);

                            sb.AppendLine($"- {loc.Location.ToFileNameWithLineNumber()}");
                            if (!string.IsNullOrEmpty(lineText))
                            {
                                sb.AppendLine($"  ```");
                                sb.AppendLine($"  {lineText.Trim()}");
                                sb.AppendLine($"  ```");
                            }

                            shown++;
                        }
                    }

                    if (totalRefs > maxReferences)
                        sb.AppendLine($"- ... ({totalRefs - maxReferences} more references)");

                    sb.AppendLine();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get references for symbol: {SymbolName}", symbol.Name);
        }

        return sb.ToString();
    }

    private static async Task<string> GetTypeMembersSummaryAsync(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        await Task.CompletedTask; // Suppress warning

        var properties = type.GetMembers().OfType<IPropertySymbol>().ToList();
        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary).ToList();
        var fields = type.GetMembers().OfType<IFieldSymbol>()
            .Where(f => !f.IsBackingField()).ToList();
        var events = type.GetMembers().OfType<IEventSymbol>().ToList();
        var nestedTypes = type.GetMembers().OfType<INamedTypeSymbol>().ToList();

        if (properties.Count > 0)
        {
            sb.AppendLine($"### Properties ({properties.Count})");
            foreach (var prop in properties.Take(15))
            {
                var accessors = new List<string>();
                if (prop.GetMethod != null) accessors.Add("get");
                if (prop.SetMethod != null) accessors.Add(prop.SetMethod.IsInitOnly ? "init" : "set");
                var accessorStr = accessors.Count > 0 ? $" {{{string.Join(", ", accessors)}}}" : "";

                sb.AppendLine($"- `{prop.Type.ToDisplayString()} {prop.Name}{accessorStr}`");
            }
            if (properties.Count > 15)
                sb.AppendLine($"- ... ({properties.Count - 15} more)");
            sb.AppendLine();
        }

        if (methods.Count > 0)
        {
            sb.AppendLine($"### Methods ({methods.Count})");
            foreach (var method in methods.Take(15))
            {
                sb.AppendLine($"- `{method.GetSignature()}`");
            }
            if (methods.Count > 15)
                sb.AppendLine($"- ... ({methods.Count - 15} more)");
            sb.AppendLine();
        }

        if (fields.Count > 0)
        {
            sb.AppendLine($"### Fields ({fields.Count})");
            foreach (var field in fields.Take(10))
            {
                var modifier = field.IsStatic && field.IsConst ? "const " : (field.IsStatic ? "static " : "");
                sb.AppendLine($"- `{modifier}{field.Type.ToDisplayString()} {field.Name}`");
            }
            if (fields.Count > 10)
                sb.AppendLine($"- ... ({fields.Count - 10} more)");
            sb.AppendLine();
        }

        if (events.Count > 0)
        {
            sb.AppendLine($"### Events ({events.Count})");
            foreach (var ev in events.Take(10))
            {
                sb.AppendLine($"- `{ev.Type.ToDisplayString()} {ev.Name}`");
            }
            if (events.Count > 10)
                sb.AppendLine($"- ... ({events.Count - 10} more)");
            sb.AppendLine();
        }

        if (nestedTypes.Count > 0)
        {
            sb.AppendLine($"### Nested Types ({nestedTypes.Count})");
            foreach (var nested in nestedTypes)
            {
                sb.AppendLine($"- `{nested.TypeKind} {nested.Name}`");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
