# Roslyn MCP Server - 项目开发计划

## 项目概述

开发一个基于 .NET 的 MCP (Model Context Protocol) Server，直接使用 Roslyn API 提供强大的 C# 代码分析和导航功能。

### 目标

1. **摆脱 LSP 限制**：直接使用 Roslyn API，获取完整的符号范围信息
2. **丰富的语义分析**：继承层次、调用图、类型成员分析
3. **Token 优化**：分层响应、智能截断
4. **减少交互**：合并工具、批量查询

---

## 项目结构

```
CSharpMcp/
├── src/
│   ├── CSharpMcp.Server/          # MCP Server 主项目
│   │   ├── Tools/                 # MCP 工具实现
│   │   │   ├── Essential/         # 核心工具
│   │   │   ├── HighValue/         # 高级工具
│   │   │   └── MediumValue/       # 中级工具
│   │   ├── Roslyn/                # Roslyn 封装层
│   │   │   ├── WorkspaceManager.cs
│   │   │   ├── SymbolFinder.cs
│   │   │   ├── DocumentationProvider.cs
│   │   │   ├── CallGraphAnalyzer.cs
│   │   │   └── InheritanceAnalyzer.cs
│   │   ├── Models/                # 数据模型
│   │   │   ├── SymbolInfo.cs
│   │   │   ├── DetailLevel.cs
│   │   │   └── OutputFormatter.cs
│   │   ├── Cache/                 # 缓存层
│   │   │   ├── CompilationCache.cs
│   │   │   └── SymbolCache.cs
│   │   └── Program.cs
│   │
│   ├── CSharpMcp.Tests/           # 单元测试
│   │   ├── Tools/
│   │   ├── Roslyn/
│   │   └── TestAssets/            # 测试用代码
│   │
│   └── CSharpMcp.IntegrationTests/# 集成测试
│       └── Scenarios/
│
├── docs/
│   ├── API.md                     # API 文档
│   ├── ARCHITECTURE.md            # 架构设计
│   └── MIGRATION.md               # 从 LSP 迁移指南
│
├── CSharpMcp.sln
├── Directory.Build.props
└── global.json
```

---

## 技术栈

| 组件 | 选择 | 理由 |
|------|------|------|
| .NET 版本 | .NET 8.0 | LTS 版本，性能优秀 |
| MCP SDK | ModelContextProtocol | 官方 SDK |
| Roslyn | Microsoft.CodeAnalysis.* | 官方编译器平台 |
| 序列化 | System.Text.Json | 高性能 |
| 日志 | Serilog | 结构化日志 |
| 测试框架 | xUnit | 社区首选 |
| Mock 框架 | Moq | 成熟稳定 |

---

## NuGet 依赖

```xml
<!-- CSharpMcp.Server -->
<PackageReference Include="ModelContextProtocol" Version="0.*" />
<PackageReference Include="Microsoft.CodeAnalysis" Version="4.*.*" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.*.*" />
<PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="4.*.*" />
<PackageReference Include="Serilog" Version="3.*.*" />
<PackageReference Include="Serilog.Extensions.Logging" Version="8.*.*" />
<PackageReference Include="Serilog.Sinks.Console" Version="5.*.*" />

<!-- CSharpMcp.Tests -->
<PackageReference Include="xUnit" Version="2.*.*" />
<PackageReference Include="xUnit.runner.visualstudio" Version="2.*.*" />
<PackageReference Include="Moq" Version="4.*.*" />
<PackageReference Include="FluentAssertions" Version="6.*.*" />
```

---

## 开发阶段规划

### Phase 1: 项目基础设施 (Week 1)

**目标**: 建立项目骨架和核心 Roslyn 集成

| 任务 | 工作量 | 优先级 | 依赖 |
|------|--------|--------|------|
| 创建解决方案和项目结构 | 0.5d | P0 | - |
| 配置 NuGet 依赖 | 0.5d | P0 | - |
| 实现 WorkspaceManager | 2d | P0 | - |
| 实现编译缓存 | 1d | P1 | WorkspaceManager |
| 配置 Serilog 日志 | 0.5d | P1 | - |
| 编写基础单元测试 | 1d | P0 | WorkspaceManager |

**验收标准**:
- [ ] 可以加载 .sln 或 .csproj 文件
- [ ] 可以获取 Compilation 和 SemanticModel
- [ ] 缓存系统正常工作
- [ ] 单元测试覆盖率 > 80%

