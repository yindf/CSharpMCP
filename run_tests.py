#!/usr/bin/env python3
"""
Simple C# MCP Server Test Runner using direct JSON-RPC via stdio
"""

import subprocess
import json
import sys
from pathlib import Path
from datetime import datetime

# ANSI colors
class C:
    G = '\033[92m'
    R = '\033[91m'
    Y = '\033[93m'
    C = '\033[96m'
    B = '\033[94m'
    N = '\033[0m'
    BD = '\033[1m'

def pr(c, s):
    import sys
    try:
        print(f"{c}{s}{C.N}")
    except:
        print(f"{s}")

def call_mcp_server(server_path, workspace, tool_name, args):
    """Call MCP server directly via stdio"""
    request = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "tools/call",
        "params": {
            "name": tool_name,
            "arguments": args
        }
    }

    print(f"  Calling: {tool_name}")

    try:
        proc = subprocess.Popen(
            [str(server_path)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            cwd=str(workspace)
        )

        request_str = json.dumps(request) + "\n"
        stdout, stderr = proc.communicate(input=request_str, timeout=60)

        # Parse response - the entire stdout is a single JSON response
        try:
            response = json.loads(stdout.strip())
            return response
        except json.JSONDecodeError:
            # If there are multiple lines, try to parse each
            for line in stdout.strip().split('\n'):
                if line.strip():
                    try:
                        return json.loads(line)
                    except:
                        continue

        return {"raw": stdout[:500], "stderr": stderr[:500] if stderr else None}

    except subprocess.TimeoutExpired:
        proc.kill()
        stdout, stderr = proc.communicate()
        return {"error": "timeout", "stdout": stdout[:200], "stderr": stderr[:200]}
    except Exception as e:
        return {"error": str(e)}

def main():
    workspace = Path.cwd()
    server = workspace / "publish" / "CSharpMcp.Server.exe"

    pr(C.BD, "\n" + "="*50)
    pr(C.C, "C# MCP Server Test Runner")
    pr(C.BD, "="*50 + "\n")

    if not server.exists():
        pr(C.R, f"Server not found: {server}")
        return 1

    results = []
    passed = 0
    failed = 0

    # Test cases: (name, tool, args)
    tests = [
        # Essential Tools
        ("get_symbols - All symbols", "GetSymbols", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "detailLevel": "Full"
        }),
        ("get_symbols - Filter Methods", "GetSymbols", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "filterKinds": ["Method"],
            "detailLevel": "Summary"
        }),
        ("get_symbols - Fuzzy path", "GetSymbols", {
            "filePath": "SimpleTestClass.cs",
            "detailLevel": "Compact"
        }),
        ("go_to_definition - Calculate", "GoToDefinition", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "lineNumber": 22,
            "symbolName": "Calculate",
            "detailLevel": "Full"
        }),
        ("find_references - TestMethod", "FindReferences", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "lineNumber": 17,
            "symbolName": "TestMethod",
            "includeContext": True,
            "contextLines": 2
        }),
        ("resolve_symbol - SimpleTestClass", "ResolveSymbol", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "lineNumber": 6,
            "symbolName": "SimpleTestClass",
            "detailLevel": "Standard"
        }),
        ("search_symbols - *Controller*", "SearchSymbols", {
            "query": "*Controller*",
            "detailLevel": "Summary",
            "maxResults": 10
        }),
        ("search_symbols - TestAssets.*", "SearchSymbols", {
            "query": "TestAssets.*",
            "detailLevel": "Compact",
            "maxResults": 20
        }),
        # HighValue Tools
        ("get_inheritance_hierarchy - BaseController", "GetInheritanceHierarchy", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs",
            "lineNumber": 6,
            "symbolName": "BaseController",
            "includeDerived": True,
            "maxDerivedDepth": 2
        }),
        ("get_inheritance_hierarchy - DerivedTestClass", "GetInheritanceHierarchy", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "lineNumber": 36,
            "symbolName": "DerivedTestClass",
            "includeDerived": False
        }),
        ("get_call_graph - Execute", "GetCallGraph", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs",
            "lineNumber": 32,
            "symbolName": "Execute",
            "direction": "Out",
            "maxDepth": 2,
            "includeExternalCalls": False
        }),
        ("get_type_members - BaseController", "GetTypeMembers", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/MediumProject/InheritanceChain.cs",
            "lineNumber": 6,
            "symbolName": "BaseController",
            "includeInherited": True
        }),
        ("get_type_members - Filter Methods", "GetTypeMembers", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "lineNumber": 6,
            "symbolName": "SimpleTestClass",
            "filterKinds": ["Method"]
        }),
        # Optimization Tools
        ("get_symbol_complete - Calculate", "GetSymbolComplete", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "lineNumber": 22,
            "symbolName": "Calculate",
            "sections": "All",
            "detailLevel": "Standard",
            "includeReferences": True,
            "maxReferences": 10,
            "includeInheritance": False,
            "includeCallGraph": True,
            "callGraphMaxDepth": 1
        }),
        ("batch_get_symbols - Batch", "BatchGetSymbols", {
            "symbols": [
                {"filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "lineNumber": 22, "symbolName": "Calculate"},
                {"filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "lineNumber": 17, "symbolName": "TestMethod"},
                {"filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs", "lineNumber": 6, "symbolName": "SimpleTestClass"}
            ],
            "detailLevel": "Summary",
            "maxConcurrency": 3
        }),
        ("get_diagnostics - File", "GetDiagnostics", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "includeWarnings": True,
            "includeInfo": True,
            "includeHidden": False
        }),
        ("get_diagnostics - Workspace", "GetDiagnostics", {
            "includeWarnings": False,
            "includeInfo": False,
            "severityFilter": ["Error"]
        }),
        # Edge cases
        ("get_symbols - Not found (error)", "GetSymbols", {
            "filePath": "nonexistent.cs"
        }),
        ("go_to_definition - Invalid symbol", "GoToDefinition", {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "lineNumber": 22,
            "symbolName": "NonExistentMethod"
        }),
        ("search_symbols - No matches", "SearchSymbols", {
            "query": "NonExistentSymbolXYZ123",
            "detailLevel": "Compact"
        }),
    ]

    for i, (name, tool, args) in enumerate(tests, 1):
        pr(C.Y, f"\n[{i}/{len(tests)}] {name}")
        result = call_mcp_server(server, workspace, tool, args)

        test_result = {
            "name": name,
            "tool": tool,
            "input": args,
            "output": result,
            "timestamp": datetime.now().isoformat()
        }

        # Check if passed
        is_error_case = "error" in name.lower() or "invalid" in name.lower() or "not found" in name.lower() or "no matches" in name.lower()
        has_error = "error" in result or (isinstance(result.get("result"), dict) and "error" in result.get("result", {}))
        has_content = "result" in result or "content" in result

        if is_error_case:
            test_passed = has_error
        else:
            test_passed = has_content and not has_error

        test_result["passed"] = test_passed
        results.append(test_result)

        if test_passed:
            pr(C.G, f"  [PASS] PASSED")
            passed += 1
        else:
            pr(C.R, f"  [FAIL] FAILED")
            if has_error:
                pr(C.R, f"  Error: {result.get('error', 'Unknown error')}")
            failed += 1

    # Summary
    pr(C.C, "\n" + "="*50)
    pr(C.C, "SUMMARY")
    pr(C.C, "="*50 + "\n")
    print(f"Total: {len(tests)}")
    pr(C.G, f"Passed: {passed}")
    pr(C.R, f"Failed: {failed}")
    rate = passed / len(tests) * 100 if tests else 0
    c = C.G if rate >= 80 else C.Y if rate >= 50 else C.R
    pr(c, f"Pass rate: {rate:.1f}%")

    # Save results
    report = {
        "summary": {
            "total": len(tests),
            "passed": passed,
            "failed": failed,
            "passRate": rate,
            "startTime": datetime.now().isoformat()
        },
        "tests": results
    }

    with open("test_results.json", "w") as f:
        json.dump(report, f, indent=2, default=str)
    pr(C.C, f"\nResults saved to: test_results.json")

    return 0 if failed == 0 else 1

if __name__ == "__main__":
    sys.exit(main())
