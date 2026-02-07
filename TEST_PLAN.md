# Roslyn MCP Server - 测试计划

## 一、测试策略概述

### 1.1 测试金字塔

```
                    ┌─────────┐
                   ╱           ╲
                  ╱    E2E      ╲      5%  - 关键用户场景
                 ╱               ╲
                └─────────────────┘
              ┌─────────────────────┐
             ╱                       ╲
            ╱    Integration          ╲   20% - 工具交互
           ╱                           ╲
          └─────────────────────────────┘
        ┌─────────────────────────────────┐
       ╱                                     ╲
      ╱            Unit Tests                 ╲  75% - 核心逻辑
     ╱                                         ╲
    └─────────────────────────────────────────────┘
```

### 1.2 测试分类

| 分类 | 标记 | 运行频率 | 执行时间 |
|------|------|----------|----------|
| 快速单元测试 | `Category("Unit")` | 每次 PR | < 5s |
| 慢速单元测试 | `Category("SlowUnit")` | 每次 PR | < 30s |
| 集成测试 | `Category("Integration")` | 每次 PR | < 2min |
| 性能测试 | `Category("Performance")` | 每日 | 可变 |
| E2E 测试 | `Category("E2E")` | 发布前 | < 5min |

---

## 二、单元测试计划

### 2.1 核心服务测试

#### WorkspaceManagerTests

```csharp
[Trait("Category", "Unit")]
public class WorkspaceManagerTests
{
    [Fact]
    public async Task LoadAsync_WithValidSolution_ReturnsWorkspaceInfo()
    {
        // Arrange
        var manager = CreateWorkspaceManager();
        var solutionPath = GetTestAssetPath("SimpleSolution.sln");

        // Act
        var info = await manager.LoadAsync(solutionPath);

        // Assert
        Assert.NotNull(info);
        Assert.Equal(WorkspaceKind.Solution, info.Kind);
        Assert.True(info.ProjectCount > 0);
    }

    [Fact]
    public async Task GetDocumentAsync_WithValidPath_ReturnsDocument()
    {
        // Arrange
        var manager = CreateWorkspaceManager();
        await manager.LoadAsync(GetTestAssetPath("SimpleSolution.sln"));
        var filePath = GetTestAssetPath("SimpleClass.cs");

        // Act
        var document = await manager.GetDocumentAsync(filePath);

        // Assert
        Assert.NotNull(document);
        Assert.Equal("SimpleClass.cs", document.Name);
    }

    [Fact]
    public async Task GetDocumentAsync_WithInvalidPath_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorkspaceManager();
        await manager.LoadAsync(GetTestAssetPath("SimpleSolution.sln"));

        // Act
        var document = await manager.GetDocumentAsync("nonexistent.cs");

        // Assert
        Assert.Null(document);
    }

    [Fact]
    public async Task GetCompilationAsync_ReturnsValidCompilation()
    {
        // Arrange
        var manager = CreateWorkspaceManager();
        await manager.LoadAsync(GetTestAssetPath("SimpleSolution.sln"));

        // Act
        var compilation = await manager.GetCompilationAsync();

        // Assert
        Assert.NotNull(compilation);
        Assert.True(compilation.References.Length > 0);
    }

    [Theory]
    [InlineData("TestClass.cs", 1, true)]
    [InlineData("TestClass.cs", 999, false)]
    [InlineData("NonExistent.cs", 1, false)]
    public async Task GetSemanticModelAsync_ReturnsExpectedResult(
        string filePath, int line, bool shouldExist)
    {
        // Arrange
        var manager = CreateWorkspaceManager();
        await manager.LoadAsync(GetTestAssetPath("SimpleSolution.sln"));

        // Act
        var model = await manager.GetSemanticModelAsync(filePath);

        // Assert
        if (shouldExist)
            Assert.NotNull(model);
        else
            Assert.Null(model);
    }
}
```

#### SymbolAnalyzerTests

```csharp
[Trait("Category", "Unit")]
public class SymbolAnalyzerTests
{
    [Fact]
    public async Task GetDocumentSymbolsAsync_ReturnsAllSymbols()
    {
        // Arrange
        var analyzer = CreateSymbolAnalyzer();
        var document = await GetTestDocumentAsync("ClassWithMembers.cs");

        // Act
        var symbols = await analyzer.GetDocumentSymbolsAsync(document);

        // Assert
        Assert.NotEmpty(symbols);
        Assert.Contains(symbols, s => s.Kind == SymbolKind.NamedType);
        Assert.Contains(symbols, s => s.Kind == SymbolKind.Method);
    }

    [Fact]
    public async Task ResolveSymbolAtPositionAsync_WithValidPosition_ReturnsSymbol()
    {
        // Arrange
        var analyzer = CreateSymbolAnalyzer();
        var document = await GetTestDocumentAsync("SimpleClass.cs");

        // Act
        var symbol = await analyzer.ResolveSymbolAtPositionAsync(
            document,
            lineNumber: 10,
            column: 15
        );

        // Assert
        Assert.NotNull(symbol);
    }

    [Fact]
    public async Task FindSymbolsByNameAsync_WithExactName_ReturnsMatchingSymbols()
    {
        // Arrange
        var analyzer = CreateSymbolAnalyzer();
        var document = await GetTestDocumentAsync("ClassWithMembers.cs");

        // Act
        var symbols = await analyzer.FindSymbolsByNameAsync(document, "TestMethod");

        // Assert
        Assert.NotEmpty(symbols);
        Assert.All(symbols, s => Assert.Contains("TestMethod", s.Name));
    }

    [Theory]
    [InlineData("TestMethod", null)]
    [InlineData("TestMethod", 15)]
    [InlineData("AnotherMethod", 25)]
    public async Task FindSymbolsByNameAsync_WithLineNumber_UsesFuzzySearch(
        string symbolName, int? lineNumber)
    {
        // Arrange
        var analyzer = CreateSymbolAnalyzer();
        var document = await GetTestDocumentAsync("ClassWithMembers.cs");

        // Act
        var symbols = await analyzer.FindSymbolsByNameAsync(document, symbolName, lineNumber);

        // Assert
        Assert.NotEmpty(symbols);
    }

    [Fact]
    public async Task ToSymbolInfoAsync_WithCompactLevel_ReturnsMinimalInfo()
    {
        // Arrange
        var analyzer = CreateSymbolAnalyzer();
        var document = await GetTestDocumentAsync("SimpleClass.cs");
        var symbols = await analyzer.GetDocumentSymbolsAsync(document);
        var symbol = symbols.First();

        // Act
        var info = await analyzer.ToSymbolInfoAsync(
            symbol,
            DetailLevel.Compact,
            null
        );

        // Assert
        Assert.NotNull(info);
        Assert.NotNull(info.Name);
        Assert.NotNull(info.Kind);
        Assert.Null(info.Documentation);
        Assert.Null(info.SourceCode);
    }

    [Fact]
    public async Task ToSymbolInfoAsync_WithFullLevel_ReturnsCompleteInfo()
    {
        // Arrange
        var analyzer = CreateSymbolAnalyzer();
        var document = await GetTestDocumentAsync("SimpleClass.cs");
        var symbols = await analyzer.GetDocumentSymbolsAsync(document);
        var symbol = symbols.First(s => s.Kind == SymbolKind.Method);

        // Act
        var info = await analyzer.ToSymbolInfoAsync(
            symbol,
            DetailLevel.Full,
            100
        );

        // Assert
        Assert.NotNull(info);
        Assert.NotNull(info.Signature);
        Assert.NotNull(info.SourceCode);
        Assert.NotEmpty(info.SourceCode);
    }
}
```

