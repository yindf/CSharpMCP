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
            "filePath": "src/CSharpMcp.Server/Program.cs",
            "detailLevel": "Summary"
        }
    }
}

request_str = json.dumps(request) + "\n"
print("Sending request:", request_str[:200])

proc = subprocess.Popen(
    ["dotnet", "run", "--no-build", "--project", "src/CSharpMcp.Server/CSharpMcp.Server.csproj"],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    text=False,
    cwd=str(workspace)
)

request_bytes = request_str.encode('utf-8')
proc.stdin.write(request_bytes)
proc.stdin.flush()
time.sleep(5)
proc.stdin.close()

stdout_bytes, stderr_bytes = proc.communicate(timeout=60)

print("\n=== STDERR (with debug) ===")
print(stderr_bytes.decode('utf-8', errors='replace'))

print("\n=== STDOUT ===")
stdout = stdout_bytes.decode('utf-8', errors='replace')
print(stdout[:2000])

try:
    response = json.loads(stdout.strip())
    print("\n=== PARSED JSON ===")
    print(json.dumps(response, indent=2)[:3000])
except Exception as e:
    print(f"\nFailed to parse: {e}")
