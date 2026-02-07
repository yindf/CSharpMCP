import subprocess
import json
from pathlib import Path
import time

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
print("Sending GetSymbols request...")

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

print(f"\nSTDOUT length: {len(stdout_bytes)}")
print(f"STDERR length: {len(stderr_bytes)}")

# Decode and parse
stdout = stdout_bytes.decode('utf-8', errors='replace')
print("\n=== FULL STDOUT ===")
print(stdout)

# Try to parse JSON
try:
    response = json.loads(stdout.strip())
    print("\n=== PARSED JSON ===")
    print(json.dumps(response, indent=2)[:5000])
except Exception as e:
    print(f"\nFailed to parse JSON: {e}")

    # Try to find JSON in the output
    for line in stdout.split('\n'):
        if line.strip().startswith('{'):
            try:
                parsed = json.loads(line)
                print("\n=== FOUND AND PARSED JSON ===")
                print(json.dumps(parsed, indent=2)[:5000])
                break
            except:
                pass