---

### Phase 2: Essential 工具 (Week 2-3)

**目标**: 实现核心代码导航功能

#### 2.1 get_symbols

| 功能 | 描述 | 工作量 |
|------|------|--------|
| 基础符号遍历 | 遍历文档所有符号 | 1d |
| 符号类型分类 | Class, Method, Property 等 | 0.5d |
| Range 信息获取 | 使用 DeclaringSyntaxReferences | 1d |
| 分层输出 | compact, summary, standard, full | 1d |
| 单元测试 | 各种代码结构测试 | 1d |

#### 2.2 go_to_definition

| 功能 | 描述 | 工作量 |
|------|------|--------|
| 符号解析 | 从位置解析 ISymbol | 1d |
| 模糊匹配 | ±10 行搜索 | 0.5d |
| 源代码提取 | 带 body_max_lines | 1d |
| 单元测试 | 重载、继承、扩展方法 | 1d |

#### 2.3 find_references

| 功能 | 描述 | 工作量 |
|------|------|--------|
| 调用上下文获取 | SymbolFinder.FindReferencesAsync | 1.5d |
| 引用位置格式化 | 带上下文代码 | 1d |
| 单元测试 | 跨项目引用 | 1d |

#### 2.4 resolve_symbol

| 功能 | 描述 | 工作量 |
|------|------|--------|
| 悬停信息 | GetDocumentationComment | 1d |
| 符号详情 | 包含类型、参数、返回值 | 1d |
| 代码预览 | 带行号的源代码片段 | 0.5d |
| 单元测试 | 各种符号类型 | 1d |

**验收标准**:
- [ ] 所有 Essential 工具可以独立工作
- [ ] 支持 fuzzy file path 匹配
- [ ] 支持 detail_level 参数
- [ ] 单元测试覆盖率 > 75%

---

### Phase 3: HighValue 工具 (Week 4-5)

**目标**: 实现高级语义分析功能

#### 3.1 search_symbols

| 功能 | 描述 | 工作量 |
|------|------|--------|
| 跨项目符号搜索 | SymbolFinder | 1d |
| 模式匹配支持 | 通配符、正则 | 1d |
| 结果分组 | 按项目/命名空间 | 0.5d |
| 单元测试 | 大型代码库 | 1d |

#### 3.2 get_inheritance_hierarchy (NEW)

| 功能 | 描述 | 工作量 |
|------|------|--------|
| 基类链获取 | BaseType 递归 | 1d |
| 接口获取 | AllInterfaces | 0.5d |
| 派生类查找 | SymbolFinder.FindDerivedClassesAsync | 1.5d |
| 深度计算 | 继承层次深度 | 0.5d |
| 单元测试 | 复杂继承体系 | 1d |

#### 3.3 get_call_graph (NEW)

| 功能 | 描述 | 工作量 |
|------|------|--------|
| 调用者分析 | SymbolFinder.FindCallersAsync | 1.5d |
| 被调用者分析 | ControlFlowGraph 分析 | 2d |
| 深度控制 | max_depth 参数 | 1d |
| 循环检测 | 避免无限递归 | 1d |
| 复杂度计算 | 圈复杂度 | 0.5d |
| 单元测试 | 各种调用模式 | 1.5d |

#### 3.4 get_type_members (NEW)

| 功能 | 描述 | 工作量 |
|------|------|--------|
| 成员遍历 | GetMembers() | 1d |
| 继承成员 | IncludeInherited 参数 | 1d |
| 成员分类 | Field, Property, Method, Event | 0.5d |
| 单元测试 | 泛型、嵌套类型 | 1d |

**验收标准**:
- [ ] 所有 HighValue 工具正常工作
- [ ] 调用图支持方向控制 (in/out/both)
- [ ] 继承层次显示完整路径
- [ ] 单元测试覆盖率 > 70%

---

### Phase 4: 优化工具 (Week 6)

**目标**: 实现性能优化和用户体验改进

#### 4.1 get_symbol_complete (NEW)

| 功能 | 描述 | 工作量 |
|------|------|--------|
| 工具合并逻辑 | 整合多个信息源 | 2d |
| Sections 参数控制 | 按需返回信息 | 1d |
| 单元测试 | 各种组合 | 1d |

#### 4.2 batch_get_symbols (NEW)

