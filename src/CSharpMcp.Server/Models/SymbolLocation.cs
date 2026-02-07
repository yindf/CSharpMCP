namespace CSharpMcp.Server.Models;

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
    /// <summary>
    /// 生成 Markdown 链接格式
    /// </summary>
    public string ToMarkdownLink()
        => $"[{System.IO.Path.GetFileName(FilePath)}]({FilePath}#L{StartLine})";

    /// <summary>
    /// 生成字符串表示
    /// </summary>
    public override string ToString()
        => $"{FilePath}:{StartLine}-{EndLine}";
}
