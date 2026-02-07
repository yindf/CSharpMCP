#!/usr/bin/env python3
"""
C# MCP Server Test Runner using dotnet run
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
    try:
        print(f"{c}{s}{C.N}")
    except:
        print(f"{s}")

def call_mcp_server(workspace, tool_name, args):
    """Call MCP server using dotnet run"""
    request = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "tools/call",
        "params": {
            "name": tool_name,
            "arguments": {
                "parameters": args
            }
        }
    }

    print(f"  Calling: {tool_name}")

    try:
        proc = subprocess.Popen(
            ["dotnet", "run", "--configuration", "Release", "--no-build", "--project", "src/CSharpMcp.Server/CSharpMcp.Server.csproj"],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=False,
            cwd=str(workspace)
        )

        request_str = json.dumps(request) + "\n"
        request_bytes = request_str.encode('utf-8')

        # Write request and flush but don't close stdin yet
        proc.stdin.write(request_bytes)
        proc.stdin.flush()

        # Wait for response
        import time
        time.sleep(3)  # Give server time to process

        # Now close stdin to signal end of input
        proc.stdin.close()

        # Read all output
        stdout_bytes, stderr_bytes = proc.communicate(timeout=60)

        # Decode stdout
        try:
            stdout = stdout_bytes.decode('utf-8')
        except:
            stdout = stdout_bytes.decode('utf-8', errors='replace')

        # Parse response
        if not stdout.strip():
            return {"error": "Empty stdout", "stderr": stderr_bytes.decode('utf-8', errors='replace')[:500] if stderr_bytes else None}

        try:
            response = json.loads(stdout.strip())
            return response
        except json.JSONDecodeError:
            # Try to find JSON in output
            for line in stdout.strip().split('\n'):
                if line.strip().startswith('{'):
                    try:
                        return json.loads(line)
                    except:
                        pass

        return {"raw": stdout[:500] if stdout else "", "stderr_count": len(stderr_bytes)}

    except subprocess.TimeoutExpired:
        proc.kill()
        stdout_bytes, stderr_bytes = proc.communicate()
        return {"error": "timeout"}
    except Exception as e:
        return {"error": str(e)}

def main():
    workspace = Path.cwd()

    pr(C.BD, "\n" + "="*50)
    pr(C.C, "C# MCP Server Test Runner (dotnet run)")
    pr(C.BD, "="*50 + "\n")

    results = []
    passed = 0
    failed = 0

    # Test cases: (name, tool, args) - using actual source files
    tests = [
        # Essential Tools - test against actual source code
        ("get_symbols - All symbols", "GetSymbols", {
            "filePath": "src/CSharpMcp.Server/Program.cs",
            "detailLevel": "Summary"
        }),
        ("get_symbols - Filter Methods", "GetSymbols", {
            "filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs",
            "filterKinds": ["Method"],
            "detailLevel": "Summary"
        }),
        ("get_symbols - Fuzzy path", "GetSymbols", {
            "filePath": "Program.cs",
            "detailLevel": "Compact"
        }),
        ("go_to_definition - Main", "GoToDefinition", {
            "filePath": "src/CSharpMcp.Server/Program.cs",
            "lineNumber": 15,
            "symbolName": "Main",
            "detailLevel": "Full"
        }),
        ("find_references - ILogger", "FindReferences", {
            "filePath": "src/CSharpMcp.Server/Program.cs",
            "lineNumber": 1,
            "symbolName": "ILogger",
            "includeContext": False,
            "contextLines": 1
        }),
        ("resolve_symbol - Program", "ResolveSymbol", {
            "filePath": "src/CSharpMcp.Server/Program.cs",
            "lineNumber": 13,
            "symbolName": "Program",
            "detailLevel": "Standard"
        }),
        ("search_symbols - *Workspace*", "SearchSymbols", {
            "query": "*Workspace*",
            "detailLevel": "Summary",
            "maxResults": 10
        }),
        ("search_symbols - IWorkspaceManager", "SearchSymbols", {
            "query": "IWorkspaceManager",
            "detailLevel": "Compact",
            "maxResults": 20
        }),
        # HighValue Tools
        ("get_inheritance_hierarchy - WorkspaceManager", "GetInheritanceHierarchy", {
            "filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs",
            "lineNumber": 11,
            "symbolName": "WorkspaceManager",
            "includeDerived": False,
            "maxDerivedDepth": 1
        }),
        ("get_inheritance_hierarchy - IDisposable", "GetInheritanceHierarchy", {
            "filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs",
            "lineNumber": 11,
            "symbolName": "IWorkspaceManager",
            "includeDerived": True,
            "maxDerivedDepth": 2
        }),
        ("get_call_graph - LoadAsync", "GetCallGraph", {
            "filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs",
            "lineNumber": 36,
            "symbolName": "LoadAsync",
            "direction": "Out",
            "maxDepth": 1,
            "includeExternalCalls": False
        }),
        ("get_type_members - WorkspaceManager", "GetTypeMembers", {
            "filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs",
            "lineNumber": 11,
            "symbolName": "WorkspaceManager",
            "includeInherited": True
        }),
        ("get_type_members - Filter Methods", "GetTypeMembers", {
            "filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs",
            "lineNumber": 11,
            "symbolName": "WorkspaceManager",
            "filterKinds": ["Method"]
        }),
        # Optimization Tools
        ("get_symbol_complete - WorkspaceManager", "GetSymbolComplete", {
            "filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs",
            "lineNumber": 11,
            "symbolName": "WorkspaceManager",
            "sections": "All",
            "detailLevel": "Standard",
            "includeReferences": False,
            "maxReferences": 10,
            "includeInheritance": True,
            "includeCallGraph": False,
            "callGraphMaxDepth": 1
        }),
        ("batch_get_symbols - Batch", "BatchGetSymbols", {
            "symbols": [
                {"filePath": "src/CSharpMcp.Server/Program.cs", "lineNumber": 13, "symbolName": "Program"},
                {"filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs", "lineNumber": 36, "symbolName": "LoadAsync"},
                {"filePath": "src/CSharpMcp.Server/Roslyn/IWorkspaceManager.cs", "lineNumber": 40, "symbolName": "IWorkspaceManager"}
            ],
            "detailLevel": "Summary",
            "maxConcurrency": 3
        }),
        ("get_diagnostics - File", "GetDiagnostics", {
            "filePath": "src/CSharpMcp.Server/Program.cs",
            "includeWarnings": True,
            "includeInfo": False,
            "includeHidden": False
        }),
        ("get_diagnostics - Workspace", "GetDiagnostics", {
            "includeWarnings": True,
            "includeInfo": False,
            "severityFilter": ["Error", "Warning"]
        }),
        # Edge cases
        ("get_symbols - Not found (error)", "GetSymbols", {
            "filePath": "nonexistent.cs"
        }),
        ("go_to_definition - Invalid symbol", "GoToDefinition", {
            "filePath": "src/CSharpMcp.Server/Program.cs",
            "lineNumber": 15,
            "symbolName": "NonExistentMethod"
        }),
        ("search_symbols - No matches", "SearchSymbols", {
            "query": "NonExistentSymbolXYZ123",
            "detailLevel": "Compact"
        }),
    ]

    for i, (name, tool, args) in enumerate(tests, 1):
        pr(C.Y, f"\n[{i}/{len(tests)}] {name}")
        result = call_mcp_server(workspace, tool, args)

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

        # Check if it's an actual error response from MCP
        is_mcp_error = result.get("result", {}).get("isError", False)

        if is_error_case:
            test_passed = has_error or is_mcp_error
        else:
            test_passed = has_content and not has_error and not is_mcp_error

        test_result["passed"] = test_passed
        results.append(test_result)

        if test_passed:
            pr(C.G, f"  [PASS] PASSED")
            passed += 1
        else:
            pr(C.R, f"  [FAIL] FAILED")
            if is_mcp_error:
                content = result.get("result", {}).get("content", [])
                if content and len(content) > 0:
                    pr(C.R, f"  MCP Error: {content[0].get('text', 'Unknown error')}")
            elif has_error:
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
