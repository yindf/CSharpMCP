using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 继承分析器实现
/// </summary>
public class InheritanceAnalyzer : IInheritanceAnalyzer
{
    private readonly ILogger<InheritanceAnalyzer> _logger;

    public InheritanceAnalyzer(ILogger<InheritanceAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InheritanceTree> GetInheritanceTreeAsync(
        INamedTypeSymbol type,
        Solution solution,
        bool includeDerived,
        int maxDerivedDepth,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting inheritance tree for: {TypeName}", type.ToDisplayString());

        // Get base types
        var baseTypes = GetBaseTypeChain(type);

        // Get interfaces
        var interfaces = type.AllInterfaces
            .Where(i => !i.IsImplicitlyDeclared)
            .ToList();

        // Get derived types
        List<Models.SymbolInfo>? derivedTypes = null;
        if (includeDerived)
        {
            var derivedSymbols = await FindDerivedTypesAsync(type, solution, cancellationToken);
            var derivedSet = new HashSet<INamedTypeSymbol>();

            await CollectDerivedTypesBfsAsync(
                derivedSymbols,
                solution,
                derivedSet,
                maxDerivedDepth,
                0,
                cancellationToken);

            derivedTypes = new List<Models.SymbolInfo>();
            foreach (var derived in derivedSet)
            {
                var info = await ToSymbolInfoAsync(derived, cancellationToken);
                derivedTypes.Add(info);
            }
        }

        // Convert to symbol info
        var baseTypeInfos = new List<Models.SymbolInfo>();
        foreach (var baseType in baseTypes)
        {
            var info = await ToSymbolInfoAsync(baseType, cancellationToken);
            baseTypeInfos.Add(info);
        }

        var interfaceInfos = new List<Models.SymbolInfo>();
        foreach (var iface in interfaces)
        {
            var info = await ToSymbolInfoAsync(iface, cancellationToken);
            interfaceInfos.Add(info);
        }

        var depth = includeDerived ? maxDerivedDepth : 0;

        _logger.LogDebug(
            "Inheritance tree for {TypeName}: {BaseCount} base types, {InterfaceCount} interfaces, {DerivedCount} derived types",
            type.ToDisplayString(), baseTypeInfos.Count, interfaceInfos.Count,
            derivedTypes?.Count ?? 0);

        return new InheritanceTree(
            baseTypeInfos,
            interfaceInfos,
            derivedTypes ?? (IReadOnlyList<Models.SymbolInfo>)Array.Empty<Models.SymbolInfo>(),
            depth
        );
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<INamedTypeSymbol>> FindDerivedTypesAsync(
        INamedTypeSymbol type,
        Solution solution,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Finding derived types for: {TypeName}", type.ToDisplayString());

        // Find all references to the type
        var derivedTypes = new List<INamedTypeSymbol>();

        // Search for all named types in the solution
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                if (semanticModel == null) continue;

                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root == null) continue;

                // Find all type declarations
                var typeDeclarations = root.DescendantNodes()
                    .Where(n => n is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax
                              or Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax
                              or Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax
                              or Microsoft.CodeAnalysis.CSharp.Syntax.RecordDeclarationSyntax);

                foreach (var typeDecl in typeDeclarations)
                {
                    var declaredSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
                    if (declaredSymbol is not INamedTypeSymbol namedType ||
                        namedType.Equals(type))
                    {
                        continue;
                    }

                    // Check if this type derives from our target type
                    if (InheritsFrom(namedType, type))
                    {
                        derivedTypes.Add(namedType);
                    }
                }
            }
        }

        _logger.LogDebug("Found {Count} derived types for {TypeName}",
            derivedTypes.Count, type.ToDisplayString());

        return derivedTypes;
    }

    /// <inheritdoc />
    public IReadOnlyList<INamedTypeSymbol> GetBaseTypeChain(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        var current = type.BaseType;

        while (current != null)
        {
            chain.Add(current);
            current = current.BaseType;
        }

        return chain;
    }

    private async Task CollectDerivedTypesBfsAsync(
        IReadOnlyList<INamedTypeSymbol> currentLevel,
        Solution solution,
        HashSet<INamedTypeSymbol> collected,
        int maxDepth,
        int currentDepth,
        CancellationToken cancellationToken)
    {
        if (currentDepth >= maxDepth || currentLevel.Count == 0)
        {
            return;
        }

        var nextLevel = new List<INamedTypeSymbol>();

        foreach (var type in currentLevel)
        {
            if (collected.Contains(type))
            {
                continue;
            }

            collected.Add(type);

            // Find types that derive from this type
            var derived = await FindDerivedTypesAsync(type, solution, cancellationToken);
            nextLevel.AddRange(derived);
        }

        await CollectDerivedTypesBfsAsync(
            nextLevel,
            solution,
            collected,
            maxDepth,
            currentDepth + 1,
            cancellationToken);
    }

    private bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        var current = type;

        while (current != null)
        {
            if (current.Equals(baseType))
            {
                return true;
            }

            // Check interfaces
            foreach (var iface in current.AllInterfaces)
            {
                if (iface.Equals(baseType))
                {
                    return true;
                }
            }

            current = current.BaseType;
        }

        return false;
    }

    private async Task<Models.SymbolInfo> ToSymbolInfoAsync(
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        var location = type.Locations.FirstOrDefault();
        var symbolLocation = new Models.SymbolLocation(
            location?.SourceTree?.FilePath ?? "",
            location?.GetLineSpan().StartLinePosition.Line + 1 ?? 0,
            location?.GetLineSpan().EndLinePosition.Line + 1 ?? 0,
            location?.GetLineSpan().StartLinePosition.Character + 1 ?? 0,
            location?.GetLineSpan().EndLinePosition.Character + 1 ?? 0
        );

        return new Models.SymbolInfo
        {
            Name = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            Kind = type.TypeKind switch
            {
                TypeKind.Class => Models.SymbolKind.Class,
                TypeKind.Interface => Models.SymbolKind.Interface,
                TypeKind.Struct => Models.SymbolKind.Struct,
                TypeKind.Enum => Models.SymbolKind.Enum,
                TypeKind.Delegate => Models.SymbolKind.Delegate,
                _ => Models.SymbolKind.Unknown
            },
            Accessibility = type.DeclaredAccessibility switch
            {
                Microsoft.CodeAnalysis.Accessibility.Public => Models.Accessibility.Public,
                Microsoft.CodeAnalysis.Accessibility.Internal => Models.Accessibility.Internal,
                Microsoft.CodeAnalysis.Accessibility.Protected => Models.Accessibility.Protected,
                Microsoft.CodeAnalysis.Accessibility.Private => Models.Accessibility.Private,
                Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => Models.Accessibility.ProtectedInternal,
                Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => Models.Accessibility.PrivateProtected,
                _ => Models.Accessibility.NotApplicable
            },
            Namespace = type.ContainingNamespace?.ToDisplayString() ?? "",
            ContainingType = type.ContainingType?.ToDisplayString(
                SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "",
            IsStatic = type.IsStatic,
            IsAbstract = type.IsAbstract,
            Location = symbolLocation
        };
    }
}
