"""Restore this machine's licensed showcase assets into a Git checkout."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import tempfile


def sha(path, normalize=False):
    data = path.read_bytes()
    return hashlib.sha256(data.replace(b"\r\n", b"\n") if normalize else data).hexdigest()


def safe_path(root, name):
    relative = PurePosixPath(name)
    if (not name or "\\" in name or ":" in name or relative.is_absolute()
            or any(part in (".", "..") for part in name.split("/"))):
        raise ValueError("Unsafe manifest path: " + name)
    root = root.resolve()
    path = root.joinpath(*relative.parts)
    if not path.resolve().is_relative_to(root):
        raise ValueError("Path escapes root: " + name)
    for parent in [path, *path.parents]:
        if parent == root:
            break
        if parent.is_symlink() or (hasattr(parent, "is_junction") and parent.is_junction()):
            raise ValueError("Linked payload/target path: " + str(parent))
    return path


def verify(store, manifest):
    if manifest.get("schema") != 1 or not manifest.get("files"):
        raise ValueError("Unsupported or empty manifest")
    seen = set()
    for entry in manifest["files"]:
        name = entry["path"]
        if name.casefold() in seen:
            raise ValueError("Duplicate path: " + name)
        seen.add(name.casefold())
        path = safe_path(store / "payload", name)
        if not path.is_file() or path.stat().st_size != entry["size"] or sha(path) != entry["sha256"]:
            raise ValueError("Missing/corrupt store payload: " + name)
    return len(seen)


def restore(store, target, manifest, group, check=False):
    verify(store, manifest)
    pending = []
    existing = 0
    for entry in manifest["files"]:
        if group != "all" and entry["group"] != group:
            continue
        name = entry["path"]
        dest = safe_path(target, name)
        if "meta_sha256_lf" in entry:
            meta = safe_path(target, name + ".meta")
            if not meta.is_file() or sha(meta, normalize=True) != entry["meta_sha256_lf"]:
                raise ValueError("Missing/incompatible tracked .meta: " + name)
        if dest.exists():
            if not dest.is_file() or sha(dest) != entry["sha256"]:
                raise ValueError("Existing file differs; nothing overwritten: " + name)
            existing += 1
        elif check:
            raise ValueError("Asset not restored: " + name)
        else:
            pending.append((safe_path(store / "payload", name), dest))
    # All sources and destinations pass before any copy starts.
    for source, dest in pending:
        dest.parent.mkdir(parents=True, exist_ok=True)
        fd, temporary = tempfile.mkstemp(prefix=".asset-restore-", dir=dest.parent)
        os.close(fd)
        temporary = Path(temporary)
        try:
            shutil.copy2(source, temporary)
            if sha(temporary) != sha(source):
                raise ValueError("Copy verification failed: " + str(dest))
            # Same-volume hard link publishes a complete file and refuses a concurrent overwrite.
            os.link(temporary, dest)
        finally:
            temporary.unlink()
    return {"copied": len(pending), "existing": existing}


def git(root, *args):
    return subprocess.run(["git", "-C", str(root), *args], check=True,
                          capture_output=True, text=True).stdout.strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["verify", "restore", "check"])
    parser.add_argument("--target", type=Path, default=Path(__file__).resolve().parents[2],
                        help="Git checkout root; default: this script's checkout")
    parser.add_argument("--store", type=Path, help="Local bundle directory containing manifest.json and payload/")
    parser.add_argument("--group", choices=["all", "runtime"], default="all",
                        help="all: Blender sources and Unity assets; runtime: Unity assets only")
    args = parser.parse_args()
    target = args.target.resolve()
    if Path(git(target, "rev-parse", "--show-toplevel")).resolve() != target:
        raise ValueError("--target must be the Git checkout root")
    common = Path(git(target, "rev-parse", "--path-format=absolute", "--git-common-dir"))
    manifest = json.loads(Path(__file__).with_name("local-assets-manifest.json").read_text(encoding="utf-8"))
    store = (args.store or common.parent / ".local-assets/showcase" / manifest["bundle"]).resolve()
    saved = json.loads((store / "manifest.json").read_text(encoding="utf-8"))
    if saved != manifest:
        raise ValueError("Store manifest differs from the versioned manifest")
    count = verify(store, manifest)
    if args.action == "verify":
        print(json.dumps({"verified": count, "store": str(store)}))
        return
    for entry in manifest["files"]:
        if args.group == "all" or entry["group"] == args.group:
            # Prevent licensed payloads from becoming ordinary untracked files in another branch.
            git(target, "check-ignore", "--", entry["path"])
    result = restore(store, target, manifest, args.group, check=args.action == "check")
    print(json.dumps({**result, "target": str(target), "store": str(store)}))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        raise SystemExit("Asset recovery failed: " + str(error))
