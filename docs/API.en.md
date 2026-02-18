# CSharp MCP Server - API Documentation

[**中文**](API.md) | **English**

## Overview

CSharp MCP Server is a Roslyn-based Model Context Protocol (MCP) Server providing powerful C# code analysis and navigation capabilities.

### Core Features

- **Beyond LSP Limitations**: Direct use of Roslyn API for complete symbol scope information
- **Rich Semantic Analysis**: Inheritance hierarchy, call graph, type member analysis
- **Token Optimization**: Tiered responses, smart truncation
- **Batch Operations**: Reduce interaction count

---

## Tool Categories

### Essential Tools (Core Tools)

#### `get_symbols`

Get all symbols in a document.

**Parameters**:
```json
{
  "file_path": "string (required) - File path, supports absolute paths, relative paths, filename-only fuzzy matching",
  "line_number": "int? (optional) - Line number for fuzzy matching",
  "symbol_name": "string? (optional) - Symbol name for verification and fuzzy matching",
  "detail_level": "DetailLevel (optional) - Output detail level: Compact, Summary, Standard, Full",
  "include_body": "bool (optional) - Whether to include method body, default true",
  "body_max_lines": "int (optional) - Maximum method body lines, default 100",
  "filter_kinds": "SymbolKind[] (optional) - Symbol type filter"
}
```

**Response**:
```markdown
## Symbols: FileName.cs

**Total: N symbol(s)**

- **SymbolName** (Method):12-45 - public static void MethodName(string param)
  - Documentation...
```

---

#### `go_to_definition`

Navigate to symbol definition.

**Parameters**:
```json
{
  "file_path": "string (required)",
  "line_number": "int? (optional)",
  "symbol_name": "string? (optional)",
  "detail_level": "DetailLevel (optional)",
  "include_body": "bool (optional)",
  "body_max_lines": "int (optional)"
}
```

**Response**:
```markdown
### Definition: `MethodName`

(lines 12-45)

**Signature**:
```csharp
void MethodName(string param);
```

**Implementation**:
```csharp
public void MethodName(string param)
{
    // ...
}
```
```

---

#### `find_references`

Find all references to a symbol.

**Parameters**:
```json
{
  "file_path": "string (required)",
  "line_number": "int? (optional)",
  "symbol_name": "string? (optional)",
  "include_context": "bool (optional) - Whether to include context code",
  "context_lines": "int (optional) - Context code line count"
}
```

**Response**:
```markdown
## References: `MethodName`

**Found 10 reference(s)**

### FileName.cs

- Line 25: ContainingMethod
  ```csharp
  MethodName("value");
  ```
```

---

#### `resolve_symbol`

Get complete symbol information (including documentation, definition, and partial references).

**Parameters**:
```json
{
  "file_path": "string (required)",
  "line_number": "int? (optional)",
  "symbol_name": "string? (optional)",
  "detail_level": "DetailLevel (optional)",
  "include_body": "bool (optional)",
  "body_max_lines": "int (optional)"
}
```

**Response**:
```markdown
## Symbol: `MethodName`

**Location**:
- File: path/to/file.cs
- Lines: 12-45

**Type**:
- Kind: Method
- Containing Type: ClassName
- Namespace: MyNamespace

**Modifiers**:
- Accessibility: Public
- Static: yes

**Signature**:
```csharp
void MethodName(string param);
```

**References** (5 found):
- file.cs:25 in OtherMethod
```

---

#### `search_symbols`

Search symbols across the entire workspace.

**Parameters**:
```json
{
  "query": "string (required) - Search query, supports wildcards like My.*, *.Controller",
  "detail_level": "DetailLevel (optional)",
  "max_results": "int (optional) - Maximum result count, default 100"
}
```

**Response**:
```markdown
## Search Results: "MyClass.*"

**Found 5 symbol(s)**

### Namespace: MyNamespace

- **MyClass** (Class) - MyClass.cs:10
- **MyMethod** (Method) - MyClass.cs:25
```

---

### HighValue Tools (Advanced Tools)

#### `get_inheritance_hierarchy`

Get type inheritance hierarchy.

**Parameters**:
```json
{
  "file_path": "string (required)",
  "line_number": "int? (optional)",
  "symbol_name": "string? (optional)",
  "include_derived": "bool (optional) - Whether to include derived types",
  "max_derived_depth": "int (optional) - Maximum derived type depth"
}
```