#### CallGraphAnalyzerTests

```csharp
[Trait("Category", "Unit")]
public class CallGraphAnalyzerTests
{
    [Fact]
    public async Task GetCallersAsync_WithCalledMethod_ReturnsCallers()
    {
        // Arrange
        var analyzer = CreateCallGraphAnalyzer();
        var method = await GetTestMethodAsync("CalledMethod");
        var solution = await GetTestSolutionAsync();

        // Act
        var callers = await analyzer.GetCallersAsync(method, solution, maxDepth: 1);

        // Assert
        Assert.NotNull(callers);
        // Assert that we find the methods that call this one
    }

    [Fact]
    public async Task GetCalleesAsync_WithCallingMethod_ReturnsCallees()
    {
        // Arrange
        var analyzer = CreateCallGraphAnalyzer();
        var method = await GetTestMethodAsync("CallingMethod");
        var document = await GetTestDocumentAsync("CallGraphTestClass.cs");

        // Act
        var callees = await analyzer.GetCalleesAsync(method, document, maxDepth: 1);

        // Assert
        Assert.NotNull(callees);
        // Assert that we find the methods called by this one
    }

    [Fact]
    public async Task CalculateCyclomaticComplexityAsync_ReturnsCorrectValue()
    {
        // Arrange
        var analyzer = CreateCallGraphAnalyzer();
        var simpleMethod = await GetTestMethodAsync("SimpleMethod");
        var complexMethod = await GetTestMethodAsync("ComplexMethod");
        var document = await GetTestDocumentAsync("ComplexityTestClass.cs");

        // Act
        var simpleComplexity = await analyzer.CalculateCyclomaticComplexityAsync(simpleMethod, document);
        var complexComplexity = await analyzer.CalculateCyclomaticComplexityAsync(complexMethod, document);

        // Assert
        Assert.True(simpleComplexity < complexComplexity);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task GetCallGraphAsync_RespectsMaxDepth(int maxDepth)
    {
        // Arrange
        var analyzer = CreateCallGraphAnalyzer();
        var method = await GetTestMethodAsync("NestedCallMethod");
        var solution = await GetTestSolutionAsync();

        // Act
        var result = await analyzer.GetCallGraphAsync(
            method,
            solution,
            CallGraphDirection.Out,
            maxDepth
        );

        // Assert
        Assert.NotNull(result);
        // Verify depth is respected
    }
}
```

#### InheritanceAnalyzerTests

```csharp
[Trait("Category", "Unit")]
public class InheritanceAnalyzerTests
{
    [Fact]
    public async Task GetInheritanceTreeAsync_ReturnsCompleteTree()
    {
        // Arrange
        var analyzer = CreateInheritanceAnalyzer();
        var type = await GetTestTypeAsync("DerivedClass");
        var solution = await GetTestSolutionAsync();

        // Act
        var tree = await analyzer.GetInheritanceTreeAsync(
            type,
            solution,
            includeDerived: true,
            maxDerivedDepth: 2
        );

        // Assert
        Assert.NotNull(tree);
        Assert.NotEmpty(tree.BaseTypes);
        Assert.NotNull(tree.Interfaces);
    }

    [Fact]
    public void GetBaseTypeChain_ReturnsCorrectChain()
    {
        // Arrange
        var analyzer = CreateInheritanceAnalyzer();
        var type = GetTestType("DeeplyDerivedClass");

        // Act
        var chain = analyzer.GetBaseTypeChain(type);

        // Assert
        Assert.Equal(3, chain.Count); // DeeplyDerived -> Derived -> Base -> Object
    }

    [Fact]
    public async Task FindDerivedTypesAsync_ReturnsAllDerived()
    {
        // Arrange
        var analyzer = CreateInheritanceAnalyzer();
        var baseType = await GetTestTypeAsync("BaseClass");
        var solution = await GetTestSolutionAsync();

        // Act
        var derived = await analyzer.FindDerivedTypesAsync(baseType, solution);

        // Assert
        Assert.Contains(derived, t => t.Name == "DerivedClass");
        Assert.Contains(derived, t => t.Name == "AnotherDerivedClass");
    }
}
```

