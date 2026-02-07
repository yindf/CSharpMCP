import subprocess
import json
from pathlib import Path
import time

workspace = Path.cwd()

request = {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
        "name": "GetSymbols",
        "arguments": {
            "parameters": {
                "filePath": "src/CSharpMcp.Server/Program.cs"
            }
        }
    }
}

request_str = json.dumps(request) + "\n"
print("Request:", request_str)

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
time.sleep(10)
proc.stdin.close()

stdout_bytes, stderr_bytes = proc.communicate(timeout=90)

print("\n=== STDERR ===")
print(stderr_bytes.decode('utf-8', errors='replace'))

print("\n=== STDOUT ===")
stdout = stdout_bytes.decode('utf-8', errors='replace')
print(stdout[:2000])
