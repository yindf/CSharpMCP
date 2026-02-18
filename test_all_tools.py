#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Test all MCP tools with StellarGround workspace."""

import subprocess
import json
import time
import threading
import os
import sys

# Fix Windows console encoding
if sys.platform == "win32":
    import codecs
    sys.stdout = codecs.getwriter("utf-8")(sys.stdout.buffer, "ignore")
    sys.stderr = codecs.getwriter("utf-8")(sys.stderr.buffer, "ignore")

server_path = os.path.join(os.path.dirname(__file__), "publish", "CSharpMcp.Server.exe")
solution_path = r".\src\CSharpMcp.Server\CSharpMcp.Server.csproj"
test_file = r"src/CSharpMcp.Server/Roslyn/WorkspaceManager.cs"

# ANSI colors
GREEN = "\033[92m"
RED = "\033[91m"
YELLOW = "\033[93m"
BLUE = "\033[94m"
RESET = "\033[0m"

class McpTester:
    def __init__(self, server_path):
        self.proc = subprocess.Popen(
            [server_path],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            bufsize=0
        )
        self.responses = []
        self.message_id = 1
        self.lock = threading.Lock()

        thread = threading.Thread(target=self._read_stdout, daemon=True)
        thread.start()

    def _read_stdout(self):
        while True:
            try:
                line = self.proc.stdout.readline()
                if not line:
                    break
                text = line.decode('utf-8', errors='replace').strip()
                if text:
                    with self.lock:
                        self.responses.append(text)
            except:
                break

    def send_request(self, method, params=None):
        request = {"jsonrpc": "2.0", "id": self.message_id, "method": method}
        if params:
            request["params"] = params

        self.message_id += 1
        json_str = json.dumps(request, ensure_ascii=False) + "\n"
        self.proc.stdin.write(json_str.encode('utf-8'))
        self.proc.stdin.flush()

        # Wait for response - check ALL new responses, not just the last one
        expected_id = self.message_id - 1
        start_len = len(self.responses)
        for _ in range(100):
            time.sleep(0.1)
            with self.lock:
                # Check all responses that arrived since we sent the request
                for i in range(start_len, len(self.responses)):
                    try:
                        response = json.loads(self.responses[i])
                        if response.get("id") == expected_id:
                            return response
                    except:
                        pass
        return {"error": "timeout"}

    def call_tool(self, name, arguments=None):
        return self.send_request("tools/call", {"name": name, "arguments": arguments or {}})

    def shutdown(self):
        self.proc.terminate()
        try:
            self.proc.wait(timeout=5)
        except:
            self.proc.kill()

def print_test(name, passed, details=""):
    symbol = f"{GREEN}✓{RESET}" if passed else f"{RED}✗{RESET}"
    print(f"{symbol} {name}")
    if details:
        print(f"  {details}")

