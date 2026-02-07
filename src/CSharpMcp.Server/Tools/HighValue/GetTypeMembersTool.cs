using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Models.Output;
using CSharpMcp.Server.Models.Tools;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.HighValue;

/// <summary>
/// get_type_members 工具 - 获取类型的成员
/// </summary>
[McpServerToolType]
public class GetTypeMembersTool
{
    /// <summary>
    /// Get all members of a type
    /// </summary>
    [McpServerTool]
    public static async Task<GetTypeMembersResponse> GetTypeMembers(
        GetTypeMembersParams parameters,
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger<GetTypeMembersTool> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            logger.LogDebug("Getting type members: {FilePath}:{LineNumber} - {SymbolName}",
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

            // Get all members
            var members = await GetMembersAsync(type, parameters.IncludeInherited, parameters.FilterKinds, cancellationToken);

            // Convert to response format
            var memberItems = members.Select(m => new MemberInfoItem(
                m.Name,
                m.Kind,
                m.Accessibility,
                m.IsStatic,
                m.IsVirtual,
                m.IsOverride,
                m.IsAbstract,
                m.Location
            )).ToList();

            var membersData = new TypeMembersData(
                TypeName: type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                Members: memberItems,
                TotalCount: members.Count
            );

            logger.LogDebug("Retrieved {Count} members for: {TypeName}", members.Count, type.Name);

            return new GetTypeMembersResponse(type.Name, membersData);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing GetTypeMembersTool");
            throw;
        }
    }

    private static async Task<List<Models.SymbolInfo>> GetMembersAsync(
        INamedTypeSymbol type,
        bool includeInherited,
        IReadOnlyList<Models.SymbolKind>? filterKinds,
        CancellationToken cancellationToken)
    {
        var members = new List<Models.SymbolInfo>();

        // Get all members
        var allMembers = includeInherited
            ? type.AllInterfaces
                .Concat(new[] { type })
                .SelectMany(t => t.GetMembers())
                .Distinct(SymbolEqualityComparer.Default)
            : type.GetMembers();

        foreach (var member in allMembers)
        {
            // Skip implicitly declared members
            if (member.IsImplicitlyDeclared)
            {
                continue;
            }

            // Get the symbol kind
            var symbolKind = member.Kind switch
            {
                SymbolKind.Method => Models.SymbolKind.Method,
                SymbolKind.Property => Models.SymbolKind.Property,
                SymbolKind.Field => Models.SymbolKind.Field,
                SymbolKind.Event => Models.SymbolKind.Event,
                SymbolKind.NamedType => member.ToDisplayString().EndsWith("Attribute)") ? Models.SymbolKind.Attribute : Models.SymbolKind.Class,
                _ => Models.SymbolKind.Unknown
            };

            // Apply filter if specified
            if (filterKinds != null && filterKinds.Count > 0 && !filterKinds.Contains(symbolKind))
            {
                continue;
            }

            // Get location
            var location = member.Locations.FirstOrDefault();
            var symbolLocation = new Models.SymbolLocation(
                location?.SourceTree?.FilePath ?? "",
                location?.GetLineSpan().StartLinePosition.Line + 1 ?? 0,
                location?.GetLineSpan().EndLinePosition.Line + 1 ?? 0,
                location?.GetLineSpan().StartLinePosition.Character + 1 ?? 0,
                location?.GetLineSpan().EndLinePosition.Character + 1 ?? 0
            );

            // Skip metadata members
            if (location?.IsInMetadata == true)
            {
                continue;
            }

            var accessibility = member.DeclaredAccessibility switch
            {
                Microsoft.CodeAnalysis.Accessibility.Public => Models.Accessibility.Public,
                Microsoft.CodeAnalysis.Accessibility.Internal => Models.Accessibility.Internal,
                Microsoft.CodeAnalysis.Accessibility.Protected => Models.Accessibility.Protected,
                Microsoft.CodeAnalysis.Accessibility.Private => Models.Accessibility.Private,
                Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => Models.Accessibility.ProtectedInternal,
                Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => Models.Accessibility.PrivateProtected,
                _ => Models.Accessibility.NotApplicable
            };

            // Build signature based on member type
            Models.SymbolSignature? signature = null;
            bool isAsync = false;

            if (member is IMethodSymbol method)
            {
                var methodParams = method.Parameters
                    .Select(param => param.Type?.ToDisplayString() ?? "object")
                    .ToList();

                var typeParameters = method.TypeParameters
                    .Select(tp => tp.Name)
                    .ToList();

                signature = new Models.SymbolSignature(
                    method.Name,
                    method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    method.ReturnType?.ToDisplayString() ?? "void",
                    methodParams,
                    typeParameters
                );
                isAsync = method.IsAsync;
            }
            else if (member is IPropertySymbol property)
            {
                var propertyParams = property.Parameters
                    .Select(param => param.Type?.ToDisplayString() ?? "object")
                    .ToList();

                // Properties don't have TypeParameters
                var typeParameters = Array.Empty<string>();

                signature = new Models.SymbolSignature(
                    property.Name,
                    property.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    property.Type?.ToDisplayString() ?? "object",
                    propertyParams,
                    typeParameters
                );
            }

            var symbolInfo = new Models.SymbolInfo
            {
                Name = member.Name,
                Kind = symbolKind,
                Accessibility = accessibility,
                Namespace = member.ContainingNamespace?.ToDisplayString() ?? "",
                ContainingType = member.ContainingType?.ToDisplayString(
                    SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "",
                IsStatic = member.IsStatic,
                IsVirtual = member.IsVirtual,
                IsOverride = member.IsOverride,
                IsAbstract = member.IsAbstract,
                IsAsync = isAsync,
                Location = symbolLocation,
                Signature = signature
            };

            members.Add(symbolInfo);
        }

        return members;
    }

    private static async Task<(ISymbol? symbol, Document document)> ResolveSymbolAsync(
        GetTypeMembersParams parameters,
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
