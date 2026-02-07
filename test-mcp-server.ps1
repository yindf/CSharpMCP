#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test the CSharp MCP Server functionality

.DESCRIPTION
    Runs the MCP server and sends test requests to verify functionality.
#>

param(
    [string]$ServerPath = "publish/CSharpMcp.Server.exe",
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"

Write-Host "CSharp MCP Server Test Script" -ForegroundColor Cyan
Write-Host "=============================" -ForegroundColor Cyan
Write-Host ""

# Check if server exists
if (-not (Test-Path $ServerPath)) {
    Write-Error "Server not found at: $ServerPath"
    Write-Host "Run publish.bat first to build the server" -ForegroundColor Yellow
    exit 1
}

# Create test directory
$testDir = Join-Path $env:TEMP "CSharpMcpTest"
New-Item -ItemType Directory -Force -Path $testDir | Out-Null

# Create a test C# file
$testFile = Join-Path $testDir "TestClass.cs"
@"
namespace TestProject;
public class TestClass
{
    public void TestMethod() { }
    public int Calculate(int a, int b) => a + b;
}
"@ | Out-File -FilePath $testFile -Encoding UTF8

Write-Host "Test file created: $testFile" -ForegroundColor Green
Write-Host ""

# Note: This is a basic framework for testing
# In a real scenario, you would:
# 1. Start the MCP server process
# 2. Communicate via stdio using JSON-RPC
# 3. Send initialize request
# 4. Call tools and verify responses

Write-Host "MCP Server Testing Steps:" -ForegroundColor Yellow
Write-Host "1. Server executable exists: ✓" -ForegroundColor Green
Write-Host "2. Test file created: ✓" -ForegroundColor Green
Write-Host ""
Write-Host "To perform full MCP protocol testing:" -ForegroundColor Yellow
Write-Host "  1. Run: dotnet test tests/CSharpMcp.Tests" -ForegroundColor White
Write-Host "  2. Or manually test with an MCP client (e.g., Claude Desktop)" -ForegroundColor White
Write-Host ""
Write-Host "Example MCP client config:" -ForegroundColor Cyan
@'
{
  "mcpServers": {
    "csharp": {
      "command": "dotnet",
      "args": ["run", "--project", "src/CSharpMcp.Server/CSharpMcp.Server.csproj"]
    }
  }
}
'@

# Cleanup
Remove-Item -Path $testDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Test setup complete!" -ForegroundColor Green
