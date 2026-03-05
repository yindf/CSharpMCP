# CSharp MCP Server

> 基于 Roslyn 的 Model Context Protocol Server，提供强大的 C# 代码分析和导航功能

**中文** | [**English**](README.en.md)

[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## 简介

CSharp MCP Server 是一个使用 Roslyn API 开发的 Model Context Protocol (MCP) Server，为 AI 助手提供强大的 C# 代码分析能力。相比基于 LSP 的实现，本服务器能够访问完整的语义信息，提供更准确和深入的代码分析。

## 特性

### 核心优势

- **🎯 完整语义分析**: 直接使用 Roslyn API，获取完整的符号范围信息和类型推断
- **🌲 继承层次分析**: 支持查看类型的完整继承链和派生类
- **📊 调用图分析**: 分析方法的调用者和被调用者，计算圈复杂度
- **⚡ Token 优化**: 分层响应、智能截断、批量查询，减少 Token 使用
- **🔍 高级代码导航**: 支持模糊匹配、批量操作、按需加载

### 工具分类

| 类别 | 工具 | 描述 |
|------|------|------|
| **Essential** | `get_symbols` | 获取文档中的所有符号 |
| | `go_to_definition` | 跳转到符号定义 |
| | `find_references` | 查找符号的所有引用 |
| | `resolve_symbol` | 获取符号的完整信息 |
| | `search_symbols` | 搜索整个工作区中的符号 |
| **HighValue** | `get_inheritance_hierarchy` | 获取类型的继承层次结构 |
| | `get_call_graph` | 获取方法的调用图 |
| | `get_type_members` | 获取类型的成员 |
| **Optimization** | `get_symbol_complete` | 整合多个信息源获取完整符号信息 |
| | `batch_get_symbols` | 批量获取符号信息 |
| | `get_diagnostics` | 获取编译诊断信息 |

## 快速开始

### 安装

```bash
# 克隆仓库
git clone https://github.com/your-org/CSharpMcp.git
cd CSharpMcp

# 还原依赖
dotnet restore

# 构建
dotnet build
```

### 使用

1. **启动服务器**:

```bash
dotnet run --project src/CSharpMcp.Server
```

2. **在 MCP 客户端中配置**:

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

### 配置工作区

首次使用时，需要加载工作区：

```json
{
  "method": "workspace/load",
  "params": {
    "path": "path/to/your/solution.sln"
  }
}
```

## 工具使用示例

### 获取文件中的所有方法

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

### 查看方法的调用图

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

### 批量获取符号信息

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

### 获取编译诊断

```json
{
  "name": "get_diagnostics",
  "arguments": {
    "file_path": "MyClass.cs",
    "include_warnings": true
  }
}
```

## 项目结构

```
CSharpMcp/
├── src/
│   ├── CSharpMcp.Server/          # MCP Server 主项目
│   │   ├── Tools/                 # MCP 工具实现
│   │   │   ├── Essential/         # 核心工具
│   │   │   ├── HighValue/         # 高级工具
│   │   │   └── Optimization/      # 优化工具
│   │   ├── Roslyn/                # Roslyn 封装层
│   │   │   ├── WorkspaceManager.cs
│   │   │   ├── SymbolAnalyzer.cs
│   │   │   ├── InheritanceAnalyzer.cs
│   │   │   └── CallGraphAnalyzer.cs
│   │   ├── Models/                # 数据模型
│   │   ├── Cache/                 # 缓存层
│   │   └── Program.cs
│   │
│   ├── CSharpMcp.Tests/           # 单元测试
│   └── CSharpMcp.IntegrationTests/ # 集成测试
│
├── docs/
│   ├── API.md                     # API 文档
│   └── ARCHITECTURE.md            # 架构设计
│
├── CSharpMcp.sln
├── PROJECT_PLAN.md                # 项目计划
├── IMPLEMENTATION_PLAN.md         # 实现计划
└── README.md
```

## 技术栈

| 组件 | 版本 | 说明 |
|------|------|------|
| .NET | 10.0 | 最新 LTS 版本 |
| Roslyn | 4.* | Microsoft.CodeAnalysis |
| MCP SDK | 0.2.0-preview | Model Context Protocol |
| Serilog | 3.* | 结构化日志 |
| xUnit | 2.* | 测试框架 |

## 重要说明

### 输出格式

**所有工具的输出均为 Markdown 格式**，便于大模型直接理解和处理。输出结构清晰，包含标题、列表、代码块等格式化元素。

#### 输出示例

**`get_definition` 输出示例：**

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
处理输入数据并返回结果

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

**`find_references` 输出示例：**

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

**`get_call_graph` 输出示例：**

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

### Token 优化策略

本服务器针对 LLM Token 使用进行了深度优化，帮助减少调用次数和 Token 消耗：

#### 1. 一次调用获取完整信息（推荐）

**避免多次调用**：使用 `get_symbol_complete` 或 `get_symbol_info` 一次性获取符号的完整信息，包括：
- 符号签名和文档
- 源代码实现
- 引用列表
- 继承层次
- 调用图

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

#### 2. 批量查询

**并行处理多个符号**：使用 `batch_get_symbols` 一次获取多个符号信息：

```json
{
  "name": "batch_get_symbols",
  "arguments": {
    "symbols": [
      {"symbol_name": "ClassA", "line_number": 10},
      {"symbol_name": "MethodB", "line_number": 25},
      {"symbol_name": "PropertyC", "line_number": 40}
    ],
    "include_body": true,
    "max_body_lines": 30
  }
}
```

#### 3. 分层响应控制

使用 `detail_level` 参数控制输出详细程度，**优先使用较低级别**：

- `Compact` - 仅名称和位置（快速浏览）
- `Summary` - 简要信息（推荐默认）
- `Standard` - 标准信息
- `Full` - 完整信息（仅在需要时使用）

#### 4. 智能截断参数

- `body_max_lines` / `max_body_lines` - 限制源代码行数（默认 50）
- `max_references` - 限制引用数量（默认 10）
- `max_results` - 限制搜索结果
- `max_callers` / `max_callees` - 限制调用图深度

#### 5. 按需加载

- `sections` 参数 - 指定需要的信息部分（Signature, Documentation, Body, References 等）
- `filter_kinds` 参数 - 过滤符号类型（Method, Property, Field 等）
- `include_body` - 是否包含实现代码（默认 true）
- `include_inherited` - 是否包含继承成员

#### 6. 模糊匹配

所有工具支持**模糊路径匹配**，无需提供完整路径：
- 文件名即可：`"MyClass.cs"`
- 相对路径：`"./Services/MyService.cs"`
- 行号辅助定位：`line_number` 参数帮助精确定位符号

## 开发

### 环境要求

- .NET 10.0 SDK
- Visual Studio 2022 或 Rider

### 构建

```bash
dotnet build
```

### 运行测试

```bash
# 单元测试
dotnet test tests/CSharpMcp.Tests

# 集成测试
dotnet test tests/CSharpMcp.IntegrationTests
```

## 文档

- [API 文档](docs/API.md) - 详细的 API 参考

## 路线图

### 已完成 ✅

- [x] Phase 1: 基础设施（WorkspaceManager, SymbolAnalyzer, 缓存）
- [x] Phase 2: Essential 工具（5个核心工具）
- [x] Phase 3: HighValue 工具（3个高级分析工具）
- [x] Phase 4: Optimization 工具（3个优化工具）

## Debug

npx @modelcontextprotocol/inspector CSharpMcp.Server.exe

## 贡献

欢迎贡献！请查看 [CONTRIBUTING.md](CONTRIBUTING.md) 了解详情。

## 许可证

MIT License - 详见 [LICENSE](LICENSE) 文件

## 致谢

- [Microsoft.CodeAnalysis](https://github.com/dotnet/roslyn) - Roslyn 编译器平台
- [Model Context Protocol](https://modelcontextprotocol.io) - MCP 规范
