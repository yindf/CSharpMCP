using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 集中的输出格式化工具，用于 Token 优化
/// 所有工具应该使用这个类来格式化输出，确保一致性
/// </summary>
public static class OutputFormatter
{
    // ========== 类型名缩写 ==========

    /// <summary>
    /// 缩短类型名，只保留最后一部分
    /// System.Collections.Generic.List&lt;StellarGround.Core.Types.Vector2Int&gt; -> List&lt;Vector2Int&gt;
    /// </summary>
    public static string ShortenTypeName(string fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return fullName;

        // 处理泛型参数
        var lessThan = fullName.IndexOf('<');
        if (lessThan > 0)
        {
            var greaterThan = fullName.LastIndexOf('>');
            if (greaterThan > lessThan)
            {
                var basePart = ShortenSimpleTypeName(fullName.Substring(0, lessThan));
                var genericPart = fullName.Substring(lessThan + 1, greaterThan - lessThan - 1);

                // 处理多个泛型参数
                var genericArgs = SplitGenericArguments(genericPart);
                var shortenedArgs = string.Join(", ", genericArgs.Select(ShortenTypeName));

                var suffix = fullName.Substring(greaterThan);
                return $"{basePart}<{shortenedArgs}{suffix}";
            }
        }

        return ShortenSimpleTypeName(fullName);
    }

    /// <summary>
    /// 分割泛型参数（处理嵌套泛型）
    /// </summary>
    private static List<string> SplitGenericArguments(string genericPart)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;

        for (int i = 0; i < genericPart.Length; i++)
        {
            if (genericPart[i] == '<') depth++;
            else if (genericPart[i] == '>') depth--;
            else if (genericPart[i] == ',' && depth == 0)
            {
                result.Add(genericPart.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }

        if (start < genericPart.Length)
            result.Add(genericPart.Substring(start).Trim());

        return result;
    }

    /// <summary>
    /// 缩短简单类型名（非泛型）
    /// </summary>
    private static string ShortenSimpleTypeName(string fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return fullName;

        // 移除数组标记
        var arraySuffix = "";
        if (fullName.EndsWith("[]"))
        {
            arraySuffix = "[]";
            fullName = fullName.Substring(0, fullName.Length - 2);
        }
        else if (fullName.EndsWith("[,]"))
        {
            arraySuffix = "[,]";
            fullName = fullName.Substring(0, fullName.Length - 3);
        }

        // 常见系统类型映射
        var shortName = fullName switch
        {
            "System.Void" => "void",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.Int16" => "short",
            "System.Byte" => "byte",
            "System.Boolean" => "bool",
            "System.String" => "string",
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Decimal" => "decimal",
            "System.Char" => "char",
            "System.Object" => "object",
            "System.DateTime" => "DateTime",
            "System.Guid" => "Guid",
            "System.TimeSpan" => "TimeSpan",
            "System.Threading.CancellationToken" => "CancellationToken",
            "System.Threading.Tasks.Task" => "Task",
            "System.Collections.Generic.IEnumerable" => "IEnumerable",
            "System.Collections.Generic.List" => "List",
            "System.Collections.Generic.Dictionary" => "Dictionary",
            "System.Collections.Generic.HashSet" => "HashSet",
            "System.Collections.Generic.Queue" => "Queue",
            "System.Collections.Generic.Stack" => "Stack",
            _ => GetLastPart(fullName)
        };

        return shortName + arraySuffix;
    }

    private static string GetLastPart(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName.Substring(lastDot + 1) : fullName;
    }

    // ========== 路径简化 ==========

    /// <summary>
    /// 获取简化的相对路径
    /// C:\Project\MyApp\Services\DataService.cs -> Services/DataService.cs
    /// </summary>
    public static string GetShortPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return fullPath;

        try
        {
            var currentDir = Directory.GetCurrentDirectory();
            var relativePath = Path.GetRelativePath(currentDir, fullPath);
            return relativePath.Replace('\\', '/');
        }
        catch
        {
            // 回退到只显示文件名
            return Path.GetFileName(fullPath);
        }
    }

    /// <summary>
    /// 格式化文件位置（简短形式）
    /// </summary>
    public static string FormatLocation(string filePath, int line)
    {
        var shortPath = GetShortPath(filePath);
        return $"{shortPath}:{line}";
    }

    /// <summary>
    /// 格式化文件位置（带结束行）
    /// </summary>
    public static string FormatLocation(string filePath, int startLine, int endLine)
    {
        var shortPath = GetShortPath(filePath);
        return startLine == endLine
            ? $"{shortPath}:{startLine}"
            : $"{shortPath}:{startLine}-{endLine}";
    }

    // ========== 符号过滤 ==========

    /// <summary>
    /// 判断方法是否是属性访问器（get_ 或 set_）
    /// </summary>
    public static bool IsPropertyAccessor(ISymbol symbol)
    {
        if (symbol is not IMethodSymbol method)
            return false;

        return method.AssociatedSymbol is IPropertySymbol;
    }

    /// <summary>
    /// 判断方法是否是事件访问器
    /// </summary>
    public static bool IsEventAccessor(ISymbol symbol)
    {
        if (symbol is not IMethodSymbol method)
            return false;

        return method.MethodKind is MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise;
    }

