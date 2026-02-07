# Roslyn MCP Server - 详细实现计划

## 一、核心数据模型

### 1.1 符号信息模型

```csharp
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
    TypeParameter
}

/// <summary>
/// 详细级别控制输出内容
/// </summary>
public enum DetailLevel
{
    /// <summary>
    /// 仅符号名称和行号 (最节省 token)
    /// </summary>
    Compact,

    /// <summary>
    /// 符号 + 类型签名 (默认)
    /// </summary>
    Summary,

    /// <summary>
    /// summary + XML 文档注释
    /// </summary>
    Standard,

    /// <summary>
    /// standard + 完整源代码片段
    /// </summary>
    Full
}

/// <summary>
/// 符号位置信息
/// </summary>
public record SymbolLocation(
    string FilePath,
    int StartLine,
    int EndLine,
    int StartColumn,
    int EndColumn
)
{
    public string ToMarkdownLink()
        => $"[{Path.GetFileName(FilePath)}]({FilePath}#L{StartLine})";

    public override string ToString()
        => $"{FilePath}:{StartLine}-{EndLine}";
}

/// <summary>
/// 符号签名信息
/// </summary>
public record SymbolSignature(
    string Name,
    string DisplayName,
    string? ReturnType,
    IReadOnlyList<ParameterInfo> Parameters,
    IReadOnlyList<string> TypeParameters
);

/// <summary>
/// 参数信息
/// </summary>
public record ParameterInfo(
    string Name,
    string Type,
    bool IsOptional,
    bool IsParams,
    bool IsOut,
    bool IsRef,
    string? DefaultValue
);

/// <summary>
/// 符号文档信息
/// </summary>
public record SymbolDocumentation(
    string? Summary,
    string? Remarks,
    IReadOnlyList<string> Examples,
    IReadOnlyList<ParameterDocumentation> ParameterDocs,
    string? ReturnsDoc
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
    public SymbolDocumentation? Documentation { get; init; }
    public string? SourceCode { get; init; }

    // 关系信息
    public IReadOnlyList<SymbolReference> References { get; init; } = [];
    public IReadOnlyList<SymbolInfo> RelatedSymbols { get; init; } = [];

    // 元数据
    public bool IsStatic { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsOverride { get; init; }
    public Accessibility Accessibility { get; init; }
}

/// <summary>
/// 符号引用信息
/// </summary>
public record SymbolReference(
    SymbolLocation Location,
    string ContainingSymbol,
    string? ContextCode
);

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
    Private
}
```

### 1.2 工具输入模型

```csharp
namespace CSharpMcp.Server.Models.Tools;

/// <summary>
/// 通用文件路径定位参数
/// </summary>
public record FileLocationParams
{
    /// <summary>
    /// 文件路径 (支持绝对路径、相对路径、仅文件名模糊匹配)
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 行号 (1-based, 用于模糊匹配)
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    /// 符号名称 (用于验证和模糊匹配)
    /// </summary>
    public string? SymbolName { get; init; }
}

/// <summary>
/// get_symbols 工具参数
/// </summary>
public record GetSymbolsParams : FileLocationParams
{
    /// <summary>
    /// 输出详细级别
    /// </summary>
    public DetailLevel DetailLevel { get; init; } = DetailLevel.Summary;

    /// <summary>
    /// 是否包含方法体
    /// </summary>
    public bool IncludeBody { get; init; } = true;

    /// <summary>
    /// 方法体最大行数
    /// </summary>
    public int BodyMaxLines { get; init; } = 100;

    /// <summary>
    /// 符号类型过滤
    /// </summary>
    public IReadOnlyList<SymbolKind>? FilterKinds { get; init; }
}

/// <summary>
/// go_to_definition 工具参数
/// </summary>
public record GoToDefinitionParams : FileLocationParams
{
    /// <summary>
    /// 输出详细级别
    /// </summary>
    public DetailLevel DetailLevel { get; init; } = DetailLevel.Standard;

    /// <summary>
    /// 是否包含方法体
    /// </summary>
    public bool IncludeBody { get; init; } = true;

    /// <summary>
    /// 方法体最大行数
    /// </summary>
    public int BodyMaxLines { get; init; } = 100;
}

/// <summary>
/// find_references 工具参数
/// </summary>
public record FindReferencesParams : FileLocationParams
{
    /// <summary>
    /// 是否包含上下文代码
    /// </summary>
    public bool IncludeContext { get; init; } = true;

    /// <summary>
    /// 上下文代码行数
    /// </summary>
    public int ContextLines { get; init; } = 3;
}

/// <summary>
/// search_symbols 工具参数
/// </summary>
public record SearchSymbolsParams
{
    /// <summary>
    /// 搜索查询 (支持通配符如 My.*, *.Controller)
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// 输出详细级别
    /// </summary>
    public DetailLevel DetailLevel { get; init; } = DetailLevel.Summary;

    /// <summary>
    /// 最大结果数量
    /// </summary>
    public int MaxResults { get; init; } = 100;
}

/// <summary>
/// get_inheritance_hierarchy 工具参数
/// </summary>
public record GetInheritanceHierarchyParams : FileLocationParams
{
    /// <summary>
    /// 是否包含派生类
    /// </summary>
    public bool IncludeDerived { get; init; } = true;

    /// <summary>
    /// 派生类最大深度
    /// </summary>
    public int MaxDerivedDepth { get; init; } = 3;
}

/// <summary>
/// get_call_graph 工具参数
/// </summary>
public record GetCallGraphParams : FileLocationParams
{
    /// <summary>
    /// 调用方向
    /// </summary>
    public CallGraphDirection Direction { get; init; } = CallGraphDirection.Both;

    /// <summary>
    /// 最大深度
    /// </summary>
    public int MaxDepth { get; init; } = 2;

    /// <summary>
    /// 是否包含外部调用
    /// </summary>
    public bool IncludeExternalCalls { get; init; } = true;
}

/// <summary>
/// 调用图方向
/// </summary>
public enum CallGraphDirection
{
    /// <summary>
    /// 调用者和被调用者
    /// </summary>
    Both,

    /// <summary>
    /// 仅调用者 (incoming calls)
    /// </summary>
    In,

    /// <summary>
    /// 仅被调用者 (outgoing calls)
    /// </summary>
    Out
}

/// <summary>
/// get_call_graph 输出
/// </summary>
public record CallGraphResult(
    IReadOnlyList<CallRelationship> Callers,
    IReadOnlyList<CallRelationship> Callees,
    CallStatistics Statistics
);

/// <summary>
/// 调用关系
/// </summary>
public record CallRelationship(
    SymbolInfo Symbol,
    IReadOnlyList<CallLocation> CallLocations
);

/// <summary>
/// 调用位置
/// </summary>
public record CallLocation(
    string ContainingSymbol,
    SymbolLocation Location,
    string? CallChain
);

/// <summary>
/// 调用统计
/// </summary>
public record CallStatistics(
    int TotalCallers,
    int TotalCallees,
    int CyclomaticComplexity
);

/// <summary>
/// get_type_members 工具参数
/// </summary>
public record GetTypeMembersParams : FileLocationParams
{
    /// <summary>
    /// 是否包含继承的成员
    /// </summary>
    public bool IncludeInherited { get; init; } = false;

    /// <summary>
    /// 成员类型过滤
    /// </summary>
    public IReadOnlyList<SymbolKind>? FilterKinds { get; init; }
}

/// <summary>
/// get_symbol_complete 工具参数
/// </summary>
public record GetSymbolCompleteParams : FileLocationParams
{
    /// <summary>
    /// 需要返回的信息部分
    /// </summary>
    public IReadOnlyList<SymbolInfoSection> Sections { get; init; } =
    [
        SymbolInfoSection.Location,
        SymbolInfoSection.Signature,
        SymbolInfoSection.Documentation
    ];

    /// <summary>
    /// 方法体最大行数 (用于 preview)
    /// </summary>
    public int BodyMaxLines { get; init; } = 30;
}

/// <summary>
/// 符号信息部分
/// </summary>
public enum SymbolInfoSection
{
    Location,
    Signature,
    Documentation,
    Preview,
    References,
    Callers,
    Callees,
    Related,
    Inheritance
}

/// <summary>
/// batch_get_symbols 工具参数
/// </summary>
public record BatchGetSymbolsParams
{
    /// <summary>
    /// 批量查询列表 (最多 10 个)
    /// </summary>
    public required IReadOnlyList<FileLocationParams> Queries { get; init; }

    /// <summary>
    /// 输出详细级别
    /// </summary>
    public DetailLevel DetailLevel { get; init; } = DetailLevel.Summary;
}
```

