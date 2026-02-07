# Contributing to CSharp MCP Server

感谢您对 CSharp MCP Server 的贡献！

## 开发流程

### 1. Fork 和克隆

```bash
# Fork 仓库到你的 GitHub 账号
# 然后克隆你的 fork
git clone https://github.com/your-username/CSharpMcp.git
cd CSharpMcp
```

### 2. 创建功能分支

```bash
git checkout -b feature/your-feature-name
```

### 3. 开发和测试

```bash
# 还原依赖
dotnet restore

# 构建
dotnet build

# 运行测试
dotnet test

# 运行测试并生成覆盖率报告
dotnet test --collect:"XPlat Code Coverage"
```

### 4. 提交更改

请遵循 [Conventional Commits](https://www.conventionalcommits.org/) 规范：

```
feat(tools): add new code analysis tool
fix(analyzer): resolve symbol resolution bug
docs(api): update API documentation
refactor(cache): improve caching performance
test(integration): add integration tests for workspace loading
```

### 5. 推送更改

```bash
git push origin feature/your-feature-name
```

### 6. 创建 Pull Request

在 GitHub 上创建 Pull Request，并填写 PR 模板。

## 代码规范

### C# 代码风格

- 使用 4 空格缩进
- 使用 PascalCase 命名类、方法、属性
- 使用 camelCase 命名局部变量、参数
- 使用 _camelCase 命名私有字段
- 所有公共成员必须添加 XML 文档注释

### 示例

```csharp
/// <summary>
/// 获取文档中的所有符号
/// </summary>
/// <param name="document">要分析的文档</param>
/// <param name="cancellationToken">取消令牌</param>
/// <returns>文档中的所有符号</returns>
public async Task<IReadOnlyList<ISymbol>> GetDocumentSymbolsAsync(
    Document document,
    CancellationToken cancellationToken)
{
    // 实现
}
```

### 测试规范

- 单元测试覆盖率目标：> 75%
- 测试方法使用描述性名称
- 使用 Arrange-Act-Assert 模式

```csharp
[Fact]
public async Task GetSymbols_WithValidDocument_ReturnsSymbols()
{
    // Arrange
    var tool = CreateTool();

    // Act
    var result = await tool.ExecuteAsync(parameters);

    // Assert
    Assert.NotNull(result);
}
```

## Pull Request 检查清单

在提交 PR 之前，请确保：

- [ ] 代码符合项目风格指南
- [ ] 所有测试通过（`dotnet test`）
- [ ] 新功能有对应的测试
- [ ] 更新了相关文档（如需要）
- [ ] 没有引入新的编译警告
- [ ] PR 描述清晰说明了更改内容

## 添加新工具

当添加新工具时，请遵循以下步骤：

1. 在相应的 `Tools/` 目录下创建工具类
2. 继承自 `McpTool` 基类
3. 实现 `ExecuteAsync` 方法
4. 在 `Models/Tools/ToolParams.cs` 中添加参数类
5. 在 `Models/Output/ToolResponses.cs` 中添加响应类
6. 在 `Program.cs` 中注册工具
7. 添加相应的单元测试和集成测试

## 报告问题

使用 [GitHub Issues](https://github.com/your-org/CSharpMcp/issues) 报告问题，请提供：

- 问题描述
- 复现步骤
- 预期行为
- 实际行为
- 环境信息（.NET 版本、操作系统等）

## 许可证

通过贡献，您同意您的代码将根据项目的 MIT 许可证进行许可。