#### CacheTests

```csharp
[Trait("Category", "Unit")]
public class CompilationCacheTests
{
    [Fact]
    public async Task GetOrAddAsync_WithMiss_CreatesAndCachesValue()
    {
        // Arrange
        var cache = CreateCache();
        var key = "test_key";
        var factory = () => Task.FromResult<Compilation?>(CreateMockCompilation());

        // Act
        var result1 = await cache.GetOrAddAsync(key, factory);
        var result2 = await cache.GetOrAddAsync(key, factory);

        // Assert
        Assert.NotNull(result1);
        Assert.Same(result1, result2); // Same instance = cached
    }

    [Fact]
    public async Task Invalidate_RemovesCachedValue()
    {
        // Arrange
        var cache = CreateCache();
        var key = "test_key";
        await cache.GetOrAddAsync(key, () => Task.FromResult<Compilation?>(CreateMockCompilation()));

        // Act
        cache.Invalidate(key);
        var result = await cache.GetOrAddAsync(key, () => Task.FromResult<Compilation?>(null));

        // Assert
        Assert.Null(result); // Factory was not called, value was removed
    }

    [Fact]
    public void GetStatistics_ReturnsCorrectMetrics()
    {
        // Arrange
        var cache = CreateCache();

        // Act
        var stats = cache.GetStatistics();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(0, stats.HitCount);
        Assert.Equal(0, stats.MissCount);
    }
}
```

### 2.2 工具测试

#### GetSymbolsToolTests

```csharp
[Trait("Category", "Unit")]
public class GetSymbolsToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithValidFile_ReturnsSymbols()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetSymbolsParams
        {
            FilePath = GetTestAssetPath("ClassWithMembers.cs"),
            DetailLevel = DetailLevel.Summary
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<GetSymbolsResponse>(response);
        Assert.NotEmpty(result.Symbols);
    }

    [Theory]
    [InlineData(DetailLevel.Compact)]
    [InlineData(DetailLevel.Summary)]
    [InlineData(DetailLevel.Standard)]
    [InlineData(DetailLevel.Full)]
    public async Task ExecuteAsync_RespectsDetailLevel(DetailLevel level)
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetSymbolsParams
        {
            FilePath = GetTestAssetPath("ClassWithMembers.cs"),
            DetailLevel = level
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<GetSymbolsResponse>(response);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ExecuteAsync_WithIncludeBodyFalse_ExcludesMethodBodies()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetSymbolsParams
        {
            FilePath = GetTestAssetPath("ClassWithMembers.cs"),
            IncludeBody = false
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<GetSymbolsResponse>(response);
        Assert.All(result.Symbols, s => Assert.Null(s.SourceCode));
    }

    [Fact]
    public async Task ExecuteAsync_WithBodyMaxLines_TruncatesOutput()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetSymbolsParams
        {
            FilePath = GetTestAssetPath("ClassWithLargeMethod.cs"),
            IncludeBody = true,
            BodyMaxLines = 10
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<GetSymbolsResponse>(response);
        var largeMethod = result.Symbols.FirstOrDefault(s =>
            s.SourceCode != null && s.SourceCode.Split('\n').Length > 10);
        Assert.NotNull(largeMethod);
    }

    [Fact]
    public async Task ExecuteAsync_WithFilterKinds_ReturnsFilteredSymbols()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetSymbolsParams
        {
            FilePath = GetTestAssetPath("ClassWithMembers.cs"),
            FilterKinds = new[] { Models.SymbolKind.Method }
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<GetSymbolsResponse>(response);
        Assert.All(result.Symbols, s => Assert.Equal(Models.SymbolKind.Method, s.Kind));
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidPath_ReturnsError()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetSymbolsParams
        {
            FilePath = "nonexistent.cs"
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<ErrorResponse>(response);
        Assert.Contains("not found", result.Message);
    }
}
```

#### GoToDefinitionToolTests

```csharp
[Trait("Category", "Unit")]
public class GoToDefinitionToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithValidLineNumber_ReturnsDefinition()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GoToDefinitionParams
        {
            FilePath = GetTestAssetPath("ClassWithReferences.cs"),
            LineNumber = 15,
            SymbolName = "ReferencedMethod"
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<GoToDefinitionResponse>(response);
        Assert.NotNull(result.Symbol);
    }

    [Fact]
    public async Task ExecuteAsync_WithOnlySymbolName_ReturnsDefinition()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GoToDefinitionParams
        {
            FilePath = GetTestAssetPath("ClassWithReferences.cs"),
            SymbolName = "ReferencedMethod"
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<GoToDefinitionResponse>(result);
        Assert.NotNull(result.Symbol);
    }

    [Fact]
    public async Task ExecuteAsync_WithFullDetailLevel_ReturnsCompleteInfo()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GoToDefinitionParams
        {
            FilePath = GetTestAssetPath("ClassWithReferences.cs"),
            LineNumber = 15,
            DetailLevel = DetailLevel.Full
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<GoToDefinitionResponse>(response);
        Assert.NotNull(result.Symbol.SourceCode);
        Assert.NotNull(result.Symbol.Signature);
    }

    [Theory]
    [InlineData(true, 5)]
    [InlineData(true, 20)]
    [InlineData(false, 100)]
    public async Task ExecuteAsync_RespectsIncludeBodyAndMaxLines(
        bool includeBody, int maxLines)
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GoToDefinitionParams
        {
            FilePath = GetTestAssetPath("ClassWithLargeMethod.cs"),
            LineNumber = 10,
            IncludeBody = includeBody,
            BodyMaxLines = maxLines
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<GoToDefinitionResponse>(response);
        if (includeBody)
        {
            var lines = result.Symbol.SourceCode?.Split('\n').Length ?? 0;
            Assert.True(lines <= maxLines);
        }
        else
        {
            Assert.Null(result.Symbol.SourceCode);
        }
    }
}
```

