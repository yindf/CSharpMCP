import subprocess
import json
import time

import sys
from pathlib import Path
server_path = Path(__file__).parent / "publish" / "CSharpMcp.Server.exe"
print(f"Server path: {server_path}")
print(f"Exists: {server_path.exists()}")

# Test request
request = {
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/list"
}

request_str = json.dumps(request) + "\n"

print("Sending request:", request_str)
print("\nStarting server...")

proc = subprocess.Popen(
    [server_path],
    stdin=subprocess.PIPE,
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    text=True
)

# Send request
proc.stdin.write(request_str)
proc.stdin.flush()

# Give it time to process
time.sleep(2)

# Try to terminate gracefully
proc.stdin.close()
try:
    stdout, stderr = proc.communicate(timeout=5)
except subprocess.TimeoutExpired:
    proc.kill()
    stdout, stderr = proc.communicate()

print("\n=== STDOUT ===")
print(repr(stdout))
print("\n=== STDERR ===")
print(stderr[:2000])
print("\n=== RAW OUTPUT ===")
print(f"stdout length: {len(stdout)}")
print(f"stderr length: {len(stderr)}")
