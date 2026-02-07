namespace CSharpMcp.Server.Models;

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
