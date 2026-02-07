using CSharpMcp.Server.Models;
using CSharpMcp.Server.Models.Tools;

namespace CSharpMcp.Server.Models.Output;

/// <summary>
/// 工具响应基类
/// </summary>
public abstract record ToolResponse
{
    /// <summary>
    /// 转换为 Markdown 格式
    /// </summary>
    public abstract string ToMarkdown();
}

/// <summary>
/// 错误响应
/// </summary>
public record ErrorResponse(string Message) : ToolResponse
{
    public override string ToMarkdown() => $"**Error**: {Message}";
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
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Symbols: {System.IO.Path.GetFileName(FilePath)}");
        sb.AppendLine($"**Total: {TotalCount} symbol{(TotalCount != 1 ? "s" : "")}**");
        sb.AppendLine();

        foreach (var symbol in Symbols)
        {
            sb.AppendLine(SymbolToMarkdown(symbol));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string SymbolToMarkdown(SymbolInfo symbol, int indent = 0)
    {
        var prefix = new string(' ', indent * 2);
        var sb = new System.Text.StringBuilder();

        var accessibility = symbol.Accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedInternal => "protected internal",
            Accessibility.PrivateProtected => "private protected",
            _ => ""
        };

        var modifiers = new List<string>();
        if (symbol.IsStatic) modifiers.Add("static");
        if (symbol.IsVirtual) modifiers.Add("virtual");
        if (symbol.IsOverride) modifiers.Add("override");
        if (symbol.IsAbstract) modifiers.Add("abstract");
        if (symbol.IsAsync) modifiers.Add("async");

        var allModifiers = new List<string>();
        if (!string.IsNullOrEmpty(accessibility)) allModifiers.Add(accessibility);
        allModifiers.AddRange(modifiers);

        var modifierStr = allModifiers.Count > 0 ? string.Join(" ", allModifiers) + " " : "";

        sb.Append($"{prefix}**{symbol.Name}** ({symbol.Kind}):{symbol.Location.StartLine}-{symbol.Location.EndLine}");

        if (symbol.Signature != null)
        {
            var returnType = !string.IsNullOrEmpty(symbol.Signature.ReturnType) ? $": {symbol.Signature.ReturnType}" : "";
            var paramsStr = symbol.Signature.Parameters.Count > 0
                ? $"({string.Join(", ", symbol.Signature.Parameters)})"
                : "()";
            sb.Append($" - {modifierStr}{symbol.Name}{paramsStr}{returnType}");
        }

        if (!string.IsNullOrEmpty(symbol.Documentation))
        {
            sb.Append($" - {symbol.Documentation}");
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
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"### Definition: `{Symbol.Name}`");

        if (IsTruncated)
        {
            sb.AppendLine($"(lines {Symbol.Location.StartLine}-{Symbol.Location.EndLine}, showing {TotalLines} of {TotalLines} total lines)");
        }
        else
        {
            sb.AppendLine($"(lines {Symbol.Location.StartLine}-{Symbol.Location.EndLine})");
        }

        sb.AppendLine();

        if (Symbol.Signature != null)
        {
            sb.AppendLine("**Signature**:");
            sb.AppendLine("```csharp");
            var returnType = !string.IsNullOrEmpty(Symbol.Signature.ReturnType) ? $"{Symbol.Signature.ReturnType} " : "";
            var paramsStr = Symbol.Signature.Parameters.Count > 0
                ? string.Join(", ", Symbol.Signature.Parameters)
                : "";
            sb.AppendLine($"{returnType}{Symbol.Name}({paramsStr});");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(Symbol.Documentation))
        {
            sb.AppendLine("**Documentation**:");
            sb.AppendLine(Symbol.Documentation);
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(Symbol.SourceCode))
        {
            sb.AppendLine("**Implementation**:");
            sb.AppendLine("```csharp");
            sb.AppendLine(Symbol.SourceCode);
            sb.AppendLine("```");

            if (IsTruncated)
            {
                var remaining = TotalLines - Symbol.SourceCode.Split('\n').Length;
                sb.AppendLine($"*... {remaining} more lines hidden*");
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// find_references 输出
/// </summary>
public record ReferenceSummary(
    int TotalReferences,
    int ReferencesInSameFile,
    int ReferencesInOtherFiles,
    IReadOnlyList<string> Files
);

public record FindReferencesResponse(
    SymbolInfo Symbol,
    IReadOnlyList<SymbolReference> References,
    ReferenceSummary Summary
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## References: `{Symbol.Name}`");
        sb.AppendLine();
        sb.AppendLine($"**Found {References.Count} reference{(References.Count != 1 ? "s" : "")}**");
        sb.AppendLine();

        // Group by file
        var grouped = References.GroupBy(r => r.Location.FilePath);

        foreach (var group in grouped)
        {
            var fileName = System.IO.Path.GetFileName(group.Key);
            sb.AppendLine($"### {fileName}");
            sb.AppendLine();

            foreach (var @ref in group)
            {
                sb.AppendLine($"- Line {@ref.Location.StartLine}: {@ref.ContainingSymbol}");
                if (!string.IsNullOrEmpty(@ref.ContextCode))
                {
                    sb.AppendLine();
                    sb.AppendLine("  ```csharp");
                    foreach (var line in @ref.ContextCode.Split('\n'))
                    {
                        sb.AppendLine($"  {line}");
                    }
                    sb.AppendLine("  ```");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("**Summary**:");
        sb.AppendLine($"- Total references: {Summary.TotalReferences}");
        sb.AppendLine($"- In same file: {Summary.ReferencesInSameFile}");
        sb.AppendLine($"- In other files: {Summary.ReferencesInOtherFiles}");
        sb.AppendLine($"- Files affected: {Summary.Files.Count}");

        return sb.ToString();
    }
}

/// <summary>
/// search_symbols 输出
/// </summary>
public record SearchSymbolsResponse(
    string Query,
    IReadOnlyList<SymbolInfo> Symbols
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Search Results: \"{Query}\"");
        sb.AppendLine();
        sb.AppendLine($"**Found {Symbols.Count} symbol{(Symbols.Count != 1 ? "s" : "")}**");
        sb.AppendLine();

        // Group by namespace
        var grouped = Symbols.GroupBy(s => s.Namespace);

        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            if (!string.IsNullOrEmpty(group.Key))
            {
                sb.AppendLine($"### Namespace: {group.Key}");
            }
            else
            {
                sb.AppendLine("### (Global Namespace)");
            }
            sb.AppendLine();

            foreach (var symbol in group)
            {
                var fileName = System.IO.Path.GetFileName(symbol.Location.FilePath);
                sb.AppendLine($"- **{symbol.Name}** ({symbol.Kind}) - {fileName}:{symbol.Location.StartLine}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// resolve_symbol 输出
/// </summary>
public record ResolveSymbolResponse(
    SymbolInfo Symbol,
    string? Definition,
    IReadOnlyList<SymbolReference>? References
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Symbol: `{Symbol.Name}`");
        sb.AppendLine();

        sb.AppendLine("**Location**:");
        sb.AppendLine($"- File: {Symbol.Location.FilePath}");
        sb.AppendLine($"- Lines: {Symbol.Location.StartLine}-{Symbol.Location.EndLine}");
        sb.AppendLine();

        sb.AppendLine("**Type**:");
        sb.AppendLine($"- Kind: {Symbol.Kind}");
        sb.AppendLine($"- Containing Type: {Symbol.ContainingType}");
        sb.AppendLine($"- Namespace: {Symbol.Namespace}");
        sb.AppendLine();

        sb.AppendLine("**Modifiers**:");
        sb.AppendLine($"- Accessibility: {Symbol.Accessibility}");
        if (Symbol.IsStatic) sb.AppendLine($"- Static: yes");
        if (Symbol.IsVirtual) sb.AppendLine($"- Virtual: yes");
        if (Symbol.IsOverride) sb.AppendLine($"- Override: yes");
        if (Symbol.IsAbstract) sb.AppendLine($"- Abstract: yes");
        sb.AppendLine();

        if (Symbol.Signature != null)
        {
            sb.AppendLine("**Signature**:");
            sb.AppendLine("```csharp");
            var returnType = !string.IsNullOrEmpty(Symbol.Signature.ReturnType) ? $"{Symbol.Signature.ReturnType} " : "";
            var paramsStr = Symbol.Signature.Parameters.Count > 0
                ? string.Join(", ", Symbol.Signature.Parameters)
                : "";
            sb.AppendLine($"{returnType}{Symbol.Name}({paramsStr});");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(Symbol.Documentation))
        {
            sb.AppendLine("**Documentation**:");
            sb.AppendLine(Symbol.Documentation);
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(Definition))
        {
            sb.AppendLine("**Definition**:");
            sb.AppendLine("```csharp");
            sb.AppendLine(Definition);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (References != null && References.Count > 0)
        {
            sb.AppendLine($"**References** ({References.Count} found):");
            foreach (var @ref in References.Take(10))
            {
                var fileName = System.IO.Path.GetFileName(@ref.Location.FilePath);
                sb.AppendLine($"- {fileName}:{@ref.Location.StartLine} in {@ref.ContainingSymbol}");
            }
            if (References.Count > 10)
            {
                sb.AppendLine($"- ... and {References.Count - 10} more");
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// get_inheritance_hierarchy 输出
/// </summary>
public record InheritanceHierarchyData(
    string TypeName,
    SymbolKind Kind,
    IReadOnlyList<string> BaseTypes,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<SymbolInfo> DerivedTypes,
    int Depth
);

public record InheritanceHierarchyResponse(
    string TypeName,
    InheritanceHierarchyData Hierarchy
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Inheritance Hierarchy: `{TypeName}`");
        sb.AppendLine();

        // Base types
        if (Hierarchy.BaseTypes.Count > 0)
        {
            sb.AppendLine("**Base Types**:");
            foreach (var baseType in Hierarchy.BaseTypes)
            {
                sb.AppendLine($"- {baseType}");
            }
            sb.AppendLine();
        }

        // Interfaces
        if (Hierarchy.Interfaces.Count > 0)
        {
            sb.AppendLine("**Implemented Interfaces**:");
            foreach (var iface in Hierarchy.Interfaces)
            {
                sb.AppendLine($"- {iface}");
            }
            sb.AppendLine();
        }

        // Derived types
        if (Hierarchy.DerivedTypes.Count > 0)
        {
            sb.AppendLine($"**Derived Types** ({Hierarchy.DerivedTypes.Count}, depth: {Hierarchy.Depth}):");
            foreach (var derived in Hierarchy.DerivedTypes)
            {
                var fileName = System.IO.Path.GetFileName(derived.Location.FilePath);
                sb.AppendLine($"- **{derived.Name}** ({derived.Kind}) - {fileName}:{derived.Location.StartLine}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// get_call_graph 输出
/// </summary>
public record CallLocationItem(
    string ContainingSymbol,
    Models.SymbolLocation Location
);

public record CallRelationshipItem(
    SymbolInfo Symbol,
    IReadOnlyList<CallLocationItem> CallLocations
);

public record CallStatisticsItem(
    int TotalCallers,
    int TotalCallees,
    int CyclomaticComplexity
);

public record CallGraphResponse(
    string MethodName,
    IReadOnlyList<CallRelationshipItem> Callers,
    IReadOnlyList<CallRelationshipItem> Callees,
    CallStatisticsItem Statistics
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Call Graph: `{MethodName}`");
        sb.AppendLine();

        // Statistics
        sb.AppendLine("**Statistics**:");
        sb.AppendLine($"- Total callers: {Statistics.TotalCallers}");
        sb.AppendLine($"- Total callees: {Statistics.TotalCallees}");
        sb.AppendLine($"- Cyclomatic complexity: {Statistics.CyclomaticComplexity}");
        sb.AppendLine();

        // Callers
        if (Callers.Count > 0)
        {
            sb.AppendLine($"**Called By** ({Callers.Count}):");
            foreach (var caller in Callers)
            {
                var fileName = System.IO.Path.GetFileName(caller.Symbol.Location.FilePath);
                sb.AppendLine($"- **{caller.Symbol.Name}** - {fileName}:{caller.Symbol.Location.StartLine}");

                if (caller.CallLocations.Count > 0)
                {
                    foreach (var loc in caller.CallLocations.Take(3))
                    {
                        var locFile = System.IO.Path.GetFileName(loc.Location.FilePath);
                        sb.AppendLine($"  - at {loc.ContainingSymbol} ({locFile}:{loc.Location.StartLine})");
                    }
                    if (caller.CallLocations.Count > 3)
                    {
                        sb.AppendLine($"  - ... and {caller.CallLocations.Count - 3} more locations");
                    }
                }
            }
            sb.AppendLine();
        }

        // Callees
        if (Callees.Count > 0)
        {
            sb.AppendLine($"**Calls** ({Callees.Count}):");
            foreach (var callee in Callees)
            {
                var fileName = System.IO.Path.GetFileName(callee.Symbol.Location.FilePath);
                sb.AppendLine($"- **{callee.Symbol.Name}** - {fileName}:{callee.Symbol.Location.StartLine}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// get_type_members 输出
/// </summary>
public record MemberInfoItem(
    string Name,
    SymbolKind Kind,
    Accessibility Accessibility,
    bool IsStatic,
    bool IsVirtual,
    bool IsOverride,
    bool IsAbstract,
    Models.SymbolLocation Location
);

public record MethodInfoItem(
    MemberInfoItem Base,
    string? ReturnType,
    IReadOnlyList<string> Parameters
);

public record EventInfoItem(
    MemberInfoItem Base,
    string? EventType
);

public record TypeMembersData(
    string TypeName,
    IReadOnlyList<MemberInfoItem> Members,
    int TotalCount
);

public record GetTypeMembersResponse(
    string TypeName,
    TypeMembersData MembersData
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Type Members: `{TypeName}`");
        sb.AppendLine();
        sb.AppendLine($"**Total: {MembersData.TotalCount} member{(MembersData.TotalCount != 1 ? "s" : "")}**");
        sb.AppendLine();

        // Group by kind
        var grouped = MembersData.Members.GroupBy(m => m.Kind);

        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            sb.AppendLine($"### {group.Key}");
            sb.AppendLine();

            foreach (var member in group)
            {
                var fileName = System.IO.Path.GetFileName(member.Location.FilePath);
                var modifiers = new List<string>();
                if (member.IsStatic) modifiers.Add("static");
                if (member.IsVirtual) modifiers.Add("virtual");
                if (member.IsOverride) modifiers.Add("override");
                if (member.IsAbstract) modifiers.Add("abstract");

                var modifierStr = modifiers.Count > 0 ? string.Join(" ", modifiers) + " " : "";
                sb.AppendLine($"- **{member.Name}** ({member.Accessibility} {modifierStr}{member.Kind}) - {fileName}:{member.Location.StartLine}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// get_symbol_complete 输出
/// </summary>
public record SymbolCompleteData(
    SymbolInfo BasicInfo,
    string? Documentation,
    string? SourceCode,
    IReadOnlyList<SymbolReference> References,
    InheritanceHierarchyData? Inheritance,
    CallGraphResponse? CallGraph
);

public record GetSymbolCompleteResponse(
    string SymbolName,
    SymbolCompleteData Data,
    bool HasMore
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Symbol: `{SymbolName}`");
        sb.AppendLine();

        // Basic info
        sb.AppendLine($"**Type**: {Data.BasicInfo.Kind}");
        sb.AppendLine($"**Location**: [{System.IO.Path.GetFileName(Data.BasicInfo.Location.FilePath)}]({Data.BasicInfo.Location.FilePath}#{Data.BasicInfo.Location.StartLine})");
        sb.AppendLine();

        // Signature
        if (Data.BasicInfo.Signature != null)
        {
            sb.AppendLine("**Signature**:");
            sb.AppendLine("```csharp");
            var returnType = !string.IsNullOrEmpty(Data.BasicInfo.Signature.ReturnType) ? $"{Data.BasicInfo.Signature.ReturnType} " : "";
            var paramsStr = Data.BasicInfo.Signature.Parameters.Count > 0
                ? string.Join(", ", Data.BasicInfo.Signature.Parameters)
                : "";
            sb.AppendLine($"{returnType}{Data.BasicInfo.Name}({paramsStr});");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // Documentation
        if (!string.IsNullOrEmpty(Data.Documentation))
        {
            sb.AppendLine("**Documentation**:");
            sb.AppendLine(Data.Documentation);
            sb.AppendLine();
        }

        // Source code
        if (!string.IsNullOrEmpty(Data.SourceCode))
        {
            sb.AppendLine("**Source Code**:");
            sb.AppendLine("```csharp");
            sb.AppendLine(Data.SourceCode);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // References
        if (Data.References.Count > 0)
        {
            sb.AppendLine($"**References** ({Data.References.Count}):");
            foreach (var @ref in Data.References.Take(20))
            {
                var fileName = System.IO.Path.GetFileName(@ref.Location.FilePath);
                sb.AppendLine($"- {fileName}:{@ref.Location.StartLine} in {@ref.ContainingSymbol}");
            }
            if (Data.References.Count > 20)
            {
                sb.AppendLine($"- ... and {Data.References.Count - 20} more");
            }
            sb.AppendLine();
        }

        // Inheritance
        if (Data.Inheritance != null)
        {
            sb.AppendLine("**Inheritance**:");
            if (Data.Inheritance.BaseTypes.Count > 0)
            {
                sb.AppendLine("- Base Types: " + string.Join(", ", Data.Inheritance.BaseTypes));
            }
            if (Data.Inheritance.Interfaces.Count > 0)
            {
                sb.AppendLine("- Interfaces: " + string.Join(", ", Data.Inheritance.Interfaces));
            }
            if (Data.Inheritance.DerivedTypes.Count > 0)
            {
                sb.AppendLine($"- Derived Types: {Data.Inheritance.DerivedTypes.Count}");
            }
            sb.AppendLine();
        }

        // Call graph
        if (Data.CallGraph != null)
        {
            sb.AppendLine("**Call Graph**:");
            sb.AppendLine($"- Callers: {Data.CallGraph.Callers.Count}");
            sb.AppendLine($"- Callees: {Data.CallGraph.Callees.Count}");
            sb.AppendLine($"- Complexity: {Data.CallGraph.Statistics.CyclomaticComplexity}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

/// <summary>
/// batch_get_symbols 输出
/// </summary>
public record BatchSymbolResult(
    string? SymbolName,
    SymbolInfo? Symbol,
    string? Error
);

public record BatchGetSymbolsResponse(
    int TotalCount,
    int SuccessCount,
    int ErrorCount,
    IReadOnlyList<BatchSymbolResult> Results
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Batch Symbol Query Results");
        sb.AppendLine();
        sb.AppendLine($"**Total**: {TotalCount} | **Success**: {SuccessCount} | **Errors**: {ErrorCount}");
        sb.AppendLine();

        foreach (var result in Results)
        {
            if (result.Error != null)
            {
                sb.AppendLine($"### ❌ {result.SymbolName ?? "Unknown"}");
                sb.AppendLine($"Error: {result.Error}");
                sb.AppendLine();
            }
            else if (result.Symbol != null)
            {
                var fileName = System.IO.Path.GetFileName(result.Symbol.Location.FilePath);
                sb.AppendLine($"### ✅ {result.Symbol.Name}");
                sb.AppendLine($"- Type: {result.Symbol.Kind}");
                sb.AppendLine($"- Location: {fileName}:{result.Symbol.Location.StartLine}");
                if (result.Symbol.Signature != null)
                {
                    var returnType = !string.IsNullOrEmpty(result.Symbol.Signature.ReturnType) ? $"{result.Symbol.Signature.ReturnType} " : "";
                    var paramsStr = result.Symbol.Signature.Parameters.Count > 0
                        ? string.Join(", ", result.Symbol.Signature.Parameters)
                        : "";
                    sb.AppendLine($"- Signature: {returnType}{result.Symbol.Name}({paramsStr})");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// get_diagnostics 输出
/// </summary>
public record DiagnosticItem(
    string Id,
    string Message,
    DiagnosticSeverity Severity,
    string FilePath,
    int StartLine,
    int EndLine,
    int StartColumn,
    int EndColumn,
    string? Category
);

public record DiagnosticsSummary(
    int TotalErrors,
    int TotalWarnings,
    int TotalInfo,
    int TotalHidden,
    int FilesWithDiagnostics
);

public record GetDiagnosticsResponse(
    DiagnosticsSummary Summary,
    IReadOnlyList<DiagnosticItem> Diagnostics
) : ToolResponse
{
    public override string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Diagnostics Report");
        sb.AppendLine();

        // Summary
        sb.AppendLine("**Summary**:");
        sb.AppendLine($"- Errors: {Summary.TotalErrors}");
        sb.AppendLine($"- Warnings: {Summary.TotalWarnings}");
        sb.AppendLine($"- Info: {Summary.TotalInfo}");
        sb.AppendLine($"- Files affected: {Summary.FilesWithDiagnostics}");
        sb.AppendLine();

        // Group by file
        var grouped = Diagnostics.GroupBy(d => d.FilePath);

        foreach (var group in grouped)
        {
            var fileName = System.IO.Path.GetFileName(group.Key);
            sb.AppendLine($"### {fileName}");
            sb.AppendLine();

            foreach (var diag in group)
            {
                var severityIcon = diag.Severity switch
                {
                    DiagnosticSeverity.Error => "❌",
                    DiagnosticSeverity.Warning => "⚠️",
                    DiagnosticSeverity.Info => "ℹ️",
                    DiagnosticSeverity.Hidden => "🔍",
                    _ => "•"
                };

                sb.AppendLine($"- {severityIcon} **{diag.Id}** (Line {diag.StartLine}): {diag.Message}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