**Response**:
```markdown
## Inheritance Hierarchy: `MyClass`

**Base Types**:
- BaseType1
- BaseType2

**Implemented Interfaces**:
- IEnumerable<T>
- IDisposable

**Derived Types** (3, depth: 2):
- **DerivedClass** (Class) - DerivedClass.cs:10
```

---

#### `get_call_graph`

Get method call graph.

**Parameters**:
```json
{
  "file_path": "string (required)",
  "line_number": "int? (optional)",
  "symbol_name": "string? (optional)",
  "direction": "CallGraphDirection (optional) - Call direction: Both, In, Out",
  "max_depth": "int (optional) - Maximum depth",
  "include_external_calls": "bool (optional) - Whether to include external calls"
}
```

**Response**:
```markdown
## Call Graph: `MethodName`

**Statistics**:
- Total callers: 5
- Total callees: 10
- Cyclomatic complexity: 3

**Called By** (5):
- **CallerMethod** - CallerClass.cs:20
  - at CallerMethod (CallerClass.cs:20)

**Calls** (10):
- **CalledMethod** - UtilClass.cs:50
```

---

#### `get_type_members`

Get type members.

**Parameters**:
```json
{
  "file_path": "string (required)",
  "line_number": "int? (optional)",
  "symbol_name": "string? (optional)",
  "include_inherited": "bool (optional) - Whether to include inherited members",
  "filter_kinds": "SymbolKind[] (optional) - Member type filter"
}
```

**Response**:
```markdown
## Type Members: `MyClass`

**Total: 15 member(s)**

### Method
- **Method1** (public static Method) - MyClass.cs:25
- **Method2** (public virtual Method) - MyClass.cs:50
```

---

### Optimization Tools

#### `get_symbol_complete`

Get complete symbol information from multiple sources to reduce API calls.

**Parameters**:
```json
{
  "file_path": "string (required)",
  "line_number": "int? (optional)",
  "symbol_name": "string? (optional)",
  "sections": "SymbolCompleteSections (optional) - Information sections to retrieve",
  "detail_level": "DetailLevel (optional)",
  "body_max_lines": "int (optional)",
  "include_references": "bool (optional)",
  "max_references": "int (optional)",
  "include_inheritance": "bool (optional)",
  "include_call_graph": "bool (optional)",
  "call_graph_max_depth": "int (optional)"
}
```

`SymbolCompleteSections` is a flags enum:
- `Basic = 1` - Basic information
- `Signature = 2` - Signature information
- `Documentation = 4` - Documentation comments
- `SourceCode = 8` - Source code
- `References = 16` - Reference locations
- `Inheritance = 32` - Inheritance hierarchy
- `CallGraph = 64` - Call graph
- `All = 127` - All information

**Response**:
```markdown
## Symbol: `MethodName`

**Type**: Method

**Signature**:
```csharp
void MethodName(string param);
```

**Documentation**:
Method description...

**Inheritance**:
- Base Types: BaseType1
- Interfaces: IEnumerable<T>

**Call Graph**:
- Callers: 5
- Callees: 10
- Complexity: 3
```

---

#### `batch_get_symbols`

Batch get symbol information using parallel processing for better performance.

**Parameters**:
```json
{
  "symbols": "FileLocationParams[] (required) - Symbol location list",
  "detail_level": "DetailLevel (optional)",
  "include_body": "bool (optional)",
  "body_max_lines": "int (optional)",
  "max_concurrency": "int (optional) - Maximum concurrency"
}
```

**Response**:
```markdown
## Batch Symbol Query Results

**Total**: 3 | **Success**: 2 | **Errors**: 1

### ✅ Method1
- Type: Method
- Location: File1.cs:25
- Signature: void Method1()

### ✅ Method2
- Type: Method
- Location: File1.cs:50
- Signature: int Method2(string param)

### ❌ Unknown
Error: Symbol not found
```

---

#### `get_diagnostics`

Get compilation diagnostics (errors, warnings, and info).

**Parameters**:
```json
{
  "file_path": "string? (optional) - File path, if not specified gets workspace-wide diagnostics",
  "include_warnings": "bool (optional) - Whether to include warnings",
  "include_info": "bool (optional) - Whether to include info",
  "include_hidden": "bool (optional) - Whether to include hidden diagnostics",
  "severity_filter": "DiagnosticSeverity[] (optional) - Severity filter"
}
```

