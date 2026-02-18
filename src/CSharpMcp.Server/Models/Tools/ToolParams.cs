using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.CodeAnalysis;

namespace CSharpMcp.Server.Models.Tools;

/// <summary>
/// 通用文件路径定位参数
/// </summary>
public record FileLocationParams
{
    /// <summary>
    /// 符号名称 (用于验证和模糊匹配)
    /// </summary>
    [Description("The name of the symbol to locate (e.g., 'MyMethod', 'MyClass')")]
    public string SymbolName { get; init; }

    /// <summary>
    /// 文件路径 (支持绝对路径、相对路径、仅文件名模糊匹配)
    /// </summary>
    [Description("Path to the file containing the symbol. Can be absolute, relative, or filename only for fuzzy matching")]
    public string FilePath { get; init; }

    /// <summary>
    /// 行号 (1-based, 用于模糊匹配)
    /// </summary>
    [Description("1-based line number near the symbol declaration (used for fuzzy matching)")]
    public int LineNumber { get; init; } = 0;
}

/// <summary>
/// get_symbols 工具参数
/// </summary>
public record GetSymbolsParams
{
    /// <summary>
    /// 文件路径 (支持绝对路径、相对路径、仅文件名模糊匹配)
    /// </summary>
    [Description("Path to the file containing the symbol. Can be absolute, relative, or filename only for fuzzy matching")]
    public string FilePath { get; init; }

    /// <summary>
    /// 是否包含方法体
    /// </summary>
    [Description("Whether to include method/property implementation in output")]
    public bool IncludeBody { get; init; } = true;

    /// <summary>
    /// 方法体最大行数
    /// </summary>
    [Description("Maximum number of lines to include for implementation code")]
    public int MaxBodyLines { get; init; } = 100;

    /// <summary>
    /// Minimum accessibility level to include (default: Private = show all)
    /// </summary>
    [Description("Minimum accessibility level: Public, Internal, Protected, Private")]
    public Accessibility MinVisibility { get; init; } = Accessibility.Private;

    /// <summary>
    /// Symbol kinds to include (null = all kinds)
    /// </summary>
    [Description("Filter by symbol kinds: e.g., NamedType, Method, Property, Field (null = all)")]
    public string[]? SymbolKinds { get; init; }

    /// <summary>
    /// Exclude local variables and parameters
    /// </summary>
    [Description("Exclude local variables and parameters from output")]
    public bool ExcludeLocal { get; init; } = true;

}

/// <summary>
/// find_references 工具参数
/// </summary>
public record FindReferencesParams : FileLocationParams
{
    /// <summary>
    /// 是否包含上下文代码
    /// </summary>
    [Description("Whether to include source code context around each reference")]
    public bool IncludeContext { get; init; } = true;

    /// <summary>
    /// 上下文代码行数
    /// </summary>
    [Description("Number of lines to show before and after each reference")]
    public int ContextLines { get; init; } = 3;

    /// <summary>
    /// Compact mode - only shows file names and counts, not individual references
    /// </summary>
    [Description("Show only file names and reference counts, not detailed code context")]
    public bool Compact { get; init; } = false;
}

/// <summary>
/// search_symbols 工具参数
/// </summary>
public record SearchSymbolsParams
{
    /// <summary>
    /// 搜索查询 (支持通配符如 My.*, *.Controller)
    /// </summary>
    [Description("Search query with optional wildcards (e.g., 'MyClass.*', '*.Controller', 'MyMethod')")]
    public required string Query { get; init; }

    /// <summary>
    /// 最大结果数量
    /// </summary>
    [Description("Maximum number of results to return")]
    public int MaxResults { get; init; } = 100;

    /// <summary>
    /// Sort results by: relevance (default), name, or kind
    /// </summary>
    [Description("Sort order: relevance (type>field, exact>wildcard), name, or kind")]
    public string SortBy { get; init; } = "relevance";
}

/// <summary>
/// resolve_symbol 工具参数
/// </summary>
public record ResolveSymbolParams : FileLocationParams
{
    /// <summary>
    /// 是否包含方法体
    /// </summary>
    [Description("Whether to include method/property implementation in output")]
    public bool IncludeBody { get; init; } = true;

    /// <summary>
    /// 方法体最大行数
    /// </summary>
    [Description("Maximum number of lines to include for implementation code")]
    public int MaxBodyLines { get; init; } = 50;

    /// <summary>
    /// When true, only return the primary symbol definition (types, methods)
    /// When false, return all matching symbols (default behavior)
    /// </summary>
    [Description("Only return primary symbol definitions (classes, interfaces, methods), excluding fields/properties")]
    public bool DefinitionsOnly { get; init; } = true;
}

/// <summary>
/// get_inheritance_hierarchy 工具参数
/// </summary>
public record GetInheritanceHierarchyParams : FileLocationParams
{
    /// <summary>
    /// 是否包含派生类
    /// </summary>
    [Description("Whether to include derived types in the hierarchy")]
    public bool IncludeDerived { get; init; } = true;

