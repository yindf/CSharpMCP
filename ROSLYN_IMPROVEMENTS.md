# Roslyn MCP Server - 改进建议文档

## 概述

本文档描述如何从基于 LSP 的 MCP server 转变为直接使用 Roslyn 的实现，以更好地利用 Roslyn 的语义信息、节省 token 并减少交互轮次。

---

## 核心改进点

### 1. 利用 Roslyn 的完整 Range 信息

**问题**: 当前 LSP 实现只能获取符号签名行的 range，无法获取完整的符号范围（如整个方法体）。

**Roslyn 解决方案**:
```csharp
// 使用 ISymbol 的 Location 获取完整范围
var syntaxNode = symbol.DeclaringSyntaxReferences
    .FirstOrDefault()?.GetSyntax();
var fullSpan = syntaxNode.FullSpan; // 获取完整语法节点的范围
var lineSpan = syntaxNode.GetLocation().GetLineSpan();
```

**改进后的输出**:
```markdown
**Execute** (Method):24-138 - (CharacterController, float) -> void
// 24-138 是完整方法体范围，包括开始和结束的大括号
```

---

### 2. 新增 Roslyn 独有功能

#### 2.1 继承层次结构 (`get_inheritance_hierarchy`)

**输入**:
```yaml
properties:
  symbol_name:
    type: string
    description: 类型名称 (e.g., 'BuildCommand')
  file_path:
    type: string
    description: 文件路径用于模糊匹配
```

**输出**:
```markdown
## Inheritance Hierarchy: `BuildCommand`

### Base Classes
- `Object` (System.Object)

### Implemented Interfaces
- `ICommand` (ICommand.cs:5-15)

### Derived Classes
- `BuildCommandAdvanced` (BuildCommandAdvanced.cs:15-89)
- `QuickBuildCommand` (QuickBuildCommand.cs:8-45)

### Inheritance Distance
- Depth: 1 (direct subclass of Object)
```

**Roslyn 实现**:
```csharp
// 获取基类
var baseType = namedSymbol.BaseType;
// 获取接口
var interfaces = namedSymbol.AllInterfaces;
// 获取派生类
var derivedClasses = await FindDerivedClasses(symbol, compilation);
```

---

#### 2.2 调用图 (`get_call_graph`)

**输入**:
```yaml
properties:
  symbol_name:
    type: string
    description: 方法名称
  file_path:
    type: string
  direction:
    type: string
    enum: ["both", "in", "out"]
    default: "both"
    description: "both"=调用者和被调用者, "in"=仅调用者, "out"=仅被调用者
  max_depth:
    type: integer
    default: 2
    description: 调用图的最大深度
```

**输出**:
```markdown
## Call Graph: `Execute`

### Called By (incoming calls)
- `PlayerController.HandleInput` (PlayerController.cs:42)
  - Calls: `ProcessInput` -> `Execute`
- `AIController.ProcessBuild` (AIController.cs:78)

### Calls (outgoing calls)
- `Validate` (BuildCommand.cs:52) - local method
- `character.Build` (ICharacter.cs:15) - interface call
- `Mathf.Clamp` (UnityEngine.Mathf) - external call

### Call Statistics
- Total callers: 2
- Total callees: 3
- Cyclomatic complexity: 5
```

**Roslyn 实现**:
```csharp
// 使用 SymbolFinder 分析调用关系
var callers = await SymbolFinder.FindCallersAsync(methodSymbol, solution, document);
var callees = await SymbolFinder.FindCalledMethodsAsync(methodSymbol, document);
```

---

#### 2.3 类型成员完整列表 (`get_type_members`)

**输入**:
```yaml
properties:
  type_name:
    type: string
    description: 类型名称
  include_inherited:
    type: boolean
    default: false
    description: 是否包含继承的成员
```

**输出**:
```markdown
## Type Members: `BuildCommand`

### Fields (5)
- **tilePosition** (Vector2Int):11 - The tile position
- **rotation** (int):12 - Building rotation in degrees
- **buildingType** (BuildingType):14 - Type of building
- **buildTime** (float):17 - Time required to build
- **elapsedTime** (float):20 - Elapsed build time

### Properties (3)
- **IsCompleted** (bool):23 - Whether construction is complete
- **Progress** (float):26 - Build progress (0-1)
- **Cost** (int):29 - Resource cost

### Methods (4)
- **Execute** (Method):32-45 - (CharacterController, float) -> void
- **Validate** (Method):48-52 - () -> bool
- **Cancel** (Method):55-58 - () -> void
- **GetEstimatedCost** (Method):61-64 - () -> int

### Events (1)
- **OnCompleted** (EventHandler):67

### Inherited Members (from Object)
- **ToString**, **Equals**, **GetHashCode**
```

