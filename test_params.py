import subprocess
import json
from pathlib import Path
import time

workspace = Path.cwd()

# Test with minimal required params only
test_cases = [
    ("Only required param (filePath)", {
        "filePath": "src/CSharpMcp.Server/Program.cs"
    }),
    ("With detailLevel as int", {
        "filePath": "src/CSharpMcp.Server/Program.cs",
        "detailLevel": 1
    }),
    ("With detailLevel 0 (Compact)", {
        "filePath": "src/CSharpMcp.Server/Program.cs",
        "detailLevel": 0
    }),
    ("With detailLevel 2 (Standard)", {
        "filePath": "src/CSharpMcp.Server/Program.cs",
        "detailLevel": 2
    }),
    ("With all params", {
        "filePath": "src/CSharpMcp.Server/Program.cs",
        "detailLevel": 1,
        "includeBody": True,
        "bodyMaxLines": 50
    }),
]

for name, params in test_cases:
    print(f"\n{'='*60}")
    print(f"Testing: {name}")
    print(f"{'='*60}")

    request = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "tools/call",
        "params": {
            "name": "GetSymbols",
            "arguments": {
                "parameters": params
            }
        }
    }

    request_str = json.dumps(request) + "\n"

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

    stdout = stdout_bytes.decode('utf-8', errors='replace')

    try:
        response = json.loads(stdout.strip())
        result = response.get("result", {})
        if result.get("isError"):
            print(f"[ERROR] {result['content'][0]['text']}")
        else:
            content = result.get("content", [])
            if content and content[0].get("type") == "text":
                text = content[0]["text"]
                print(f"[SUCCESS] Response length: {len(text)} chars")
                # Print first 500 chars
                print(f"Preview:\n{text[:500]}...")
    except Exception as e:
        print(f"[FAILED] {e}")
        print(f"Raw stdout: {stdout[:300]}")
