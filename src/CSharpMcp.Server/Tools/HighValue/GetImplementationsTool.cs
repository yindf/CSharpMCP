using System;
using System.Collections.Generic;
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

namespace CSharpMcp.Server.Tools.HighValue;

[McpServerToolType]
public class GetImplementationsTool
{
    [McpServerTool, Description("Find all implementations of an interface, abstract class, or virtual/abstract method. Returns a list of all types that implement or derive from the specified symbol, or all methods that override/implement a method.")]
    public static async Task<string> GetImplementations(
        [Description("The name of the interface, base class, or method to find implementations for")] string symbolName,
        IWorkspaceManager workspaceManager,
        ILogger<GetImplementationsTool> logger,
        CancellationToken cancellationToken,
        [Description("Path to the file containing the symbol")] string filePath = "",
        [Description("1-based line number near the symbol declaration")] int lineNumber = 0,
        [Description("Filter by symbol kind: NamedType (class/interface/struct/enum), Method, Property")] string symbolKind = "",
        [Description("Maximum number of implementations to return")] int maxResults = 50)
    {
        try
        {
            var workspaceError = WorkspaceErrorHelper.CheckWorkspaceLoaded(workspaceManager, "Get Implementations");
            if (workspaceError != null)
            {
                return workspaceError;
            }

            await workspaceManager.EnsureRefreshAsync(cancellationToken);

            logger.LogInformation("Finding implementations: {FilePath}:{LineNumber} - {SymbolName}",
                filePath, lineNumber, symbolName);

            // Parse symbolKind filter
            SymbolKind? parsedKind = ParseSymbolKind(symbolKind);

            // Get all matching symbols for disambiguation
            var allSymbols = await SymbolResolver.ResolveAllSymbolsAsync(
                filePath, lineNumber, symbolName ?? "", workspaceManager,
                SymbolFilter.TypeAndMember, parsedKind, cancellationToken);

            if (allSymbols.Count == 0)
            {
                logger.LogWarning("Symbol not found: {SymbolName}", symbolName ?? "at specified location");
                return MarkdownHelper.BuildSymbolNotFoundResponse(
                    filePath,
                    lineNumber,
                    symbolName,
                    "- Line numbers should point to an interface, abstract class, or virtual/abstract method declaration\n- Use `SearchSymbols` first to find the symbol\n- Or provide a valid `symbolName` parameter");
            }

            // Handle disambiguation when multiple symbols found
            ResolvedSymbol? selectedResolved;
            if (allSymbols.Count > 1 && !parsedKind.HasValue)
            {
                // Try to auto-select by preference: interface/abstract methods first
                selectedResolved = TrySelectResolvedByImplementationPriority(allSymbols);

                if (selectedResolved == null)
                {
                    // Could not auto-select, show disambiguation list
                    logger.LogInformation("Multiple symbols found for {SymbolName}, showing disambiguation", symbolName);
                    return BuildDisambiguationResponse(symbolName, allSymbols);
                }

                logger.LogInformation("Auto-selected symbol: {SymbolName} ({Kind}) in {File}",
                    selectedResolved.Symbol.Name, selectedResolved.Symbol.Kind, selectedResolved.FilePath);
            }
            else
            {
                selectedResolved = allSymbols.First();
            }

            var symbol = selectedResolved.Symbol;
            var solution = workspaceManager.GetCurrentSolution();

            // Handle based on symbol kind
            if (symbol is INamedTypeSymbol type)
            {
                return await HandleTypeImplementationsAsync(type, selectedResolved, solution, maxResults, logger, cancellationToken);
            }
            else if (symbol is IMethodSymbol method)
            {
                return await HandleMethodImplementationsAsync(method, selectedResolved, solution, maxResults, logger, cancellationToken);
            }
            else if (symbol is IPropertySymbol property)
            {
                return await HandlePropertyImplementationsAsync(property, selectedResolved, solution, maxResults, logger, cancellationToken);
            }
            else
            {
                logger.LogWarning("Symbol is not a type, method, or property: {SymbolName}", symbol.Name);
                return BuildNotSupportedResponse(symbol);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetImplementationsTool");
            return GetErrorHelpResponse($"Failed to find implementations: {ex.Message}");
        }
    }

    private static async Task<string> HandleTypeImplementationsAsync(
        INamedTypeSymbol type,
        ResolvedSymbol resolved,
        Solution solution,
        int maxResults,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var implementations = new List<INamedTypeSymbol>();

        if (type.TypeKind == TypeKind.Interface)
        {
            var found = await SymbolFinder.FindImplementationsAsync(
                type, solution, cancellationToken: cancellationToken);
            implementations.AddRange(found.OfType<INamedTypeSymbol>());
        }
        else if (type.IsAbstract || type.TypeKind == TypeKind.Class)
        {
            var found = await SymbolFinder.FindDerivedClassesAsync(
                type, solution, cancellationToken: cancellationToken);
            implementations.AddRange(found);
        }

        logger.LogInformation("Found {Count} type implementations for: {TypeName}", implementations.Count, type.Name);

        return BuildTypeImplementationsMarkdown(type, resolved, implementations, maxResults);
    }

    private static async Task<string> HandleMethodImplementationsAsync(
        IMethodSymbol method,
        ResolvedSymbol resolved,
        Solution solution,
        int maxResults,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var implementations = new List<IMethodSymbol>();

        // Find all implementations/overrides of this method
        if (method.IsVirtual || method.IsAbstract || method.IsOverride)
        {
            var found = await SymbolFinder.FindImplementationsAsync(
                method, solution, cancellationToken: cancellationToken);
            implementations.AddRange(found.OfType<IMethodSymbol>());
        }
        else if (method.ContainingType.TypeKind == TypeKind.Interface)
        {
            // Interface method - find all implementations
            var found = await SymbolFinder.FindImplementationsAsync(
                method, solution, cancellationToken: cancellationToken);
            implementations.AddRange(found.OfType<IMethodSymbol>());
        }

        logger.LogInformation("Found {Count} method implementations for: {MethodName}", implementations.Count, method.Name);

        return BuildMethodImplementationsMarkdown(method, resolved, implementations, maxResults);
    }

    private static async Task<string> HandlePropertyImplementationsAsync(
        IPropertySymbol property,
        ResolvedSymbol resolved,
        Solution solution,
        int maxResults,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var implementations = new List<IPropertySymbol>();

        // Find all implementations/overrides of this property
        if (property.IsVirtual || property.IsAbstract || property.IsOverride)
        {
            var found = await SymbolFinder.FindImplementationsAsync(
                property, solution, cancellationToken: cancellationToken);
            implementations.AddRange(found.OfType<IPropertySymbol>());
        }
        else if (property.ContainingType.TypeKind == TypeKind.Interface)
        {
            // Interface property - find all implementations
            var found = await SymbolFinder.FindImplementationsAsync(
                property, solution, cancellationToken: cancellationToken);
            implementations.AddRange(found.OfType<IPropertySymbol>());
        }

        logger.LogInformation("Found {Count} property implementations for: {PropertyName}", implementations.Count, property.Name);

        return BuildPropertyImplementationsMarkdown(property, resolved, implementations, maxResults);
    }

    private static string BuildTypeImplementationsMarkdown(
        INamedTypeSymbol type,
        ResolvedSymbol resolved,
        List<INamedTypeSymbol> implementations,
        int maxResults)
    {
        var sb = new StringBuilder();
        var typeName = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var kind = type.TypeKind == TypeKind.Interface ? "Interface" : "Base Class";

        sb.AppendLine($"# Implementations of `{typeName}`");
        sb.AppendLine();
        sb.AppendLine($"**Kind**: {kind} ({type.TypeKind})");
        sb.AppendLine($"**Location**: {MarkdownHelper.FormatFileLocation(resolved.FilePath, resolved.StartLine)}");
        sb.AppendLine();

        // Show other partial definitions if any
        MarkdownHelper.AppendOtherPartialDefinitions(sb, resolved);

        // Summary
        sb.AppendLine($"## Summary");
        sb.AppendLine();
        sb.AppendLine($"Found **{implementations.Count}** implementation{(implementations.Count != 1 ? "s" : "")}");
        sb.AppendLine();

        if (implementations.Count == 0)
        {
            sb.AppendLine("No implementations found.");
            return sb.ToString();
        }

        // Implementation list grouped by namespace
        sb.AppendLine($"## Implementations{(maxResults < implementations.Count ? $" (showing {maxResults} of {implementations.Count})" : "")}");
        sb.AppendLine();

        var byNamespace = implementations
            .GroupBy(i => i.ContainingNamespace?.ToDisplayString() ?? "<global>")
            .OrderBy(g => g.Key);

        var shown = 0;
        foreach (var nsGroup in byNamespace)
        {
            if (shown >= maxResults) break;

            sb.AppendLine($"### `{nsGroup.Key}`");
            sb.AppendLine();

            foreach (var impl in nsGroup)
            {
                if (shown >= maxResults) break;

                var implStartLine = impl.GetLineRange().startLine;
                var implPath = impl.GetFilePath();
                var implName = impl.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

                sb.AppendLine($"- **`{implName}`** | {impl.TypeKind} | {MarkdownHelper.FormatFileLocation(implPath, implStartLine)}");

                shown++;
            }

            sb.AppendLine();
        }

        if (implementations.Count > maxResults)
        {
            sb.AppendLine($"_... and {implementations.Count - maxResults} more implementations_");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildMethodImplementationsMarkdown(
        IMethodSymbol method,
        ResolvedSymbol resolved,
        List<IMethodSymbol> implementations,
        int maxResults)
    {
        var sb = new StringBuilder();
        var methodName = method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var containingType = method.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        sb.AppendLine($"# Implementations of `{method.Name}`");
        sb.AppendLine();
        sb.AppendLine($"**Signature**: `{methodName}`");

        var methodKind = method.IsAbstract ? "Abstract" :
                         method.IsVirtual ? "Virtual" :
                         method.IsOverride ? "Override" :
                         method.ContainingType.TypeKind == TypeKind.Interface ? "Interface" : "Method";
        sb.AppendLine($"**Kind**: {methodKind}");
        sb.AppendLine($"**Containing Type**: `{containingType}`");
        sb.AppendLine($"**Location**: {MarkdownHelper.FormatFileLocation(resolved.FilePath, resolved.StartLine)}");
        sb.AppendLine();

        // Show other partial definitions if any
        MarkdownHelper.AppendOtherPartialDefinitions(sb, resolved);

        // Summary
        sb.AppendLine($"## Summary");
        sb.AppendLine();
        sb.AppendLine($"Found **{implementations.Count}** implementation{(implementations.Count != 1 ? "s" : "")}");
        sb.AppendLine();

        if (implementations.Count == 0)
        {
            sb.AppendLine("No implementations found.");
            sb.AppendLine();
            sb.AppendLine("> **Note**: This method may not be virtual/abstract, or has no overrides.");
            return sb.ToString();
        }

        // Implementation list grouped by type
        sb.AppendLine($"## Implementations{(maxResults < implementations.Count ? $" (showing {maxResults} of {implementations.Count})" : "")}");
        sb.AppendLine();

        var shown = 0;
        foreach (var impl in implementations.Take(maxResults))
        {
            var implType = impl.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var implSignature = impl.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var (implStartLine, _) = impl.GetLineRange();
            var implPath = impl.GetFilePath();

            sb.AppendLine($"### `{implType}`");
            sb.AppendLine();
            sb.AppendLine($"- **Signature**: `{implSignature}`");
            sb.AppendLine($"- **Location**: {MarkdownHelper.FormatFileLocation(implPath, implStartLine)}");
            sb.AppendLine();

            shown++;
        }

        if (implementations.Count > maxResults)
        {
            sb.AppendLine($"_... and {implementations.Count - maxResults} more implementations_");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildPropertyImplementationsMarkdown(
        IPropertySymbol property,
        ResolvedSymbol resolved,
        List<IPropertySymbol> implementations,
        int maxResults)
    {
        var sb = new StringBuilder();
        var propertyName = property.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var containingType = property.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        sb.AppendLine($"# Implementations of `{property.Name}`");
        sb.AppendLine();
        sb.AppendLine($"**Property**: `{propertyName}`");

        var propKind = property.IsAbstract ? "Abstract" :
                       property.IsVirtual ? "Virtual" :
                       property.IsOverride ? "Override" :
                       property.ContainingType.TypeKind == TypeKind.Interface ? "Interface" : "Property";
        sb.AppendLine($"**Kind**: {propKind}");
        sb.AppendLine($"**Containing Type**: `{containingType}`");
        sb.AppendLine($"**Location**: {MarkdownHelper.FormatFileLocation(resolved.FilePath, resolved.StartLine)}");
        sb.AppendLine();

        // Show other partial definitions if any
        MarkdownHelper.AppendOtherPartialDefinitions(sb, resolved);

        // Summary
        sb.AppendLine($"## Summary");
        sb.AppendLine();
        sb.AppendLine($"Found **{implementations.Count}** implementation{(implementations.Count != 1 ? "s" : "")}");
        sb.AppendLine();

        if (implementations.Count == 0)
        {
            sb.AppendLine("No implementations found.");
            return sb.ToString();
        }

        // Implementation list
        sb.AppendLine($"## Implementations{(maxResults < implementations.Count ? $" (showing {maxResults} of {implementations.Count})" : "")}");
        sb.AppendLine();

        var shown = 0;
        foreach (var impl in implementations.Take(maxResults))
        {
            var implType = impl.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var (implStartLine, _) = impl.GetLineRange();
            var implPath = impl.GetFilePath();

            sb.AppendLine($"- **`{implType}.{impl.Name}`** | {MarkdownHelper.FormatFileLocation(implPath, implStartLine)}");

            shown++;
        }

        if (implementations.Count > maxResults)
        {
            sb.AppendLine($"_... and {implementations.Count - maxResults} more implementations_");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildNotSupportedResponse(ISymbol symbol)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Symbol Not Supported");
        sb.AppendLine();
        sb.AppendLine($"Symbol `{symbol.Name}` is a `{symbol.Kind}` which is not supported.");
        sb.AppendLine();
        sb.AppendLine("**Supported symbol kinds:**");
        sb.AppendLine("- Interface - find all classes that implement it");
        sb.AppendLine("- Abstract class - find all derived classes");
        sb.AppendLine("- Virtual/abstract method - find all overrides");
        sb.AppendLine("- Interface method - find all implementations");
        sb.AppendLine("- Virtual/abstract property - find all overrides");
        sb.AppendLine();

        return sb.ToString();
    }

    private static string GetErrorHelpResponse(string message)
    {
        return MarkdownHelper.BuildErrorResponse(
            "Get Implementations",
            message,
            "GetImplementations(\n    symbolName: \"ICommand\",\n    filePath: \"path/to/ICommand.cs\",  // optional\n    lineNumber: 5  // optional\n)",
            "- `GetImplementations(symbolName: \"ICommand\")` - Find all implementations of ICommand interface\n- `GetImplementations(symbolName: \"BaseController\")` - Find all derived controllers\n- `GetImplementations(symbolName: \"Execute\")` - Find all overrides of Execute method\n- `GetImplementations(symbolName: \"Process\", lineNumber: 42)` - Find overrides of Process method"
        );
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
            _ => null
        };
    }

    /// <summary>
    /// Try to auto-select a resolved symbol by implementation priority.
    /// Priority: Interface > Abstract class > Abstract method > Virtual method > Regular method
    /// </summary>
    private static ResolvedSymbol? TrySelectResolvedByImplementationPriority(IReadOnlyList<ResolvedSymbol> resolvedSymbols)
    {
        // First priority: Interfaces (for finding implementations)
        var iface = resolvedSymbols.FirstOrDefault(rs => rs.Symbol is INamedTypeSymbol { TypeKind: TypeKind.Interface });
        if (iface != null) return iface;

        // Second priority: Abstract classes
        var abstractClass = resolvedSymbols.FirstOrDefault(rs => rs.Symbol is INamedTypeSymbol { IsAbstract: true, TypeKind: TypeKind.Class });
        if (abstractClass != null) return abstractClass;

        // Third priority: Abstract methods
        var abstractMethod = resolvedSymbols.FirstOrDefault(rs => rs.Symbol is IMethodSymbol { IsAbstract: true });
        if (abstractMethod != null) return abstractMethod;

        // Fourth priority: Virtual methods
        var virtualMethod = resolvedSymbols.FirstOrDefault(rs => rs.Symbol is IMethodSymbol { IsVirtual: true });
        if (virtualMethod != null) return virtualMethod;

        // Fifth priority: Interface methods
        var interfaceMethod = resolvedSymbols.FirstOrDefault(rs => rs.Symbol is IMethodSymbol method && method.ContainingType?.TypeKind == TypeKind.Interface);
        if (interfaceMethod != null) return interfaceMethod;

        // Sixth priority: Regular types (classes)
        var regularType = resolvedSymbols.FirstOrDefault(rs => rs.Symbol is INamedTypeSymbol { TypeKind: TypeKind.Class });
        if (regularType != null) return regularType;

        // Fallback: Return first resolved symbol
        return resolvedSymbols.FirstOrDefault();
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

                // Add implementation-relevant info
                var implInfo = GetImplementationInfo(sym);

                sb.AppendLine($"- `{sym.Name}` | **{kind}** {implInfo} | {MarkdownHelper.FormatFileLocation(filePath, startLine)}");
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
        sb.AppendLine($"   - `GetImplementations(symbolName: \"{symbolName}\", symbolKind: \"NamedType\")` - for classes/interfaces");
        sb.AppendLine($"   - `GetImplementations(symbolName: \"{symbolName}\", symbolKind: \"Method\")` - for methods");
        sb.AppendLine($"   - `GetImplementations(symbolName: \"{symbolName}\", symbolKind: \"Property\")` - for properties");
        sb.AppendLine();
        sb.AppendLine("2. **Specify file and line number**:");
        sb.AppendLine($"   - `GetImplementations(symbolName: \"{symbolName}\", filePath: \"path/to/File.cs\", lineNumber: 42)`");
        sb.AppendLine();

        return sb.ToString();
    }

    private static string GetImplementationInfo(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Interface } => "[Interface]",
            INamedTypeSymbol { IsAbstract: true } => "[Abstract]",
            INamedTypeSymbol { IsSealed: true } => "[Sealed]",
            INamedTypeSymbol { IsStatic: true } => "[Static]",
            IMethodSymbol { IsAbstract: true } => "[Abstract]",
            IMethodSymbol { IsVirtual: true } => "[Virtual]",
            IMethodSymbol { IsOverride: true } => "[Override]",
            IMethodSymbol m when m.ContainingType?.TypeKind == TypeKind.Interface => "[Interface method]",
            IPropertySymbol { IsAbstract: true } => "[Abstract]",
            IPropertySymbol { IsVirtual: true } => "[Virtual]",
            _ => ""
        };
    }
}