#### FindReferencesToolTests

```csharp
[Trait("Category", "Unit")]
public class FindReferencesToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithValidSymbol_ReturnsReferences()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new FindReferencesParams
        {
            FilePath = GetTestAssetPath("ClassWithReferences.cs"),
            LineNumber = 20,
            SymbolName = "ReferencedMethod"
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<FindReferencesResponse>(response);
        Assert.NotEmpty(result.References);
    }

    [Fact]
    public async Task ExecuteAsync_WithIncludeContext_IncludesCodeContext()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new FindReferencesParams
        {
            FilePath = GetTestAssetPath("ClassWithReferences.cs"),
            LineNumber = 20,
            SymbolName = "ReferencedMethod",
            IncludeContext = true,
            ContextLines = 3
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<FindReferencesResponse>(response);
        Assert.All(result.References, r => Assert.NotNull(r.ContextCode));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCorrectSummary()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new FindReferencesParams
        {
            FilePath = GetTestAssetPath("ClassWithReferences.cs"),
            LineNumber = 20
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<FindReferencesResponse>(response);
        Assert.NotNull(result.Summary);
        Assert.Equal(result.References.Count, result.Summary.TotalReferences);
    }
}
```

#### SearchSymbolsToolTests

```csharp
[Trait("Category", "Unit")]
public class SearchSymbolsToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithQuery_ReturnsMatchingSymbols()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new SearchSymbolsParams
        {
            Query = "Test"
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<SearchSymbolsResponse>(response);
        Assert.NotEmpty(result.Symbols);
        Assert.All(result.Symbols, s => Assert.Contains("test", s.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("MyClass.*")]
    [InlineData("*.Test*")]
    [InlineData("*Service")]
    public async Task ExecuteAsync_WithWildcardQuery_ReturnsMatches(string query)
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new SearchSymbolsParams
        {
            Query = query
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<SearchSymbolsResponse>(response);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ExecuteAsync_WithMaxResults_LimitsResults()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new SearchSymbolsParams
        {
            Query = "M",
            MaxResults = 5
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<SearchSymbolsResponse>(response);
        Assert.True(result.Symbols.Count <= 5);
    }
}
```

#### GetInheritanceHierarchyToolTests

```csharp
[Trait("Category", "Unit")]
public class GetInheritanceHierarchyToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithDerivedType_ReturnsHierarchy()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetInheritanceHierarchyParams
        {
            FilePath = GetTestAssetPath("DerivedClass.cs"),
            SymbolName = "DerivedClass"
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<InheritanceHierarchyResponse>(response);
        Assert.NotEmpty(result.Hierarchy.BaseTypes);
    }

    [Fact]
    public async Task ExecuteAsync_WithInterface_ReturnsImplementedInterfaces()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetInheritanceHierarchyParams
        {
            FilePath = GetTestAssetPath("ClassImplementingInterface.cs"),
            SymbolName = "MyImplementation"
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<InheritanceHierarchyResponse>(response);
        Assert.NotEmpty(result.Hierarchy.Interfaces);
    }

    [Fact]
    public async Task ExecuteAsync_WithIncludeDerived_ReturnsDerivedTypes()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetInheritanceHierarchyParams
        {
            FilePath = GetTestAssetPath("BaseClass.cs"),
            SymbolName = "BaseClass",
            IncludeDerived = true
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<InheritanceHierarchyResponse>(response);
        Assert.NotEmpty(result.Hierarchy.DerivedTypes);
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesCorrectDepth()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetInheritanceHierarchyParams
        {
            FilePath = GetTestAssetPath("DeeplyDerivedClass.cs"),
            SymbolName = "DeeplyDerivedClass"
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<InheritanceHierarchyResponse>(response);
        Assert.Equal(3, result.Hierarchy.Depth);
    }
}
```

#### GetCallGraphToolTests

```csharp
[Trait("Category", "Unit")]
public class GetCallGraphToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithDirectionBoth_ReturnsBothCallersAndCallees()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetCallGraphParams
        {
            FilePath = GetTestAssetPath("MethodWithCalls.cs"),
            SymbolName = "TargetMethod",
            Direction = CallGraphDirection.Both
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<CallGraphResponse>(result);
        Assert.NotNull(result.Callers);
        Assert.NotNull(result.Callees);
    }

    [Theory]
    [InlineData(CallGraphDirection.In)]
    [InlineData(CallGraphDirection.Out)]
    public async Task ExecuteAsync_RespectsDirection(CallGraphDirection direction)
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetCallGraphParams
        {
            FilePath = GetTestAssetPath("MethodWithCalls.cs"),
            SymbolName = "TargetMethod",
            Direction = direction
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<CallGraphResponse>(result);
        if (direction == CallGraphDirection.In || direction == CallGraphDirection.Both)
        {
            Assert.NotNull(result.Callers);
        }
        if (direction == CallGraphDirection.Out || direction == CallGraphDirection.Both)
        {
            Assert.NotNull(result.Callees);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCorrectStatistics()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetCallGraphParams
        {
            FilePath = GetTestAssetPath("MethodWithCalls.cs"),
            SymbolName = "TargetMethod"
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<CallGraphResponse>(result);
        Assert.NotNull(result.Statistics);
        Assert.Equal(result.Callers.Count, result.Statistics.TotalCallers);
        Assert.Equal(result.Callees.Count, result.Statistics.TotalCallees);
    }
}
```

#### GetTypeMembersToolTests

