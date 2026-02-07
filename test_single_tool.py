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

print("Sending request:", request_str)
print("\nStarting server...")

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

# Give it time to process
time.sleep(5)

# Try to read output before closing
import select
import sys
import os
import msvcrt

# Check if there's data to read
if msvcrt.kbhit():
    print("Key available")

# Try non-blocking read
import os
try:
    # Windows doesn't have select on pipes, so we use a different approach
    # Just wait a bit and read
    time.sleep(2)
    # Read available data
    output = proc.stdout.read(65536)
    print("\n=== OUTPUT ===")
    print(repr(output[:2000]))
except Exception as e:
    print(f"Error reading: {e}")

# Now close and get final output
proc.stdin.close()
try:
    stdout, stderr = proc.communicate(timeout=10)
except subprocess.TimeoutExpired:
    proc.kill()
    stdout, stderr = proc.communicate()

print("\n=== FINAL STDOUT ===")
print(repr(stdout[:2000]))
print("\n=== STDERR ===")
print(stderr[:2000])
print("\n=== LENGTHS ===")
print(f"stdout: {len(stdout)}, stderr: {len(stderr)}")
