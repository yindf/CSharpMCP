using System.Text;

namespace CSharpMcp.Server.Roslyn;

/// <summary>
/// 工作区错误消息辅助类
/// </summary>
public static class WorkspaceErrorHelper
{
    /// <summary>
    /// 获取工作区未加载的错误消息
    /// </summary>
    public static string GetWorkspaceNotLoadedError(string? toolName = null)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(toolName))
        {
            sb.AppendLine($"## {toolName} - Workspace Not Loaded");
        }
        else
        {
            sb.AppendLine("## Workspace Not Loaded");
        }

        sb.AppendLine();
        sb.AppendLine("The workspace has not been loaded yet. You need to load a C# solution or project first.");
        sb.AppendLine();
        sb.AppendLine("**How to fix:**");
        sb.AppendLine();
        sb.AppendLine("1. **Load a solution file** (.sln or .slnx):");
        sb.AppendLine("   ```");
        sb.AppendLine("   LoadWorkspace(filePath: \"path/to/your/solution.sln\")");
        sb.AppendLine("   ```");
        sb.AppendLine();
        sb.AppendLine("2. **Load a project file** (.csproj):");
        sb.AppendLine("   ```");
        sb.AppendLine("   LoadWorkspace(filePath: \"path/to/your/project.csproj\")");
        sb.AppendLine("   ```");
        sb.AppendLine();
        sb.AppendLine("3. **Load from directory** (will search for .sln/.csproj files):");
        sb.AppendLine("   ```");
        sb.AppendLine("   LoadWorkspace(filePath: \"path/to/your/project\")");
        sb.AppendLine("   ```");
        sb.AppendLine();
        sb.AppendLine("**Note:** After loading, wait a few seconds for the Language Server to complete indexing before using other tools.");
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// 检查工作区是否已加载，如果未加载则返回错误消息
    /// </summary>
    /// <returns>如果工作区已加载返回 null，否则返回错误消息</returns>
    public static string? CheckWorkspaceLoaded(IWorkspaceManager workspaceManager, string? toolName = null)
    {
        if (!workspaceManager.IsWorkspaceLoaded)
        {
            return GetWorkspaceNotLoadedError(toolName);
        }
        return null;
    }
}