    /// <summary>
    /// 派生类最大深度
    /// </summary>
    [Description("Maximum depth to traverse for derived types (0 = direct descendants only)")]
    public int MaxDerivedDepth { get; init; } = 3;
}

/// <summary>
/// get_call_graph 工具参数
/// </summary>
public record GetCallGraphParams : FileLocationParams
{
    /// <summary>
    /// 最多显示调用者数量
    /// </summary>
    [Description("Maximum number of callers to display")]
    public int MaxCallers { get; init; } = 20;

    /// <summary>
    /// 最多显示调用数量
    /// </summary>
    [Description("Maximum number of callees to display")]
    public int MaxCallees { get; init; } = 10;
}

/// <summary>
/// get_type_members 工具参数
/// </summary>
public record GetTypeMembersParams : FileLocationParams
{
    /// <summary>
    /// 是否包含继承的成员
    /// </summary>
    [Description("Whether to include members inherited from base types")]
    public bool IncludeInherited { get; init; } = false;

}

/// <summary>
/// get_symbol_info 工具参数 - 整合多个信息源获取完整符号信息
/// </summary>
public record GetSymbolInfoParams : FileLocationParams
{
    /// <summary>
    /// 方法体最大行数
    /// </summary>
    [Description("Maximum lines of implementation code to include")]
    public int MaxBodyLines { get; init; } = 100;

    /// <summary>
    /// 是否包含引用
    /// </summary>
    [Description("Whether to include symbol references")]
    public bool IncludeReferences { get; init; } = true;

    /// <summary>
    /// 引用最大数量
    /// </summary>
    [Description("Maximum number of references to include")]
    public int MaxReferences { get; init; } = 50;

    /// <summary>
    /// 是否包含继承信息
    /// </summary>
    [Description("Whether to include inheritance hierarchy")]
    public bool IncludeInheritance { get; init; } = false;

    /// <summary>
    /// 是否包含调用图
    /// </summary>
    [Description("Whether to include call graph analysis")]
    public bool IncludeCallGraph { get; init; } = false;

    /// <summary>
    /// 调用图最大深度
    /// </summary>
    [Description("Maximum depth for call graph traversal")]
    public int CallGraphMaxDepth { get; init; } = 1;
}

/// <summary>
/// batch_get_symbols 工具参数 - 批量获取符号信息
/// </summary>
public record BatchGetSymbolsParams
{
    /// <summary>
    /// 符号位置列表
    /// </summary>
    [Description("List of symbol locations to query in batch")]
    public required IReadOnlyList<FileLocationParams> Symbols { get; init; }

    /// <summary>
    /// 是否包含方法体
    /// </summary>
    [Description("Whether to include implementation code")]
    public bool IncludeBody { get; init; } = true;

    /// <summary>
    /// 方法体最大行数
    /// </summary>
    [Description("Maximum lines per symbol implementation")]
    public int MaxBodyLines { get; init; } = 50;
}

/// <summary>
/// get_diagnostics 工具参数 - 获取编译诊断信息
/// </summary>
public record GetDiagnosticsParams
{
    /// <summary>
    /// 文件路径 (可选，不指定则获取整个工作区的诊断)
    /// </summary>
    [Description("Optional file path to get diagnostics for specific file (null = entire workspace)")]
    public string? FilePath { get; init; }

    /// <summary>
    /// 是否包含警告
    /// </summary>
    [Description("Whether to include warning diagnostics")]
    public bool IncludeWarnings { get; init; } = true;

    /// <summary>
    /// 是否包含信息
    /// </summary>
    [Description("Whether to include info diagnostics")]
    public bool IncludeInfo { get; init; } = false;

    /// <summary>
    /// 是否包含隐藏诊断
    /// </summary>
    [Description("Whether to include hidden diagnostics")]
    public bool IncludeHidden { get; init; } = false;

    /// <summary>
    /// 严重性过滤
    /// </summary>
    [Description("Optional list of severities to include (null = all severities)")]
    public IReadOnlyList<DiagnosticSeverity>? SeverityFilter { get; init; }
}

/// <summary>
/// 诊断严重性
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// 错误
    /// </summary>
    Error,

    /// <summary>
    /// 警告
    /// </summary>
    Warning,

    /// <summary>
    /// 信息
    /// </summary>
    Info,

    /// <summary>
    /// 隐藏
    /// </summary>
    Hidden
}

/// <summary>
/// load_workspace 工具参数 - 加载 C# 解决方案或项目
/// </summary>
public record LoadWorkspaceParams
{
    /// <summary>
    /// 工作区路径 (支持 .sln 文件、.csproj 文件或包含它们的目录)
    /// </summary>
    [Description("Path to .sln file, .csproj file, or directory containing them")]
    public required string Path { get; init; }
}
