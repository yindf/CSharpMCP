# Roslyn MCP Server - 项目初始化脚本
# 此脚本创建完整的项目结构和基础文件

$ErrorActionPreference = "Stop"

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "Roslyn MCP Server 初始化" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# 项目配置
$SolutionName = "CSharpMcp"
$BaseDir = $PSScriptRoot
$SrcDir = Join-Path $BaseDir "src"
$TestsDir = Join-Path $BaseDir "tests"
$DocsDir = Join-Path $BaseDir "docs"

# 创建目录结构
Write-Host "创建目录结构..." -ForegroundColor Yellow

$directories = @(
    (Join-Path $SrcDir "CSharpMcp.Server\Tools\Essential"),
    (Join-Path $SrcDir "CSharpMcp.Server\Tools\HighValue"),
    (Join-Path $SrcDir "CSharpMcp.Server\Tools\MediumValue"),
    (Join-Path $SrcDir "CSharpMcp.Server\Roslyn"),
    (Join-Path $SrcDir "CSharpMcp.Server\Models"),
    (Join-Path $SrcDir "CSharpMcp.Server\Models\Tools"),
    (Join-Path $SrcDir "CSharpMcp.Server\Models\Output"),
    (Join-Path $SrcDir "CSharpMcp.Server\Cache"),
    (Join-Path $TestsDir "CSharpMcp.Tests\Tools"),
    (Join-Path $TestsDir "CSharpMcp.Tests\Roslyn"),
    (Join-Path $TestsDir "CSharpMcp.Tests\TestAssets\SimpleProject"),
    (Join-Path $TestsDir "CSharpMcp.Tests\TestAssets\MediumProject"),
    (Join-Path $TestsDir "CSharpMcp.Tests\TestAssets\EdgeCases"),
    (Join-Path $TestsDir "CSharpMcp.IntegrationTests\Scenarios"),
    $DocsDir
)

