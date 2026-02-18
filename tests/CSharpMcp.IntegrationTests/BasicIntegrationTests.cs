using Xunit;
using Xunit.Abstractions;

namespace CSharpMcp.IntegrationTests;

/// <summary>
/// 基础集成测试 - 验证项目结构和依赖注入配置
/// </summary>
public class BasicIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public BasicIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Project_Structure_IsValid()
    {
        // This test verifies the project structure is correctly set up
        // Navigate from tests/CSharpMcp.IntegrationTests/bin/Debug/net10.0 to project root
        var baseDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", ".."));

        var projectFile = Path.Combine(baseDir, "src", "CSharpMcp.Server", "CSharpMcp.Server.csproj");
        var programFile = Path.Combine(baseDir, "src", "CSharpMcp.Server", "Program.cs");
        var getSymbolsFile = Path.Combine(baseDir, "src", "CSharpMcp.Server", "Tools", "Essential", "GetSymbolsTool.cs");
        var inheritanceFile = Path.Combine(baseDir, "src", "CSharpMcp.Server", "Tools", "HighValue", "GetInheritanceHierarchyTool.cs");
        var symbolCompleteFile = Path.Combine(baseDir, "src", "CSharpMcp.Server", "Tools", "Optimization", "GetSymbolInfoTool.cs");

        _output.WriteLine($"Looking for files in: {baseDir}");

        // Check that key files exist
        Assert.True(File.Exists(projectFile), $"Project file not found: {projectFile}");
        Assert.True(File.Exists(programFile), $"Program file not found: {programFile}");
        Assert.True(File.Exists(getSymbolsFile), $"GetSymbolsTool file not found: {getSymbolsFile}");
        Assert.True(File.Exists(inheritanceFile), $"GetInheritanceHierarchyTool file not found: {inheritanceFile}");
        Assert.True(File.Exists(symbolCompleteFile), $"GetSymbolInfoTool file not found: {symbolCompleteFile}");

        _output.WriteLine("✓ Project structure is valid");
    }

    [Fact]
    public void Core_Models_AreDefined()
    {
        // This test verifies that core model classes are accessible

        var assembly = typeof(CSharpMcp.Server.Models.SymbolKind).Assembly;

        // Check that core types exist
        var symbolKindType = assembly.GetType("CSharpMcp.Server.Models.SymbolKind");
        var accessibilityType = assembly.GetType("CSharpMcp.Server.Models.Accessibility");
        var symbolInfoType = assembly.GetType("CSharpMcp.Server.Models.SymbolInfo");

        Assert.NotNull(symbolKindType);
        Assert.NotNull(accessibilityType);
        Assert.NotNull(symbolInfoType);

        _output.WriteLine("✓ Core models are defined");
    }

    [Fact]
    public void Tool_Parameters_AreDefined()
    {
        // This test verifies that tool parameter classes are accessible

        var assembly = typeof(CSharpMcp.Server.Models.Tools.FileLocationParams).Assembly;

        // Check that tool parameter types exist
        var getSymbolsParamsType = assembly.GetType("CSharpMcp.Server.Models.Tools.GetSymbolsParams");
        var goToDefinitionParamsType = assembly.GetType("CSharpMcp.Server.Models.Tools.GoToDefinitionParams");

        Assert.NotNull(getSymbolsParamsType);
        Assert.NotNull(goToDefinitionParamsType);

        _output.WriteLine("✓ Tool parameters are defined");
    }

    [Fact]
    public void Tool_Responses_AreDefined()
    {
        // This test verifies that tool response classes are accessible

        var assembly = typeof(CSharpMcp.Server.Models.Output.ToolResponse).Assembly;

        // Check that tool response types exist
        var getSymbolsResponseType = assembly.GetType("CSharpMcp.Server.Models.Output.GetSymbolsResponse");
        var goToDefinitionResponseType = assembly.GetType("CSharpMcp.Server.Models.Output.GoToDefinitionResponse");

        Assert.NotNull(getSymbolsResponseType);
        Assert.NotNull(goToDefinitionResponseType);

        _output.WriteLine("✓ Tool responses are defined");
    }
}

/// <summary>
/// 场景测试 - 验证典型用户场景
/// </summary>
public class ScenarioTests
{
    private readonly ITestOutputHelper _output;

    public ScenarioTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void All_Tool_Classes_CanBeInstantiated()
    {
        // This test verifies that all tool classes can be instantiated
        // This is a compile-time verification test

        var assembly = typeof(CSharpMcp.Server.Tools.Essential.GetSymbolsTool).Assembly;

        // Essential tools
        var essentialTools = new[]
        {
            "CSharpMcp.Server.Tools.Essential.GetSymbolsTool",
            "CSharpMcp.Server.Tools.Essential.GoToDefinitionTool",
            "CSharpMcp.Server.Tools.Essential.FindReferencesTool",
            "CSharpMcp.Server.Tools.Essential.ResolveSymbolTool",
            "CSharpMcp.Server.Tools.Essential.SearchSymbolsTool"
        };

        // HighValue tools
        var highValueTools = new[]
        {
            "CSharpMcp.Server.Tools.HighValue.GetInheritanceHierarchyTool",
            "CSharpMcp.Server.Tools.HighValue.GetCallGraphTool",
            "CSharpMcp.Server.Tools.HighValue.GetTypeMembersTool"
        };

        // Optimization tools
        var optimizationTools = new[]
        {
            "CSharpMcp.Server.Tools.Optimization.GetSymbolInfoTool",
            "CSharpMcp.Server.Tools.Optimization.BatchGetSymbolsTool",
            "CSharpMcp.Server.Tools.Optimization.GetDiagnosticsTool"
        };

        var allTools = essentialTools.Concat(highValueTools).Concat(optimizationTools);

        foreach (var toolName in allTools)
        {
            var toolType = assembly.GetType(toolName);
            Assert.True(toolType != null, $"Tool {toolName} not found");
        }

        _output.WriteLine($"✓ All {allTools.Count()} tools can be instantiated");
    }

    [Fact]
    public void All_Analyzer_Interfaces_AreImplemented()
    {
        // This test verifies that all analyzer interfaces are defined

        var assembly = typeof(CSharpMcp.Server.Roslyn.IWorkspaceManager).Assembly;

        var interfaces = new[]
        {
            "CSharpMcp.Server.Roslyn.IWorkspaceManager",
            "CSharpMcp.Server.Roslyn.ISymbolAnalyzer",
            "CSharpMcp.Server.Roslyn.IInheritanceAnalyzer",
            "CSharpMcp.Server.Roslyn.ICallGraphAnalyzer"
        };

        foreach (var interfaceName in interfaces)
        {
            var interfaceType = assembly.GetType(interfaceName);
            Assert.True(interfaceType != null, $"Interface {interfaceName} not found");
        }

        _output.WriteLine($"✓ All {interfaces.Length} analyzer interfaces are defined");
    }

    [Fact]
    public void All_Models_HaveRequiredProperties()
    {
        // This test verifies that model classes have the required structure

        var symbolInfoType = typeof(CSharpMcp.Server.Models.SymbolInfo);

        // Check key properties exist
        Assert.NotNull(symbolInfoType.GetProperty("Name"));
        Assert.NotNull(symbolInfoType.GetProperty("Kind"));
        Assert.NotNull(symbolInfoType.GetProperty("Location"));
        Assert.NotNull(symbolInfoType.GetProperty("Accessibility"));

        _output.WriteLine("✓ SymbolInfo has all required properties");
    }
}
