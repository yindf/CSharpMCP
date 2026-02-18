using System;
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

        // Add file logging for debugging
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
        bool usingEnvVar = !string.IsNullOrEmpty(workspacePath);

        // If no environment variable, use current directory
        if (string.IsNullOrEmpty(workspacePath))
        {
            workspacePath = System.IO.Directory.GetCurrentDirectory();
            logger.LogInformation("No CSHARPMCP_WORKSPACE environment variable set, using current directory");
        }

        logger.LogInformation("Auto-loading workspace from: {Path}", workspacePath);

        try
        {
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start workspace auto-load");
        }

        await host.RunAsync();
    }
}
