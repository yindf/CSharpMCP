import subprocess
import json
from pathlib import Path
import time

workspace = Path.cwd()

# Test request - try different formats
test_formats = [
    # Format 1: Direct arguments (what we're using)
    {
        "name": "Direct arguments format",
        "request": {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/call",
            "params": {
                "name": "GetSymbols",
                "arguments": {
                    "filePath": "src/CSharpMcp.Server/Program.cs",
                    "detailLevel": 1  # Summary = 1
                }
            }
        }
    },
    # Format 2: Wrapped in parameters
    {
        "name": "Wrapped in parameters",
        "request": {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/call",
            "params": {
                "name": "GetSymbols",
                "arguments": {
                    "parameters": {
                        "filePath": "src/CSharpMcp.Server/Program.cs",
                        "detailLevel": 1
                    }
                }
            }
        }
    },
    # Format 3: detailLevel as string
    {
        "name": "detailLevel as string",
        "request": {
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
    },
]

for test in test_formats:
    print(f"\n{'='*60}")
    print(f"Testing: {test['name']}")
    print(f"{'='*60}")

    request_str = json.dumps(test['request']) + "\n"

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
    time.sleep(3)
    proc.stdin.close()

    stdout_bytes, stderr_bytes = proc.communicate(timeout=60)

    stdout = stdout_bytes.decode('utf-8', errors='replace')

    try:
        response = json.loads(stdout.strip())
        if response.get("result", {}).get("isError"):
            print(f"[ERROR] {response['result']['content'][0]['text']}")
        else:
            print(f"[SUCCESS]")
            content = response.get("result", {}).get("content", [])
            if content and content[0].get("type") == "text":
                text = content[0]["text"]
                print(f"   Response length: {len(text)} chars")
                print(f"   Preview: {text[:200]}...")
    except Exception as e:
        print(f"[FAILED] {e}")
        print(f"   Raw stdout: {stdout[:200]}")
