# CSharpMcp Token 优化改进计划

> 基于 2025-03-05 输出格式深度分析

## 目标

- 减少约 40-50% 的 Token 消耗
- 保持 Markdown 输出格式的可读性
- 不增加调用次数

---

## Phase 1: 高优先级改进

### 1.1 类型名缩写

**问题**: 完整类型名如 `System.Collections.Generic.List<StellarGround.Core.Types.Vector2Int>` 占用大量 Token

**方案**: 输出时自动缩短类型名，只保留最后一部分

```
// Before
`System.Collections.Generic.List<StellarGround.Core.Types.Vector2Int>`

// After
`List<Vector2Int>`
```

**实现位置**: `SymbolExtensions.cs` 中的 `ToDisplayString()` 相关方法

**参数**: 无需参数，默认缩短

---

### 1.2 属性与 getter/setter 合并显示

**问题**: 属性 `Id` 和方法 `get_Id`、`set_Id` 重复显示

**当前输出**:
```
- **Id** (Property) L17-19 - `System.Guid Id`
- **get_Id** (Method) L18-18
- **set_Id** (Method) L19-19
```

**目标输出**:
```
- **Id**: Guid { get; set; } :17
```

**实现位置**:
- `GetSymbolsTool.cs` - 过滤掉 `get_` 和 `set_` 方法
- `SymbolExtensions.cs` - 添加属性访问器格式化

---

### 1.3 引用结果智能截断

**问题**: `find_references` 显示 90 个引用，每个文件完整路径

**当前输出**:
```
### Character.cs
`C:\Data\GitHub\...\Character.cs` (2 references)
- **L255**: `if (Pathfinder.FindPath(...))`
- **L345**: `if (Pathfinder.FindPath(...))`
```

**目标输出**:
```
### Character.cs (2 refs)
- L255, L345
```

**方案**:
- 默认只显示行号列表，不显示代码片段
- 添加 `include_context: true` 参数才显示代码

**实现位置**: `FindReferencesTool.cs`

---

### 1.4 路径简化

**问题**: 完整绝对路径过长

**当前**: `C:\Data\GitHub\StellarGround\Client\Assets\Scripts\Core\Character\Character.cs`

**目标**: `Core/Character/Character.cs`

**方案**: 基于工作区根目录计算相对路径

**实现位置**: `MarkdownHelper.cs` 的 `FormatFileLocation` 方法

---

## Phase 2: 中优先级改进

### 2.1 get_symbols 输出优化

**问题**: 命名空间单独列出、行号范围过长

**当前**:
```
- **Core** (Namespace) L4-53
- **Character** (class) L10-409
- **Id** (Property) L17-19
```

**目标**:
```yaml
file: Character.cs
namespace: StellarGround.Core

class Character (L10):
  properties:
    Id: Guid :17
    Name: string :19
  methods:
    Tick(deltaTime): void :119
```

**实现**: 添加 `compact: true` 参数

---

### 2.2 search_symbols 输出压缩

**问题**: 每个结果 3 行

**当前**:
```
- **_buildingManager** (in StellarGround...) `private Field`
  - `BuildingSystemIntegrationTests.cs:23`
  - `BuildingManager _buildingManager`
```

**目标**:
```
- _buildingManager: BuildingManager @ BuildingSystemIntegrationTests.cs:23
```

---

### 2.3 get_call_graph 格式统一

**问题**: Callers 和 Callees 格式不一致

**目标格式**:
```
## Call Graph: `Tick`

**Callers (1)**:
- GameSession.Tick() @ L946

**Callees (2)**:
- Buildings.Tick(deltaTime): void @ L291
- NavigationGraph.Rebuild(): void @ L156
```

---

## Phase 3: 可选改进

### 3.1 输出模式参数

```typescript
{
  output_mode: "compact" | "standard" | "detailed"
}
```

| 模式 | 说明 |
|------|------|
| `compact` | 最小 Token，省略代码片段 |
| `standard` | 当前格式 |
| `detailed` | 包含完整上下文 |

### 3.2 YAML 输出选项

```typescript
{
  format: "markdown" | "yaml"
}
```

YAML 对某些大模型更友好，但需要评估实际效果。

---

## 不采纳的建议

| 建议 | 原因 |
|------|------|
| JSON 输出格式 | YAML 已足够，JSON 冗余更多 |
| 语义化分组 | 过于复杂，按字母顺序更可预测 |
| 智能缓存提示 | MCP 协议层处理，工具层不需要 |
| 批量查询去重 | 复杂度高，收益有限 |

---

## 实现顺序

1. **Week 1**: Phase 1 全部完成
   - 类型名缩写
   - 属性合并
   - 引用截断
   - 路径简化

2. **Week 2**: Phase 2 完成
   - get_symbols 优化
   - search_symbols 优化
   - call_graph 统一

3. **Week 3**: Phase 2 测试 + Phase 3 评估

---

## 预期收益

| 工具 | 当前 Token | 优化后 | 节省 |
|------|-----------|--------|------|
| `get_symbol_info` | 100% | 60% | **40%** |
| `find_references` | 100% | 40% | **60%** |
| `get_symbols` | 100% | 50% | **50%** |
| `search_symbols` | 100% | 45% | **55%** |

**总体预期节省: ~45% Token**
