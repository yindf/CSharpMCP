# CSharp MCP Server

> A Roslyn-based Model Context Protocol Server providing powerful C# code analysis and navigation capabilities

[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Documentation / 文档

Please select your language / 请选择语言:

- **[English Documentation](README.en.md)** - English version of the documentation
- **[中文文档](README.zh.md)** - 中文版文档

## Quick Links / 快速链接

| English | 中文 |
|---------|------|
| [API Reference](docs/API.en.md) | [API 参考](docs/API.md) |

## Overview

CSharp MCP Server is a Model Context Protocol (MCP) Server built with Roslyn API, providing powerful C# code analysis capabilities for AI assistants. Unlike LSP-based implementations, this server accesses complete semantic information for more accurate and in-depth code analysis.

### Key Features

- 🎯 **Complete Semantic Analysis** - Direct Roslyn API access for full symbol information
- 🌲 **Inheritance Hierarchy** - View complete type inheritance chains
- 📊 **Call Graph Analysis** - Analyze method callers and callees
- ⚡ **Token Optimization** - Tiered responses and smart truncation
- 🔍 **Advanced Navigation** - Fuzzy matching and batch operations

## Quick Start

```bash
# Clone the repository
git clone https://github.com/yindf/CSharpLSP.git
cd CSharpLSP

# Build
dotnet build

# Run
dotnet run --project src/CSharpMcp.Server
```

## License

MIT License - See [LICENSE](LICENSE) file for details.
