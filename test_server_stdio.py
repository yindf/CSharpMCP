import subprocess
import json
from pathlib import Path
import time

workspace = Path.cwd()

# Test: tools/list first
request1 = {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/list"
}

request_str = json.dumps(request1) + "\n"
print("Sending tools/list request...")

proc = subprocess.Popen(
    ["dotnet", "run", "--no-build", "--project", "src/CSharpMcp.Server/CSharpMcp.Server.csproj"],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    text=False,
    cwd=str(workspace)
)

request_bytes = request_str.encode('utf-8')

# Send the request
proc.stdin.write(request_bytes)
proc.stdin.flush()

# Wait a bit for processing
time.sleep(2)

# Send a second request - the actual tool call
request2 = {
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/call",
    "params": {
        "name": "GetSymbols",
        "arguments": {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "detailLevel": "Summary"
        }
    }
}

request2_str = json.dumps(request2) + "\n"
proc.stdin.write(request2_str.encode('utf-8'))
proc.stdin.flush()

# Wait for response
time.sleep(5)

# Close stdin
proc.stdin.close()

# Get output
stdout_bytes, stderr_bytes = proc.communicate(timeout=10)

print("\n=== STDOUT ===")
print(stdout_bytes.decode('utf-8', errors='replace')[:2000])

print("\n=== STDERR ===")
print(stderr_bytes.decode('utf-8', errors='replace')[:1000])
