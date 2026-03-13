using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CSharpMcp.Server.Roslyn;
using System.Text.Json;

namespace CSharpMcp.Server;

/// <summary>
/// Roslyn MCP Server 主程序
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // CRITICAL: Configure all logs to go to stderr
        // MCP stdio transport requires ONLY JSON-RPC messages on stdout
        builder.Logging.AddConsole(consoleLogOptions =>
        {
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        // Add file logging for debugging (disabled for production)
        builder.Logging.AddProvider(new FileLoggerProvider("C:/Project/CSharpMcp/mcp.log"));

        // Core services (injected into tool methods by MCP SDK)
        builder.Services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        builder.Services.AddSingleton<IInheritanceAnalyzer, InheritanceAnalyzer>();

        // Set up MCP Server with stdio transport
        // Tools are auto-discovered via [McpServerToolType] and [McpServerTool] attributes
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(Program).Assembly, serializerOptions: McpJsonOptions.Options);

        var host = builder.Build();

        // Auto-load workspace on startup
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var workspaceManager = host.Services.GetRequiredService<IWorkspaceManager>();

        // First, try CSHARPMCP_WORKSPACE environment variable
        var workspacePath = Environment.GetEnvironmentVariable("CSHARPMCP_WORKSPACE");

        // If no environment variable, search for solution file
        if (string.IsNullOrEmpty(workspacePath))
        {
            workspacePath = FindSolutionFile(logger);
        }

        if (workspacePath != null)
        {
            logger.LogInformation("Auto-loading workspace from: {Path}", workspacePath);

            // Load in background without blocking server startup
            _ = Task.Run(async () =>
            {
                try
                {
                    var info = await workspaceManager.LoadAsync(workspacePath);
                    logger.LogInformation("Workspace auto-loaded successfully: {Path} ({ProjectCount} projects, {DocumentCount} documents)",
                        info.Path, info.ProjectCount, info.DocumentCount);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to auto-load workspace from {Path}", workspacePath);
                    logger.LogInformation("To manually load a workspace, call LoadWorkspace with the path to your .sln or .csproj file");
                }
            });
        }
        else
        {
            logger.LogInformation("No solution file found. Set CSHARPMCP_WORKSPACE or call LoadWorkspace manually.");
        }

        await host.RunAsync();
    }

    /// <summary>
    /// Search for solution file: current directory, then up the tree, then common subdirs
    /// </summary>
    private static string? FindSolutionFile(ILogger logger)
    {
        var currentDir = Directory.GetCurrentDirectory();
        logger.LogInformation("Searching for solution file starting from: {Path}", currentDir);

        // 1. Check current directory
        var sln = Directory.GetFiles(currentDir, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (sln != null)
        {
            logger.LogInformation("Found solution in current directory: {Path}", sln);
            return sln;
        }

        // 2. Search up the directory tree
        var dir = new DirectoryInfo(currentDir);
        while (dir?.Parent != null)
        {
            sln = dir.GetFiles("*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault()?.FullName;
            if (sln != null)
            {
                logger.LogInformation("Found solution in parent directory: {Path}", sln);
                return sln;
            }
            dir = dir.Parent;
        }

        // 3. Search in common subdirectories (src, project folders)
        var searchDirs = new[] { "src", "Source", "Sources" };
        foreach (var subDir in searchDirs)
        {
            var subPath = Path.Combine(currentDir, subDir);
            if (Directory.Exists(subPath))
            {
                sln = Directory.GetFiles(subPath, "*.sln", SearchOption.AllDirectories).FirstOrDefault();
                if (sln != null)
                {
                    logger.LogInformation("Found solution in {SubDir}: {Path}", subDir, sln);
                    return sln;
                }
            }
        }

        logger.LogInformation("No solution file found");
        return null;
    }
}