---

### 3. 节省 Token 的策略

#### 3.1 分层响应模式

**新增参数**: `detail_level`

```yaml
detail_level:
  type: string
  enum: ["compact", "summary", "standard", "full"]
  default: "summary"
  description: |
    compact: 仅符号名称和行号 (最节省 token)
    summary: 符号 + 类型签名 (默认)
    standard: summary + XML 文档注释
    full: standard + 完整源代码片段
```

**输出对比**:

**compact 模式**:
```markdown
## Symbols: BuildCommand.cs
BuildCommand(C):8-24
  Execute(M):24-138
  Validate(M):52-54
```

**summary 模式**:
```markdown
## Symbols: BuildCommand.cs
**BuildCommand** (Class):8-24
  **Execute** (Method):24-138 - (CharacterController, float) -> void
  **Validate** (Method):52-54 - () -> bool
```

**full 模式**:
```markdown
## Symbols: BuildCommand.cs
**BuildCommand** (Class):8-24 // Command to build a structure
  **Execute** (Method):24-138 - (CharacterController, float) -> void

  ```csharp
  /// <summary>
  /// Executes the build command with the given character
  /// </summary>
  public void Execute(CharacterController character, float deltaTime)
  {
      // implementation...
  }
  ```
```

---

#### 3.2 智能截断策略改进

**新增参数**:
```yaml
include_body:
  type: boolean
  default: true
  description: 是否包含方法体/实现代码

body_max_lines:
  type: integer
  default: 100
  description: 最多返回的代码行数
```