| 功能 | 描述 | 工作量 |
|------|------|--------|
| 批量处理逻辑 | 并行查询 | 1.5d |
| 结果聚合 | 统一输出格式 | 0.5d |
| 单元测试 | 性能测试 | 1d |

#### 4.3 get_diagnostics

| 功能 | 描述 | 工作量 |
|------|------|--------|
| 编译错误获取 | GetDiagnostics | 1d |
| 警告获取 | include_warnings 参数 | 0.5d |
| 格式化输出 | Markdown 表格 | 0.5d |
| 单元测试 | 各种错误类型 | 1d |

**验收标准**:
- [ ] get_symbol_complete 减少至少 50% 的交互次数
- [ ] batch_get_symbols 支持最多 10 个查询
- [ ] 所有输出使用统一 Markdown 格式

---

### Phase 5: 集成与部署 (Week 7-8)

**目标**: 完善测试、文档和部署

| 任务 | 工作量 | 优先级 |
|------|--------|--------|
| 集成测试编写 | 2d | P0 |
| 性能测试 | 1d | P1 |
| 端到端测试 | 1d | P0 |
| API 文档编写 | 1d | P0 |
| README 编写 | 0.5d | P0 |
| 发布配置 | 0.5d | P1 |

---

## 测试计划

### 测试策略

```
┌─────────────────────────────────────────────────────────────┐
│                      测试金字塔                              │
├─────────────────────────────────────────────────────────────┤
│                    ┌─────┐                                   │
│                   ╱       ╲                                  │
│                  ╱  E2E    ╲      10% - 关键用户场景         │
│                 ╱           ╲                                │
│                └─────────────┘                               │
│              ┌───────────────────┐                          │
│             ╱                     ╲                         │
│            ╱   Integration        ╲    20% - 工具交互        │
│           ╱                         ╲                       │
│          └───────────────────────────┘                      │
│        ┌───────────────────────────────────┐                │
│       ╱                                       ╲              │
│      ╱            Unit Tests                  ╲  70% - 核心逻辑 │
│     ╱                                           ╲            │
│    └───────────────────────────────────────────────┘        │
└─────────────────────────────────────────────────────────────┘
```

### 单元测试

**框架**: xUnit + Moq + FluentAssertions

**覆盖范围**:

| 模块 | 测试类 | 目标覆盖率 |
|------|--------|-----------|
| Roslyn/WorkspaceManager | WorkspaceManagerTests | 85% |
| Roslyn/SymbolFinder | SymbolFinderTests | 80% |
| Roslyn/CallGraphAnalyzer | CallGraphAnalyzerTests | 75% |
| Roslyn/InheritanceAnalyzer | InheritanceAnalyzerTests | 80% |
| Tools/Essential | *ToolTests | 75% |
| Tools/HighValue | *ToolTests | 70% |
| Cache | CacheTests | 85% |

**测试分类**:
```csharp
public enum TestCategory
{
    Unit,           // 快速单元测试
    Integration,    // 需要文件系统集成
    Performance,    // 性能基准测试
    Regression      // 回归测试标记
}
```

### 集成测试

**场景覆盖**:

1. **基本导航流程**
   - [ ] 加载解决方案 → 搜索符号 → 跳转定义 → 查找引用

2. **代码分析流程**
   - [ ] 获取类型成员 → 查看继承层次 → 分析调用图

3. **大型代码库**
   - [ ] 加载 .NET Runtime 源码
   - [ ] 搜索性能验证 (< 2s)

4. **错误处理**
   - [ ] 无效文件路径
   - [ ] 编译错误的项目
   - [ ] 不存在的符号

### 性能测试

**基准指标**:

| 操作 | 目标 | 测量方法 |
|------|------|----------|
| 加载解决方案 | < 5s | Stopwatch |
| 获取文档符号 | < 500ms | BenchmarkDotNet |
| 搜索符号 (1000+) | < 1s | BenchmarkDotNet |
| 查找引用 | < 2s | BenchmarkDotNet |
| 调用图分析 | < 3s | BenchmarkDotNet |
| 继承层次分析 | < 1s | BenchmarkDotNet |

**性能测试代码**:
```csharp
[MemoryDiagnoser]
public class ToolPerformanceBenchmarks
{
    [Benchmark]
    public async Task<string> GetSymbols_LargeFile()
    {
        // Test with 1000+ line file
    }

    [Benchmark]
    public async Task<string> SearchSymbols_Workspace()
    {
        // Test across entire workspace
    }
}
```

