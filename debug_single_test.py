import subprocess
import json
from pathlib import Path

workspace = Path.cwd()

# Test request
request = {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
        "name": "GetSymbols",
        "arguments": {
            "filePath": "tests/CSharpMcp.Tests/TestAssets/SimpleTestClass.cs",
            "detailLevel": "Summary"
        }
    }
}

request_str = json.dumps(request) + "\n"
print("Request:", request_str[:200])

proc = subprocess.Popen(
    ["dotnet", "run", "--no-build", "--project", "src/CSharpMcp.Server/CSharpMcp.Server.csproj"],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    text=False,
    cwd=str(workspace)
)

request_bytes = request_str.encode('utf-8')
stdout_bytes, stderr_bytes = proc.communicate(input=request_bytes, timeout=60)

print("\n=== STDOUT (bytes) ===")
print(f"Length: {len(stdout_bytes)}")
print(f"First 500 bytes: {stdout_bytes[:500]}")

print("\n=== STDERR (bytes) ===")
print(f"Length: {len(stderr_bytes)}")
print(f"First 500 bytes: {stderr_bytes[:500]}")

# Try to find JSON response
stdout = stdout_bytes.decode('utf-8', errors='replace')
print("\n=== STDOUT (decoded) ===")
print(stdout[:1000])

# Look for JSON lines
for line in stdout.split('\n'):
    if line.strip().startswith('{'):
        print("\n=== FOUND JSON LINE ===")
        print(line[:500])
        try:
            parsed = json.loads(line)
            print("\n=== PARSED JSON ===")
            print(json.dumps(parsed, indent=2)[:1000])
        except:
            print("Failed to parse")
