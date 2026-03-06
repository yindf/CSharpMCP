using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CSharpMcp.Server.Roslyn;

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
/// 工作区信息
/// </summary>
public record WorkspaceInfo(
    string Path,
    WorkspaceKind Kind,
    int ProjectCount,
    int DocumentCount
);

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
    /// 确保工作区是最新的（处理待处理的文件变更）
    /// 用于符号查询前确保编译状态是最新的
    /// </summary>
    Task EnsureUpToDateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查工作区是否需要重新加载（当 sln/csproj 变更或文件删除/添加时）
    /// </summary>
    bool NeedsWorkspaceReload { get; }

    /// <summary>
    /// 是否需要 Unity 刷新（当 Unity 项目的 cs 文件增删时）
    /// </summary>
    bool NeedsUnityRefresh { get; }

    /// <summary>
    /// 获取 Unity 刷新提示信息（用于显示给大模型），如果没有则返回 null
    /// </summary>
    string? GetUnityRefreshHint();

    /// <summary>
    /// 清除 Unity 刷新提示（在提示显示后调用）
    /// </summary>
    void ClearUnityRefreshHint();

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
    /// 获取源文本
    /// </summary>
    Task<SourceText?> GetSourceTextAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>s
    /// 获取当前解决方案
    /// </summary>
    Solution? GetCurrentSolution();

    /// <summary>
    /// 获取所有项目
    /// </summary>
    IEnumerable<Project> GetProjects();

    /// <summary>
    /// 获取工作区是否已加载
    /// </summary>
    bool IsWorkspaceLoaded { get; }

    Task<IEnumerable<ISymbol>> SearchSymbolsWithPatternAsync(string query, SymbolFilter filter, CancellationToken cancellationToken);
    Task<IEnumerable<ISymbol>> SearchSymbolsAsync(string query, SymbolFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// 检查文件是否已被删除（物理文件不存在，但仍在 Workspace 中）
    /// </summary>
    bool IsFileDeleted(string filePath);

    /// <summary>
    /// 获取所有已删除的文件路径
    /// </summary>
    IReadOnlySet<string> GetDeletedFilePaths();

    /// <summary>
    /// 强制重新编译整个工作区，用于获取最新的诊断信息
    /// </summary>
    Task ForceRecompileAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 确保工作区状态足够新鲜以进行诊断
    /// 组合了 EnsureUpToDateAsync 和 ForceRecompileAsync 的功能
    /// </summary>
    Task EnsureRefreshAsync(CancellationToken cancellationToken = default);
}
