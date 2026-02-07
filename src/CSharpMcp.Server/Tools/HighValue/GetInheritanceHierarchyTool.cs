using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Models.Output;
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
    [McpServerTool]
    public static async Task<InheritanceHierarchyResponse> GetInheritanceHierarchy(
        GetInheritanceHierarchyParams parameters,
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
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

            logger.LogDebug("Getting inheritance hierarchy: {FilePath}:{LineNumber} - {SymbolName}",
                parameters.FilePath, parameters.LineNumber, parameters.SymbolName);

            // Resolve the type symbol
            var (symbol, document) = await ResolveSymbolAsync(parameters, workspaceManager, symbolAnalyzer, cancellationToken);
            if (symbol == null)
            {
                logger.LogWarning("Type not found: {SymbolName}", parameters.SymbolName ?? "at specified location");
                throw new FileNotFoundException($"Type not found: {parameters.SymbolName ?? "at specified location"}");
            }

            if (symbol is not INamedTypeSymbol type)
            {
                logger.LogWarning("Symbol is not a type: {SymbolName}", symbol.Name);
                throw new ArgumentException($"Symbol '{symbol.Name}' is not a type");
            }

            // Get the solution
            var solution = document.Project.Solution;

            // Get inheritance tree
            var tree = await inheritanceAnalyzer.GetInheritanceTreeAsync(
                type,
                solution,
                parameters.IncludeDerived,
                parameters.MaxDerivedDepth,
                cancellationToken);

            // Build response
            var hierarchyData = new InheritanceHierarchyData(
                TypeName: type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Kind: type.TypeKind switch
                {
                    TypeKind.Class => Models.SymbolKind.Class,
                    TypeKind.Interface => Models.SymbolKind.Interface,
                    TypeKind.Struct => Models.SymbolKind.Struct,
                    TypeKind.Enum => Models.SymbolKind.Enum,
                    TypeKind.Delegate => Models.SymbolKind.Delegate,
                    _ => Models.SymbolKind.Unknown
                },
                BaseTypes: tree.BaseTypes.Select(b => b.Name).ToList(),
                Interfaces: tree.Interfaces.Select(i => i.Name).ToList(),
                DerivedTypes: tree.DerivedTypes,
                Depth: tree.Depth
            );

            logger.LogDebug("Retrieved inheritance hierarchy for: {TypeName}", type.Name);

            return new InheritanceHierarchyResponse(type.Name, hierarchyData);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetInheritanceHierarchyTool");
            throw;
        }
    }

    private static async Task<(ISymbol? symbol, Document document)> ResolveSymbolAsync(
        GetInheritanceHierarchyParams parameters,
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        CancellationToken cancellationToken)
    {
        var document = await workspaceManager.GetDocumentAsync(parameters.FilePath, cancellationToken);
        if (document == null)
        {
            return (null, null!);
        }

        ISymbol? symbol = null;

        // Try by position
        if (parameters.LineNumber.HasValue)
        {
            symbol = await symbolAnalyzer.ResolveSymbolAtPositionAsync(
                document,
                parameters.LineNumber.Value,
                1,
                cancellationToken);

            // If we found a symbol but it's not a type, try to get the containing type
            if (symbol is not INamedTypeSymbol && symbol != null)
            {
                symbol = symbol.ContainingType;
            }
        }

        // Try by name
        if (symbol == null && !string.IsNullOrEmpty(parameters.SymbolName))
        {
            var symbols = await symbolAnalyzer.FindSymbolsByNameAsync(
                document,
                parameters.SymbolName,
                parameters.LineNumber,
                cancellationToken);

            // Prefer named type symbols
            symbol = symbols.OfType<INamedTypeSymbol>().FirstOrDefault()
                     ?? symbols.FirstOrDefault();
        }

        return (symbol, document);
    }
}
