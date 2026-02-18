# CSharp MCP Server - API 文档

**中文** | [**English**](API.en.md)

## 概述

CSharp MCP Server 是一个基于 Roslyn 的 Model Context Protocol (MCP) Server，提供强大的 C# 代码分析和导航功能。

### 核心特性

- **摆脱 LSP 限制**: 直接使用 Roslyn API，获取完整的符号范围信息
- **丰富的语义分析**: 继承层次、调用图、类型成员分析
- **Token 优化**: 分层响应、智能截断
- **批量操作**: 减少交互次数

---

## 工具分类

### Essential 工具 (核心工具)

#### `get_symbols`

获取文档中的所有符号。

**参数**:
```json
{
  "file_path": "string (必需) - 文件路径，支持绝对路径、相对路径、仅文件名模糊匹配",
  "line_number": "int? (可选) - 行号，用于模糊匹配",
  "symbol_name": "string? (可选) - 符号名称，用于验证和模糊匹配",
  "detail_level": "DetailLevel (可选) - 输出详细级别: Compact, Summary, Standard, Full",
  "include_body": "bool (可选) - 是否包含方法体，默认 true",
  "body_max_lines": "int (可选) - 方法体最大行数，默认 100",
  "filter_kinds": "SymbolKind[] (可选) - 符号类型过滤"
}
```

**响应**:
```markdown
## Symbols: FileName.cs

**Total: N symbol(s)**

- **SymbolName** (Method):12-45 - public static void MethodName(string param)
  - Documentation...
```

---

#### `go_to_definition`

跳转到符号定义。

**参数**:
```json
{
  "file_path": "string (必需)",
  "line_number": "int? (可选)",
  "symbol_name": "string? (可选)",
  "detail_level": "DetailLevel (可选)",
  "include_body": "bool (可选)",
  "body_max_lines": "int (可选)"
}
```

**响应**:
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

查找符号的所有引用。

**参数**:
```json
{
  "file_path": "string (必需)",
  "line_number": "int? (可选)",
  "symbol_name": "string? (可选)",
  "include_context": "bool (可选) - 是否包含上下文代码",
  "context_lines": "int (可选) - 上下文代码行数"
}
```

**响应**:
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

获取符号的完整信息（包含文档、定义和部分引用）。

**参数**:
```json
{
  "file_path": "string (必需)",
  "line_number": "int? (可选)",
  "symbol_name": "string? (可选)",
  "detail_level": "DetailLevel (可选)",
  "include_body": "bool (可选)",
  "body_max_lines": "int (可选)"
}
```

**响应**:
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

搜索整个工作区中的符号。

**参数**:
```json
{
  "query": "string (必需) - 搜索查询，支持通配符如 My.*, *.Controller",
  "detail_level": "DetailLevel (可选)",
  "max_results": "int (可选) - 最大结果数量，默认 100"
}
```

**响应**:
```markdown
## Search Results: "MyClass.*"

**Found 5 symbol(s)**

### Namespace: MyNamespace

- **MyClass** (Class) - MyClass.cs:10
- **MyMethod** (Method) - MyClass.cs:25
```

---

### HighValue 工具 (高级工具)

#### `get_inheritance_hierarchy`

获取类型的继承层次结构。

**参数**:
```json
{
  "file_path": "string (必需)",
  "line_number": "int? (可选)",
  "symbol_name": "string? (可选)",
  "include_derived": "bool (可选) - 是否包含派生类",
  "max_derived_depth": "int (可选) - 派生类最大深度"
}
```

**响应**:
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

获取方法的调用图。

**参数**:
```json
{
  "file_path": "string (必需)",
  "line_number": "int? (可选)",
  "symbol_name": "string? (可选)",
  "direction": "CallGraphDirection (可选) - 调用方向: Both, In, Out",
  "max_depth": "int (可选) - 最大深度",
  "include_external_calls": "bool (可选) - 是否包含外部调用"
}
```

**响应**:
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

获取类型的成员。

**参数**:
```json
{
  "file_path": "string (必需)",
  "line_number": "int? (可选)",
  "symbol_name": "string? (可选)",
  "include_inherited": "bool (可选) - 是否包含继承的成员",
  "filter_kinds": "SymbolKind[] (可选) - 成员类型过滤"
}
```

**响应**:
```markdown
## Type Members: `MyClass`

**Total: 15 member(s)**

### Method
- **Method1** (public static Method) - MyClass.cs:25
- **Method2** (public virtual Method) - MyClass.cs:50
```

---

### Optimization 工具 (优化工具)

#### `get_symbol_complete`

整合多个信息源获取完整符号信息，减少API调用次数。

**参数**:
```json
{
  "file_path": "string (必需)",
  "line_number": "int? (可选)",
  "symbol_name": "string? (可选)",
  "sections": "SymbolCompleteSections (可选) - 要获取的信息部分",
  "detail_level": "DetailLevel (可选)",
  "body_max_lines": "int (可选)",
  "include_references": "bool (可选)",
  "max_references": "int (可选)",
  "include_inheritance": "bool (可选)",
  "include_call_graph": "bool (可选)",
  "call_graph_max_depth": "int (可选)"
}
```