```csharp
[Trait("Category", "Unit")]
public class GetTypeMembersToolTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsAllMembers()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetTypeMembersParams
        {
            FilePath = GetTestAssetPath("ClassWithMembers.cs"),
            SymbolName = "TestClass"
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<GetTypeMembersResponse>(result);
        Assert.NotNull(result.Members);
    }

    [Fact]
    public async Task ExecuteAsync_WithIncludeInherited_ReturnsInheritedMembers()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetTypeMembersParams
        {
            FilePath = GetTestAssetPath("DerivedClass.cs"),
            SymbolName = "DerivedClass",
            IncludeInherited = true
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<GetTypeMembersResponse>(result);
        Assert.NotEmpty(result.Members.InheritedMembers);
    }

    [Fact]
    public async Task ExecuteAsync_WithFilterKinds_ReturnsFilteredMembers()
    {
        // Arrange
        var tool = CreateTool();
        var parameters = new GetTypeMembersParams
        {
            FilePath = GetTestAssetPath("ClassWithMembers.cs"),
            SymbolName = "TestClass",
            FilterKinds = new[] { Models.SymbolKind.Method, Models.SymbolKind.Property }
        };

        // Act
        var response = await tool.ExecuteAsync(parameters);

        // Assert
        var result = Assert.IsType<GetTypeMembersResponse>(result);
        Assert.NotEmpty(result.Members.Methods);
        Assert.NotEmpty(result.Members.Properties);
    }
}
```

---

## 三、集成测试计划

### 3.1 工作流集成测试

```csharp
[Trait("Category", "Integration")]
public class AnalysisWorkflowTests
{
    [Fact]
    public async Task CompleteSymbolAnalysisWorkflow()
    {
        // 1. Load workspace
        var workspaceManager = CreateWorkspaceManager();
        await workspaceManager.LoadAsync(GetTestAssetPath("TestSolution.sln"));

        // 2. Search for symbols
        var searchTool = new SearchSymbolsTool(...);
        var searchResult = await searchTool.ExecuteAsync(new SearchSymbolsParams
        {
            Query = "TestClass"
        });
        var searchResponse = Assert.IsType<SearchSymbolsResponse>(searchResult);

        // 3. Get detailed symbol info
        var getSymbolsTool = new GetSymbolsTool(...);
        var getSymbolsResult = await getSymbolsTool.ExecuteAsync(new GetSymbolsParams
        {
            FilePath = searchResponse.Symbols.First().Location.FilePath
        });
        Assert.IsType<GetSymbolsResponse>(getSymbolsResult);

        // 4. Go to definition
        var goToDefinitionTool = new GoToDefinitionTool(...);
        var goToResult = await goToDefinitionTool.ExecuteAsync(new GoToDefinitionParams
        {
            FilePath = searchResponse.Symbols.First().Location.FilePath,
            SymbolName = searchResponse.Symbols.First().Name
        });
        Assert.IsType<GoToDefinitionResponse>(goToResult);

        // 5. Find references
        var findRefsTool = new FindReferencesTool(...);
        var refsResult = await findRefsTool.ExecuteAsync(new FindReferencesParams
        {
            FilePath = searchResponse.Symbols.First().Location.FilePath,
            SymbolName = searchResponse.Symbols.First().Name
        });
        Assert.IsType<FindReferencesResponse>(refsResult);
    }

    [Fact]
    public async Task CodeAnalysisWorkflow()
    {
        // 1. Load workspace
        var workspaceManager = CreateWorkspaceManager();
        await workspaceManager.LoadAsync(GetTestAssetPath("TestSolution.sln"));

        // 2. Get type members
        var getMembersTool = new GetTypeMembersTool(...);
        var membersResult = await getMembersTool.ExecuteAsync(new GetTypeMembersParams
        {
            FilePath = GetTestAssetPath("DerivedClass.cs"),
            SymbolName = "DerivedClass",
            IncludeInherited: true
        });
        var membersResponse = Assert.IsType<GetTypeMembersResponse>(membersResult);

        // 3. Get inheritance hierarchy
        var hierarchyTool = new GetInheritanceHierarchyTool(...);
        var hierarchyResult = await hierarchyTool.ExecuteAsync(new GetInheritanceHierarchyParams
        {
            FilePath = GetTestAssetPath("DerivedClass.cs"),
            SymbolName = "DerivedClass"
        });
        Assert.IsType<InheritanceHierarchyResponse>(hierarchyResult);

        // 4. Get call graph for a method
        var callGraphTool = new GetCallGraphTool(...);
        var callGraphResult = await callGraphTool.ExecuteAsync(new GetCallGraphParams
        {
            FilePath = GetTestAssetPath("DerivedClass.cs"),
            SymbolName = membersResponse.Members.Methods.First().Name
        });
        Assert.IsType<CallGraphResponse>(callGraphResult);
    }
}
```

### 3.2 跨项目集成测试

```csharp
[Trait("Category", "Integration")]
public class CrossProjectTests
{
    [Fact]
    public async Task FindReferences_AcrossProjects_FindsAllReferences()
    {
        // Arrange
        var workspaceManager = CreateWorkspaceManager();
        await workspaceManager.LoadAsync(GetTestAssetPath("MultiProjectSolution.sln"));

        var findRefsTool = new FindReferencesTool(...);

        // Act
        var result = await findRefsTool.ExecuteAsync(new FindReferencesParams
        {
            FilePath = GetTestAssetPath("Project1/SharedClass.cs"),
            SymbolName = "SharedMethod"
        });

        // Assert
        var response = Assert.IsType<FindReferencesResponse>(result);
        // Should find references in multiple projects
        Assert.True(response.Summary.Files.Count > 1);
    }

    [Fact]
    public async Task SearchSymbols_AcrossProjects_ReturnsAllMatches()
    {
        // Arrange
        var workspaceManager = CreateWorkspaceManager();
        await workspaceManager.LoadAsync(GetTestAssetPath("MultiProjectSolution.sln"));

        var searchTool = new SearchSymbolsTool(...);

        // Act
        var result = await searchTool.ExecuteAsync(new SearchSymbolsParams
        {
            Query = "Shared*",
            MaxResults = 100
        });

        // Assert
        var response = Assert.IsType<SearchSymbolsResponse>(result);
        Assert.NotEmpty(response.Symbols);
        // Verify symbols from multiple projects
    }
}
```

