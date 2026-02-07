# C# MCP Server Comprehensive Test Script
# This script tests all tools exposed by the C# MCP server

param(
    [string]$ServerPath = "publish/CSharpMcp.Server.exe",
    [string]$WorkspacePath = (Get-Location).Path,
    [string]$OutputPath = "test_results.json"
)

# Initialize test results
$testResults = @{
    summary = @{
        total = 0
        passed = 0
        failed = 0
        startTime = (Get-Date -Format "o")
        endTime = $null
    }
    tests = @()
}

function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

function Invoke-McpTool {
    param(
        [string]$ToolName,
        [hashtable]$Arguments
    )

    $payload = @{
        jsonrpc = "2.0"
        id = 1
        method = "tools/call"
        params = @{
            name = $ToolName
            arguments = $Arguments
        }
    } | ConvertTo-Json -Depth 10

    Write-Host "Invoking tool: $ToolName" -ForegroundColor Cyan
    Write-Host "Arguments: $($Arguments | ConvertTo-Json -Compress)" -ForegroundColor DarkCyan

    $process = Start-Process -FilePath $ServerPath -ArgumentList "--stdio", "-w", $WorkspacePath -NoNewWindow -RedirectStandardInput -RedirectStandardOutput -RedirectStandardError -PassThru

    # Send the request
    $inputBytes = [System.Text.Encoding]::UTF8.GetBytes($payload + "`n")
    $process.StandardInput.BaseStream.Write($inputBytes, 0, $inputBytes.Length)
    $process.StandardInput.Close()

    # Read response with timeout
    $output = $process.StandardOutput.ReadToEnd()
    $errorOutput = $process.StandardError.ReadToEnd()

    Start-Sleep -Milliseconds 500

    if (!$process.HasExited) {
        $process.Kill()
    }

    if ($errorOutput) {
        Write-Host "Error output: $errorOutput" -ForegroundColor Red
    }

    # Parse JSON response
    try {
        $jsonOutput = $output | ConvertFrom-Json
        return $jsonOutput
    }
    catch {
        Write-Host "Failed to parse JSON: $_" -ForegroundColor Red
        Write-Host "Raw output: $output" -ForegroundColor Yellow
        return $null
    }
}

function Add-TestResult {
    param(
        [string]$TestName,
        [string]$Tool,
        [hashtable]$Input,
        [object]$Output,
        [bool]$Passed,
        [string]$ErrorMessage = $null
    )

    $testResult = @{
        name = $TestName
        tool = $Tool
        input = $Input
        output = if ($Output -is [string]) { $Output } else { $Output | ConvertTo-Json -Depth 10 -Compress }
        passed = $Passed
        error = $ErrorMessage
        timestamp = (Get-Date -Format "o")
    }

    $script:testResults.tests += $testResult
    $script:testResults.summary.total++

    if ($Passed) {
        $script:testResults.summary.passed++
        Write-ColorOutput Green "✓ PASSED: $TestName"
    }
    else {
        $script:testResults.summary.failed++
        Write-ColorOutput Red "✗ FAILED: $TestName"
        if ($ErrorMessage) {
            Write-Host "  Error: $ErrorMessage" -ForegroundColor Yellow
        }
    }
    Write-Host ""
}

# ==============================================================================
# TEST SUITE: Essential Tools
# ==============================================================================

Write-ColorOutput Cyan "`n========================================"
Write-ColorOutput Cyan "TESTING ESSENTIAL TOOLS"
Write-ColorOutput Cyan "========================================`n"