### 端到端测试

**测试场景**:

| 场景 | 步骤 | 验证点 |
|------|------|--------|
| 完整分析流程 | 1. 加载项目<br>2. 获取符号<br>3. 查看定义<br>4. 查找引用<br>5. 分析调用图 | 所有工具串行工作正常 |
| Token 优化 | 1. 使用 detail_level=compact<br>2. 使用 detail_level=full | 输出大小有明显差异 |
| 批量操作 | 1. 批量查询 10 个符号 | 单次调用完成 |
| 错误恢复 | 1. 触发错误<br>2. 重新加载工作区 | 系统自动恢复 |

---

## 测试数据

### TestAssets 结构

```
TestAssets/
├── SimpleProject/              # 基础测试
│   ├── SimpleClass.cs
│   ├── WithInheritance.cs
│   └── WithGenerics.cs
│
├── MediumProject/              # 中等复杂度
│   ├── Interfaces/
│   ├── Classes/
│   └── Extensions/
│
├── LargeSolution/              # 大型解决方案
│   ├── Project1/               # 10+ 项目
│   ├── Project2/
│   └── ...
│
├── EdgeCases/                  # 边界情况
│   ├── InvalidSyntax.cs
│   ├── PreprocessorDirectives.cs
│   └── UnsafeCode.cs
│
└── Performance/                # 性能测试
    └── LargeFile.cs            # 5000+ 行
```

---

## 开发工作流

### 分支策略

```
main (protected)
  ↑
  └── release/* (发布分支)
       ↑
       └── develop (开发主分支)
            ↑
            ├── feature/* (功能分支)
            ├── bugfix/* (修复分支)
            └── hotfix/* (紧急修复)
```

### 提交规范

```
<type>(<scope>): <subject>

<body>

<footer>
```

**类型**:
- `feat`: 新功能
- `fix`: Bug 修复
- `refactor`: 重构
- `test`: 测试相关
- `docs`: 文档
- `perf`: 性能优化
- `chore`: 构建/工具

**示例**:
```
feat(tools): implement get_inheritance_hierarchy tool

- Add base type traversal
- Add interface listing
- Add derived class discovery

Closes #123
```

### Code Review 检查清单

- [ ] 代码符合项目风格指南
- [ ] 单元测试通过
- [ ] 新功能有对应测试
- [ ] 没有编译警告
- [ ] XML 文档注释完整
- [ ] 性能测试通过（如适用）
- [ ] 文档已更新

---

## 质量指标

### 代码质量

| 指标 | 目标 | 工具 |
|------|------|------|
| 单元测试覆盖率 | > 75% | Coverlet |
| 代码重复率 | < 5% | ReSharper |
| 圈复杂度 | < 15 | SonarQube |
| 维护性指数 | > 70 | SonarQube |

### API 稳定性

| 版本 | 类型 | 说明 |
|------|------|------|
| 0.1.x | Alpha | 内部测试 |
| 0.2.x | Beta | 公开测试 |
| 0.3.x | RC | 候选发布 |
| 1.0.x | Stable | 稳定发布 |

---

## 里程碑

| 里程碑 | 日期 | 交付物 |
|--------|------|--------|
| M1: 基础设施 | Week 1 结束 | 项目结构、WorkspaceManager |
| M2: Essential 工具 | Week 3 结束 | 4 个核心工具可用 |
| M3: HighValue 工具 | Week 5 结束 | 高级分析功能完成 |
| M4: 优化完成 | Week 6 结束 | 性能优化、新工具 |
| M5: 发布准备 | Week 8 结束 | v0.1.0 发布 |

---

## 风险管理

| 风险 | 影响 | 概率 | 缓解措施 |
|------|------|------|----------|
| Roslyn API 学习曲线 | 高 | 中 | 提前技术预研、参考现有实现 |
| 性能不达标 | 高 | 低 | 早期性能测试、缓存优化 |
| MCP SDK 变更 | 中 | 中 | 版本锁定、关注上游更新 |
| 测试覆盖不足 | 中 | 中 | TDD 实践、强制 CR |

---

## 发布清单

- [ ] 所有 P0 测试通过
- [ ] 性能基准达标
- [ ] API 文档完整
- [ ] README 更新
- [ ] 变更日志生成
- [ ] NuGet 包打包
- [ ] Docker 镜像构建（可选）
