"""Install the exact official Xray release; verify the published SHA-256."""
import hashlib
import io
import os
import platform
import sys
import urllib.request
import zipfile
from pathlib import Path

VERSION = "v26.10.31"
ASSETS = {
    "amd64": ("Xray-linux-64.zip", "e4bd9119f50ae7c41658b1abd0679ae06f98bd864a2907ff33f2f2cf7081b214"),
    "arm64": ("Xray-linux-arm64-v8a.zip", "1811c8f1da0c3b6dc707ebaf1c8390e987ba39859a8f0a7aadd8526157f2ec8d"),
}
arch = os.environ.get("TARGETARCH") or {"x86_64": "amd64", "aarch64": "arm64"}.get(platform.machine())
if arch not in ASSETS:
    raise SystemExit("Xray installer supports Linux amd64 and arm64.")
name, digest = ASSETS[arch]
url = f"https://github.com/XTLS/Xray-core/releases/download/{VERSION}/{name}"
with urllib.request.urlopen(url, timeout=120) as response:
    data = response.read(40 * 1024 * 1024)
if hashlib.sha256(data).hexdigest() != digest:
    raise SystemExit("Xray checksum mismatch")
target = Path(sys.argv[1] if len(sys.argv) > 1 else "/usr/local/bin/xray")
target.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(io.BytesIO(data)) as archive:
    target.write_bytes(archive.read("xray"))
target.chmod(0o755)
print(f"Installed verified Xray {VERSION} ({arch}).")