`DiagnosticSeverity` enum:
- `Error`
- `Warning`
- `Info`
- `Hidden`

**Response**:
```markdown
## Diagnostics Report

**Summary**:
- Errors: 2
- Warnings: 5
- Info: 1
- Files affected: 3

### File1.cs

- ❌ **CS0219** (Line 15): Variable is assigned but its value is never used
- ⚠️ **CS0168** (Line 20): The variable 'x' is declared but never used
```

---

## Enum Types

### `DetailLevel`

Controls output detail level for token optimization.

- `Compact` - Minimal information, name and location only
- `Summary` - Brief information, includes basic signature
- `Standard` - Standard information, includes documentation and partial source code
- `Full` - Complete information, includes all source code

### `SymbolKind`

Symbol type classification.

**Types**:
- `Class`, `Struct`, `Interface`, `Enum`, `Record`, `Delegate`, `Attribute`

**Members**:
- `Method`, `Property`, `Field`, `Event`, `Constructor`, `Destructor`

**Others**:
- `Namespace`, `Parameter`, `Local`, `TypeParameter`, `Unknown`

### `Accessibility`

Accessibility levels.

- `Public`, `Internal`, `Protected`, `ProtectedInternal`, `PrivateProtected`, `Private`, `NotApplicable`

---

## File Path Matching

Tools support multiple file path formats:

1. **Absolute path**: `C:\Project\MyFile.cs`
2. **Relative path**: `src\MyFile.cs`
3. **Filename only**: `MyFile.cs` (fuzzy match files in workspace)

---

## Token Optimization Strategies

### 1. Tiered Responses

Use `detail_level` parameter to control output verbosity:

- **Quick Browse**: Use `Compact` to get symbol list
- **Understand Code**: Use `Summary` or `Standard` to view signatures and documentation
- **Deep Dive**: Use `Full` to get complete source code

### 2. Smart Truncation

- `body_max_lines` limits returned source code lines
- `max_references` limits returned reference count
- `max_results` limits search result count

### 3. Batch Operations

- Use `batch_get_symbols` to query multiple symbols at once
- Use `get_symbol_complete` to get all needed information

### 4. On-Demand Loading

- Use `sections` parameter to specify required information sections
- Use `filter_kinds` to filter unwanted symbol types

---

## Error Handling

All tools return on error:

```json
{
  "error": "Error message"
}
```

Common errors:
- File not found: `"File not found: path/to/file.cs"`
- Symbol not found: `"Symbol not found: SymbolName"`
- Workspace not loaded: `"Workspace not loaded"`
- Invalid parameters: `"Invalid parameters type"`

---

## Usage Examples

### Example 1: Get All Methods in a File

```json
{
  "file_path": "MyClass.cs",
  "filter_kinds": ["Method"],
  "detail_level": "Summary"
}
```

### Example 2: Find All Calls to a Method

```json
{
  "file_path": "MyClass.cs",
  "line_number": 25,
  "symbol_name": "MyMethod",
  "direction": "Out",
  "max_depth": 2
}
```

### Example 3: Batch Get Symbol Information

```json
{
  "symbols": [
    {"file_path": "MyClass.cs", "line_number": 25},
    {"file_path": "MyClass.cs", "line_number": 50}
  ],
  "detail_level": "Standard",
  "max_concurrency": 5
}
```

### Example 4: Get Complete Symbol Information

```json
{
  "file_path": "MyClass.cs",
  "line_number": 25,
  "symbol_name": "MyMethod",
  "sections": "All",
  "include_references": true,
  "max_references": 20,
  "include_inheritance": true,
  "include_call_graph": true
}
```

---

## Performance Guide

### Best Practices

1. **Use Batch Queries**: Use `batch_get_symbols` instead of multiple individual calls
2. **Control Detail Level**: Use `Summary` for quick browsing
3. **Limit Result Count**: Set `max_results`, `max_references`, etc. parameters
4. **On-Demand Loading**: Use `sections` to get only needed information

### Performance Metrics

| Operation | Target Performance |
|-----------|-------------------|
| Load Solution | < 5s |
| Get Document Symbols | < 500ms |
| Search Symbols (1000+) | < 1s |
| Find References | < 2s |
| Call Graph Analysis | < 3s |
| Inheritance Hierarchy Analysis | < 1s |
| Batch Query (10 items) | < 1s |