### 3.3 错误处理集成测试

```csharp
[Trait("Category", "Integration")]
public class ErrorHandlingTests
{
    [Fact]
    public async Task InvalidFilePath_ReturnsErrorResponse()
    {
        // Arrange
        var tool = new GetSymbolsTool(...);

        // Act
        var result = await tool.ExecuteAsync(new GetSymbolsParams
        {
            FilePath = "completely/nonexistent/path.cs"
        });

        // Assert
        Assert.IsType<ErrorResponse>(result);
    }

    [Fact]
    public async Task CompilationErrors_DoesNotCrash()
    {
        // Arrange
        var workspaceManager = CreateWorkspaceManager();
        await workspaceManager.LoadAsync(GetTestAssetPath("SolutionWithErrors.sln"));

        var tool = new GetSymbolsTool(...);

        // Act & Assert
        var result = await tool.ExecuteAsync(new GetSymbolsParams
        {
            FilePath = GetTestAssetPath("FileWithErrors.cs")
        });

        // Should either return results or error, but not throw
        Assert.True(result is GetSymbolsResponse or ErrorResponse);
    }

    [Fact]
    public async Task SymbolNotFound_ReturnsHelpfulError()
    {
        // Arrange
        var tool = new GoToDefinitionTool(...);

        // Act
        var result = await tool.ExecuteAsync(new GoToDefinitionParams
        {
            FilePath = GetTestAssetPath("SimpleClass.cs"),
            SymbolName = "NonExistentSymbol"
        });

        // Assert
        var error = Assert.IsType<ErrorResponse>(result);
        Assert.Contains("not found", error.Message);
    }
}
```

---

## 四、性能测试计划

### 4.1 基准测试

```csharp
[Trait("Category", "Performance")]
public class PerformanceBenchmarks
{
    [Fact]
    public async Task LoadLargeSolution_CompletesInTime()
    {
        // Arrange
        var workspaceManager = CreateWorkspaceManager();
        var sw = Stopwatch.StartNew();

        // Act
        await workspaceManager.LoadAsync(GetTestAssetPath("LargeSolution.sln"));
        sw.Stop();

        // Assert
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"Loading took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
    }

    [Fact]
    public async Task GetSymbols_5000LineFile_CompletesFast()
    {
        // Arrange
        var tool = new GetSymbolsTool(...);
        var sw = Stopwatch.StartNew();

        // Act
        var result = await tool.ExecuteAsync(new GetSymbolsParams
        {
            FilePath = GetTestAssetPath("Performance/LargeFile.cs")
        });
        sw.Stop();

        // Assert
        Assert.IsType<GetSymbolsResponse>(result);
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"GetSymbols took {sw.ElapsedMilliseconds}ms, expected < 500ms");
    }

    [Fact]
    public async Task SearchSymbols_Workspace_ReturnsQuickly()
    {
        // Arrange
        var tool = new SearchSymbolsTool(...);
        var sw = Stopwatch.StartNew();

        // Act
        var result = await tool.ExecuteAsync(new SearchSymbolsParams
        {
            Query = "Test",
            MaxResults = 100
        });
        sw.Stop();

        // Assert
        Assert.IsType<SearchSymbolsResponse>(result);
        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"Search took {sw.ElapsedMilliseconds}ms, expected < 1000ms");
    }

    [Fact]
    public async Task CallGraph_ComplexMethod_CompletesInTime()
    {
        // Arrange
        var tool = new GetCallGraphTool(...);
        var sw = Stopwatch.StartNew();

        // Act
        var result = await tool.ExecuteAsync(new GetCallGraphParams
        {
            FilePath = GetTestAssetPath("ComplexMethod.cs"),
            SymbolName = "ComplexMethod",
            MaxDepth = 3
        });
        sw.Stop();

        // Assert
        Assert.IsType<CallGraphResponse>(result);
        Assert.True(sw.ElapsedMilliseconds < 3000,
            $"Call graph took {sw.ElapsedMilliseconds}ms, expected < 3000ms");
    }
}
```

### 4.2 缓存有效性测试

```csharp
[Trait("Category", "Performance")]
public class CacheEffectivenessTests
{
    [Fact]
    public async Task SecondCall_IsFasterDueToCache()
    {
        // Arrange
        var tool = new GetSymbolsTool(...);

        // Act - First call
        var sw1 = Stopwatch.StartNew();
        await tool.ExecuteAsync(new GetSymbolsParams
        {
            FilePath = GetTestAssetPath("ClassWithMembers.cs")
        });
        sw1.Stop();

        // Act - Second call (should be cached)
        var sw2 = Stopwatch.StartNew();
        await tool.ExecuteAsync(new GetSymbolsParams
        {
            FilePath = GetTestAssetPath("ClassWithMembers.cs")
        });
        sw2.Stop();

        // Assert
        Assert.True(sw2.ElapsedMilliseconds < sw1.ElapsedMilliseconds,
            $"Second call ({sw2.ElapsedMilliseconds}ms) should be faster than first ({sw1.ElapsedMilliseconds}ms)");
    }

    [Fact]
    public async Task CacheHitRate_IsAboveThreshold()
    {
        // Arrange
        var workspaceManager = CreateWorkspaceManager();
        await workspaceManager.LoadAsync(GetTestAssetPath("TestSolution.sln"));

        // Act - Make multiple calls to same documents
        var filePaths = Enumerable.Range(0, 10)
            .Select(_ => GetTestAssetPath("ClassWithMembers.cs"))
            .ToList();

        foreach (var path in filePaths)
        {
            await workspaceManager.GetDocumentAsync(path);
        }

        // Assert
        var status = workspaceManager.GetStatus();
        Assert.True(status.CacheHitRate > 0.5,
            $"Cache hit rate {status.CacheHitRate:P0} should be > 50%");
    }
}
```