`SymbolCompleteSections` 是一个标志枚举:
- `Basic = 1` - 基本信息
- `Signature = 2` - 签名信息
- `Documentation = 4` - 文档注释
- `SourceCode = 8` - 源代码
- `References = 16` - 引用位置
- `Inheritance = 32` - 继承层次
- `CallGraph = 64` - 调用图
- `All = 127` - 所有信息

**响应**:
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

批量获取符号信息，使用并行处理提高性能。

**参数**:
```json
{
  "symbols": "FileLocationParams[] (必需) - 符号位置列表",
  "detail_level": "DetailLevel (可选)",
  "include_body": "bool (可选)",
  "body_max_lines": "int (可选)",
  "max_concurrency": "int (可选) - 最大并发数"
}
```

**响应**:
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

获取编译诊断信息（错误、警告和信息）。

**参数**:
```json
{
  "file_path": "string? (可选) - 文件路径，不指定则获取整个工作区的诊断",
  "include_warnings": "bool (可选) - 是否包含警告",
  "include_info": "bool (可选) - 是否包含信息",
  "include_hidden": "bool (可选) - 是否包含隐藏诊断",
  "severity_filter": "DiagnosticSeverity[] (可选) - 严重性过滤"
}
```

`DiagnosticSeverity` 枚举:
- `Error`
- `Warning`
- `Info`
- `Hidden`

**响应**:
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

## 枚举类型

### `DetailLevel`

控制输出详细级别，优化 token 使用。

- `Compact` - 最小信息，仅名称和位置
- `Summary` - 简要信息，包含基本签名
- `Standard` - 标准信息，包含文档和部分源代码
- `Full` - 完整信息，包含所有源代码

### `SymbolKind`

符号类型分类。

**类型**:
- `Class`, `Struct`, `Interface`, `Enum`, `Record`, `Delegate`, `Attribute`

**成员**:
- `Method`, `Property`, `Field`, `Event`, `Constructor`, `Destructor`

**其他**:
- `Namespace`, `Parameter`, `Local`, `TypeParameter`, `Unknown`

### `Accessibility`

可访问性。

- `Public`, `Internal`, `Protected`, `ProtectedInternal`, `PrivateProtected`, `Private`, `NotApplicable`

---

## 文件路径匹配

工具支持多种文件路径格式：

1. **绝对路径**: `C:\Project\MyFile.cs`
2. **相对路径**: `src\MyFile.cs`
3. **仅文件名**: `MyFile.cs` (模糊匹配工作区中的文件)

---

## Token 优化策略

### 1. 分层响应

使用 `detail_level` 参数控制输出详细程度：

- **快速浏览**: 使用 `Compact` 获取符号列表
- **理解代码**: 使用 `Summary` 或 `Standard` 查看签名和文档
- **深入研究**: 使用 `Full` 获取完整源代码

### 2. 智能截断

- `body_max_lines` 限制返回的源代码行数
- `max_references` 限制返回的引用数量
- `max_results` 限制搜索结果数量

### 3. 批量操作

- 使用 `batch_get_symbols` 一次性查询多个符号
- 使用 `get_symbol_complete` 获取所有需要的信息

### 4. 按需加载

- 使用 `sections` 参数指定需要的信息部分
- 使用 `filter_kinds` 过滤不需要的符号类型

---

## 错误处理

所有工具在遇到错误时返回:

```json
{
  "error": "Error message"
}
```

常见错误:
- 文件未找到: `"File not found: path/to/file.cs"`
- 符号未找到: `"Symbol not found: SymbolName"`
- 工作区未加载: `"Workspace not loaded"`
- 无效参数: `"Invalid parameters type"`

---

## 使用示例

### 示例 1: 获取文件中所有方法

```json
{
  "file_path": "MyClass.cs",
  "filter_kinds": ["Method"],
  "detail_level": "Summary"
}
```

### 示例 2: 查找方法的所有调用

```json
{
  "file_path": "MyClass.cs",
  "line_number": 25,
  "symbol_name": "MyMethod",
  "direction": "Out",
  "max_depth": 2
}
```

### 示例 3: 批量获取符号信息

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

### 示例 4: 获取完整符号信息

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

## 性能指南

### 最佳实践

1. **使用批量查询**: 使用 `batch_get_symbols` 替代多次单独调用
2. **控制详细级别**: 使用 `Summary` 进行快速浏览
3. **限制结果数量**: 设置 `max_results`、`max_references` 等参数
4. **按需加载**: 使用 `sections` 仅获取需要的信息

### 性能指标

| 操作 | 目标性能 |
|------|----------|
| 加载解决方案 | < 5s |
| 获取文档符号 | < 500ms |
| 搜索符号 (1000+) | < 1s |
| 查找引用 | < 2s |
| 调用图分析 | < 3s |
| 继承层次分析 | < 1s |
| 批量查询 (10个) | < 1s |