# Test 1: get_symbols - Get all symbols in SimpleTestClass.cs
Write-ColorOutput Yellow "Test 1: get_symbols - Get all symbols in SimpleTestClass.cs"
$result = Invoke-McpTool -ToolName "get_symbols" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    detail_level = "Full"
}
Add-TestResult -TestName "get_symbols - SimpleTestClass.cs" -Tool "get_symbols" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    detail_level = "Full"
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 2: get_symbols - Filter by method kind
Write-ColorOutput Yellow "Test 2: get_symbols - Filter by Method kind"
$result = Invoke-McpTool -ToolName "get_symbols" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    filter_kinds = @("Method")
    detail_level = "Summary"
}
Add-TestResult -TestName "get_symbols - Filter by Method" -Tool "get_symbols" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    filter_kinds = @("Method")
    detail_level = "Summary"
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 3: get_symbols - Fuzzy file path matching
Write-ColorOutput Yellow "Test 3: get_symbols - Fuzzy file path matching (filename only)"
$result = Invoke-McpTool -ToolName "get_symbols" -Arguments @{
    file_path = "SimpleTestClass.cs"
    detail_level = "Compact"
}
Add-TestResult -TestName "get_symbols - Fuzzy path matching" -Tool "get_symbols" -Input @{
    file_path = "SimpleTestClass.cs"
    detail_level = "Compact"
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 4: go_to_definition - Navigate to Calculate method
Write-ColorOutput Yellow "Test 4: go_to_definition - Navigate to Calculate method"
$result = Invoke-McpTool -ToolName "go_to_definition" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 22
    symbol_name = "Calculate"
    detail_level = "Full"
}
Add-TestResult -TestName "go_to_definition - Calculate method" -Tool "go_to_definition" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 22
    symbol_name = "Calculate"
    detail_level = "Full"
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 5: find_references - Find references to TestMethod
Write-ColorOutput Yellow "Test 5: find_references - Find references to TestMethod"
$result = Invoke-McpTool -ToolName "find_references" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 17
    symbol_name = "TestMethod"
    include_context = $true
    context_lines = 2
}
Add-TestResult -TestName "find_references - TestMethod" -Tool "find_references" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 17
    symbol_name = "TestMethod"
    include_context = $true
    context_lines = 2
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 6: resolve_symbol - Resolve SimpleTestClass symbol
Write-ColorOutput Yellow "Test 6: resolve_symbol - Resolve SimpleTestClass"
$result = Invoke-McpTool -ToolName "resolve_symbol" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 6
    symbol_name = "SimpleTestClass"
    detail_level = "Standard"
}
Add-TestResult -TestName "resolve_symbol - SimpleTestClass" -Tool "resolve_symbol" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 6
    symbol_name = "SimpleTestClass"
    detail_level = "Standard"
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 7: search_symbols - Search for all *Controller* symbols
Write-ColorOutput Yellow "Test 7: search_symbols - Search for *Controller*"
$result = Invoke-McpTool -ToolName "search_symbols" -Arguments @{
    query = "*Controller*"
    detail_level = "Summary"
    max_results = 10
}
Add-TestResult -TestName "search_symbols - *Controller*" -Tool "search_symbols" -Input @{
    query = "*Controller*"
    detail_level = "Summary"
    max_results = 10
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 8: search_symbols - Search for TestAssets.* namespace
Write-ColorOutput Yellow "Test 8: search_symbols - Search for TestAssets.*"
$result = Invoke-McpTool -ToolName "search_symbols" -Arguments @{
    query = "TestAssets.*"
    detail_level = "Compact"
    max_results = 20
}
Add-TestResult -TestName "search_symbols - TestAssets.*" -Tool "search_symbols" -Input @{
    query = "TestAssets.*"
    detail_level = "Compact"
    max_results = 20
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# ==============================================================================
# TEST SUITE: HighValue Tools
# ==============================================================================

Write-ColorOutput Cyan "`n========================================"
Write-ColorOutput Cyan "TESTING HIGHVALUE TOOLS"
Write-ColorOutput Cyan "========================================`n"

# Test 9: get_inheritance_hierarchy - BaseController inheritance
Write-ColorOutput Yellow "Test 9: get_inheritance_hierarchy - BaseController"
$result = Invoke-McpTool -ToolName "get_inheritance_hierarchy" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs"
    line_number = 6
    symbol_name = "BaseController"
    include_derived = $true
    max_derived_depth = 2
}
Add-TestResult -TestName "get_inheritance_hierarchy - BaseController" -Tool "get_inheritance_hierarchy" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs"
    line_number = 6
    symbol_name = "BaseController"
    include_derived = $true
    max_derived_depth = 2
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 10: get_inheritance_hierarchy - DerivedTestClass inheritance
Write-ColorOutput Yellow "Test 10: get_inheritance_hierarchy - DerivedTestClass"
$result = Invoke-McpTool -ToolName "get_inheritance_hierarchy" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 36
    symbol_name = "DerivedTestClass"
    include_derived = $false
}
Add-TestResult -TestName "get_inheritance_hierarchy - DerivedTestClass" -Tool "get_inheritance_hierarchy" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 36
    symbol_name = "DerivedTestClass"
    include_derived = $false
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 11: get_call_graph - Call graph for Execute method
Write-ColorOutput Yellow "Test 11: get_call_graph - Execute method"
$result = Invoke-McpTool -ToolName "get_call_graph" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs"
    line_number = 32
    symbol_name = "Execute"
    direction = "Out"
    max_depth = 2
    include_external_calls = $false
}
Add-TestResult -TestName "get_call_graph - Execute method" -Tool "get_call_graph" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs"
    line_number = 32
    symbol_name = "Execute"
    direction = "Out"
    max_depth = 2
    include_external_calls = $false
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 12: get_type_members - Members of BaseController
Write-ColorOutput Yellow "Test 12: get_type_members - BaseController members"
$result = Invoke-McpTool -ToolName "get_type_members" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs"
    line_number = 6
    symbol_name = "BaseController"
    include_inherited = $true
}
Add-TestResult -TestName "get_type_members - BaseController" -Tool "get_type_members" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs"
    line_number = 6
    symbol_name = "BaseController"
    include_inherited = $true
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 13: get_type_members - Filter by Method kind
Write-ColorOutput Yellow "Test 13: get_type_members - Filter by Method kind"
$result = Invoke-McpTool -ToolName "get_type_members" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 6
    symbol_name = "SimpleTestClass"
    filter_kinds = @("Method")
}
Add-TestResult -TestName "get_type_members - Filter by Method" -Tool "get_type_members" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 6
    symbol_name = "SimpleTestClass"
    filter_kinds = @("Method")
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# ==============================================================================
# TEST SUITE: Optimization Tools
# ==============================================================================

