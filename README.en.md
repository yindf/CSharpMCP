# CSharp MCP Server

> A Roslyn-based Model Context Protocol Server providing powerful C# code analysis and navigation capabilities

[**中文**](README.zh.md) | **English**

[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Introduction

CSharp MCP Server is a Model Context Protocol (MCP) Server built with Roslyn API, providing powerful C# code analysis capabilities for AI assistants. Unlike LSP-based implementations, this server accesses complete semantic information for more accurate and in-depth code analysis.

## Features

### Core Advantages

- **🎯 Complete Semantic Analysis**: Direct use of Roslyn API for full symbol scope information and type inference
- **🌲 Inheritance Hierarchy Analysis**: View complete inheritance chains and derived types
- **📊 Call Graph Analysis**: Analyze method callers and callees, calculate cyclomatic complexity
- **⚡ Token Optimization**: Tiered responses, smart truncation, batch queries to reduce token usage
- **🔍 Advanced Code Navigation**: Fuzzy matching, batch operations, on-demand loading

### Tool Categories

| Category | Tool | Description |
|----------|------|-------------|
| **Essential** | `get_symbols` | Get all symbols in a document |
| | `go_to_definition` | Navigate to symbol definition |
| | `find_references` | Find all references to a symbol |
| | `resolve_symbol` | Get complete symbol information |
| | `search_symbols` | Search symbols across the workspace |
| **HighValue** | `get_inheritance_hierarchy` | Get type inheritance hierarchy |
| | `get_call_graph` | Get method call graph |
| | `get_type_members` | Get type members |
| **Optimization** | `get_symbol_complete` | Get complete symbol info from multiple sources |
| | `batch_get_symbols` | Batch get symbol information |
| | `get_diagnostics` | Get compilation diagnostics |

## Quick Start

### Installation

```bash
# Clone the repository
git clone https://github.com/yindf/CSharpLSP.git
cd CSharpLSP

# Restore dependencies
dotnet restore

# Build
dotnet build
```

### Usage

1. **Start the server**:

```bash
dotnet run --project src/CSharpMcp.Server
```

2. **Configure in MCP client**:

```json
{
  "mcpServers": {
    "csharp": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/CSharpMcp.Server/CSharpMcp.Server.csproj"]
    }
  }
}
```

### Configure Workspace

Load the workspace on first use:

```json
{
  "method": "workspace/load",
  "params": {
    "path": "path/to/your/solution.sln"
  }
}
```

## Tool Usage Examples

### Get All Methods in a File

```json
{
  "name": "get_symbols",
  "arguments": {
    "file_path": "MyClass.cs",
    "filter_kinds": ["Method"],
    "detail_level": "Summary"
  }
}
```

### View Method Call Graph

```json
{
  "name": "get_call_graph",
  "arguments": {
    "file_path": "MyClass.cs",
    "line_number": 25,
    "symbol_name": "MyMethod",
    "direction": "Both",
    "max_depth": 2
  }
}
```

### Batch Get Symbol Information

```json
{
  "name": "batch_get_symbols",
  "arguments": {
    "symbols": [
      {"file_path": "MyClass.cs", "line_number": 25},
      {"file_path": "MyClass.cs", "line_number": 50}
    ],
    "detail_level": "Standard"
  }
}
```

### Get Compilation Diagnostics

```json
{
  "name": "get_diagnostics",
  "arguments": {
    "file_path": "MyClass.cs",
    "include_warnings": true
  }
}
```

## Project Structure

```
CSharpMcp/
├── src/
│   ├── CSharpMcp.Server/          # MCP Server main project
│   │   ├── Tools/                 # MCP tool implementations
│   │   │   ├── Essential/         # Core tools
│   │   │   ├── HighValue/         # Advanced tools
│   │   │   └── Optimization/      # Optimization tools
│   │   ├── Roslyn/                # Roslyn wrapper layer
│   │   │   ├── WorkspaceManager.cs
│   │   │   ├── SymbolExtensions.cs
│   │   │   ├── InheritanceAnalyzer.cs
│   │   │   └── SymbolResolver.cs
│   │   ├── Models/                # Data models
│   │   └── Program.cs
│   │
│   ├── CSharpMcp.Tests/           # Unit tests
│   └── CSharpMcp.IntegrationTests/ # Integration tests
│
├── docs/
│   └── API.md                     # API documentation
│
└── README.md
```

## Tech Stack

| Component | Version | Description |
|-----------|---------|-------------|
| .NET | 10.0 | Latest LTS version |
| Roslyn | 4.* | Microsoft.CodeAnalysis |
| MCP SDK | 0.2.0-preview | Model Context Protocol |
| xUnit | 2.* | Testing framework |

## Important Notes

### Output Format

**All tool outputs are in Markdown format**, making it easy for LLMs to understand and process directly. The output structure is clear, containing headings, lists, code blocks, and other formatted elements.

#### Output Examples

**`get_definition` output example:**

```markdown
## Symbol: `ProcessData`

**Basic Info**:
- **Kind**: Method
- **Accessibility**: public
- **Containing Type**: MyNamespace.DataService
- **Location**: `DataService.cs:45-78`

**Signature**:
```csharp
public async Task<Result> ProcessData(
    string input,
    CancellationToken cancellationToken = default)
```

**Documentation**:
Process input data and return result

**Implementation**:
```csharp
public async Task<Result> ProcessData(
    string input,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrEmpty(input))
        throw new ArgumentException("Input cannot be null");

    var data = await _repository.GetDataAsync(cancellationToken);
    return _processor.Transform(data);
}
```
```

