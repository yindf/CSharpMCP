import subprocess
import json
import time
from pathlib import Path

server_path = Path(__file__).parent / "publish" / "CSharpMcp.Server.exe"

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

print("Starting server and sending request...")

proc = subprocess.Popen(
    [str(server_path)],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    text=True
)

# Send request
proc.stdin.write(request_str)
proc.stdin.flush()

# Wait for response
time.sleep(10)

# Close stdin to signal end of input (this triggers server shutdown)
proc.stdin.close()

# Read all output
stdout, stderr = proc.communicate(timeout=30)

print("\n=== STDOUT (first 3000 chars) ===")
print(stdout[:3000])
print("\n=== STDERR (first 1000 chars) ===")
print(stderr[:1000])
print("\n=== FULL STDOUT LENGTH ===")
print(len(stdout))

# Try to parse JSON
try:
    response = json.loads(stdout.strip())
    print("\n=== PARSED JSON ===")
    print(json.dumps(response, indent=2)[:2000])
except:
    print("\nFailed to parse as JSON")
