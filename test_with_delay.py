import subprocess
import json
import time
from pathlib import Path

server_path = Path(__file__).parent / "publish" / "CSharpMcp.Server.exe"

# Test request - simpler
request = {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
        "name": "SearchSymbols",
        "arguments": {
            "query": "SimpleTestClass",
            "detailLevel": "Summary",
            "maxResults": 10
        }
    }
}

request_str = json.dumps(request) + "\n"

print("Starting server...")

proc = subprocess.Popen(
    [str(server_path)],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    text=True
)

# Give server time to start
time.sleep(2)

print("Sending request...")

# Send request
proc.stdin.write(request_str)
proc.stdin.flush()

# Wait longer for response (workspace needs to load)
time.sleep(15)

# Close stdin to signal end
proc.stdin.close()

# Read all output
stdout, stderr = proc.communicate(timeout=60)

print("\n=== STDOUT ===")
print(stdout[:5000])
print("\n=== STDERR ===")
print(stderr[:2000])
print("\n=== LENGTHS ===")
print(f"stdout: {len(stdout)}, stderr: {len(stderr)}")

# Try to parse JSON
try:
    response = json.loads(stdout.strip())
    print("\n=== PARSED JSON ===")
    print(json.dumps(response, indent=2)[:3000])
    if "result" in response:
        result = response["result"]
        if "content" in result:
            print("\n=== CONTENT ===")
            for item in result["content"]:
                print(item.get("text", "")[:1000])
except Exception as e:
    print(f"\nFailed to parse as JSON: {e}")