**`find_references` output example:**

```markdown
## References: `ProcessData`

**Found 5 references in 3 files**

### DataService.cs
- L52: `await ProcessData(input, token);`
- L89: `var result = ProcessData(data);`

### DataController.cs
- L34: `return await _service.ProcessData(request);`

### DataServiceTests.cs
- L15: `var result = _service.ProcessData("test");`
- L22: `await _service.ProcessData(null);`
```

**`get_call_graph` output example:**

```markdown
## Call Graph: `ProcessData`

**Callers (2)**:
- `DataController.Post()` - DataController.cs:34
- `DataController.Get()` - DataController.cs:56

**Callees (3)**:
- `_repository.GetDataAsync()` - Repository.cs:23
- `_processor.Transform()` - Processor.cs:15
- `ThrowHelper.ArgError()` - ThrowHelper.cs:8
```

### Token Optimization Strategies

This server is deeply optimized for LLM token usage, helping reduce call count and token consumption:

#### 1. Get Complete Information in One Call (Recommended)

**Avoid Multiple Calls**: Use `get_symbol_complete` or `get_symbol_info` to get complete symbol information at once, including:
- Symbol signature and documentation
- Source code implementation
- Reference list
- Inheritance hierarchy
- Call graph

```json
{
  "name": "get_symbol_info",
  "arguments": {
    "symbol_name": "MyClass",
    "max_body_lines": 50,
    "max_references": 10
  }
}
```

#### 2. Batch Queries

**Process Multiple Symbols in Parallel**: Use `batch_get_symbols` to get information for multiple symbols at once:

```json
{
  "name": "batch_get_symbols",
  "arguments": {
    "symbols": [
      {"symbol_name": "Method1", "line_number": 25},
      {"symbol_name": "Method2", "line_number": 50}
    ],
    "detail_level": "Summary"
  }
}
```

#### 3. Tiered Responses

Use the `detail_level` parameter to control output verbosity:

- `Compact` - Name and location only (quick browsing)
- `Summary` - Brief information (recommended default)
- `Standard` - Standard information
- `Full` - Complete information (use only when needed)

#### 4. Smart Truncation Parameters

- `body_max_lines` / `max_body_lines` - Limit source code lines (default 50)
- `max_references` - Limit reference count (default 10)
- `max_results` - Limit search results
- `max_callers` / `max_callees` - Limit call graph depth

#### 5. On-Demand Loading

- `sections` parameter - Specify required information sections (Signature, Documentation, Body, References, etc.)
- `filter_kinds` parameter - Filter symbol types (Method, Property, Field, etc.)
- `include_body` - Whether to include implementation code (default true)
- `include_inherited` - Whether to include inherited members

#### 6. Fuzzy Matching

All tools support **fuzzy path matching**, no need to provide full paths:
- Filename only: `"MyClass.cs"`
- Relative path: `"./Services/MyService.cs"`
- Line number for precise location: `line_number` parameter helps locate symbols accurately

## Development

### Requirements

- .NET 10.0 SDK
- Visual Studio 2022 or Rider

### Build

```bash
dotnet build
```

### Run Tests

```bash
# Unit tests
dotnet test tests/CSharpMcp.Tests

# Integration tests
dotnet test tests/CSharpMcp.IntegrationTests
```

## Documentation

- [API Documentation](docs/API.en.md) - Detailed API reference

## Roadmap

### Completed ✅

- [x] Phase 1: Infrastructure (WorkspaceManager, SymbolAnalyzer, Cache)
- [x] Phase 2: Essential Tools (5 core tools)
- [x] Phase 3: HighValue Tools (3 advanced analysis tools)
- [x] Phase 4: Optimization Tools (3 optimization tools)

## Debug

```bash
npx @modelcontextprotocol/inspector dotnet run --project src/CSharpMcp.Server
```

## Contributing

Contributions are welcome! Please check [CONTRIBUTING.md](CONTRIBUTING.md) for details.

## License

MIT License - See [LICENSE](LICENSE) file for details

## Acknowledgments

- [Microsoft.CodeAnalysis](https://github.com/dotnet/roslyn) - Roslyn compiler platform
- [Model Context Protocol](https://modelcontextprotocol.io) - MCP specification