Write-ColorOutput Cyan "`n========================================"
Write-ColorOutput Cyan "TESTING OPTIMIZATION TOOLS"
Write-ColorOutput Cyan "========================================`n"

# Test 14: get_symbol_complete - Get complete symbol info
Write-ColorOutput Yellow "Test 14: get_symbol_complete - Complete info for Calculate"
$result = Invoke-McpTool -ToolName "get_symbol_complete" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 22
    symbol_name = "Calculate"
    sections = "All"
    detail_level = "Standard"
    include_references = $true
    max_references = 10
    include_inheritance = $false
    include_call_graph = $true
    call_graph_max_depth = 1
}
Add-TestResult -TestName "get_symbol_complete - Calculate method" -Tool "get_symbol_complete" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 22
    symbol_name = "Calculate"
    sections = "All"
    detail_level = "Standard"
    include_references = $true
    max_references = 10
    include_inheritance = $false
    include_call_graph = $true
    call_graph_max_depth = 1
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 15: batch_get_symbols - Batch query multiple symbols
Write-ColorOutput Yellow "Test 15: batch_get_symbols - Batch query"
$result = Invoke-McpTool -ToolName "batch_get_symbols" -Arguments @{
    symbols = @(
        @{
            file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
            line_number = 22
            symbol_name = "Calculate"
        },
        @{
            file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
            line_number = 17
            symbol_name = "TestMethod"
        },
        @{
            file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
            line_number = 6
            symbol_name = "SimpleTestClass"
        }
    )
    detail_level = "Summary"
    max_concurrency = 3
}
Add-TestResult -TestName "batch_get_symbols - Batch query" -Tool "batch_get_symbols" -Input @{
    symbols = @(
        @{file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"; line_number = 22; symbol_name = "Calculate"},
        @{file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"; line_number = 17; symbol_name = "TestMethod"},
        @{file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"; line_number = 6; symbol_name = "SimpleTestClass"}
    )
    detail_level = "Summary"
    max_concurrency = 3
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 16: get_diagnostics - Get diagnostics for SimpleTestClass.cs
Write-ColorOutput Yellow "Test 16: get_diagnostics - File specific diagnostics"
$result = Invoke-McpTool -ToolName "get_diagnostics" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    include_warnings = $true
    include_info = $true
    include_hidden = $false
}
Add-TestResult -TestName "get_diagnostics - SimpleTestClass.cs" -Tool "get_diagnostics" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    include_warnings = $true
    include_info = $true
    include_hidden = $false
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# Test 17: get_diagnostics - Workspace-wide diagnostics
Write-ColorOutput Yellow "Test 17: get_diagnostics - Workspace diagnostics"
$result = Invoke-McpTool -ToolName "get_diagnostics" -Arguments @{
    include_warnings = $false
    include_info = $false
    severity_filter = @("Error")
}
Add-TestResult -TestName "get_diagnostics - Workspace" -Tool "get_diagnostics" -Input @{
    include_warnings = $false
    include_info = $false
    severity_filter = @("Error")
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content))

# ==============================================================================
# EDGE CASE TESTS
# ==============================================================================

Write-ColorOutput Cyan "`n========================================"
Write-ColorOutput Cyan "TESTING EDGE CASES"
Write-ColorOutput Cyan "========================================`n"

# Test 18: File not found error
Write-ColorOutput Yellow "Test 18: get_symbols - Non-existent file (should error)"
$result = Invoke-McpTool -ToolName "get_symbols" -Arguments @{
    file_path = "nonexistent.cs"
}
Add-TestResult -TestName "get_symbols - Non-existent file error" -Tool "get_symbols" -Input @{
    file_path = "nonexistent.cs"
} -Output $result -Passed ($null -ne $result -and ($result.error -or $result.result?.error))

# Test 19: Symbol not found error
Write-ColorOutput Yellow "Test 19: go_to_definition - Invalid symbol (should error)"
$result = Invoke-McpTool -ToolName "go_to_definition" -Arguments @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 22
    symbol_name = "NonExistentMethod"
}
Add-TestResult -TestName "go_to_definition - Invalid symbol error" -Tool "go_to_definition" -Input @{
    file_path = "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs"
    line_number = 22
    symbol_name = "NonExistentMethod"
} -Output $result -Passed ($null -ne $result -and ($result.error -or $result.result?.error))

# Test 20: search_symbols - Empty search (should return limited/no results)
Write-ColorOutput Yellow "Test 20: search_symbols - Specific query with no matches"
$result = Invoke-McpTool -ToolName "search_symbols" -Arguments @{
    query = "NonExistentSymbolXYZ123"
    detail_level = "Compact"
}
Add-TestResult -TestName "search_symbols - No matches query" -Tool "search_symbols" -Input @{
    query = "NonExistentSymbolXYZ123"
    detail_level = "Compact"
} -Output $result -Passed ($null -ne $result -and ($result.result -or $result.content -or $result.error))

# ==============================================================================
# FINAL SUMMARY
# ==============================================================================

$testResults.summary.endTime = (Get-Date -Format "o")

Write-ColorOutput Cyan "`n========================================"
Write-ColorOutput Cyan "TEST SUMMARY"
Write-ColorOutput Cyan "========================================`n"

Write-Host "Total Tests: $($testResults.summary.total)" -ForegroundColor White
Write-Host "Passed: $($testResults.summary.passed)" -ForegroundColor Green
Write-Host "Failed: $($testResults.summary.failed)" -ForegroundColor Red
$passRate = if ($testResults.summary.total -gt 0) { [math]::Round(($testResults.summary.passed / $testResults.summary.total) * 100, 2) } else { 0 }
Write-Host "Pass Rate: $passRate%" -ForegroundColor $(if ($passRate -ge 80) { "Green" } elseif ($passRate -ge 50) { "Yellow" } else { "Red" })

# Save results to JSON
$testResults | ConvertTo-Json -Depth 10 | Out-File -FilePath $OutputPath -Encoding UTF8
Write-Host "`nTest results saved to: $OutputPath" -ForegroundColor Cyan

# Exit with appropriate code
exit $(if ($testResults.summary.failed -eq 0) { 0 } else { 1 })
