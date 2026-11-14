"""Build this worktree without editing or rebuilding the accepted scene."""
from pathlib import Path
import subprocess
import os
import json
import hashlib
from datetime import datetime, timezone, timedelta
import sys

root = Path(__file__).resolve().parents[2]
log = root / "Validation/Integration/build.log"
log.parent.mkdir(parents=True, exist_ok=True)
environment = dict(os.environ)
environment.setdefault("ALLUSERSPROFILE", environment.get("PROGRAMDATA", "C:/ProgramData"))
result = subprocess.run([
    "D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe", "-batchmode", "-quit",
    "-projectPath", str(root / "Prototype"), "-executeMethod",
    "TrackingBuild.VerifyAndBuild", "-logFile", str(log)], env=environment)
if result.returncode == 0:
    names = subprocess.check_output(["git", "ls-files", "-z", "Prototype/Assets", "Prototype/Packages", "Prototype/ProjectSettings"], cwd=root).decode().split("\0")
    hashes = {}
    for name in filter(None, names):
        path = root / name
        if path.suffix in (".cs", ".shader", ".mat", ".asset", ".json", ".unity", ".txt", ".lighting"):
            hashes[name] = hashlib.sha256(path.read_bytes()).hexdigest()
    player = root / "Prototype/Builds/Showcase/Showcase.exe"
    manifest = dict(source_commit=subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
                    verified_at=datetime.now(timezone(timedelta(hours=8))).isoformat(),
                    unity="2022.3.62f3", source_hashes=hashes,
                    player_sha256=hashlib.sha256(player.read_bytes()).hexdigest())
    (log.parent / "build-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print("Unity exit", result.returncode, "log", log)
sys.exit(result.returncode)
