using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Models.Output;
using CSharpMcp.Server.Roslyn;

namespace CSharpMcp.Server.Tools.Essential;

/// <summary>
/// load_workspace 工具 - 加载 C# 解决方案或项目
/// </summary>
[McpServerToolType]
public class LoadWorkspaceTool
{
    /// <summary>
    /// Load a C# solution or project for analysis
    /// </summary>
    [McpServerTool, Description("Load a C# solution (.sln), project (.csproj), or directory for analysis")]
    public static async Task<string> LoadWorkspace(
        [Description("Path to .sln file, .csproj file, or directory containing them")] string path,
        IWorkspaceManager workspaceManager,
        ILogger<LoadWorkspaceTool> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("LoadWorkspace called - Path: {Path}", path ?? "null");

            if (string.IsNullOrWhiteSpace(path))
            {
                logger.LogError("LoadWorkspace Path is null or empty");
                return GetErrorHelpResponse("Path is required. Specify the path to a .sln file, .csproj file, or directory containing them.");
            }

            logger.LogInformation("Loading workspace from: {Path}", path);

            var workspaceInfo = await workspaceManager.LoadAsync(path, cancellationToken);

            logger.LogInformation(
                "Workspace loaded successfully: {Kind} with {ProjectCount} projects and {DocumentCount} documents",
                workspaceInfo.Kind,
                workspaceInfo.ProjectCount,
                workspaceInfo.DocumentCount
            );

            return new LoadWorkspaceResponse(
                workspaceInfo.Path,
                workspaceInfo.Kind,
                workspaceInfo.ProjectCount,
                workspaceInfo.DocumentCount
            ).ToMarkdown();
        }
        catch (FileNotFoundException ex)
        {
            logger.LogError(ex, "File not found when loading workspace");
            return GetErrorHelpResponse($"File or directory not found: `{path}`\n\nMake sure the path exists and is accessible.");
        }
        catch (NotSupportedException ex)
        {
            logger.LogError(ex, "Unsupported file type when loading workspace");
            return GetErrorHelpResponse($"Unsupported file type. Please provide a path to:\n- A `.sln` solution file (traditional format)\n- A `.csproj` project file\n- A directory containing these files\n\nNote: `.slnx` files (VS2022 format) are not supported. Use a `.sln` or `.csproj` file.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading workspace");
            return GetErrorHelpResponse($"Failed to load workspace: {ex.Message}\n\nCommon issues:\n- Project has build errors\n- Missing required SDKs or NuGet packages\n- Invalid project file format");
        }
    }

    private static string GetErrorHelpResponse(string message)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Load Workspace - Failed");
        sb.AppendLine();
        sb.AppendLine(message);
        sb.AppendLine();
        sb.AppendLine("**Usage:**");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("LoadWorkspace(path: string)");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("**Examples:**");
        sb.AppendLine("- `LoadWorkspace(path: \"C:/MyProject/MySolution.sln\")`");
        sb.AppendLine("- `LoadWorkspace(path: \"./MyProject.csproj\")`");
        sb.AppendLine("- `LoadWorkspace(path: \".\")` - Auto-detect in current directory");
        sb.AppendLine();
        return sb.ToString();
    }
}
