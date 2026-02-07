# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Phase 1: 基础设施（WorkspaceManager, SymbolAnalyzer, CompilationCache）
- Phase 2: Essential 工具（get_symbols, go_to_definition, find_references, resolve_symbol, search_symbols）
- Phase 3: HighValue 工具（get_inheritance_hierarchy, get_call_graph, get_type_members）
- Phase 4: Optimization 工具（get_symbol_complete, batch_get_symbols, get_diagnostics）
- 完整的 API 文档
- 集成测试框架

### Changed
- 从 LSP 迁移到直接使用 Roslyn API
- 改进的语义信息获取
- Token 优化策略

## [0.1.0] - 2024-01-XX

### Added
- 初始版本发布
- 基础代码导航功能
- Roslyn 集成
- MCP 协议支持

### Known Limitations
- 需要预先加载工作区
- 大型代码库性能待优化
- 部分高级功能未实现
