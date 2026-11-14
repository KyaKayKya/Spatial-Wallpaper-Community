"""Review a frozen private-source snapshot against a fresh public dev branch.

Default is dry-run. --apply creates only a local task branch and commit; this
tool never pushes, opens a PR, merges, executes frozen source, or copies history.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile

if __package__:
    from . import prepare_public_source as exporter
else:
    import prepare_public_source as exporter

GENERATED = {"SOURCE_MANIFEST.json", "SHA256SUMS", "OMITTED_INPUTS.json",
             "SOURCE_LICENSE_STATUS.md", "THIRD_PARTY_NOTICES.md"}


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def protected(name: str) -> bool:
    return (name == "wallpaper.json" or name in {"market/featured.json", "market/revocations.json"}
            or name.startswith(("market/registry/", "market/submissions/", "market/details/", "market/previews/")))


def allowed(name: str) -> None:
    exporter.safe_path(name)
    if protected(name) or (name not in GENERATED | {"README.md", "README.en.md"}
                           and exporter.category(name) is not None):
        raise ValueError("snapshot may not own this public path: " + name)


def parse_sums(raw: bytes) -> dict[str, str]:
    result = {}
    folded = set()
    for line in raw.decode("utf-8").splitlines():
        match = re.fullmatch(r"([0-9a-f]{64})  (.+)", line)
        if not match:
            raise ValueError("invalid snapshot SHA256SUMS")
        value, name = match.groups()
        exporter.safe_path(name)
        if name.casefold() in folded:
            raise ValueError("duplicate snapshot checksum path")
        folded.add(name.casefold())
        result[name] = value
    return result


def verify_snapshot(root: Path, commit: str) -> tuple[dict, dict[str, bytes]]:
    sums_raw = (root / "SHA256SUMS").read_bytes()
    sums = parse_sums(sums_raw)
    paths = {p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file()}
    if paths != set(sums) | {"SHA256SUMS"}:
        raise ValueError("snapshot checksum inventory differs from files")
    data = {name: root.joinpath(*name.split("/")).read_bytes() for name in sums}
    for name, raw in data.items():
        allowed(name)
        if digest(raw) != sums[name]:
            raise ValueError("snapshot checksum mismatch: " + name)
    manifest = json.loads(data["SOURCE_MANIFEST.json"])
    if manifest.get("format") != 1 or manifest.get("source_commit") != commit or manifest.get("history_included") is not False:
        raise ValueError("snapshot identity mismatch")
    listed = set()
    for entry in manifest["files"]:
        name = entry["path"]
        if name in listed or name not in data or entry["sha256"] != digest(data[name]) or entry["bytes"] != len(data[name]):
            raise ValueError("snapshot source manifest mismatch")
        listed.add(name)
    if listed | (GENERATED - {"SHA256SUMS"}) != set(data):
        raise ValueError("snapshot manifest ownership mismatch")
    data["SHA256SUMS"] = sums_raw
    return manifest, data


def git(repo: Path, *args: str, input_data: bytes | None = None) -> bytes:
    return subprocess.run(["git", "-C", str(repo), *args], input=input_data,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True).stdout


def read_tree(repo: Path, ref: str) -> dict[str, tuple[str, str]]:
    result = {}
    for record in git(repo, "ls-tree", "-rz", ref).split(b"\0"):
        if record:
            header, name = record.split(b"\t", 1)
            mode, kind, oid = header.decode("ascii").split()
            result[name.decode("utf-8")] = (mode, oid)
    return result


def blob(repo: Path, entries: dict, name: str) -> bytes:
    mode, oid = entries[name]
    if mode not in {"100644", "100755"}:
        raise ValueError("public path is a link or submodule: " + name)
    return git(repo, "cat-file", "blob", oid)


def batch_blobs(repo: Path, entries: dict, names) -> dict[str, bytes]:
    """Read only requested paths in one Git process, preserving raw object bytes."""
    requested = sorted(set(names))
    if not requested:
        return {}
    for name in requested:
        mode, oid = entries[name]
        if mode not in {"100644", "100755"}:
            raise ValueError("public path is a link or submodule: " + name)
        if not re.fullmatch(r"(?:[0-9a-f]{40}|[0-9a-f]{64})", oid):
            raise ValueError("invalid public Git object identity")
    payload = ("\n".join(entries[name][1] for name in requested) + "\n").encode("ascii")
    stream = io.BytesIO(git(repo, "cat-file", "--batch", input_data=payload))
    result = {}
    for name in requested:
        oid = entries[name][1]
        header = stream.readline().split()
        if len(header) != 3 or header[:2] != [oid.encode("ascii"), b"blob"] or not re.fullmatch(rb"(?:0|[1-9][0-9]*)", header[2]):
            raise ValueError("Git batch blob identity/type/size mismatch")
        size = int(header[2])
        raw = stream.read(size)
        if len(raw) != size or stream.read(1) != b"\n":
            raise ValueError("truncated Git batch blob")
        object_hash = hashlib.new("sha1" if len(oid) == 40 else "sha256")
        object_hash.update(b"blob " + str(size).encode("ascii") + b"\0" + raw)
        if object_hash.hexdigest() != oid:
            raise ValueError("Git batch blob content does not match object identity")
        result[name] = raw
    if stream.read(1):
        raise ValueError("unexpected trailing Git batch data")
    return result


def refresh_base(repo: Path, remote: str, base: str) -> str:
    if not re.fullmatch(r"[A-Za-z0-9_-]+", remote) or not base.startswith("dev/"):
        raise ValueError("public base must be an explicit dev/* branch and simple remote name")
    git(repo, "check-ref-format", "refs/heads/" + base)
    url = git(repo, "remote", "get-url", remote).decode().strip().rstrip("/").removesuffix(".git")
    if url not in {exporter.PUBLIC_REPOSITORY, "git@github.com:KyaKayKya/Spatial-Wallpaper-Community"}:
        raise ValueError("public remote is not the configured community repository")
    ref = "refs/remotes/" + remote + "/" + base
    git(repo, "fetch", "--no-tags", remote, "refs/heads/" + base + ":" + ref)
    return git(repo, "rev-parse", "--verify", ref + "^{commit}").decode().strip()


def prior_owned(repo: Path, entries: dict) -> dict[str, str]:
    if "SOURCE_MANIFEST.json" not in entries:
        return {}
    if "SHA256SUMS" not in entries:
        raise ValueError("previous public snapshot lacks SHA256SUMS")
    manifest_raw = blob(repo, entries, "SOURCE_MANIFEST.json")
    manifest = json.loads(manifest_raw)
    sums = parse_sums(blob(repo, entries, "SHA256SUMS"))
    if manifest.get("format") != 1 or manifest.get("history_included") is not False or sums.get("SOURCE_MANIFEST.json") != digest(manifest_raw):
        raise ValueError("previous public manifest identity/checksum mismatch")
    owned = {}
    for entry in manifest["files"]:
        name = entry["path"]
        # Old exporter included operational files; relinquish them without deletion.
        if protected(name):
            continue
        allowed(name)
        if name in owned or sums.get(name) != entry["sha256"]:
            raise ValueError("previous public manifest has duplicate or inconsistent source")
        owned[name] = entry["sha256"]
    for name in GENERATED - {"SHA256SUMS"}:
        if name not in sums:
            raise ValueError("previous generated snapshot record missing")
        owned[name] = sums[name]
    # Old exporter generated the README rather than listing its source template.
    for name in {"README.md", "README.en.md"}:
        if name in sums:
            owned[name] = sums[name]
    owned["SHA256SUMS"] = digest(blob(repo, entries, "SHA256SUMS"))
    return owned


def guard_path(repo: Path, name: str) -> None:
    target = repo.joinpath(*name.split("/"))
    for path in [target, *target.parents]:
        if path == repo.parent:
            break
        try:
            stat = path.lstat()
        except FileNotFoundError:
            continue
        if path.is_symlink() or getattr(stat, "st_file_attributes", 0) & 0x400:
            raise ValueError("public output path is a link/reparse point")
        if path.exists() and path != target and not path.is_dir():
            raise ValueError("public output parent is a file")
    if target.is_dir():
        raise ValueError("public output file collides with a directory")


def synchronize(source_repo: Path, commit: str, public_repo: Path, base: str,
                branch: str, apply: bool = False, remote: str = "origin") -> dict:
    source_repo, public_repo = source_repo.resolve(), public_repo.resolve()
    if Path(git(public_repo, "rev-parse", "--show-toplevel").decode().strip()).resolve() != public_repo:
        raise ValueError("--public-repo must name the exact clone root")
    if source_repo == public_repo:
        raise ValueError("source and public repositories must differ")
    if not branch.startswith("task/"):
        raise ValueError("sync branch must be a new task/* branch")
    git(public_repo, "check-ref-format", "refs/heads/" + branch)
    if git(public_repo, "status", "--porcelain=v1", "--untracked-files=all"):
        raise ValueError("public checkout has uncommitted files; preserve them before sync")
    base_commit = refresh_base(public_repo, remote, base)
    entries = read_tree(public_repo, base_commit)
    owned = prior_owned(public_repo, entries)
    temp_parent = source_repo / "Validation" / "PublicSync"
    guard_path(source_repo, "Validation/PublicSync/.temporary-probe")
    temp_parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="source-", dir=temp_parent) as temporary:
        snapshot = Path(temporary) / "snapshot"
        receipt = exporter.prepare(source_repo, commit, snapshot)
        manifest, incoming = verify_snapshot(snapshot, commit)
        modes = {item["path"]: item["git_mode"] for item in manifest["files"]}
        if receipt["sha256sums"] != digest(incoming["SHA256SUMS"]):
            raise ValueError("export receipt checksum mismatch")
        changes, removes = {}, []
        needed = (set(incoming) | set(owned)) & set(entries)
        current_blobs = batch_blobs(public_repo, entries, needed)
        case_paths = {name.casefold(): name for name in entries}
        for name, raw in incoming.items():
            guard_path(public_repo, name)
            if name.casefold() in case_paths and case_paths[name.casefold()] != name:
                raise ValueError("public case-insensitive path collision: " + name)
            if name in entries:
                current = current_blobs[name]
                if current == raw and entries[name][0] == modes.get(name, "100644"):
                    continue
                if name not in owned or digest(current) != owned[name]:
                    raise ValueError("refusing to overwrite unowned or publicly modified path: " + name)
            elif public_repo.joinpath(*name.split("/")).exists():
                raise ValueError("refusing to overwrite ignored/untracked path: " + name)
            changes[name] = raw
        for name in sorted(set(owned) - set(incoming)):
            guard_path(public_repo, name)
            if name in entries:
                if digest(current_blobs[name]) != owned[name]:
                    raise ValueError("refusing to remove publicly modified owned path: " + name)
                removes.append(name)
        report = {"source_commit": commit, "base_branch": base, "base_commit": base_commit,
                  "branch": branch, "snapshot_sha256sums": receipt["sha256sums"],
                  "apply": apply, "changed": sorted(changes), "removed": removes,
                  "publication": "not-pushed", "commit": None}
        if not apply or not (changes or removes):
            return report
        # Fetch/plan never changes the caller's current branch or index. Only --apply does.
        git(public_repo, "switch", "--no-overwrite-ignore", "--no-track", "-c", branch, base_commit)
        for name in removes:
            public_repo.joinpath(*name.split("/")).unlink()
            git(public_repo, "update-index", "--force-remove", "--", name)
        for name, raw in changes.items():
            path = public_repo.joinpath(*name.split("/"))
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
            path.chmod(0o755 if modes.get(name) == "100755" else 0o644)
            # No git add: attributes, CRLF conversion and filters may change reviewed bytes.
            oid = git(public_repo, "hash-object", "-w", "--stdin", input_data=raw).decode().strip()
            git(public_repo, "update-index", "--add", "--cacheinfo", modes.get(name, "100644"), oid, name)
        git(public_repo, "-c", "core.hooksPath=", "commit", "-m", "Sync public source from " + commit)
        result_commit = git(public_repo, "rev-parse", "HEAD").decode().strip()
        result_tree = read_tree(public_repo, result_commit)
        committed_blobs = batch_blobs(public_repo, result_tree, incoming)
        for name, raw in incoming.items():
            if committed_blobs[name] != raw:
                raise ValueError("committed bytes differ from reviewed snapshot: " + name)
        report["commit"] = result_commit
        report["next_commands_argv"] = [["git", "-C", str(public_repo), "push", "-u", remote, branch],
                                        ["gh", "pr", "create", "--repo", "KyaKayKya/Spatial-Wallpaper-Community", "--base", base, "--head", branch]]
        return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--commit", required=True, help="Full frozen 40-character committed SHA")
    parser.add_argument("--public-repo", type=Path, required=True, help="Clean local community repository clone")
    parser.add_argument("--base", required=True, help="Actual public dev/* branch, refreshed before planning")
    parser.add_argument("--branch", required=True, help="New local task/* branch for review")
    parser.add_argument("--remote", default="origin")
    parser.add_argument("--apply", action="store_true", help="Create local reviewable branch/commit; never push")
    args = parser.parse_args()
    try:
        result = synchronize(args.repo, args.commit, args.public_repo, args.base,
                             args.branch, args.apply, args.remote)
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print("Public source sync failed: " + str(error), file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
