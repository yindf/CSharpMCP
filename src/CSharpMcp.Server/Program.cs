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

        // Core services (injected into tool methods by MCP SDK)
        builder.Services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        builder.Services.AddSingleton<ISymbolAnalyzer, SymbolAnalyzer>();
        builder.Services.AddSingleton<IInheritanceAnalyzer, InheritanceAnalyzer>();
        builder.Services.AddSingleton<ICallGraphAnalyzer, CallGraphAnalyzer>();

        // Set up MCP Server with stdio transport
        // Tools are auto-discovered via [McpServerToolType] and [McpServerTool] attributes
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(Program).Assembly, serializerOptions: McpJsonOptions.Options);

        await builder.Build().RunAsync();
    }
}
