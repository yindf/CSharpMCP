namespace CSharpMcp.Server.Models;

/// <summary>
/// 符号类型分类
/// </summary>
public enum SymbolKind
{
    // 类型
    Class,
    Struct,
    Interface,
    Enum,
    Record,
    Delegate,
    Attribute,

    // 成员
    Method,
    Property,
    Field,
    Event,
    Constructor,
    Destructor,

    // 其他
    Namespace,
    Parameter,
    Local,
    TypeParameter,
    Unknown
}
