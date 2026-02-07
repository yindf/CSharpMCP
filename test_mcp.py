#!/usr/bin/env python3
"""
C# MCP Server Comprehensive Test Script
Tests all tools exposed by the C# MCP server using MCP inspector
"""

import subprocess
import json
import sys
import os
from pathlib import Path
from datetime import datetime
from typing import Any, Dict, List

# ANSI color codes
class Colors:
    GREEN = '\033[92m'
    RED = '\033[91m'
    YELLOW = '\033[93m'
    CYAN = '\033[96m'
    BLUE = '\033[94m'
    RESET = '\033[0m'
    BOLD = '\033[1m'

def print_color(color: str, text: str):
    print(f"{color}{text}{Colors.RESET}")

class McpTester:
    def __init__(self, server_path: str, workspace_path: str):
        self.server_path = Path(server_path)
        self.workspace_path = Path(workspace_path)
        self.results = {
            "summary": {
                "total": 0,
                "passed": 0,
                "failed": 0,
                "startTime": datetime.now().isoformat(),
                "endTime": None
            },
            "tests": []
        }

    def invoke_tool(self, tool_name: str, arguments: Dict[str, Any]) -> Dict[str, Any]:
        """Invoke an MCP tool using npx @modelcontextprotocol/inspector"""

        payload = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/call",
            "params": {
                "name": tool_name,
                "arguments": arguments
            }
        }

        # Create a temporary file with the request
        temp_input = Path("mcp_test_input.json")
        with open(temp_input, 'w') as f:
            json.dump(payload, f)

        try:
            # Use npx mcp inspector with stdio transport
            cmd = [
                "npx", "--yes", "@modelcontextprotocol/inspector",
                "stdio",
                str(self.server_path),
                "--",
                "-w", str(self.workspace_path)
            ]

            result = subprocess.run(
                cmd,
                input=json.dumps(payload) + "\n",
                capture_output=True,
                text=True,
                timeout=30,
                cwd=str(self.workspace_path)
            )

            output = result.stdout
            if result.stderr:
                print(f"stderr: {result.stderr}")

            try:
                response = json.loads(output.strip().split('\n')[-1])
                return response
            except json.JSONDecodeError:
                return {"raw_output": output, "error": "Failed to parse JSON"}

        except subprocess.TimeoutExpired:
            return {"error": "Command timed out"}
        except Exception as e:
            return {"error": str(e)}
        finally:
            if temp_input.exists():
                temp_input.unlink()

    def add_test_result(self, test_name: str, tool: str, input_params: Dict,
                       output: Any, passed: bool, error_msg: str = None):
        """Add a test result to the results collection"""
        test_result = {
            "name": test_name,
            "tool": tool,
            "input": input_params,
            "output": str(output) if isinstance(output, str) or output is None else json.dumps(output, default=str),
            "passed": passed,
            "error": error_msg,
            "timestamp": datetime.now().isoformat()
        }

        self.results["tests"].append(test_result)
        self.results["summary"]["total"] += 1

        if passed:
            self.results["summary"]["passed"] += 1
            print_color(Colors.GREEN, f"✓ PASSED: {test_name}")
        else:
            self.results["summary"]["failed"] += 1
            print_color(Colors.RED, f"✗ FAILED: {test_name}")
            if error_msg:
                print(f"  Error: {error_msg}")
        print()

    def run_all_tests(self):
        """Run all test suites"""
        print_color(Colors.BOLD, "\n" + "="*60)
        print_color(Colors.CYAN, "C# MCP SERVER COMPREHENSIVE TEST SUITE")
        print_color(Colors.BOLD, "="*60 + "\n")

        self.test_essential_tools()
        self.test_highvalue_tools()
        self.test_optimization_tools()
        self.test_edge_cases()

        self.results["summary"]["endTime"] = datetime.now().isoformat()
        self.print_summary()

    def test_essential_tools(self):
        """Test Essential tools"""
        print_color(Colors.CYAN, "\n" + "="*40)
        print_color(Colors.CYAN, "TESTING ESSENTIAL TOOLS")
        print_color(Colors.CYAN, "="*40 + "\n")

        # Test 1: get_symbols - all symbols
        print_color(Colors.YELLOW, "Test 1: get_symbols - All symbols in SimpleTestClass.cs")
        result = self.invoke_tool("get_symbols", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "detail_level": "Full"
        })
        self.add_test_result(
            "get_symbols - SimpleTestClass.cs",
            "get_symbols",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "detail_level": "Full"},
            result,
            self.check_result_valid(result)
        )

        # Test 2: get_symbols - filter by Method
        print_color(Colors.YELLOW, "Test 2: get_symbols - Filter by Method kind")
        result = self.invoke_tool("get_symbols", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "filter_kinds": ["Method"],
            "detail_level": "Summary"
        })
        self.add_test_result(
            "get_symbols - Filter by Method",
            "get_symbols",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "filter_kinds": ["Method"], "detail_level": "Summary"},
            result,
            self.check_result_valid(result)
        )

        # Test 3: get_symbols - fuzzy path
        print_color(Colors.YELLOW, "Test 3: get_symbols - Fuzzy path matching")
        result = self.invoke_tool("get_symbols", {
            "file_path": "SimpleTestClass.cs",
            "detail_level": "Compact"
        })
        self.add_test_result(
            "get_symbols - Fuzzy path matching",
            "get_symbols",
            {"file_path": "SimpleTestClass.cs", "detail_level": "Compact"},
            result,
            self.check_result_valid(result)
        )

        # Test 4: go_to_definition
        print_color(Colors.YELLOW, "Test 4: go_to_definition - Calculate method")
        result = self.invoke_tool("go_to_definition", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "line_number": 22,
            "symbol_name": "Calculate",
            "detail_level": "Full"
        })
        self.add_test_result(
            "go_to_definition - Calculate method",
            "go_to_definition",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "line_number": 22, "symbol_name": "Calculate", "detail_level": "Full"},
            result,
            self.check_result_valid(result)
        )

        # Test 5: find_references
        print_color(Colors.YELLOW, "Test 5: find_references - TestMethod")
        result = self.invoke_tool("find_references", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "line_number": 17,
            "symbol_name": "TestMethod",
            "include_context": True,
            "context_lines": 2
        })
        self.add_test_result(
            "find_references - TestMethod",
            "find_references",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "line_number": 17, "symbol_name": "TestMethod", "include_context": True, "context_lines": 2},
            result,
            self.check_result_valid(result)
        )

        # Test 6: resolve_symbol
        print_color(Colors.YELLOW, "Test 6: resolve_symbol - SimpleTestClass")
        result = self.invoke_tool("resolve_symbol", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "line_number": 6,
            "symbol_name": "SimpleTestClass",
            "detail_level": "Standard"
        })
        self.add_test_result(
            "resolve_symbol - SimpleTestClass",
            "resolve_symbol",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "line_number": 6, "symbol_name": "SimpleTestClass", "detail_level": "Standard"},
            result,
            self.check_result_valid(result)
        )

        # Test 7: search_symbols - *Controller*
        print_color(Colors.YELLOW, "Test 7: search_symbols - *Controller*")
        result = self.invoke_tool("search_symbols", {
            "query": "*Controller*",
            "detail_level": "Summary",
            "max_results": 10
        })
        self.add_test_result(
            "search_symbols - *Controller*",
            "search_symbols",
            {"query": "*Controller*", "detail_level": "Summary", "max_results": 10},
            result,
            self.check_result_valid(result)
        )

        # Test 8: search_symbols - TestAssets.*
        print_color(Colors.YELLOW, "Test 8: search_symbols - TestAssets.*")
        result = self.invoke_tool("search_symbols", {
            "query": "TestAssets.*",
            "detail_level": "Compact",
            "max_results": 20
        })
        self.add_test_result(
            "search_symbols - TestAssets.*",
            "search_symbols",
            {"query": "TestAssets.*", "detail_level": "Compact", "max_results": 20},
            result,
            self.check_result_valid(result)
        )

    def test_highvalue_tools(self):
        """Test HighValue tools"""
        print_color(Colors.CYAN, "\n" + "="*40)
        print_color(Colors.CYAN, "TESTING HIGHVALUE TOOLS")
        print_color(Colors.CYAN, "="*40 + "\n")

        # Test 9: get_inheritance_hierarchy - BaseController
        print_color(Colors.YELLOW, "Test 9: get_inheritance_hierarchy - BaseController")
        result = self.invoke_tool("get_inheritance_hierarchy", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs",
            "line_number": 6,
            "symbol_name": "BaseController",
            "include_derived": True,
            "max_derived_depth": 2
        })
        self.add_test_result(
            "get_inheritance_hierarchy - BaseController",
            "get_inheritance_hierarchy",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs", "line_number": 6, "symbol_name": "BaseController", "include_derived": True, "max_derived_depth": 2},
            result,
            self.check_result_valid(result)
        )

        # Test 10: get_inheritance_hierarchy - DerivedTestClass
        print_color(Colors.YELLOW, "Test 10: get_inheritance_hierarchy - DerivedTestClass")
        result = self.invoke_tool("get_inheritance_hierarchy", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "line_number": 36,
            "symbol_name": "DerivedTestClass",
            "include_derived": False
        })
        self.add_test_result(
            "get_inheritance_hierarchy - DerivedTestClass",
            "get_inheritance_hierarchy",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "line_number": 36, "symbol_name": "DerivedTestClass", "include_derived": False},
            result,
            self.check_result_valid(result)
        )

        # Test 11: get_call_graph
        print_color(Colors.YELLOW, "Test 11: get_call_graph - Execute method")
        result = self.invoke_tool("get_call_graph", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs",
            "line_number": 32,
            "symbol_name": "Execute",
            "direction": "Out",
            "max_depth": 2,
            "include_external_calls": False
        })
        self.add_test_result(
            "get_call_graph - Execute method",
            "get_call_graph",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs", "line_number": 32, "symbol_name": "Execute", "direction": "Out", "max_depth": 2, "include_external_calls": False},
            result,
            self.check_result_valid(result)
        )

        # Test 12: get_type_members
        print_color(Colors.YELLOW, "Test 12: get_type_members - BaseController")
        result = self.invoke_tool("get_type_members", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs",
            "line_number": 6,
            "symbol_name": "BaseController",
            "include_inherited": True
        })
        self.add_test_result(
            "get_type_members - BaseController",
            "get_type_members",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs", "line_number": 6, "symbol_name": "BaseController", "include_inherited": True},
            result,
            self.check_result_valid(result)
        )

        # Test 13: get_type_members - filter by Method
        print_color(Colors.YELLOW, "Test 13: get_type_members - Filter by Method")
        result = self.invoke_tool("get_type_members", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "line_number": 6,
            "symbol_name": "SimpleTestClass",
            "filter_kinds": ["Method"]
        })
        self.add_test_result(
            "get_type_members - Filter by Method",
            "get_type_members",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "line_number": 6, "symbol_name": "SimpleTestClass", "filter_kinds": ["Method"]},
            result,
            self.check_result_valid(result)
        )

    def test_optimization_tools(self):
        """Test Optimization tools"""
        print_color(Colors.CYAN, "\n" + "="*40)
        print_color(Colors.CYAN, "TESTING OPTIMIZATION TOOLS")
        print_color(Colors.CYAN, "="*40 + "\n")

        # Test 14: get_symbol_complete
        print_color(Colors.YELLOW, "Test 14: get_symbol_complete - Calculate method")
        result = self.invoke_tool("get_symbol_complete", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "line_number": 22,
            "symbol_name": "Calculate",
            "sections": "All",
            "detail_level": "Standard",
            "include_references": True,
            "max_references": 10,
            "include_inheritance": False,
            "include_call_graph": True,
            "call_graph_max_depth": 1
        })
        self.add_test_result(
            "get_symbol_complete - Calculate method",
            "get_symbol_complete",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "line_number": 22, "symbol_name": "Calculate", "sections": "All", "detail_level": "Standard", "include_references": True, "max_references": 10, "include_inheritance": False, "include_call_graph": True, "call_graph_max_depth": 1},
            result,
            self.check_result_valid(result)
        )

        # Test 15: batch_get_symbols
        print_color(Colors.YELLOW, "Test 15: batch_get_symbols - Batch query")
        result = self.invoke_tool("batch_get_symbols", {
            "symbols": [
                {
                    "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
                    "line_number": 22,
                    "symbol_name": "Calculate"
                },
                {
                    "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
                    "line_number": 17,
                    "symbol_name": "TestMethod"
                },
                {
                    "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
                    "line_number": 6,
                    "symbol_name": "SimpleTestClass"
                }
            ],
            "detail_level": "Summary",
            "max_concurrency": 3
        })
        self.add_test_result(
            "batch_get_symbols - Batch query",
            "batch_get_symbols",
            {"symbols": [...], "detail_level": "Summary", "max_concurrency": 3},
            result,
            self.check_result_valid(result)
        )

        # Test 16: get_diagnostics - file specific
        print_color(Colors.YELLOW, "Test 16: get_diagnostics - SimpleTestClass.cs")
        result = self.invoke_tool("get_diagnostics", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "include_warnings": True,
            "include_info": True,
            "include_hidden": False
        })
        self.add_test_result(
            "get_diagnostics - SimpleTestClass.cs",
            "get_diagnostics",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "include_warnings": True, "include_info": True, "include_hidden": False},
            result,
            self.check_result_valid(result)
        )

        # Test 17: get_diagnostics - workspace
        print_color(Colors.YELLOW, "Test 17: get_diagnostics - Workspace")
        result = self.invoke_tool("get_diagnostics", {
            "include_warnings": False,
            "include_info": False,
            "severity_filter": ["Error"]
        })
        self.add_test_result(
            "get_diagnostics - Workspace",
            "get_diagnostics",
            {"include_warnings": False, "include_info": False, "severity_filter": ["Error"]},
            result,
            self.check_result_valid(result)
        )

    def test_edge_cases(self):
        """Test edge cases and error handling"""
        print_color(Colors.CYAN, "\n" + "="*40)
        print_color(Colors.CYAN, "TESTING EDGE CASES")
        print_color(Colors.CYAN, "="*40 + "\n")

        # Test 18: File not found
        print_color(Colors.YELLOW, "Test 18: get_symbols - Non-existent file (should error)")
        result = self.invoke_tool("get_symbols", {
            "file_path": "nonexistent.cs"
        })
        self.add_test_result(
            "get_symbols - Non-existent file error",
            "get_symbols",
            {"file_path": "nonexistent.cs"},
            result,
            self.check_has_error(result)
        )

        # Test 19: Symbol not found
        print_color(Colors.YELLOW, "Test 19: go_to_definition - Invalid symbol (should error)")
        result = self.invoke_tool("go_to_definition", {
            "file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "line_number": 22,
            "symbol_name": "NonExistentMethod"
        })
        self.add_test_result(
            "go_to_definition - Invalid symbol error",
            "go_to_definition",
            {"file_path": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "line_number": 22, "symbol_name": "NonExistentMethod"},
            result,
            self.check_has_error(result)
        )

        # Test 20: Search with no matches
        print_color(Colors.YELLOW, "Test 20: search_symbols - No matches query")
        result = self.invoke_tool("search_symbols", {
            "query": "NonExistentSymbolXYZ123",
            "detail_level": "Compact"
        })
        self.add_test_result(
            "search_symbols - No matches query",
            "search_symbols",
            {"query": "NonExistentSymbolXYZ123", "detail_level": "Compact"},
            result,
            self.check_result_valid(result)  # Should return empty results, not error
        )

    def check_result_valid(self, result) -> bool:
        """Check if result is valid (has content or result)"""
        if result is None:
            return False
        if isinstance(result, dict):
            return "result" in result or "content" in result or "error" not in result
        return True

    def check_has_error(self, result) -> bool:
        """Check if result has expected error"""
        if result is None:
            return False
        if isinstance(result, dict):
            return "error" in result or (result.get("result") and isinstance(result["result"], dict) and "error" in result["result"])
        return False

    def print_summary(self):
        """Print test summary"""
        print_color(Colors.CYAN, "\n" + "="*40)
        print_color(Colors.CYAN, "TEST SUMMARY")
        print_color(Colors.CYAN, "="*40 + "\n")

        summary = self.results["summary"]
        print(f"Total Tests: {summary['total']}")
        print_color(Colors.GREEN, f"Passed: {summary['passed']}")
        print_color(Colors.RED, f"Failed: {summary['failed']}")

        pass_rate = (summary['passed'] / summary['total'] * 100) if summary['total'] > 0 else 0
        color = Colors.GREEN if pass_rate >= 80 else Colors.YELLOW if pass_rate >= 50 else Colors.RED
        print_color(color, f"Pass Rate: {pass_rate:.1f}%")

        # Save results to file
        output_path = Path("test_results.json")
        with open(output_path, 'w') as f:
            json.dump(self.results, f, indent=2, default=str)
        print(f"\nTest results saved to: {output_path}")

        return summary['failed'] == 0

def main():
    workspace = Path.cwd()
    server_path = workspace / "publish" / "CSharpMcp.Server.exe"

    if not server_path.exists():
        print_color(Colors.RED, f"Server not found at: {server_path}")
        sys.exit(1)

    tester = McpTester(str(server_path), str(workspace))
    success = tester.run_all_tests()

    sys.exit(0 if success else 1)

if __name__ == "__main__":
    main()