---

## 五、端到端测试计划

### 5.1 用户场景测试

```csharp
[Trait("Category", "E2E")]
public class EndToEndTests
{
    [Fact]
    public async Task Scenario_CodeNavigation()
    {
        // User wants to:
        // 1. Find a class by name
        // 2. See its members
        // 3. Navigate to a method definition
        // 4. Find where that method is called

        var server = CreateMcpServer();

        // Step 1: Search for class
        var searchResult = await server.CallToolAsync("search_symbols", new
        {
            query = "UserController"
        });
        var searchResponse = ParseResponse<SearchSymbolsResponse>(searchResult);
        Assert.NotEmpty(searchResponse.Symbols);

        // Step 2: Get class members
        var membersResult = await server.CallToolAsync("get_type_members", new
        {
            file_path = searchResponse.Symbols[0].Location.FilePath,
            symbol_name = "UserController"
        });
        var membersResponse = ParseResponse<GetTypeMembersResponse>(membersResult);
        Assert.NotEmpty(membersResponse.Members.Methods);

        // Step 3: Go to method definition
        var defResult = await server.CallToolAsync("go_to_definition", new
        {
            file_path = searchResponse.Symbols[0].Location.FilePath,
            symbol_name = membersResponse.Members.Methods[0].Name
        });
        var defResponse = ParseResponse<GoToDefinitionResponse>(defResult);
        Assert.NotNull(defResponse.Symbol);

        // Step 4: Find references
        var refsResult = await server.CallToolAsync("find_references", new
        {
            file_path = searchResponse.Symbols[0].Location.FilePath,
            symbol_name = membersResponse.Members.Methods[0].Name
        });
        var refsResponse = ParseResponse<FindReferencesResponse>(refsResult);
        Assert.NotEmpty(refsResponse.References);
    }

    [Fact]
    public async Task Scenario_CodeAnalysis()
    {
        // User wants to:
        // 1. Understand a class's inheritance
        // 2. Analyze a complex method's call graph
        // 3. Review all overridden members

        var server = CreateMcpServer();

        // Step 1: Get inheritance hierarchy
        var hierarchyResult = await server.CallToolAsync("get_inheritance_hierarchy", new
        {
            file_path = GetTestAssetPath("DerivedController.cs"),
            symbol_name = "DerivedController"
        });
        var hierarchyResponse = ParseResponse<InheritanceHierarchyResponse>(hierarchyResult);
        Assert.NotEmpty(hierarchyResponse.Hierarchy.BaseTypes);

        // Step 2: Get call graph
        var callGraphResult = await server.CallToolAsync("get_call_graph", new
        {
            file_path = GetTestAssetPath("DerivedController.cs"),
            symbol_name = "ProcessRequest",
            direction = "both",
            max_depth = 2
        });
        var callGraphResponse = ParseResponse<CallGraphResponse>(callGraphResult);
        Assert.NotNull(callGraphResponse.Callers);
        Assert.NotNull(callGraphResponse.Callees);

        // Step 3: Get type members with inherited
        var membersResult = await server.CallToolAsync("get_type_members", new
        {
            file_path = GetTestAssetPath("DerivedController.cs"),
            symbol_name = "DerivedController",
            include_inherited = true
        });
        var membersResponse = ParseResponse<GetTypeMembersResponse>(membersResult);
        Assert.NotEmpty(membersResponse.Members.InheritedMembers);
    }

    [Fact]
    public async Task Scenario_TokenOptimization()
    {
        // User wants to minimize token usage:
        // 1. Use compact mode for overview
        // 2. Use full mode only when needed
        // 3. Use batch queries

        var server = CreateMcpServer();

        // Step 1: Get compact overview
        var compactResult = await server.CallToolAsync("get_symbols", new
        {
            file_path = GetTestAssetPath("LargeClass.cs"),
            detail_level = "compact"
        });
        var compactResponse = ParseResponse<GetSymbolsResponse>(compactResult);
        var compactSize = compactResponse.ToMarkdown().Length;

        // Step 2: Get full details
        var fullResult = await server.CallToolAsync("get_symbols", new
        {
            file_path = GetTestAssetPath("LargeClass.cs"),
            detail_level = "full"
        });
        var fullResponse = ParseResponse<GetSymbolsResponse>(fullResult);
        var fullSize = fullResponse.ToMarkdown().Length;

        // Assert: compact should be significantly smaller
        Assert.True(compactSize < fullSize / 2,
            $"Compact ({compactSize} chars) should be < 50% of full ({fullSize} chars)");

        // Step 3: Batch query
        var batchResult = await server.CallToolAsync("batch_get_symbols", new
        {
            queries = new[]
            {
                new { file_path = GetTestAssetPath("Class1.cs"), symbol_name = "Class1" },
                new { file_path = GetTestAssetPath("Class2.cs"), symbol_name = "Class2" },
                new { file_path = GetTestAssetPath("Class3.cs"), symbol_name = "Class3" }
            }
        });
        var batchResponse = ParseResponse<BatchGetSymbolsResponse>(batchResult);
        Assert.Equal(3, batchResponse.Results.Count);
    }
}
```

---

## 六、测试数据

### 6.1 测试代码文件

```csharp
// TestAssets/SimpleProject/ClassWithMembers.cs
namespace TestProject
{
    /// <summary>
    /// A test class with various members for testing
    /// </summary>
    public class ClassWithMembers : IDisposable
    {
        // Fields
        private int _id;
        private readonly string _name;
        public const int MaxCount = 100;

        // Properties
        public int Id { get; set; }
        public string Name { get; }
        public bool IsEnabled { get; private set; }

        // Events
        public event EventHandler? SomethingHappened;

        // Constructor
        public ClassWithMembers(int id, string name)
        {
            _id = id;
            _name = name;
        }

        // Methods
        public void Process()
        {
            // Simple method
        }

        public virtual void ProcessVirtual()
        {
            // Virtual method
        }

        private void InternalProcess()
        {
            // Private method
        }

        protected void ProtectedProcess()
        {
            // Protected method
        }

        public static void StaticProcess()
        {
            // Static method
        }

        public string GetData()
        {
            return _name;
        }

        public void Dispose()
        {
            // Cleanup
        }
    }

    public interface ITestInterface
    {
        void Process();
        string GetData();
    }

    public enum TestEnum
    {
        First,
        Second,
        Third
    }

    public record TestRecord(int Id, string Name);
}
```