### 1.3 工具输出模型

```csharp
namespace CSharpMcp.Server.Models.Output;

/// <summary>
/// 工具响应基类
/// </summary>
public abstract record ToolResponse
{
    public abstract string ToMarkdown();
}

/// <summary>
/// get_symbols 输出
/// </summary>
public record GetSymbolsResponse(
    string FilePath,
    IReadOnlyList<SymbolInfo> Symbols,
    int TotalCount
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Symbols: {Path.GetFileName(FilePath)}");
        sb.AppendLine();

        foreach (var symbol in Symbols)
        {
            sb.AppendLine(symbol.ToMarkdown());
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// go_to_definition 输出
/// </summary>
public record GoToDefinitionResponse(
    SymbolInfo Symbol,
    bool IsTruncated,
    int TotalLines
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### Definition: `{Symbol.Name}`");

        if (IsTruncated)
        {
            sb.AppendLine($"(lines {Symbol.Location.StartLine}-{Symbol.Location.EndLine}, showing first {TotalLines})");
        }
        else
        {
            sb.AppendLine($"(lines {Symbol.Location.StartLine}-{Symbol.Location.EndLine})");
        }

        sb.AppendLine();

        if (Symbol.SourceCode != null)
        {
            sb.AppendLine("```csharp");
            sb.AppendLine(Symbol.SourceCode);
            sb.AppendLine("```");

            if (IsTruncated)
            {
                var remaining = Symbol.Location.EndLine - Symbol.Location.StartLine - TotalLines;
                sb.AppendLine($"*... {remaining} more lines hidden*");
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// find_references 输出
/// </summary>
public record FindReferencesResponse(
    SymbolInfo Symbol,
    IReadOnlyList<SymbolReference> References,
    ReferenceSummary Summary
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## References: `{Symbol.Name}`");
        sb.AppendLine();
        sb.AppendLine($"**Found {References.Count} reference{(References.Count != 1 ? "s" : "")}**");
        sb.AppendLine();

        foreach (var ref in References)
        {
            sb.AppendLine($"- {ref.ContainingSymbol} at {ref.Location.ToMarkdownLink()}");
            if (ref.ContextCode != null)
            {
                sb.AppendLine();
                sb.AppendLine("  ```csharp");
                foreach (var line in ref.ContextCode.Split('\n'))
                {
                    sb.AppendLine($"  {line}");
                }
                sb.AppendLine("  ```");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// 引用摘要
/// </summary>
public record ReferenceSummary(
    int TotalReferences,
    int ReferencesInSameFile,
    int ReferencesInOtherFiles,
    IReadOnlyList<string> Files
);

/// <summary>
/// get_inheritance_hierarchy 输出
/// </summary>
public record InheritanceHierarchyResponse(
    SymbolInfo Type,
    InheritanceTree Hierarchy
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Inheritance Hierarchy: `{Type.Name}`");
        sb.AppendLine();

        if (Hierarchy.BaseTypes.Count > 0)
        {
            sb.AppendLine("### Base Classes");
            foreach (var baseType in Hierarchy.BaseTypes)
            {
                sb.AppendLine($"- `{baseType.Name}` ({baseType.Location})");
            }
            sb.AppendLine();
        }

        if (Hierarchy.Interfaces.Count > 0)
        {
            sb.AppendLine("### Implemented Interfaces");
            foreach (var iface in Hierarchy.Interfaces)
            {
                sb.AppendLine($"- `{iface.Name}` ({iface.Location})");
            }
            sb.AppendLine();
        }

        if (Hierarchy.DerivedTypes.Count > 0)
        {
            sb.AppendLine("### Derived Classes");
            foreach (var derived in Hierarchy.DerivedTypes)
            {
                sb.AppendLine($"- `{derived.Name}` ({derived.Location})");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"### Inheritance Distance");
        sb.AppendLine($"- Depth: {Hierarchy.Depth}");

        return sb.ToString();
    }
}

/// <summary>
/// 继承树
/// </summary>
public record InheritanceTree(
    IReadOnlyList<SymbolInfo> BaseTypes,
    IReadOnlyList<SymbolInfo> Interfaces,
    IReadOnlyList<SymbolInfo> DerivedTypes,
    int Depth
);

/// <summary>
/// get_call_graph 输出
/// </summary>
public record CallGraphResponse(
    string MethodName,
    IReadOnlyList<CallRelationship> Callers,
    IReadOnlyList<CallRelationship> Callees,
    CallStatistics Statistics
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Call Graph: `{MethodName}`");
        sb.AppendLine();

        if (Callers.Count > 0)
        {
            sb.AppendLine("### Called By (incoming calls)");
            foreach (var caller in Callers)
            {
                sb.AppendLine($"- `{caller.Symbol.Name}` ({caller.Symbol.Location})");
                foreach (var loc in caller.CallLocations)
                {
                    if (loc.CallChain != null)
                    {
                        sb.AppendLine($"  - Calls: {loc.CallChain}");
                    }
                }
            }
            sb.AppendLine();
        }

        if (Callees.Count > 0)
        {
            sb.AppendLine("### Calls (outgoing calls)");
            foreach (var callee in Callees)
            {
                var isLocal = callee.Symbol.Location.FilePath ==
                              Callers.FirstOrDefault()?.Symbol.Location.FilePath;
                var isExternal = !isLocal;

                sb.AppendLine($"- `{callee.Symbol.Name}` ({callee.Symbol.Location}) " +
                              $"- {(isLocal ? "local method" : isExternal ? "external call" : "same assembly call")}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("### Call Statistics");
        sb.AppendLine($"- Total callers: {Statistics.TotalCallers}");
        sb.AppendLine($"- Total callees: {Statistics.TotalCallees}");
        sb.AppendLine($"- Cyclomatic complexity: {Statistics.CyclomaticComplexity}");

        return sb.ToString();
    }
}

/// <summary>
/// get_type_members 输出
/// </summary>
public record GetTypeMembersResponse(
    string TypeName,
    TypeMembers Members
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Type Members: `{TypeName}`");
        sb.AppendLine();

        if (Members.Fields.Count > 0)
        {
            sb.AppendLine($"### Fields ({Members.Fields.Count})");
            foreach (var field in Members.Fields)
            {
                sb.AppendLine($"- **{field.Name}** ({field.Type}):{field.Location.StartLine} - {field.Documentation}");
            }
            sb.AppendLine();
        }

        if (Members.Properties.Count > 0)
        {
            sb.AppendLine($"### Properties ({Members.Properties.Count})");
            foreach (var prop in Members.Properties)
            {
                sb.AppendLine($"- **{prop.Name}** ({prop.Type}):{prop.Location.StartLine} - {prop.Documentation}");
            }
            sb.AppendLine();
        }

        if (Members.Methods.Count > 0)
        {
            sb.AppendLine($"### Methods ({Members.Methods.Count})");
            foreach (var method in Members.Methods)
            {
                sb.AppendLine($"- **{method.Name}** ({method.Kind}):{method.Location.StartLine}-{method.Location.EndLine} - {method.Signature}");
            }
            sb.AppendLine();
        }

        if (Members.Events.Count > 0)
        {
            sb.AppendLine($"### Events ({Members.Events.Count})");
            foreach (var evt in Members.Events)
            {
                sb.AppendLine($"- **{evt.Name}** ({evt.HandlerType}):{evt.Location.StartLine}");
            }
            sb.AppendLine();
        }

        if (Members.InheritedMembers.Count > 0)
        {
            sb.AppendLine("### Inherited Members");
            foreach (var (baseType, members) in Members.InheritedMembers)
            {
                sb.AppendLine($"- From **{baseType}**: {string.Join(", ", members)}");
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// 类型成员集合
/// </summary>
public record TypeMembers(
    IReadOnlyList<MemberInfo> Fields,
    IReadOnlyList<MemberInfo> Properties,
    IReadOnlyList<MethodInfo> Methods,
    IReadOnlyList<EventInfo> Events,
    IReadOnlyList<(string BaseType, IReadOnlyList<string> Members)> InheritedMembers
);

/// <summary>
/// 成员信息
/// </summary>
public record MemberInfo(
    string Name,
    string Type,
    SymbolLocation Location,
    string Documentation
);

/// <summary>
/// 方法信息
/// </summary>
public record MethodInfo(
    string Name,
    SymbolKind Kind,
    SymbolLocation Location,
    string Signature
);

/// <summary>
/// 事件信息
/// </summary>
public record EventInfo(
    string Name,
    string HandlerType,
    SymbolLocation Location
);

/// <summary>
/// get_symbol_complete 输出
/// </summary>
public record SymbolCompleteResponse(
    SymbolInfo Symbol,
    SymbolCompleteData Data
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Complete Symbol Info: `{Symbol.Name}`");
        sb.AppendLine();

        if (Data.Location != null)
        {
            sb.AppendLine("### Location");
            sb.AppendLine($"**File**: {Path.GetFileName(Data.Location.FilePath)}");
            sb.AppendLine($"**Range**: {Data.Location.StartLine}-{Data.Location.EndLine} ({Data.Location.EndLine - Data.Location.StartLine} lines)");
            sb.AppendLine();
        }

        if (Data.Signature != null)
        {
            sb.AppendLine("### Signature");
            sb.AppendLine("```csharp");
            sb.AppendLine(Data.Signature);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (Data.Documentation != null)
        {
            sb.AppendLine("### Documentation");
            sb.AppendLine(Data.Documentation);
            sb.AppendLine();
        }

        if (Data.Preview != null)
        {
            sb.AppendLine("### Implementation Preview");
            sb.AppendLine(Data.Preview);
            sb.AppendLine();
        }

        if (Data.References != null && Data.References.Count > 0)
        {
            sb.AppendLine($"### References ({Data.References.Count} found)");
            foreach (var ref in Data.References)
            {
                sb.AppendLine($"- `{ref.Symbol}` ({ref.Location})");
            }
            sb.AppendLine();
        }

        if (Data.RelatedSymbols != null)
        {
            if (Data.RelatedSymbols.Calls.Count > 0)
            {
                sb.AppendLine("**Calls**:");
                foreach (var call in Data.RelatedSymbols.Calls)
                {
                    sb.AppendLine($"- `{call}`");
                }
                sb.AppendLine();
            }

            if (Data.RelatedSymbols.CalledBy.Count > 0)
            {
                sb.AppendLine("**Called By**:");
                foreach (var caller in Data.RelatedSymbols.CalledBy)
                {
                    sb.AppendLine($"- `{caller}`");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// 完整符号数据
/// </summary>
public record SymbolCompleteData(
    string? Location,
    string? Signature,
    string? Documentation,
    string? Preview,
    IReadOnlyList<ReferenceItem>? References,
    RelatedSymbols? RelatedSymbols
);

/// <summary>
/// 引用项
/// </summary>
public record ReferenceItem(
    string Symbol,
    string Location
);

/// <summary>
/// 相关符号
/// </summary>
public record RelatedSymbols(
    IReadOnlyList<string> Calls,
    IReadOnlyList<string> CalledBy
);

/// <summary>
/// batch_get_symbols 输出
/// </summary>
public record BatchGetSymbolsResponse(
    IReadOnlyList<BatchResultItem> Results
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Batch Symbol Results");
        sb.AppendLine();

        for (int i = 0; i < Results.Count; i++)
        {
            var result = Results[i];
            sb.AppendLine($"### Query {i + 1}: `{result.SymbolName}` in {result.FileName}");

            if (result.Error != null)
            {
                sb.AppendLine($"**Error**: {result.Error}");
            }
            else
            {
                sb.AppendLine(result.SymbolData ?? "Not found");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// 批量结果项
/// </summary>
public record BatchResultItem(
    string SymbolName,
    string FileName,
    string? SymbolData,
    string? Error
);
```

---

## 二、核心服务接口

### 2.1 工作区管理服务

```csharp
namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 工作区管理服务接口
/// </summary>
public interface IWorkspaceManager
{
    /// <summary>
    /// 加载解决方案或项目
    /// </summary>
    Task<WorkspaceInfo> LoadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取文档
    /// </summary>
    Task<Document?> GetDocumentAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取编译
    /// </summary>
    Task<Compilation?> GetCompilationAsync(string? projectPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取语义模型
    /// </summary>
    Task<SemanticModel?> GetSemanticModelAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 刷新工作区（检测文件变化）
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取工作区状态
    /// </summary>
    WorkspaceStatus GetStatus();
}

/// <summary>
/// 工作区信息
/// </summary>
public record WorkspaceInfo(
    string Path,
    WorkspaceKind Kind,
    int ProjectCount,
    int DocumentCount
);

/// <summary>
/// 工作区类型
/// </summary>
public enum WorkspaceKind
{
    Solution,
    Project,
    Folder
}

/// <summary>
/// 工作区状态
/// </summary>
public record WorkspaceStatus(
    bool IsLoaded,
    int ProjectsLoaded,
    int DocumentsAnalyzed,
    double CacheHitRate,
    DateTime LastUpdate
);
```

### 2.2 符号分析服务

```csharp
namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 符号分析服务接口
/// </summary>
public interface ISymbolAnalyzer
{
    /// <summary>
    /// 获取文档中的所有符号
    /// </summary>
    Task<IReadOnlyList<ISymbol>> GetDocumentSymbolsAsync(
        Document document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在位置处解析符号
    /// </summary>
    Task<ISymbol?> ResolveSymbolAtPositionAsync(
        Document document,
        int lineNumber,
        int column,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按名称查找符号（支持模糊匹配）
    /// </summary>
    Task<IReadOnlyList<ISymbol>> FindSymbolsByNameAsync(
        Document document,
        string symbolName,
        int? approximateLineNumber = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找符号的所有引用
    /// </summary>
    Task<IReadOnlyList<ReferencedSymbol>> FindReferencesAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取符号的定义位置
    /// </summary>
    Task<SymbolLocation?> GetSymbolLocationAsync(
        ISymbol symbol,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 转换为 SymbolInfo 模型
    /// </summary>
    Task<Models.SymbolInfo> ToSymbolInfoAsync(
        ISymbol symbol,
        DetailLevel detailLevel = DetailLevel.Summary,
        int? bodyMaxLines = null,
        CancellationToken cancellationToken = default);
}
```

### 2.3 调用图分析服务

```csharp
namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 调用图分析服务接口
/// </summary>
public interface ICallGraphAnalyzer
{
    /// <summary>
    /// 获取方法的调用者
    /// </summary>
    Task<IReadOnlyList<MethodSymbol>> GetCallersAsync(
        IMethodSymbol method,
        Solution solution,
        int maxDepth = 2,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取方法调用的其他方法
    /// </summary>
    Task<IReadOnlyList<MethodSymbol>> GetCalleesAsync(
        IMethodSymbol method,
        Document document,
        int maxDepth = 2,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算方法的圈复杂度
    /// </summary>
    Task<int> CalculateCyclomaticComplexityAsync(
        IMethodSymbol method,
        Document document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取完整调用图
    /// </summary>
    Task<CallGraphResult> GetCallGraphAsync(
        IMethodSymbol method,
        Solution solution,
        CallGraphDirection direction,
        int maxDepth,
        CancellationToken cancellationToken = default);
}
```

### 2.4 继承分析服务

```csharp
namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 继承分析服务接口
/// </summary>
public interface IInheritanceAnalyzer
{
    /// <summary>
    /// 获取类型的继承层次结构
    /// </summary>
    Task<InheritanceTree> GetInheritanceTreeAsync(
        INamedTypeSymbol type,
        Solution solution,
        bool includeDerived,
        int maxDerivedDepth,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找所有派生类型
    /// </summary>
    Task<IReadOnlyList<INamedTypeSymbol>> FindDerivedTypesAsync(
        INamedTypeSymbol type,
        Solution solution,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取继承链（基类链）
    /// </summary>
    IReadOnlyList<INamedTypeSymbol> GetBaseTypeChain(
        INamedTypeSymbol type);
}
```

### 2.5 文档提供程序

```csharp
namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 文档提供程序接口
/// </summary>
public interface IDocumentationProvider
{
    /// <summary>
    /// 获取符号的文档注释
    /// </summary>
    Task<Models.SymbolDocumentation?> GetDocumentationAsync(
        ISymbol symbol,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 提取源代码片段
    /// </summary>
    Task<string?> ExtractSourceCodeAsync(
        ISymbol symbol,
        bool includeBody,
        int maxLines,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 格式化符号签名为字符串
    /// </summary>
    string FormatSignature(ISymbol symbol);
}
```

---

## 三、MCP 工具实现

### 3.1 工具基类

```csharp
namespace CSharpMcp.Server.Tools;

/// <summary>
/// MCP 工具基类
/// </summary>
public abstract class McpTool
{
    protected readonly IWorkspaceManager WorkspaceManager;
    protected readonly ISymbolAnalyzer SymbolAnalyzer;
    protected readonly ILogger Logger;

    protected McpTool(
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger logger)
    {
        WorkspaceManager = workspaceManager;
        SymbolAnalyzer = symbolAnalyzer;
        Logger = logger;
    }

    /// <summary>
    /// 执行工具逻辑
    /// </summary>
    public abstract Task<ToolResponse> ExecuteAsync<TParams>(
        TParams parameters,
        CancellationToken cancellationToken) where TParams : notnull;

    /// <summary>
    /// 验证参数
    /// </summary>
    protected virtual void ValidateParams<TParams>(TParams parameters)
        where TParams : notnull
    {
        // 默认验证逻辑
    }

    /// <summary>
    /// 处理错误
    /// </summary>
    protected virtual Task<ToolResponse> HandleErrorAsync(Exception ex)
    {
        Logger.LogError(ex, "Error executing tool {ToolName}", GetType().Name);
        return Task.FromResult<ToolResponse>(new ErrorResponse(ex.Message));
    }
}

/// <summary>
/// 错误响应
/// </summary>
public record ErrorResponse(string Message) : ToolResponse
{
    public override string ToMarkdown() => $"**Error**: {Message}";
}
```

### 3.2 Essential 工具

```csharp
namespace CSharpMcp.Server.Tools.Essential;

/// <summary>
/// get_symbols 工具
/// </summary>
public class GetSymbolsTool : McpTool
{
    public GetSymbolsTool(
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger logger)
        : base(workspaceManager, symbolAnalyzer, logger)
    {
    }

    public override async Task<ToolResponse> ExecuteAsync<TParams>(
        TParams parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateParams(parameters);

            if (parameters is not GetSymbolsParams params)
            {
                throw new ArgumentException("Invalid parameters type");
            }

            var document = await WorkspaceManager.GetDocumentAsync(params.FilePath, cancellationToken);
            if (document == null)
            {
                return new ErrorResponse($"Document not found: {params.FilePath}");
            }

            var symbols = await SymbolAnalyzer.GetDocumentSymbolsAsync(document, cancellationToken);

            // 应用过滤
            if (params.FilterKinds != null && params.FilterKinds.Count > 0)
            {
                symbols = symbols.Where(s => params.FilterKinds.Contains(s.Kind)).ToList();
            }

            // 转换为 SymbolInfo
            var symbolInfos = new List<Models.SymbolInfo>();
            foreach (var symbol in symbols)
            {
                var info = await SymbolAnalyzer.ToSymbolInfoAsync(
                    symbol,
                    params.DetailLevel,
                    params.IncludeBody ? params.BodyMaxLines : null,
                    cancellationToken);

                symbolInfos.Add(info);
            }

            return new GetSymbolsResponse(
                params.FilePath,
                symbolInfos,
                symbols.Count);
        }
        catch (Exception ex)
        {
            return await HandleErrorAsync(ex);
        }
    }
}

/// <summary>
/// go_to_definition 工具
/// </summary>
public class GoToDefinitionTool : McpTool
{
    public GoToDefinitionTool(
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger logger)
        : base(workspaceManager, symbolAnalyzer, logger)
    {
    }

    public override async Task<ToolResponse> ExecuteAsync<TParams>(
        TParams parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateParams(parameters);

            if (parameters is not GoToDefinitionParams params)
            {
                throw new ArgumentException("Invalid parameters type");
            }

            // 尝试通过位置查找
            if (params.LineNumber.HasValue)
            {
                var document = await WorkspaceManager.GetDocumentAsync(params.FilePath, cancellationToken);
                if (document == null)
                {
                    return new ErrorResponse($"Document not found: {params.FilePath}");
                }

                var symbol = await SymbolAnalyzer.ResolveSymbolAtPositionAsync(
                    document,
                    params.LineNumber.Value,
                    1, // 默认第一列
                    cancellationToken);

                if (symbol != null)
                {
                    var info = await SymbolAnalyzer.ToSymbolInfoAsync(
                        symbol,
                        params.DetailLevel,
                        params.IncludeBody ? params.BodyMaxLines : null,
                        cancellationToken);

                    var sourceLines = info.SourceCode?.Split('\n').Length ?? 0;
                    var totalLines = info.Location.EndLine - info.Location.StartLine + 1;
                    var isTruncated = params.IncludeBody && params.BodyMaxLines < totalLines;

                    return new GoToDefinitionResponse(info, isTruncated, sourceLines);
                }
            }

            // 尝试通过名称查找（模糊匹配）
            if (!string.IsNullOrEmpty(params.SymbolName))
            {
                var document = await WorkspaceManager.GetDocumentAsync(params.FilePath, cancellationToken);
                if (document == null)
                {
                    return new ErrorResponse($"Document not found: {params.FilePath}");
                }

                var symbols = await SymbolAnalyzer.FindSymbolsByNameAsync(
                    document,
                    params.SymbolName,
                    params.LineNumber,
                    cancellationToken);

                if (symbols.Count > 0)
                {
                    var info = await SymbolAnalyzer.ToSymbolInfoAsync(
                        symbols[0],
                        params.DetailLevel,
                        params.IncludeBody ? params.BodyMaxLines : null,
                        cancellationToken);

                    var sourceLines = info.SourceCode?.Split('\n').Length ?? 0;
                    var totalLines = info.Location.EndLine - info.Location.StartLine + 1;
                    var isTruncated = params.IncludeBody && params.BodyMaxLines < totalLines;

                    return new GoToDefinitionResponse(info, isTruncated, sourceLines);
                }
            }

            return new ErrorResponse($"Symbol not found: {params.SymbolName ?? "at specified location"}");
        }
        catch (Exception ex)
        {
            return await HandleErrorAsync(ex);
        }
    }
}

/// <summary>
/// find_references 工具
/// </summary>
public class FindReferencesTool : McpTool
{
    public FindReferencesTool(
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger logger)
        : base(workspaceManager, symbolAnalyzer, logger)
    {
    }

    public override async Task<ToolResponse> ExecuteAsync<TParams>(
        TParams parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateParams(parameters);

            if (parameters is not FindReferencesParams params)
            {
                throw new ArgumentException("Invalid parameters type");
            }

            // 首先解析符号
            var document = await WorkspaceManager.GetDocumentAsync(params.FilePath, cancellationToken);
            if (document == null)
            {
                return new ErrorResponse($"Document not found: {params.FilePath}");
            }

            ISymbol? symbol = null;

            // 尝试通过位置查找
            if (params.LineNumber.HasValue)
            {
                symbol = await SymbolAnalyzer.ResolveSymbolAtPositionAsync(
                    document,
                    params.LineNumber.Value,
                    1,
                    cancellationToken);
            }

            // 尝试通过名称查找
            if (symbol == null && !string.IsNullOrEmpty(params.SymbolName))
            {
                var symbols = await SymbolAnalyzer.FindSymbolsByNameAsync(
                    document,
                    params.SymbolName,
                    params.LineNumber,
                    cancellationToken);

                symbol = symbols.FirstOrDefault();
            }

            if (symbol == null)
            {
                return new ErrorResponse($"Symbol not found");
            }

            // 获取解决方案
            var solution = document.Project.Solution;

            // 查找引用
            var referencedSymbols = await SymbolAnalyzer.FindReferencesAsync(
                symbol,
                solution,
                cancellationToken);

            // 转换为引用信息
            var references = new List<Models.SymbolReference>();
            var files = new HashSet<string>();
            var sameFileCount = 0;

            foreach (var refSym in referencedSymbols)
            {
                foreach (var loc in refSym.Locations)
                {
                    var location = new Models.SymbolLocation(
                        loc.Document.FilePath,
                        loc.Location.GetLineSpan().StartLinePosition.Line + 1,
                        loc.Location.GetLineSpan().EndLinePosition.Line + 1,
                        loc.Location.GetLineSpan().StartLinePosition.Character + 1,
                        loc.Location.GetLineSpan().EndLinePosition.Character + 1
                    );

                    string? contextCode = null;
                    if (params.IncludeContext)
                    {
                        contextCode = await ExtractContextCodeAsync(loc.Document, location, params.ContextLines, cancellationToken);
                    }

                    references.Add(new Models.SymbolReference(
                        location,
                        refSym.Definition?.Name ?? "Unknown",
                        contextCode
                    ));

                    files.Add(location.FilePath);
                    if (location.FilePath == params.FilePath)
                    {
                        sameFileCount++;
                    }
                }
            }

            var summary = new ReferenceSummary(
                references.Count,
                sameFileCount,
                references.Count - sameFileCount,
                files.ToList()
            );

            var symbolInfo = await SymbolAnalyzer.ToSymbolInfoAsync(symbol, cancellationToken: cancellationToken);

            return new FindReferencesResponse(symbolInfo, references, summary);
        }
        catch (Exception ex)
        {
            return await HandleErrorAsync(ex);
        }
    }

    private async Task<string?> ExtractContextCodeAsync(
        Document document,
        Models.SymbolLocation location,
        int contextLines,
        CancellationToken cancellationToken)
    {
        var sourceText = await document.GetTextAsync(cancellationToken);
        var lines = sourceText.Lines;

        var startLine = Math.Max(0, location.StartLine - contextLines - 1);
        var endLine = Math.Min(lines.Count - 1, location.EndLine + contextLines - 1);

        if (startLine >= endLine)
            return null;

        var text = sourceText.GetSubText(
            TextSpan.FromBounds(
                lines[startLine].Start,
                lines[endLine].End
            )
        ).ToString();

        return text;
    }
}
```

### 3.3 HighValue 工具

```csharp
namespace CSharpMcp.Server.Tools.HighValue;

/// <summary>
/// search_symbols 工具
/// </summary>
public class SearchSymbolsTool : McpTool
{
    public SearchSymbolsTool(
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger logger)
        : base(workspaceManager, symbolAnalyzer, logger)
    {
    }

    public override async Task<ToolResponse> ExecuteAsync<TParams>(
        TParams parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateParams(parameters);

            if (parameters is not SearchSymbolsParams params)
            {
                throw new ArgumentException("Invalid parameters type");
            }

            var compilation = await WorkspaceManager.GetCompilationAsync(cancellationToken: cancellationToken);
            if (compilation == null)
            {
                return new ErrorResponse("No compilation loaded");
            }

            // 使用 Roslyn 的 SymbolFinder 搜索符号
            var searchQuery = params.Query.Replace("*", "").Replace(".", "");

            var symbols = await Task.Run(() =>
            {
                return compilation.GetSymbolsWithName(
                    n => n.Contains(searchQuery, StringComparison.OrdinalIgnoreCase),
                    SymbolFilter.All).Take(params.MaxResults).ToList();
            }, cancellationToken);

            var results = new List<Models.SymbolInfo>();
            foreach (var symbol in symbols)
            {
                var locations = symbol.Locations.Where(l => l.IsInSource).ToList();
                if (locations.Count > 0)
                {
                    var info = await SymbolAnalyzer.ToSymbolInfoAsync(
                        symbol,
                        params.DetailLevel,
                        null,
                        cancellationToken);

                    results.Add(info);
                }
            }

            return new SearchSymbolsResponse(params.Query, results);
        }
        catch (Exception ex)
        {
            return await HandleErrorAsync(ex);
        }
    }
}

/// <summary>
/// get_inheritance_hierarchy 工具
/// </summary>
public class GetInheritanceHierarchyTool : McpTool
{
    private readonly IInheritanceAnalyzer _inheritanceAnalyzer;

    public GetInheritanceHierarchyTool(
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        IInheritanceAnalyzer inheritanceAnalyzer,
        ILogger logger)
        : base(workspaceManager, symbolAnalyzer, logger)
    {
        _inheritanceAnalyzer = inheritanceAnalyzer;
    }

    public override async Task<ToolResponse> ExecuteAsync<TParams>(
        TParams parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateParams(parameters);

            if (parameters is not GetInheritanceHierarchyParams params)
            {
                throw new ArgumentException("Invalid parameters type");
            }

            // 解析类型符号
            var document = await WorkspaceManager.GetDocumentAsync(params.FilePath, cancellationToken);
            if (document == null)
            {
                return new ErrorResponse($"Document not found: {params.FilePath}");
            }

            var symbols = await SymbolAnalyzer.FindSymbolsByNameAsync(
                document,
                params.SymbolName ?? "",
                params.LineNumber,
                cancellationToken);

            var typeSymbol = symbols.FirstOrDefault() as INamedTypeSymbol;
            if (typeSymbol == null)
            {
                return new ErrorResponse($"Type not found: {params.SymbolName}");
            }

            // 获取解决方案
            var solution = document.Project.Solution;

            // 获取继承树
            var tree = await _inheritanceAnalyzer.GetInheritanceTreeAsync(
                typeSymbol,
                solution,
                params.IncludeDerived,
                params.MaxDerivedDepth,
                cancellationToken);

            var symbolInfo = await SymbolAnalyzer.ToSymbolInfoAsync(typeSymbol, cancellationToken: cancellationToken);

            return new InheritanceHierarchyResponse(symbolInfo, tree);
        }
        catch (Exception ex)
        {
            return await HandleErrorAsync(ex);
        }
    }
}

/// <summary>
/// get_call_graph 工具
/// </summary>
public class GetCallGraphTool : McpTool
{
    private readonly ICallGraphAnalyzer _callGraphAnalyzer;

    public GetCallGraphTool(
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ICallGraphAnalyzer callGraphAnalyzer,
        ILogger logger)
        : base(workspaceManager, symbolAnalyzer, logger)
    {
        _callGraphAnalyzer = callGraphAnalyzer;
    }

    public override async Task<ToolResponse> ExecuteAsync<TParams>(
        TParams parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateParams(parameters);

            if (parameters is not GetCallGraphParams params)
            {
                throw new ArgumentException("Invalid parameters type");
            }

            // 解析方法符号
            var document = await WorkspaceManager.GetDocumentAsync(params.FilePath, cancellationToken);
            if (document == null)
            {
                return new ErrorResponse($"Document not found: {params.FilePath}");
            }

            var symbols = await SymbolAnalyzer.FindSymbolsByNameAsync(
                document,
                params.SymbolName ?? "",
                params.LineNumber,
                cancellationToken);

            var methodSymbol = symbols.FirstOrDefault() as IMethodSymbol;
            if (methodSymbol == null)
            {
                return new ErrorResponse($"Method not found: {params.SymbolName}");
            }

            // 获取解决方案
            var solution = document.Project.Solution;

            // 获取调用图
            var result = await _callGraphAnalyzer.GetCallGraphAsync(
                methodSymbol,
                solution,
                params.Direction,
                params.MaxDepth,
                cancellationToken);

            return new CallGraphResponse(
                methodSymbol.Name,
                result.Callers,
                result.Callees,
                result.Statistics);
        }
        catch (Exception ex)
        {
            return await HandleErrorAsync(ex);
        }
    }
}

/// <summary>
/// get_type_members 工具
/// </summary>
public class GetTypeMembersTool : McpTool
{
    public GetTypeMembersTool(
        IWorkspaceManager workspaceManager,
        ISymbolAnalyzer symbolAnalyzer,
        ILogger logger)
        : base(workspaceManager, symbolAnalyzer, logger)
    {
    }

    public override async Task<ToolResponse> ExecuteAsync<TParams>(
        TParams parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateParams(parameters);

            if (parameters is not GetTypeMembersParams params)
            {
                throw new ArgumentException("Invalid parameters type");
            }

            // 解析类型符号
            var document = await WorkspaceManager.GetDocumentAsync(params.FilePath, cancellationToken);
            if (document == null)
            {
                return new ErrorResponse($"Document not found: {params.FilePath}");
            }

            var symbols = await SymbolAnalyzer.FindSymbolsByNameAsync(
                document,
                params.SymbolName ?? "",
                params.LineNumber,
                cancellationToken);

            var typeSymbol = symbols.FirstOrDefault() as INamedTypeSymbol;
            if (typeSymbol == null)
            {
                return new ErrorResponse($"Type not found: {params.SymbolName}");
            }

            // 获取成员
            var members = typeSymbol.GetMembers();
            var fields = new List<Models.MemberInfo>();
            var properties = new List<Models.MemberInfo>();
            var methods = new List<Models.MethodInfo>();
            var events = new List<Models.EventInfo>();
            var inheritedMembers = new List<(string BaseType, IReadOnlyList<string> Members)>();

            foreach (var member in members)
            {
                if (member.IsImplicitlyDeclared)
                    continue;

                var location = await SymbolAnalyzer.GetSymbolLocationAsync(member, cancellationToken);
                if (location == null)
                    continue;

                switch (member.Kind)
                {
                    case SymbolKind.Field:
                        var field = (IFieldSymbol)member;
                        fields.Add(new Models.MemberInfo(
                            member.Name,
                            field.Type.ToDisplayString(),
                            location,
                            member.GetDocumentationCommentXml() ?? ""
                        ));
                        break;

                    case SymbolKind.Property:
                        var property = (IPropertySymbol)member;
                        properties.Add(new Models.MemberInfo(
                            member.Name,
                            property.Type.ToDisplayString(),
                            location,
                            member.GetDocumentationCommentXml() ?? ""
                        ));
                        break;

                    case SymbolKind.Method:
                        var method = (IMethodSymbol)member;
                        methods.Add(new Models.MethodInfo(
                            member.Name,
                            method.MethodKind switch
                            {
                                MethodKind.Ordinary => Models.SymbolKind.Method,
                                MethodKind.Constructor => Models.SymbolKind.Constructor,
                                _ => Models.SymbolKind.Method
                            },
                            location,
                            method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat)
                        ));
                        break;

                    case SymbolKind.Event:
                        var evt = (IEventSymbol)member;
                        events.Add(new Models.EventInfo(
                            member.Name,
                            evt.Type.ToDisplayString(),
                            location
                        ));
                        break;
                }
            }

            // 处理继承成员
            if (params.IncludeInherited)
            {
                var baseType = typeSymbol.BaseType;
                while (baseType != null)
                {
                    var baseMembers = baseType.GetMembers()
                        .Where(m => !m.IsImplicitlyDeclared)
                        .Select(m => m.Name)
                        .ToList();

                    if (baseMembers.Count > 0)
                    {
                        inheritedMembers.Add((baseType.Name, baseMembers));
                    }

                    baseType = baseType.BaseType;
                }
            }

            var typeMembers = new Models.TypeMembers(
                fields,
                properties,
                methods,
                events,
                inheritedMembers
            );

            return new GetTypeMembersResponse(typeSymbol.Name, typeMembers);
        }
        catch (Exception ex)
        {
            return await HandleErrorAsync(ex);
        }
    }
}
```

---

## 四、实现检查清单

### Phase 1: 基础设施 ✅

- [ ] 创建解决方案结构
- [ ] 配置项目依赖
- [ ] 实现 WorkspaceManager
- [ ] 实现编译缓存
- [ ] 配置日志系统

### Phase 2: Essential 工具

- [ ] `get_symbols` - 获取文档符号
- [ ] `go_to_definition` - 跳转到定义
- [ ] `find_references` - 查找引用
- [ ] `resolve_symbol` - 解析符号信息

### Phase 3: HighValue 工具

- [ ] `search_symbols` - 搜索符号
- [ ] `get_inheritance_hierarchy` - 继承层次
- [ ] `get_call_graph` - 调用图
- [ ] `get_type_members` - 类型成员

### Phase 4: 优化工具

- [ ] `get_symbol_complete` - 完整符号信息
- [ ] `batch_get_symbols` - 批量查询
- [ ] `get_diagnostics` - 诊断信息

### Phase 5: 集成与部署

- [ ] 集成测试
- [ ] 性能测试
- [ ] API 文档
- [ ] 发布配置
