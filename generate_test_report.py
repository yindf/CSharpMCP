#!/usr/bin/env python3
"""
C# MCP Server Test Report Generator
Generates a comprehensive test report with all tool tests
"""

import subprocess
import json
import time
from pathlib import Path
from datetime import datetime

def call_tool(workspace, tool_name, params):
    """Call an MCP tool and return the result"""
    request = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "tools/call",
        "params": {
            "name": tool_name,
            "arguments": {
                "parameters": params
            }
        }
    }

    request_str = json.dumps(request) + "\n"

    proc = subprocess.Popen(
        ["dotnet", "run", "--configuration", "Release", "--no-build", "--project", "src/CSharpMcp.Server/CSharpMcp.Server.csproj"],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=False,
        cwd=str(workspace)
    )

    proc.stdin.write(request_str.encode('utf-8'))
    proc.stdin.flush()
    time.sleep(5)
    proc.stdin.close()

    stdout_bytes, stderr_bytes = proc.communicate(timeout=90)
    stdout = stdout_bytes.decode('utf-8', errors='replace')

    try:
        return json.loads(stdout.strip())
    except:
        return {"error": "Failed to parse response", "raw": stdout[:500]}

def main():
    workspace = Path.cwd()

    print("="*60)
    print("C# MCP SERVER TEST REPORT")
    print("="*60)
    print(f"Date: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"Workspace: {workspace}")
    print("="*60)
    print()

    test_cases = [
        # ESSENTIAL TOOLS
        ("GetSymbols", "Get all symbols in Program.cs", {
            "filePath": "src/CSharpMcp.Server/Program.cs",
            "detailLevel": 2  # Standard
        }),
        ("GetSymbols", "Filter by Method kind", {
            "filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs",
            "filterKinds": [7],  # Method = 7
            "detailLevel": 1  # Summary
        }),
        ("GetSymbols", "Fuzzy path matching", {
            "filePath": "Program.cs",
            "detailLevel": 0  # Compact
        }),
        ("GoToDefinition", "Navigate to Main method", {
            "filePath": "src/CSharpMcp.Server/Program.cs",
            "lineNumber": 15,
            "symbolName": "Main",
            "detailLevel": 3  # Full
        }),
        ("FindReferences", "Find references to IWorkspaceManager", {
            "filePath": "src/CSharpMcp.Server/Roslyn/IWorkspaceManager.cs",
            "lineNumber": 40,
            "symbolName": "IWorkspaceManager",
            "includeContext": False
        }),
        ("ResolveSymbol", "Resolve WorkspaceManager symbol", {
            "filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs",
            "lineNumber": 11,
            "symbolName": "WorkspaceManager",
            "detailLevel": 2  # Standard
        }),
        ("SearchSymbols", "Search for *Workspace*", {
            "query": "*Workspace*",
            "detailLevel": 1,  # Summary
            "maxResults": 10
        }),
        ("SearchSymbols", "Search for IWorkspaceManager", {
            "query": "IWorkspaceManager",
            "detailLevel": 0,  # Compact
            "maxResults": 20
        }),
        # HIGHVALUE TOOLS
        ("GetInheritanceHierarchy", "Get WorkspaceManager hierarchy", {
            "filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs",
            "lineNumber": 11,
            "symbolName": "WorkspaceManager",
            "includeDerived": True,
            "maxDerivedDepth": 2
        }),
        ("GetInheritanceHierarchy", "Get IWorkspaceManager hierarchy", {
            "filePath": "src/CSharpMcp.Server/Roslyn/IWorkspaceManager.cs",
            "lineNumber": 40,
            "symbolName": "IWorkspaceManager",
            "includeDerived": True
        }),
        ("GetCallGraph", "Get call graph for LoadAsync", {
            "filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs",
            "lineNumber": 36,
            "symbolName": "LoadAsync",
            "direction": 2,  # Out = 2
            "maxDepth": 1,
            "includeExternalCalls": False
        }),
        ("GetTypeMembers", "Get WorkspaceManager members", {
            "filePath": "src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs",
            "lineNumber": 11,
            "symbolName": "WorkspaceManager",
            "includeInherited": True
        }),
        ("GetTypeMembers", "Get Program members (filter Methods)", {
            "filePath": "src/CSharpMcp.Server/Program.cs",
            "lineNumber": 13,
            "symbolName": "Program",
            "filterKinds": [7]  # Method = 7
        }),
        # OPTIMIZATION TOOLS
        ("GetSymbolComplete", "Get complete info for Main", {
            "filePath": "src/CSharpMcp.Server/Program.cs",
            "lineNumber": 15,
            "symbolName": "Main",
            "sections": 1,  # Basic
            "detailLevel": 2,  # Standard
            "includeReferences": False,
            "includeInheritance": False,
            "includeCallGraph": False
        }),
        ("BatchGetSymbols", "Batch query multiple symbols", {
            "symbols": [
                {"filePath": "src/CSharpMcp.Server/Program.cs", "lineNumber": 13, "symbolName": "Program"},
                {"filePath": "src/CSharpMcp.Server/Program.cs", "lineNumber": 15, "symbolName": "Main"}
            ],
            "detailLevel": 1,  # Summary
            "maxConcurrency": 2
        }),
        ("GetDiagnostics", "Get diagnostics for Program.cs", {
            "filePath": "src/CSharpMcp.Server/Program.cs",
            "includeWarnings": True,
            "includeInfo": False,
            "includeHidden": False
        }),
        ("GetDiagnostics", "Get workspace diagnostics", {
            "includeWarnings": True,
            "includeInfo": False
        }),
    ]

    results = []
    passed = 0
    failed = 0

    for i, (tool, description, params) in enumerate(test_cases, 1):
        print(f"[{i}/{len(test_cases)}] Testing: {tool} - {description}")
        result = call_tool(workspace, tool, params)

        is_error = result.get("result", {}).get("isError", False)
        test_passed = not is_error

        if test_passed:
            print(f"  Status: PASSED")
            passed += 1
        else:
            print(f"  Status: FAILED")
            error_msg = result.get("result", {}).get("content", [{}])[0].get("text", "Unknown error")
            print(f"  Error: {error_msg}")
            failed += 1

        results.append({
            "test": f"{tool} - {description}",
            "tool": tool,
            "description": description,
            "input": params,
            "output": result,
            "passed": test_passed
        })
        print()

    # SUMMARY
    print("="*60)
    print("SUMMARY")
    print("="*60)
    print(f"Total Tests: {len(test_cases)}")
    print(f"Passed: {passed}")
    print(f"Failed: {failed}")
    print(f"Pass Rate: {passed/len(test_cases)*100:.1f}%")
    print()

    # Save report
    report = {
        "timestamp": datetime.now().isoformat(),
        "workspace": str(workspace),
        "summary": {
            "total": len(test_cases),
            "passed": passed,
            "failed": failed,
            "passRate": passed/len(test_cases)*100
        },
        "tests": results
    }

    with open("test_report.json", "w") as f:
        json.dump(report, f, indent=2)

    print(f"Detailed report saved to: test_report.json")

    return 0 if failed == 0 else 1

if __name__ == "__main__":
    import sys
    sys.exit(main())