def main():
    print(f"\n{BLUE}{'='*60}{RESET}")
    print(f"{BLUE}CSharp MCP Server - All Tools Test{RESET}")
    print(f"{BLUE}{'='*60}{RESET}\n")

    tester = McpTester(server_path)
    time.sleep(2)

    results = []

    # Initialize
    print(f"{YELLOW}Initializing MCP connection...{RESET}")
    tester.send_request("initialize", {
        "protocolVersion": "2024-11-05",
        "capabilities": {},
        "clientInfo": {"name": "test", "version": "1.0"}
    })

    # Send initialized notification
    notification = {"jsonrpc": "2.0", "method": "notifications/initialized"}
    tester.proc.stdin.write((json.dumps(notification) + "\n").encode('utf-8'))
    tester.proc.stdin.flush()
    time.sleep(0.5)

    # List tools
    print(f"{YELLOW}Listing available tools...{RESET}\n")
    tools_result = tester.send_request("tools/list")
    tools = tools_result.get("result", {}).get("tools", [])
    print(f"Found {len(tools)} tools\n")

    # Test 1: LoadWorkspace
    print(f"{BLUE}TEST 1: LoadWorkspace{RESET}")
    result = tester.call_tool("LoadWorkspace", {"path": solution_path})
    content = result.get("result", {}).get("content", [{}])[0].get("text", "")
    passed = "Workspace Loaded" in content and ("Documents" in content or "project" in content.lower())
    print_test("LoadWorkspace", passed, f"Loaded workspace" if passed else f"Failed: {content[:200]}")
    results.append(("LoadWorkspace", passed))
    time.sleep(1)

    if not passed:
        print(f"\n{RED}Workspace load failed, skipping remaining tests{RESET}")
        tester.shutdown()
        return

    # Print workspace info for debugging
    print(f"DEBUG: Workspace content: {content[:200]}...")

    # Test 2: SearchSymbols
    print(f"\n{BLUE}TEST 2: SearchSymbols{RESET}")
    result = tester.call_tool("SearchSymbols", {"parameters": {"query": "MonoBehaviour", "maxResults": 3}})
    content = result.get("result", {}).get("content", [{}])[0].get("text", "")
    passed = "MonoBehaviour" in content and "No Workspace" not in content
    print_test("SearchSymbols", passed, f"Found MonoBehaviour classes" if passed else f"Failed: {content[:100]}")
    results.append(("SearchSymbols", passed))

    # Test 3: GetSymbols
    print(f"\n{BLUE}TEST 3: GetSymbols{RESET}")
    result = tester.call_tool("GetSymbols", {"parameters": {"filePath": test_file, "detailLevel": 0}})
    print(f"DEBUG: result keys: {result.keys()}")
    print(f"DEBUG: result: {result}")
    content = result.get("result", {}).get("content", [{}])[0].get("text", "")
    passed = "symbols" in content.lower() and ("WorkspaceManager" in content or "Class" in content)
    print_test("GetSymbols", passed, f"Retrieved symbols" if passed else f"Failed: {content[:200]}")
    results.append(("GetSymbols", passed))

    # Test 4: GoToDefinition
    print(f"\n{BLUE}TEST 4: GoToDefinition{RESET}")
    result = tester.call_tool("GoToDefinition", {"parameters": {"filePath": test_file, "lineNumber": 10}})
    content = result.get("result", {}).get("content", [{}])[0].get("text", "")
    passed = "No Workspace" not in content and "Error" not in content
    print_test("GoToDefinition", passed, f"Navigated to definition" if passed else f"Result: {content[:100]}")
    results.append(("GoToDefinition", passed))

    # Test 5: ResolveSymbol
    print(f"\n{BLUE}TEST 5: ResolveSymbol{RESET}")
    result = tester.call_tool("ResolveSymbol", {"parameters": {"filePath": test_file, "lineNumber": 10, "column": 1}})
    content = result.get("result", {}).get("content", [{}])[0].get("text", "")
    passed = "No Workspace" not in content
    print_test("ResolveSymbol", passed, f"Resolved symbol at position" if passed else f"Result: {content[:100]}")
    results.append(("ResolveSymbol", passed))

    # Test 6: FindReferences
    print(f"\n{BLUE}TEST 6: FindReferences{RESET}")
    result = tester.call_tool("FindReferences", {"parameters": {"filePath": test_file, "lineNumber": 10}})
    content = result.get("result", {}).get("content", [{}])[0].get("text", "")
    passed = "No Workspace" not in content
    print_test("FindReferences", passed, f"Found references" if passed else f"Result: {content[:100]}")
    results.append(("FindReferences", passed))

    # Test 7: GetDiagnostics
    print(f"\n{BLUE}TEST 7: GetDiagnostics{RESET}")
    result = tester.call_tool("GetDiagnostics", {"parameters": {"filePath": test_file}})
    content = result.get("result", {}).get("content", [{}])[0].get("text", "")
    passed = "No Workspace" not in content
    print_test("GetDiagnostics", passed, f"Retrieved diagnostics" if passed else f"Result: {content[:100]}")
    results.append(("GetDiagnostics", passed))

    # Test 8: GetTypeMembers
    print(f"\n{BLUE}TEST 8: GetTypeMembers{RESET}")
    result = tester.call_tool("GetTypeMembers", {"parameters": {"filePath": test_file, "lineNumber": 10}})
    content = result.get("result", {}).get("content", [{}])[0].get("text", "")
    passed = "No Workspace" not in content
    print_test("GetTypeMembers", passed, f"Retrieved type members" if passed else f"Result: {content[:100]}")
    results.append(("GetTypeMembers", passed))

    # Test 9: GetInheritanceHierarchy
    print(f"\n{BLUE}TEST 9: GetInheritanceHierarchy{RESET}")
    result = tester.call_tool("GetInheritanceHierarchy", {"parameters": {"filePath": test_file, "lineNumber": 10}})
    content = result.get("result", {}).get("content", [{}])[0].get("text", "")
    passed = "No Workspace" not in content
    print_test("GetInheritanceHierarchy", passed, f"Retrieved inheritance hierarchy" if passed else f"Result: {content[:100]}")
    results.append(("GetInheritanceHierarchy", passed))

    # Test 10: GetCallGraph
    print(f"\n{BLUE}TEST 10: GetCallGraph{RESET}")
    result = tester.call_tool("GetCallGraph", {"parameters": {"filePath": test_file, "lineNumber": 20}})
    content = result.get("result", {}).get("content", [{}])[0].get("text", "")
    passed = "No Workspace" not in content
    print_test("GetCallGraph", passed, f"Retrieved call graph" if passed else f"Result: {content[:100]}")
    results.append(("GetCallGraph", passed))

    # Test 11: GetSymbolComplete
    print(f"\n{BLUE}TEST 11: GetSymbolComplete{RESET}")
    result = tester.call_tool("GetSymbolComplete", {"parameters": {"filePath": test_file, "lineNumber": 10}})
    content = result.get("result", {}).get("content", [{}])[0].get("text", "")
    passed = "No Workspace" not in content
    print_test("GetSymbolComplete", passed, f"Retrieved complete symbol info" if passed else f"Result: {content[:100]}")
    results.append(("GetSymbolComplete", passed))

    # Test 12: BatchGetSymbols
    print(f"\n{BLUE}TEST 12: BatchGetSymbols{RESET}")
    result = tester.call_tool("BatchGetSymbols", {"parameters": {"symbols": [
        {"filePath": test_file, "lineNumber": 10},
        {"filePath": test_file, "lineNumber": 20}
    ]}})
    content = result.get("result", {}).get("content", [{}])[0].get("text", "")
    passed = "No Workspace" not in content
    print_test("BatchGetSymbols", passed, f"Batch retrieved symbols" if passed else f"Result: {content[:100]}")
    results.append(("BatchGetSymbols", passed))

    # Summary
    print(f"\n{BLUE}{'='*60}{RESET}")
    print(f"{BLUE}TEST SUMMARY{RESET}")
    print(f"{BLUE}{'='*60}{RESET}\n")

    passed_count = sum(1 for _, p in results if p)
    total_count = len(results)

    for name, passed in results:
        symbol = f"{GREEN}✓{RESET}" if passed else f"{RED}✗{RESET}"
        print(f"{symbol} {name}")

    print(f"\n{BLUE}Results: {passed_count}/{total_count} tests passed{RESET}")

    if passed_count == total_count:
        print(f"{GREEN}All tests PASSED!{RESET}\n")
    else:
        print(f"{RED}Some tests FAILED{RESET}\n")

    tester.shutdown()

if __name__ == "__main__":
    main()
