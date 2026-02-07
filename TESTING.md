# Testing Guide

This document explains how to test the CSharp MCP Server.

## Test Overview

| Test Type | Location | Purpose |
|-----------|----------|---------|
| **Unit Tests** | `tests/CSharpMcp.Tests/` | Test individual components |
| **Integration Tests** | `tests/CSharpMcp.IntegrationTests/` | Verify project structure |
| **Manual Tests** | `test-mcp-server.ps1` | Interactive testing |
| **MCP Client Test** | Real MCP client | End-to-end testing |

## Running Tests

### Unit Tests

```bash
# Run all tests
dotnet test

# Run specific test project
dotnet test tests/CSharpMcp.Tests
dotnet test tests/CSharpMcp.IntegrationTests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Integration Tests

```bash
dotnet test tests/CSharpMcp.IntegrationTests
```

These tests verify:
- Project structure is valid
- All tool classes exist
- Models are properly defined
- Analyzer interfaces are implemented

## Manual Testing with MCP Client

### Option 1: Using Claude Desktop

1. **Build the server**:
   ```bash
   dotnet build src/CSharpMcp.Server
   ```

2. **Configure Claude Desktop** (`claude_desktop_config.json`):
   ```json
   {
     "mcpServers": {
       "csharp": {
         "command": "dotnet",
         "args": [
           "run",
           "--project",
           "C:\\Path\\To\\CSharpMcp.Server\\CSharpMcp.Server.csproj"
         ]
       }
     }
   }
   ```

3. **Restart Claude Desktop**

4. **Test commands in Claude**:
   - "List all tools in the csharp MCP server"
   - "Get all symbols in MyClass.cs"
   - "Find references to MyMethod"
   - "Show the call graph for MyMethod"
   - "Get the inheritance hierarchy of MyClass"

### Option 2: Using Published Executable

1. **Build self-contained executable**:
   ```bash
   .\publish.bat
   ```

2. **Configure Claude Desktop**:
   ```json
   {
     "mcpServers": {
       "csharp": {
         "command": "C:\\Path\\To\\CSharpMcp\\publish\\CSharpMcp.Server.exe"
       }
     }
   }
   ```

### Option 3: Using Inspector CLI

```bash
# Install MCP Inspector
npm install -g @modelcontextprotocol/inspector

# Run inspector against the server
npx @modelcontextprotocol/inspector dotnet run --project src/CSharpMcp.Server
```

## Test Scenarios

### Essential Tools

#### 1. Get Symbols
```
Request:
{
  "name": "get_symbols",
  "arguments": {
    "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
    "filter_kinds": ["Method", "Class"],
    "detail_level": "Standard"
  }
}

Expected: List of classes and methods in the file
```

#### 2. Go To Definition
```
Request:
{
  "name": "go_to_definition",
  "arguments": {
    "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
    "line_number": 22,
    "symbol_name": "Calculate"
  }
}

Expected: Definition location of Calculate method
```

#### 3. Find References
```
Request:
{
  "name": "find_references",
  "arguments": {
    "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
    "line_number": 10,
    "include_declarations": true
  }
}

Expected: All references to the symbol at line 10
```

#### 4. Resolve Symbol
```
Request:
{
  "name": "resolve_symbol",
  "arguments": {
    "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
    "line_number": 22,
    "sections": ["All"]
  }
}

Expected: Complete symbol information including documentation
```

#### 5. Search Symbols
```
Request:
{
  "name": "search_symbols",
  "arguments": {
    "query": "Calculate",
    "max_results": 10
  }
}

Expected: All symbols matching "Calculate" in the workspace
```

### HighValue Tools

#### 6. Get Inheritance Hierarchy
```
Request:
{
  "name": "get_inheritance_hierarchy",
  "arguments": {
    "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
    "line_number": 37,
    "include_derived_classes": true,
    "max_depth": 5
  }
}

Expected: Inheritance chain for DerivedTestClass
```

#### 7. Get Call Graph
```
Request:
{
  "name": "get_call_graph",
  "arguments": {
    "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
    "line_number": 10,
    "direction": "Both",
    "max_depth": 2
  }
}

Expected: Call graph showing callers and callees
```

#### 8. Get Type Members
```
Request:
{
  "name": "get_type_members",
  "arguments": {
    "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
    "line_number": 6
  }
}

Expected: All members of SimpleTestClass
```

### Optimization Tools

#### 9. Get Symbol Complete
```
Request:
{
  "name": "get_symbol_complete",
  "arguments": {
    "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
    "line_number": 22,
    "sections": ["Base", "Metadata", "Inheritance", "CallGraph", "References"]
  }
}

Expected: Combined information from multiple sources
```

#### 10. Batch Get Symbols
```
Request:
{
  "name": "batch_get_symbols",
  "arguments": {
    "symbols": [
      {"file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "line_number": 10},
      {"file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "line_number": 22}
    ],
    "detail_level": "Standard"
  }
}

Expected: Information for both symbols in parallel
```

#### 11. Get Diagnostics
```
Request:
{
  "name": "get_diagnostics",
  "arguments": {
    "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
    "include_warnings": true
  }
}

Expected: Compilation errors and warnings
```

## Debugging

### Enable Verbose Logging

Edit `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "CSharpMcp.Server": "Trace"
    }
  }
}
```

### Common Issues

| Issue | Solution |
|-------|----------|
| Server not starting | Check .NET SDK version (requires .NET 10.0) |
| No tools available | Verify server initialized correctly |
| File not found | Use absolute paths or load workspace first |
| Out of memory | Reduce workspace size or use filtering |

## Performance Testing

### Benchmark Large Codebases

```bash
# Create a large test project
cd tests/TestAssets/LargeProject
dotnet new sln -n LargeTest

# Add multiple projects with many files
# Then test with:
dotnet run --project src/CSharpMcp.Server --workspace-path tests/TestAssets/LargeProject/LargeTest.sln
```

### Measure Token Usage

The server includes token optimization. Check response sizes:
```
detail_level: Compact  - ~50 tokens per symbol
detail_level: Summary  - ~100 tokens per symbol
detail_level: Standard - ~200 tokens per symbol
detail_level: Full     - ~500+ tokens per symbol
```

## Continuous Integration

### GitHub Actions Example

```yaml
name: Test

on: [push, pull_request]

jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore
      - run: dotnet build --no-restore
      - run: dotnet test --no-build --verbosity normal
```

## Next Steps

1. Add tests for new features in `tests/CSharpMcp.Tests/`
2. Update integration tests when adding new tools
3. Test with real-world C# projects
4. Report issues found during testing
