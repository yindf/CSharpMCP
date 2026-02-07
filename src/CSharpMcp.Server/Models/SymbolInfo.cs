using Microsoft.CodeAnalysis;

namespace CSharpMcp.Server.Models;

/// <summary>
/// 可访问性
/// </summary>
public enum Accessibility
{
    Public,
    Internal,
    Protected,
    ProtectedInternal,
    PrivateProtected,
    Private,
    NotApplicable
}

/// <summary>
/// 符号签名信息
/// </summary>
public record SymbolSignature(
    string Name,
    string DisplayName,
    string? ReturnType,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<string> TypeParameters
);

/// <summary>
/// 符号引用信息
/// </summary>
public record SymbolReference(
    SymbolLocation Location,
    string ContainingSymbol,
    string? ContextCode
);

/// <summary>
/// 核心符号信息模型
/// </summary>
public record SymbolInfo
{
    public required string Name { get; init; }
    public required SymbolKind Kind { get; init; }
    public required SymbolLocation Location { get; init; }
    public required string ContainingType { get; init; }
    public required string Namespace { get; init; }

    // 可选详细信息
    public SymbolSignature? Signature { get; init; }
    public string? Documentation { get; init; }
    public string? SourceCode { get; init; }

    // 关系信息
    public IReadOnlyList<SymbolReference> References { get; init; } = [];
    public IReadOnlyList<SymbolInfo> RelatedSymbols { get; init; } = [];

    // 元数据
    public bool IsStatic { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsOverride { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsAsync { get; init; }
    public Accessibility Accessibility { get; init; }
}