**改进后的截断逻辑**:
```markdown
## go_to_definition 输出 (body_max_lines: 50)

### Definition: `Execute` (lines 24-138, showing first 50)

```csharp
24: public void Execute(CharacterController character, float deltaTime)
25: {
26:     // Step 1: Validate input
27:     if (character == null)
...
75:     // Step 10: Process tenth batch
76:     for (int i = 0; i < count; i++)
77:     {
78:         result.Append(input[0]);
79:         counter++;
80:     }

*... 88 more lines hidden (use body_max_lines: 150 to see more)*
```

---

### 4. 减少交互轮次

#### 4.1 合并工具: `get_symbol_complete`

**目的**: 一次请求获取符号的所有相关信息

**输入**:
```yaml
properties:
  symbol_name:
    type: string
  file_path:
    type: string
  sections:
    type: array
    items:
      type: string
      enum: ["location", "signature", "documentation", "preview", "references", "callers", "callees", "related"]
    default: ["location", "signature", "documentation"]
    description: 指定需要返回的信息部分
```

**输出**:
```markdown
## Complete Symbol Info: `Execute`

### Location
**File**: BuildCommand.cs
**Range**: 24-138 (115 lines)

### Signature
```csharp
public void Execute(CharacterController character, float deltaTime)
```

### Documentation
Executes the build command with the given character. Validates parameters,
checks building conditions, and applies the build effect.

**Parameters**:
- `character`: The character executing the build
- `deltaTime`: Time elapsed since last frame

**Returns**: void

### Implementation Preview (first 30 lines)
```csharp
24: public void Execute(CharacterController character, float deltaTime)
25: {
26:     if (character == null)
27:         throw new ArgumentNullException(nameof(character));
28:
29:     if (!Validate())
30:         return;
31:
32:     character.Build(this);
33:     elapsedTime += deltaTime;
34:     ...
```

### References (3 found)
- `PlayerController.HandleInput` (PlayerController.cs:42)
- `AIController.ProcessBuild` (AIController.cs:78)
- `BuildManager.QueueCommand` (BuildManager.cs:156)

### Related Symbols
**Calls**:
- `Validate()` (local)
- `character.Build()` (ICharacter.Build)

**Called By**:
- `PlayerController.HandleInput()`
- `AIController.ProcessBuild()`

**Overrides**: None
**Implements**: ICommand.Execute
```

---

#### 4.2 批量查询

**新工具**: `batch_get_symbols`

**输入**:
```yaml
properties:
  queries:
    type: array
    items:
      type: object
      properties:
        symbol_name:
          type: string
        file_path:
          type: string
    maxItems: 10
```

**输出**:
```markdown
## Batch Symbol Results

### Query 1: `Execute` in BuildCommand.cs
**Execute** (Method):24-138 - (CharacterController, float) -> void
// summary...

### Query 2: `Validate` in BuildCommand.cs
**Validate** (Method):52-54 - () -> bool
// summary...

### Query 3: `PlayerController` in PlayerController.cs
**PlayerController** (Class):8-200
  Members: 15 methods, 8 fields, 5 properties
```

---

### 5. 需要修改的现有文档内容

#### 5.1 删除 LSP 限制说明

**删除** (第 52-53 行):
```markdown
**Rationale**:
- LLMs may estimate line numbers imprecisely
- LSP `range` only covers signature line (csharp-ls limitation)  // 删除这行
- Fuzzy search with ±10 lines finds correct symbol even with errors
```

**替换为**:
```markdown
**Rationale**:
- LLMs may estimate line numbers imprecisely
- Roslyn provides accurate full span information for all symbols
- Fuzzy search with ±10 lines finds correct symbol even with errors
```

#### 5.2 更新 search_symbols 工具说明

**删除** (第 456 行):
```markdown
- No method body (LSP limitation)  // 删除这行
```

**替换为**:
```markdown
- Method body available via `go_to_definition` or `get_symbol_complete`
- Set `detail_level: full` to include method bodies in search results
```

#### 5.3 添加新工具到优先级列表

```markdown
## Priority Guidelines

### ESSENTIAL Tools
- `get_symbols` - Use FIRST for any file analysis
- `go_to_definition` - Navigate to definitions
- `find_references` - Track symbol usage
- `get_symbol_complete` - **NEW** Get all symbol info in one call

### HIGH VALUE Tools
- `search_symbols` - Discover symbols across workspace
- `resolve_symbol` - Comprehensive symbol information
- `get_inheritance_hierarchy` - **NEW** Type inheritance analysis
- `get_call_graph` - **NEW** Method call relationships
- `get_type_members` - **NEW** Complete member listing

### MEDIUM VALUE Tools
- `get_diagnostics` - Error checking
- `set_workspace` - Initial setup (optional)
- `batch_get_symbols` - **NEW** Bulk symbol queries
```

---

### 6. 性能优化建议

#### 6.1 缓存策略

```csharp
// 实现 Roslyn 编译缓存
private ConcurrentDictionary<string, Compilation> _compilationCache;
private ConcurrentDictionary<string, List<ISymbol>> _symbolCache;

// 缓存键格式
string GetCacheKey(string projectPath, string documentPath)
{
    return $"{projectPath}|{documentPath}";
}
```

#### 6.2 增量更新

```markdown
## Workspace Change Notifications

当检测到文件变化时，发送增量更新：

**输入**:
```yaml
properties:
  force_refresh:
    type: boolean
    default: false
    description: 强制重新加载编译
```

**输出**:
```markdown
## Workspace Status

**Projects Loaded**: 3
**Documents Analyzed**: 127
**Cache Hit Rate**: 85%
**Last Update**: 2024-01-15 10:23:45

**Changes Detected**:
- Modified: BuildCommand.cs (2 minutes ago)
- Added: NewFeature.cs (5 minutes ago)
```

---

### 7. 新的输出格式建议

#### 7.1 结构化 Markdown + 元数据

```markdown
<!-- METADATA
{
  "version": "1.0",
  "tool": "get_symbol_complete",
  "symbol": {
    "name": "Execute",
    "kind": "Method",
    "file": "BuildCommand.cs",
    "range": [24, 138]
  },
  "token_estimate": 450,
  "cache_status": "hit"
}
-->

## Symbol: `Execute`
...
```

#### 7.2 可导航输出

```markdown
## Symbol: `Execute`

[Jump to Definition](BuildCommand.cs#L24) |
[View References](#references) |
[Call Graph](#call-graph)

### Definition
...
```

---

## 总结

从 LSP 转向直接使用 Roslyn 的主要优势：

1. **完整的 Range 信息**: 方法体、类的完整范围
2. **丰富的语义分析**: 继承、接口实现、调用关系
3. **节省 Token**: 分层响应、按需加载
4. **减少交互**: 合并工具、批量查询
5. **更好的性能**: 缓存、增量更新

建议实现优先级：
1. **高优先级**: `get_symbol_complete`, 分层响应
2. **中优先级**: `get_inheritance_hierarchy`, `get_call_graph`
3. **低优先级**: 批量查询、增量更新