    /// <summary>
    /// 过滤掉属性和事件访问器
    /// </summary>
    public static IEnumerable<ISymbol> FilterOutAccessors(IEnumerable<ISymbol> symbols)
    {
        return symbols.Where(s => !IsPropertyAccessor(s) && !IsEventAccessor(s));
    }

    // ========== 属性格式化 ==========

    /// <summary>
    /// 获取属性的紧凑格式
    /// "Id: Guid { get; set; }"
    /// </summary>
    public static string FormatPropertyCompact(IPropertySymbol property)
    {
        var type = ShortenTypeName(property.Type.ToDisplayString());
        var accessors = new List<string>();

        if (property.GetMethod != null) accessors.Add("get");
        if (property.SetMethod != null)
            accessors.Add(property.SetMethod.IsInitOnly ? "init" : "set");

        var accessorStr = accessors.Count > 0 ? $" {{ {string.Join("; ", accessors)} }}" : "";
        return $"{property.Name}: {type}{accessorStr}";
    }

    /// <summary>
    /// 获取字段的紧凑格式
    /// "_logger: ILogger"
    /// </summary>
    public static string FormatFieldCompact(IFieldSymbol field)
    {
        var type = ShortenTypeName(field.Type.ToDisplayString());
        var modifier = "";

        if (field.IsConst) modifier = "const ";
        else if (field.IsStatic) modifier = "static ";
        else if (field.IsReadOnly) modifier = "readonly ";

        return $"{modifier}{field.Name}: {type}";
    }

    /// <summary>
    /// 获取方法的紧凑格式
    /// "ProcessData(input: string): Task&lt;Result&gt;"
    /// </summary>
    public static string FormatMethodCompact(IMethodSymbol method)
    {
        var returnType = method.ReturnsVoid ? "void" : ShortenTypeName(method.ReturnType.ToDisplayString());
        var parameters = string.Join(", ", method.Parameters.Select(p =>
        {
            var type = ShortenTypeName(p.Type.ToDisplayString());
            var defaultValue = p.HasExplicitDefaultValue ? $" = {FormatDefaultValue(p.ExplicitDefaultValue)}" : "";
            return $"{p.Name}: {type}{defaultValue}";
        }));

        return $"{method.Name}({parameters}): {returnType}";
    }

    private static string FormatDefaultValue(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            bool b => b.ToString().ToLower(),
            _ => value.ToString()
        };
    }

    // ========== 引用格式化 ==========

    /// <summary>
    /// 格式化引用摘要（紧凑格式）
    /// </summary>
    public static string FormatReferencesSummary(int totalRefs, int fileCount)
    {
        return $"{totalRefs} refs in {fileCount} files";
    }

    /// <summary>
    /// 格式化引用位置列表（紧凑格式）
    /// </summary>
    public static string FormatReferenceLocationsCompact(IEnumerable<Location> locations, int maxCount = 5)
    {
        var lines = locations.Take(maxCount).Select(loc =>
        {
            var lineSpan = loc.GetLineSpan();
            return (lineSpan.Path, Line: lineSpan.StartLinePosition.Line + 1);
        }).GroupBy(x => x.Path).Select(g =>
        {
            var shortPath = GetShortPath(g.Key);
            var lineNumbers = string.Join(", ", g.Select(x => x.Line).Take(5));
            var more = g.Count() > 5 ? $", +{g.Count() - 5}" : "";
            return $"{shortPath}: [{lineNumbers}{more}]";
        });

        return string.Join("\n", lines);
    }

    // ========== 构建器模式 ==========

    /// <summary>
    /// 创建紧凑的符号标题
    /// </summary>
    public static string BuildSymbolHeader(ISymbol symbol)
    {
        var kind = symbol.GetDisplayKind();
        var access = symbol.GetAccessibilityString();
        var (startLine, endLine) = symbol.GetLineRange();
        var shortPath = GetShortPath(symbol.GetFilePath());

        return $"# `{symbol.GetDisplayName()}`\n\n**{kind}** | `{access}` | {shortPath}:{startLine}";
    }

    /// <summary>
    /// 构建成员列表（紧凑格式）
    /// </summary>
    public static string BuildMembersListCompact(IEnumerable<ISymbol> members, int maxItems = 15)
    {
        var sb = new StringBuilder();
        var memberList = members.ToList();

        // 按类型分组
        var properties = memberList.OfType<IPropertySymbol>().Take(maxItems).ToList();
        var methods = memberList.OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .Take(maxItems).ToList();
        var fields = memberList.OfType<IFieldSymbol>().Take(maxItems).ToList();

        if (properties.Count > 0)
        {
            sb.AppendLine($"**Properties** ({properties.Count}):");
            foreach (var prop in properties)
            {
                sb.AppendLine($"  - {FormatPropertyCompact(prop)}");
            }
            sb.AppendLine();
        }

        if (methods.Count > 0)
        {
            sb.AppendLine($"**Methods** ({methods.Count}):");
            foreach (var method in methods)
            {
                sb.AppendLine($"  - {FormatMethodCompact(method)}");
            }
            sb.AppendLine();
        }

        if (fields.Count > 0)
        {
            sb.AppendLine($"**Fields** ({fields.Count}):");
            foreach (var field in fields)
            {
                sb.AppendLine($"  - {FormatFieldCompact(field)}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
