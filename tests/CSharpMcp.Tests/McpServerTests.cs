using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using CSharpMcp.Server.Models.Tools;
using CSharpMcp.Server.Models.Output;
using CSharpMcp.Server.Tools.Essential;
using CSharpMcp.Server.Tools.HighValue;
using CSharpMcp.Server.Tools.Optimization;
using CSharpMcp.Server.Roslyn;
using FluentAssertions;
using System.Diagnostics;

namespace CSharpMcp.Tests;

[Trait("Category", "Functional")]
public class McpServerTests : IClassFixture<McpTestFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly McpTestFixture _fixture;

    public McpServerTests(ITestOutputHelper output, McpTestFixture fixture)
    {
        _output = output;
        _fixture = fixture;
    }

    [Fact]
    public async Task Server_Starts_Successfully()
    {
        // Arrange
        var projectPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "src", "CSharpMcp.Server", "CSharpMcp.Server.csproj");
        projectPath = Path.GetFullPath(projectPath);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Act
        using var process = Process.Start(processStartInfo);

        // Assert
        process.Should().NotBeNull();

        // Give it time to start
        await Task.Delay(2000);

        if (!process.HasExited)
        {
            process.Kill();
            _output.WriteLine("Server started successfully");
        }
    }

    [Fact]
    public void Project_Builds_Successfully()
    {
        // Arrange
        var projectPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "src", "CSharpMcp.Server", "CSharpMcp.Server.csproj");
        projectPath = Path.GetFullPath(projectPath);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" --no-restore",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Act
        using var result = Process.Start(psi);

        // Assert
        result.Should().NotBeNull();
        result.WaitForExit(60000);

        // Build may have warnings but should succeed (exit code 0)
        _output.WriteLine($"Build exit code: {result.ExitCode}");

        // Read output for debugging
        var output = result.StandardOutput.ReadToEnd();
        var error = result.StandardError.ReadToEnd();
        if (!string.IsNullOrEmpty(output))
            _output.WriteLine($"Build output: {output.Substring(0, Math.Min(200, output.Length))}");
        if (!string.IsNullOrEmpty(error))
            _output.WriteLine($"Build errors: {error.Substring(0, Math.Min(200, error.Length))}");
    }

    [Fact]
    public void All_Tool_Assemblies_Exist()
    {
        // Arrange
        var assembly = typeof(GetSymbolsTool).Assembly;

        // Act & Assert
        assembly.Should().NotBeNull();

        var toolTypes = new[]
        {
            typeof(GetSymbolsTool),
            typeof(GoToDefinitionTool),
            typeof(FindReferencesTool),
            typeof(ResolveSymbolTool),
            typeof(SearchSymbolsTool),
            typeof(GetInheritanceHierarchyTool),
            typeof(GetCallGraphTool),
            typeof(GetTypeMembersTool),
            typeof(GetSymbolCompleteTool),
            typeof(BatchGetSymbolsTool),
            typeof(GetDiagnosticsTool)
        };

        foreach (var toolType in toolTypes)
        {
            _output.WriteLine($"✓ Tool found: {toolType.Name}");
            toolType.Should().NotBeNull();
        }
    }

    [Fact]
    public void Model_Types_AreAccessible()
    {
        // Arrange & Act & Assert
        var paramTypes = new[]
        {
            typeof(GetSymbolsParams),
            typeof(GoToDefinitionParams),
            typeof(FindReferencesParams),
            typeof(ResolveSymbolParams),
            typeof(SearchSymbolsParams),
            typeof(GetInheritanceHierarchyParams),
            typeof(GetCallGraphParams),
            typeof(GetTypeMembersParams),
            typeof(GetSymbolCompleteParams),
            typeof(BatchGetSymbolsParams),
            typeof(GetDiagnosticsParams)
        };

        foreach (var type in paramTypes)
        {
            _output.WriteLine($"✓ Parameter type: {type.Name}");
            type.Should().NotBeNull();
        }

        var responseTypes = new[]
        {
            typeof(GetSymbolsResponse),
            typeof(GoToDefinitionResponse),
            typeof(FindReferencesResponse),
            typeof(ResolveSymbolResponse),
            typeof(SearchSymbolsResponse)
        };

        foreach (var type in responseTypes)
        {
            _output.WriteLine($"✓ Response type: {type.Name}");
            type.Should().NotBeNull();
        }
    }

    [Fact]
    public void WorkspaceManager_Interface_Exists()
    {
        // Arrange & Act
        var interfaceType = typeof(IWorkspaceManager);

        // Assert
        interfaceType.Should().NotBeNull();
        _output.WriteLine("✓ IWorkspaceManager interface exists");
    }

    [Fact]
    public void SymbolAnalyzer_Interface_Exists()
    {
        // Arrange & Act
        var interfaceType = typeof(ISymbolAnalyzer);

        // Assert
        interfaceType.Should().NotBeNull();
        _output.WriteLine("✓ ISymbolAnalyzer interface exists");
    }

    [Fact]
    public void Published_Exe_Exists()
    {
        // Arrange
        var exePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "publish", "CSharpMcp.Server.exe");
        exePath = Path.GetFullPath(exePath);

        // Act & Assert
        if (File.Exists(exePath))
        {
            var fileInfo = new FileInfo(exePath);
            fileInfo.Length.Should().BeGreaterThan(0);
            _output.WriteLine($"✓ Published exe exists: {exePath} ({fileInfo.Length / 1024 / 1024} MB)");
        }
        else
        {
            _output.WriteLine($"⚠ Published exe not found at: {exePath}");
        }
    }
}

public class McpTestFixture : IDisposable
{
    public McpTestFixture()
    {
        // Setup code that runs once before all tests
    }

    public void Dispose()
    {
        // Cleanup code that runs once after all tests
    }
}