foreach ($dir in $directories) {
    if (!(Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "  创建: $dir" -ForegroundColor Gray
    }
}

Write-Host "目录结构创建完成" -ForegroundColor Green
Write-Host ""

# 创建解决方案文件
Write-Host "创建解决方案文件..." -ForegroundColor Yellow

$slnPath = Join-Path $BaseDir "$SolutionName.sln"
$projects = @(
    "src\CSharpMcp.Server\CSharpMcp.Server.csproj",
    "tests\CSharpMcp.Tests\CSharpMcp.Tests.csproj",
    "tests\CSharpMcp.IntegrationTests\CSharpMcp.IntegrationTests.csproj"
)

$slnContent = @"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
"

foreach ($project in $projects) {
    $guid = [System.Guid]::NewGuid().ToString().ToUpper()
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
    $slnContent += @"

Project(`"{$guid}`") = `"$projectName`", `"$project`", `"{$guid}`"
EndProject
"
}

$slnContent += @"

Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
	EndGlobalSection
EndGlobal
"@

$slnContent | Out-File -FilePath $slnPath -Encoding UTF8
Write-Host "  创建: $slnPath" -ForegroundColor Gray
Write-Host "解决方案文件创建完成" -ForegroundColor Green
Write-Host ""

# 创建全局配置文件
Write-Host "创建全局配置文件..." -ForegroundColor Yellow

# global.json
$globalJson = @{
    sdk = @{
        version = "8.0.400"
        rollForward = "latestFeature"
    }
} | ConvertTo-Json -Depth 10

$globalJson | Out-File -FilePath (Join-Path $BaseDir "global.json") -Encoding UTF8

# Directory.Build.props
$directoryBuildProps = @'
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <WarningsAsErrors />
    <WarningsNotAsErrors>CS1591</WarningsNotAsErrors>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>

    <!-- 代码分析 -->
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisMode>AllEnabledByDefault</AnalysisMode>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="8.0.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
'@

$directoryBuildProps | Out-File -FilePath (Join-Path $BaseDir "Directory.Build.props") -Encoding UTF8

# .editorconfig
$editorConfig = @'
# top-most EditorConfig file
root = true

# All files
[*]
charset = utf-8
indent_style = space
indent_size = 4
insert_final_newline = true
trim_trailing_whitespace = true
end_of_line = lf

# Code files
[*.{cs,csx,vb,vbx}]
indent_size = 4

# XML project files
[*.{csproj,vbproj,vcxproj,vcxproj.filters,proj,projitems,shproj}]
indent_size = 2

# JSON config files
[*.{json,json5,webmanifest}]
indent_size = 2

# YAML files
[*.{yml,yaml}]
indent_size = 2

# Markdown files
[*.md]
trim_trailing_whitespace = false

# C# files
[*.cs]

# New line preferences
csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true

# Indentation preferences
csharp_indent_case_contents = true
csharp_indent_switch_labels = true

# Space preferences
csharp_space_after_cast = false
csharp_space_after_keywords_in_control_flow_statements = true

# Naming conventions
dotnet_naming_rule.interface_should_be_begins_with_i.severity = warning
dotnet_naming_rule.interface_should_be_begins_with_i.symbols = interface
dotnet_naming_rule.interface_should_be_begins_with_i.style = begins_with_i

dotnet_naming_symbols.interface.applicable_kinds = interface
dotnet_naming_symbols.interface.applicable_accessibilities = public, protected, internal, private, protected_internal, private_protected

dotnet_naming_style.begins_with_i.required_prefix = I
dotnet_naming_style.begins_with_i.capitalization = pascal_case

# Async methods
dotnet_naming_rule.async_methods_end_with_async.severity = suggestion
dotnet_naming_rule.async_methods_end_with_async.symbols = async_methods
dotnet_naming_rule.async_methods_end_with_async.style = end_with_async

dotnet_naming_symbols.async_methods.applicable_kinds = method
dotnet_naming_symbols.async_methods.applicable_accessibilities = *

dotnet_naming_style.end_with_async.required_suffix = Async
dotnet_naming_style.end_with_async.capitalization = pascal_case
'@

$editorConfig | Out-File -FilePath (Join-Path $BaseDir ".editorconfig") -Encoding UTF8

Write-Host "  创建: global.json" -ForegroundColor Gray
Write-Host "  创建: Directory.Build.props" -ForegroundColor Gray
Write-Host "  创建: .editorconfig" -ForegroundColor Gray
Write-Host "全局配置文件创建完成" -ForegroundColor Green
Write-Host ""

# 创建服务器项目文件
Write-Host "创建服务器项目文件..." -ForegroundColor Yellow

$serverCsproj = @'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>CSharpMcp.Server</RootNamespace>
    <AssemblyName>CSharpMcp.Server</AssemblyName>
    <Version>0.1.0</Version>
    <Authors>CSharpMcp Contributors</Authors>
    <Description>Roslyn-based MCP Server for C# code analysis</Description>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>csharp-mcp</ToolCommandName>
  </PropertyGroup>

  <ItemGroup>
    <!-- MCP SDK -->
    <PackageReference Include="ModelContextProtocol" Version="0.1.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />

    <!-- Roslyn -->
    <PackageReference Include="Microsoft.CodeAnalysis" Version="4.11.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="4.11.0" />

    <!-- Logging -->
    <PackageReference Include="Serilog" Version="4.1.0" />
    <PackageReference Include="Serilog.Extensions.Logging" Version="8.0.0" />
    <PackageReference Include="Serilog.Sinks.Console" Version="5.0.1" />
    <PackageReference Include="Serilog.Enrichers.Thread" Version="4.0.0" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
'@

$serverCsproj | Out-File -FilePath (Join-Path $SrcDir "CSharpMcp.Server\CSharpMcp.Server.csproj") -Encoding UTF8

# appsettings.json
$appsettings = @{
    serilog = @{
        MinimumLevel = "Information"
        WriteTo = @(
            @{ Name = "Console" }
        )
        Enrich = @("FromLogContext", "WithThreadId")
    }
    workspace = @{
        DefaultPath = "."
        CacheCompilation = $true
        CacheSymbols = $true
    }
} | ConvertTo-Json -Depth 10

$appsettings | Out-File -FilePath (Join-Path $SrcDir "CSharpMcp.Server\appsettings.json") -Encoding UTF8

Write-Host "  创建: CSharpMcp.Server.csproj" -ForegroundColor Gray
Write-Host "  创建: appsettings.json" -ForegroundColor Gray
Write-Host "服务器项目文件创建完成" -ForegroundColor Green
Write-Host ""

# 创建测试项目文件
Write-Host "创建测试项目文件..." -ForegroundColor Yellow

$testsCsproj = @'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>CSharpMcp.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.0" />
    <PackageReference Include="xUnit" Version="2.9.0" />
    <PackageReference Include="xUnit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CSharpMcp.Server\CSharpMcp.Server.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="TestAssets\**\*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
'@

$testsCsproj | Out-File -FilePath (Join-Path $TestsDir "CSharpMcp.Tests\CSharpMcp.Tests.csproj") -Encoding UTF8
Write-Host "  创建: CSharpMcp.Tests.csproj" -ForegroundColor Gray

$integrationTestsCsproj = @'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>CSharpMcp.IntegrationTests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.0" />
    <PackageReference Include="xUnit" Version="2.9.0" />
    <PackageReference Include="xUnit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CSharpMcp.Server\CSharpMcp.Server.csproj" />
  </ItemGroup>

</Project>
'@

$integrationTestsCsproj | Out-File -FilePath (Join-Path $TestsDir "CSharpMcp.IntegrationTests\CSharpMcp.IntegrationTests.csproj") -Encoding UTF8
Write-Host "  创建: CSharpMcp.IntegrationTests.csproj" -ForegroundColor Gray
Write-Host "测试项目文件创建完成" -ForegroundColor Green
Write-Host ""

# 创建 .gitignore
Write-Host "创建 .gitignore 文件..." -ForegroundColor Yellow

$gitignore = @'
## .NET Core
bin/
obj/
out/

## Visual Studio
.vs/
.vscode/
*.user
*.suo
*.userosscache
*.sln.docstates

## Rider
.idea/

## Build results
[Dd]ebug/
[Dd]ebugPublic/
[Rr]elease/
[Rr]eleases/
x64/
x86/
[Ww][Ii][Nn]32/
[Aa][Rr][Mm]/
[Aa][Rr][Mm]64/
bld/
[Bb]in/
[Oo]bj/
[Ll]og/
[Ll]ogs/

## Tests
TestResults/
*.Coverage
*.coveragexml
*.coverage.json
*.opencover
coverage/
coverage.xml
coverage.html

## NuGet
*.nupkg
*.snupkg
packages/
.nuget/
project.lock.json
project.fragment.lock.json

## Other
*.log
*.tmp
*.temp
*.cache
*.swp
*~
.DS_Store
Thumbs.db
mcp_*.log
'@

$gitignore | Out-File -FilePath (Join-Path $BaseDir ".gitignore") -Encoding UTF8
Write-Host "  创建: .gitignore" -ForegroundColor Gray
Write-Host ".gitignore 文件创建完成" -ForegroundColor Green
Write-Host ""

# 创建基础源代码文件
Write-Host "创建基础源代码文件..." -ForegroundColor Yellow

# Models/SymbolKind.cs
$symbolKindCs = @'
namespace CSharpMcp.Server.Models;

/// <summary>
/// 符号类型分类
/// </summary>
public enum SymbolKind
{
    // 类型
    Class,
    Struct,
    Interface,
    Enum,
    Record,
    Delegate,

    // 成员
    Method,
    Property,
    Field,
    Event,
    Constructor,
    Destructor,

    // 其他
    Namespace,
    Parameter,
    Local,
    TypeParameter
}
'@

$symbolKindCs | Out-File -FilePath (Join-Path $SrcDir "CSharpMcp.Server\Models\SymbolKind.cs") -Encoding UTF8

# Models/DetailLevel.cs
$detailLevelCs = @'
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
'@

$detailLevelCs | Out-File -FilePath (Join-Path $SrcDir "CSharpMcp.Server\Models\DetailLevel.cs") -Encoding UTF8

# Models/SymbolLocation.cs
$symbolLocationCs = @'
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
'@

$symbolLocationCs | Out-File -FilePath (Join-Path $SrcDir "CSharpMcp.Server\Models\SymbolLocation.cs") -Encoding UTF8

# Program.cs
$programCs = @'
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Threading.Tasks;

namespace CSharpMcp.Server;

/// <summary>
/// Roslyn MCP Server 主程序
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            Log.Information("Starting CSharp MCP Server v{Version}", "0.1.0");

            var builder = Host.CreateApplicationBuilder(args);

            // 配置服务
            builder.Services.AddLogging(configure =>
            {
                configure.ClearProviders();
                configure.AddSerilog(dispose: true);
            });

            // TODO: 添加核心服务
            // builder.Services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
            // builder.Services.AddSingleton<ISymbolAnalyzer, SymbolAnalyzer>();
            // builder.Services.AddSingleton<ICallGraphAnalyzer, CallGraphAnalyzer>();
            // builder.Services.AddSingleton<IInheritanceAnalyzer, InheritanceAnalyzer>();
            // builder.Services.AddSingleton<IDocumentationProvider, DocumentationProvider>();

            // TODO: 添加工具
            // builder.Services.AddTransient<GetSymbolsTool>();
            // builder.Services.AddTransient<GoToDefinitionTool>();
            // builder.Services.AddTransient<FindReferencesTool>();
            // builder.Services.AddTransient<SearchSymbolsTool>();
            // builder.Services.AddTransient<GetInheritanceHierarchyTool>();
            // builder.Services.AddTransient<GetCallGraphTool>();
            // builder.Services.AddTransient<GetTypeMembersTool>();
            // builder.Services.AddTransient<GetDiagnosticsTool>();

            var host = builder.Build();

            Log.Information("Server started successfully");

            await host.RunAsync();

            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Server terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
'@

$programCs | Out-File -FilePath (Join-Path $SrcDir "CSharpMcp.Server\Program.cs") -Encoding UTF8

Write-Host "  创建: Models/SymbolKind.cs" -ForegroundColor Gray
Write-Host "  创建: Models/DetailLevel.cs" -ForegroundColor Gray
Write-Host "  创建: Models/SymbolLocation.cs" -ForegroundColor Gray
Write-Host "  创建: Program.cs" -ForegroundColor Gray
Write-Host "基础源代码文件创建完成" -ForegroundColor Green
Write-Host ""

# 创建基础测试文件
Write-Host "创建基础测试文件..." -ForegroundColor Yellow

$placeholderTestCs = @'
using Xunit;
using FluentAssertions;

namespace CSharpMcp.Tests;

[Trait("Category", "Unit")]
public class PlaceholderTest
{
    [Fact]
    public void Placeholder_Passes()
    {
        // 此测试仅为验证测试框架正常工作
        true.Should().BeTrue();
    }
}
'@

$placeholderTestCs | Out-File -FilePath (Join-Path $TestsDir "CSharpMcp.Tests\PlaceholderTest.cs") -Encoding UTF8
Write-Host "  创建: PlaceholderTest.cs" -ForegroundColor Gray
Write-Host "基础测试文件创建完成" -ForegroundColor Green
Write-Host ""

# 创建测试资源文件
Write-Host "创建测试资源文件..." -ForegroundColor Yellow

$simpleClassCs = @'
namespace TestProject;

/// <summary>
/// A simple test class for basic testing
/// </summary>
public class SimpleClass
{
    private int _id;

    public SimpleClass(int id)
    {
        _id = id;
    }

    public int Id => _id;

    public void Process()
    {
        // Simple processing
    }

    public string GetData()
    {
        return "Sample data";
    }
}
'@

$simpleClassCs | Out-File -FilePath (Join-Path $TestsDir "CSharpMcp.Tests\TestAssets\SimpleProject\SimpleClass.cs") -Encoding UTF8
Write-Host "  创建: TestAssets/SimpleProject/SimpleClass.cs" -ForegroundColor Gray

$inheritanceChainCs = @'
namespace TestProject.Inheritance;

/// <summary>
/// Base class for inheritance testing
/// </summary>
public abstract class BaseController
{
    public virtual void Initialize()
    {
        // Base initialization
    }

    public abstract void Process();
}

/// <summary>
/// Derived controller class
/// </summary>
public class DerivedController : BaseController
{
    public override void Initialize()
    {
        base.Initialize();
        // Additional initialization
    }

    public override void Process()
    {
        // Process implementation
    }

    public void Execute()
    {
        Initialize();
        Process();
    }
}
'@

$inheritanceChainCs | Out-File -FilePath (Join-Path $TestsDir "CSharpMcp.Tests\TestAssets\MediumProject\InheritanceChain.cs") -Encoding UTF8
Write-Host "  创建: TestAssets/MediumProject/InheritanceChain.cs" -ForegroundColor Gray
Write-Host "测试资源文件创建完成" -ForegroundColor Green
Write-Host ""

# 创建 README
Write-Host "创建 README.md..." -ForegroundColor Yellow

$readme = @'
# CSharp MCP Server

A Model Context Protocol (MCP) server that provides powerful C# code analysis and navigation capabilities using Roslyn.

## Features

### Essential Tools
- **get_symbols** - Get all symbols in a document
- **go_to_definition** - Navigate to symbol definitions
- **find_references** - Find all references to a symbol
- **resolve_symbol** - Get comprehensive symbol information

### HighValue Tools
- **search_symbols** - Search for symbols across the workspace
- **get_inheritance_hierarchy** - Analyze type inheritance
- **get_call_graph** - Analyze method call relationships
- **get_type_members** - Get complete type member listing

### Optimizations
- **Token Saving** - Configurable detail levels (compact, summary, standard, full)
- **Reduced Interactions** - Combined tools, batch queries
- **Performance** - Compilation caching, incremental updates

## Installation

```bash
dotnet tool install --global CSharpMcp.Server --version 0.1.0
```

## Quick Start

```bash
# Start the server
csharp-mcp

# Or run directly
dotnet run --project src/CSharpMcp.Server/CSharpMcp.Server.csproj
```

## Development

### Building

```bash
dotnet build
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run unit tests only
dotnet test --filter "Category=Unit"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Project Structure

```
CSharpMcp/
├── src/
│   └── CSharpMcp.Server/          # Main server project
│       ├── Tools/                 # MCP tool implementations
│       ├── Roslyn/                # Roslyn integration layer
│       ├── Models/                # Data models
│       └── Cache/                 # Caching layer
├── tests/
│   ├── CSharpMcp.Tests/           # Unit tests
│   └── CSharpMcp.IntegrationTests/# Integration tests
└── docs/                          # Documentation
```

## Documentation

- [Project Plan](PROJECT_PLAN.md) - Development timeline and milestones
- [Implementation Plan](IMPLEMENTATION_PLAN.md) - Detailed implementation specifications
- [Test Plan](TEST_PLAN.md) - Testing strategy and test cases
- [Improvements Document](ROSLYN_IMPROVEMENTS.md) - Design rationale

## Contributing

Contributions are welcome! Please read our contributing guidelines and submit pull requests.

## License

MIT License - see LICENSE file for details
'@

$readme | Out-File -FilePath (Join-Path $BaseDir "README.md") -Encoding UTF8
Write-Host "  创建: README.md" -ForegroundColor Gray
Write-Host "README.md 创建完成" -ForegroundColor Green
Write-Host ""

# 总结
Write-Host "==================================" -ForegroundColor Cyan
Write-Host "初始化完成！" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "接下来可以执行以下操作：" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. 还原依赖包:" -ForegroundColor White
Write-Host "   dotnet restore" -ForegroundColor Gray
Write-Host ""
Write-Host "2. 构建项目:" -ForegroundColor White
Write-Host "   dotnet build" -ForegroundColor Gray
Write-Host ""
Write-Host "3. 运行测试:" -ForegroundColor White
Write-Host "   dotnet test" -ForegroundColor Gray
Write-Host ""
Write-Host "4. 查看开发计划:" -ForegroundColor White
Write-Host "   - PROJECT_PLAN.md - 项目开发计划" -ForegroundColor Gray
Write-Host "   - IMPLEMENTATION_PLAN.md - 详细实现计划" -ForegroundColor Gray
Write-Host "   - TEST_PLAN.md - 测试计划" -ForegroundColor Gray
Write-Host ""