```csharp
// TestAssets/MediumProject/Inheritance/InheritanceChain.cs
namespace TestProject.Inheritance
{
    public abstract class BaseController
    {
        public virtual void Initialize() { }
        public abstract void Process();
    }

    public class Controller : BaseController
    {
        private readonly ILogger _logger;

        public Controller(ILogger logger)
        {
            _logger = logger;
        }

        public override void Initialize()
        {
            base.Initialize();
            _logger.Log("Initialized");
        }

        public override void Process()
        {
            // Process logic
        }

        public void Execute()
        {
            Initialize();
            Process();
        }
    }

    public class DerivedController : Controller
    {
        public DerivedController(ILogger logger) : base(logger) { }

        public override void Process()
        {
            base.Process();
            // Additional processing
        }

        public new void Execute()
        {
            base.Execute();
            // Additional execution
        }
    }

    public class DeeplyDerivedController : DerivedController
    {
        public DeeplyDerivedController(ILogger logger) : base(logger) { }

        public void ProcessRequest()
        {
            Execute();
        }
    }
}
```

```csharp
// TestAssets/MediumProject/CallGraph/CallGraphTestClass.cs
namespace TestProject.CallGraph
{
    public class CallGraphTestClass
    {
        public void EntryMethod()
        {
            Helper1();
            Helper2();
        }

        private void Helper1()
        {
            NestedHelper();
        }

        private void Helper2()
        {
            if (DateTime.Now.Second > 30)
            {
                Helper1();
            }
            else
            {
                NestedHelper();
            }
        }

        private void NestedHelper()
        {
            // Leaf method
        }

        public int ComplexMethod(int input)
        {
            if (input < 0)
                return -1;
            else if (input < 10)
                return input * 2;
            else if (input < 100)
            {
                int sum = 0;
                for (int i = 0; i < input; i++)
                {
                    sum += i;
                }
                return sum;
            }
            else
            {
                return Helper1ReturnsOne();
            }
        }

        private int Helper1ReturnsOne()
        {
            return 1;
        }
    }

    public class CallerClass
    {
        private readonly CallGraphTestClass _testClass = new();

        public void CallEntry()
        {
            _testClass.EntryMethod();
        }

        public void CallComplex()
        {
            _testClass.ComplexMethod(50);
        }
    }
}
```

```csharp
// TestAssets/EdgeCases/Generics.cs
namespace TestProject.Generics
{
    public interface IRepository<T>
    {
        T GetById(int id);
        IEnumerable<T> GetAll();
        void Add(T entity);
        void Delete(T entity);
    }

    public class Repository<T> : IRepository<T> where T : class
    {
        private readonly List<T> _items = new();

        public T GetById(int id) => _items[id];
        public IEnumerable<T> GetAll() => _items;
        public void Add(T entity) => _items.Add(entity);
        public void Delete(T entity) => _items.Remove(entity);
    }

    public class Service<TKey, TValue> where TKey : notnull where TValue : new()
    {
        private readonly Dictionary<TKey, TValue> _cache = new();

        public TValue Get(TKey key)
        {
            if (_cache.TryGetValue(key, out var value))
                return value;

            value = new TValue();
            _cache[key] = value;
            return value;
        }
    }

    public static class EnumerableExtensions
    {
        public static IEnumerable<T> WhereIf<T>(
            this IEnumerable<T> source,
            bool condition,
            Func<T, bool> predicate)
        {
            return condition ? source.Where(predicate) : source;
        }
    }
}
```

---

## 七、测试执行计划

### 7.1 本地开发测试

```bash
# 运行所有单元测试
dotnet test --filter "Category=Unit"

# 运行特定类的测试
dotnet test --filter "FullyQualifiedName~WorkspaceManagerTests"

# 运行并生成代码覆盖率
dotnet test --collect:"XPlat Code Coverage"
```

### 7.2 CI/CD 测试

```yaml
# .github/workflows/test.yml
name: Run Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'

      - name: Run Unit Tests
        run: dotnet test --filter "Category=Unit" --no-build

      - name: Run Integration Tests
        run: dotnet test --filter "Category=Integration" --no-build

      - name: Run Performance Tests
        run: dotnet test --filter "Category=Performance" --no-build

      - name: Upload Coverage
        uses: codecov/codecov-action@v3
```

---

## 八、测试通过标准

### 8.1 代码覆盖率目标

| 组件 | 目标覆盖率 | 最低覆盖率 |
|------|-----------|-----------|
| Roslyn 服务 | 80% | 70% |
| Essential 工具 | 75% | 65% |
| HighValue 工具 | 70% | 60% |
| MediumValue 工具 | 70% | 60% |
| 缓存层 | 85% | 75% |

### 8.2 性能基准

| 操作 | 目标 | 最大可接受 |
|------|------|-----------|
| 加载解决方案 | < 3s | 5s |
| 获取符号 | < 300ms | 500ms |
| 搜索符号 | < 500ms | 1s |
| 查找引用 | < 1s | 2s |
| 调用图 | < 2s | 3s |

### 8.3 发布前检查清单

- [ ] 所有单元测试通过
- [ ] 所有集成测试通过
- [ ] 代码覆盖率达标
- [ ] 性能基准通过
- [ ] 无内存泄漏
- [ ] 无线程安全问题
- [ ] API 文档完整
