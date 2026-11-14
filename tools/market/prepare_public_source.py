"""Export a reviewable source-only snapshot from one full Git commit SHA.

Reads Git objects, never working files or LFS storage. Does not publish, create
Git history, select a project license, fetch dependencies, or execute source.
"""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys

PROFILE = "spatial-public-source/1"
PUBLIC_REPOSITORY = "https://github.com/KyaKayKya/Spatial-Wallpaper-Community"
MAX_FILE_BYTES = 2 * 1024 * 1024
MAX_TOTAL_BYTES = 64 * 1024 * 1024
TEXT_SUFFIXES = {
    ".cs", ".c", ".cc", ".cpp", ".h", ".hpp", ".m", ".mm", ".py",
    ".ps1", ".sh", ".cmd", ".bat", ".js", ".ts", ".json", ".yaml",
    ".yml", ".toml", ".xml", ".config", ".asmdef", ".asmref", ".shader",
    ".hlsl", ".cginc", ".compute", ".txt", ".md", ".rst", ".uxml",
    ".uss", ".csproj", ".props", ".targets", ".sln", ".cmake", ".lock",
}
SOURCE_ROOTS = ("Packages/com.spatialwallpaper.sdk/", "Supervisor/", "tools/",
                "tests/", "tracker/")
PROJECT_MODULES = {
    "Blueprint", "Calibration", "ContentRuntime", "ContentValidation",
    "Desktop", "Display", "Library", "Limpool", "MiniShotGame",
    "MiniShotGameSdk", "NativeArtworks", "Parking", "Pool", "RainyCafe",
    "Showcase", "Tracking", "Train", "WallpaperSdkScenes",
}
LICENSED_SOURCE_ROOTS = (
    "Packages/com.spatialwallpaper.sdk/ThirdParty/Cilbox/",
    "Prototype/Assets/Blueprint/ThirdParty/fNbt/",
)
PUBLIC_DOCS = {
    "docs/guides/公开源码快照.md",
    "docs/guides/场景固定窗口规范.md",
    "docs/guides/壁纸市场创作者入门.md",
    "docs/guides/壁纸市场维护.md",
    "docs/guides/外来原生作品导出.md",
    "docs/guides/SDK0.4发行与体验.md",
}
PRIVATE_PARTS = {
    ".git", ".worktrees", ".codex", ".agents", ".aws", ".ssh",
    "validation", "builds", "temp", "obj", "bin", "logs",
    "node_modules", "__pycache__", ".venv", "venv", ".cache",
}
SECRET_NAME = re.compile(
    r"(?:^\.env(?:\.|$)|^\.netrc$|^\.git-credentials$|"
    r"(?:^|[._-])(?:credentials?|secrets?|private[-_]?key)(?:[._-]|$)|"
    r"\.(?:pfx|p12|pem|key|keystore)$)", re.I)
SECRET_CONTENT = (
    re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----"),
    re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})\b"),
    re.compile(r"\bAKIA[0-9A-Z]{16}\b"),
    re.compile(r"https?://[^\s/:]+:[^\s/@]+@[^\s/]+", re.I),
    re.compile(r"[\"']?(?:api[_-]?key|access[_-]?token|client[_-]?secret|password)"
               r"[\"']?\s*[:=]\s*[\"'][A-Za-z0-9_./+=-]{24,}[\"']", re.I),
)


def git(repo: Path, *args: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(repo), *args], stderr=subprocess.PIPE)


def safe_path(name: str) -> None:
    parts = PurePosixPath(name).parts
    if (not parts or name.startswith("/") or "\\" in name or ":" in name
            or any(ord(c) < 32 or c in '<>"|?*' for c in name)
            or any(p in {"", ".", ".."} or p.lower() == ".git"
                   or p.endswith((".", " "))
                   or p.split(".")[0].upper() in {
                       "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
                       *("COM" + str(i) for i in range(1, 10)),
                       *("LPT" + str(i) for i in range(1, 10))} for p in parts)):
        raise ValueError("non-canonical source path")


def is_license(name: str) -> bool:
    leaf = PurePosixPath(name).name.lower()
    return (leaf.startswith(("license", "copying", "notice", "copyright"))
            or "-license" in leaf or "_license" in leaf
            or leaf == "sources_and_licenses.md") and not leaf.endswith(".meta")


def category(name: str) -> str | None:
    """Return an exclusion reason, or None for an eligible text source."""
    path = PurePosixPath(name)
    parts = path.parts
    # Library is also a real product/tools module; only the Unity cache path is private.
    if (name.startswith("Prototype/Library/")
            or any(p.lower() in PRIVATE_PARTS for p in parts)):
        return "private-or-generated-directory"
    if any(SECRET_NAME.search(p) for p in parts):
        return "credential-filename"
    if name.startswith("docs/") and name not in PUBLIC_DOCS:
        return "private-documentation"
    if name.startswith("art/"):
        return "original-artwork"
    if name.startswith(("Packages/", "Prototype/")) and is_license(name):
        return None
    if "thirdparty" in (p.lower() for p in parts):
        if not name.startswith(LICENSED_SOURCE_ROOTS):
            return "third-party-input-not-authorized"
    if name.startswith("Prototype/Assets/"):
        if len(parts) < 4 or parts[2] not in PROJECT_MODULES:
            return "third-party-input-not-authorized"
        if any(p.lower() in {"source", "localweather", "fonts", "icons",
                             "generated", "baked", "bakedstudy"}
               for p in parts[3:-1]):
            return "original-or-third-party-asset-input"
        if (path.suffix == ".json" and not name.startswith(LICENSED_SOURCE_ROOTS)
                and name != "Prototype/Assets/Library/Resources/Library/MarketTrust.json"):
            return "artwork-resource-metadata"
        allowed = True
    else:
        allowed = (name.startswith(SOURCE_ROOTS)
                   or name.startswith(("Prototype/Packages/", "Prototype/ProjectSettings/",
                                       "market/schemas/", "market/templates/"))
                   or (path.parent.as_posix() == "market" and path.suffix == ".json")
                   or (path.parent.as_posix() == ".github/workflows"
                       and path.name.startswith("market") and path.suffix in {".yml", ".yaml"})
                   or name in PUBLIC_DOCS or name in {"market/requirements.txt", ".github/PULL_REQUEST_TEMPLATE/market.md", "LICENSE", "PROJECT_LICENSE_SCOPE.md"})
    if not allowed:
        return "outside-source-allowlist"
    if path.suffix == ".meta":
        return None  # Retained later only with an included file or directory.
    if name.startswith("Prototype/ProjectSettings/") and path.suffix == ".asset":
        return None
    if path.suffix.lower() not in TEXT_SUFFIXES and path.name not in {
            "LICENSE", "COPYING", "NOTICE", "Makefile", "CMakeLists.txt",
            ".gitignore", ".python-version"}:
        return "asset-or-binary-input"
    return None


def tree(repo: Path, commit: str) -> list[dict]:
    result = []
    names = set()
    for record in git(repo, "ls-tree", "-r", "-z", "-l", commit).split(b"\0"):
        if not record:
            continue
        header, raw_name = record.split(b"\t", 1)
        mode, kind, oid, size = header.decode("ascii").split()
        name = raw_name.decode("utf-8", "strict")
        safe_path(name)
        if name.casefold() in names:
            raise ValueError("case-insensitive source path collision")
        names.add(name.casefold())
        result.append({"path": name, "mode": mode, "kind": kind,
                       "blob": oid, "size": int(size) if size != "-" else 0})
    return result


class BlobReader:
    def __init__(self, repo: Path):
        self.process = subprocess.Popen(["git", "-C", str(repo), "cat-file", "--batch"],
                                        stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=subprocess.PIPE)

    def read(self, entry: dict) -> bytes:
        self.process.stdin.write((entry["blob"] + "\n").encode("ascii"))
        self.process.stdin.flush()
        header = self.process.stdout.readline().decode("ascii").strip().split()
        if header != [entry["blob"], "blob", str(entry["size"])]:
            raise ValueError("Git blob identity or size mismatch")
        data = self.process.stdout.read(entry["size"])
        if len(data) != entry["size"] or self.process.stdout.read(1) != b"\n":
            raise ValueError("truncated Git blob")
        return data

    def close(self) -> None:
        self.process.stdin.close()
        self.process.stdout.close()
        self.process.stderr.read()
        self.process.stderr.close()
        code = self.process.wait()
        if code:
            raise RuntimeError("git cat-file failed: " + str(code))


def prepare(repo: Path, commit: str, output: Path) -> dict:
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("--commit must be a full 40-character frozen SHA, not a branch or HEAD")
    if git(repo, "rev-parse", "--verify", commit + "^{commit}").decode().strip() != commit:
        raise ValueError("source commit does not exist")
    if output.exists():
        raise FileExistsError("output must be a new directory")
    for ancestor in [output.parent, *output.parent.parents]:
        if ancestor.exists() and (ancestor.is_symlink()
                or getattr(ancestor.lstat(), "st_file_attributes", 0) & 0x400):
            raise ValueError("output ancestor is a link or reparse point")
    entries = tree(repo, commit)
    excluded = Counter()
    omissions = []
    selected = {}
    total = 0
    reader = BlobReader(repo)
    try:
        for entry in entries:
            name = entry["path"]
            reason = category(name)
            if entry["mode"] not in {"100644", "100755"} or entry["kind"] != "blob":
                reason = "symlink-or-submodule"
            if reason is None and entry["size"] > MAX_FILE_BYTES:
                reason = "text-file-size-budget"
            if reason is None:
                data = reader.read(entry)
                if data.startswith(b"version https://git-lfs.github.com/spec/v1"):
                    reason = "lfs-pointer-not-a-source-file"
                elif b"\0" in data:
                    reason = "binary-content"
                else:
                    try:
                        text = data.decode("utf-8-sig", "strict")
                    except UnicodeDecodeError:
                        reason = "non-utf8-input"
                    else:
                        if any(pattern.search(text) for pattern in SECRET_CONTENT):
                            reason = "credential-content-review-required"
                if reason is None:
                    total += len(data)
                    if total > MAX_TOTAL_BYTES:
                        raise ValueError("source snapshot exceeds total text budget")
                    selected[name] = (entry, data)
            if reason:
                excluded[reason] += 1
                # Publish only missing asset/source inputs, never private docs,
                # credential paths, validation contents or development history.
                if (name.startswith(("Prototype/Assets/", "Packages/"))
                        and reason not in {"credential-filename", "credential-content-review-required"}
                        and not name.endswith(".meta")):
                    omissions.append({"path": name, "reason": reason})
    finally:
        reader.close()
    for name in list(selected):
        if name.endswith(".meta"):
            target = name[:-5]
            if target not in selected and not any(p.startswith(target + "/") for p in selected):
                del selected[name]
                excluded["orphan-unity-meta"] += 1
    project_license = "MIT" if "LICENSE" in selected and selected["LICENSE"][1].startswith(b"MIT License") and "PROJECT_LICENSE_SCOPE.md" in selected else None
    license_notice = "Project-authored source is licensed under MIT; see LICENSE and PROJECT_LICENSE_SCOPE.md. Third-party inputs and separately marked CC0 examples keep their own terms." if project_license else "No project-wide license is selected; source availability alone grants no additional reuse rights."
    output.mkdir(parents=True, exist_ok=False)
    manifest = {"format": 1, "profile": PROFILE, "source_commit": commit,
                "public_repository": PUBLIC_REPOSITORY,
                "history_included": False, "project_license_selected": bool(project_license), "project_license": project_license,
                "excluded_counts": dict(sorted(excluded.items())), "files": []}
    licenses = []
    for name, (entry, data) in sorted(selected.items()):
        target = output.joinpath(*PurePosixPath(name).parts)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        if entry["mode"] == "100755":
            target.chmod(0o755)
        manifest["files"].append({"path": name, "git_blob": entry["blob"],
                                  "sha256": hashlib.sha256(data).hexdigest(),
                                  "bytes": len(data), "git_mode": entry["mode"]})
        if is_license(name):
            licenses.append(name)
    (output / "SOURCE_MANIFEST.json").write_text(json.dumps(manifest, ensure_ascii=False,
                                                         indent=2) + "\n", encoding="utf-8")
    (output / "OMITTED_INPUTS.json").write_text(json.dumps({"format": 1, "source_commit": commit,
        "notice": "Omitted assets must be obtained separately with their own distribution rights. No LFS fetching is performed.",
        "inputs": omissions}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (output / "SOURCE_LICENSE_STATUS.md").write_text("# Source license status\n\n" + license_notice + "\n", encoding="utf-8")
    (output / "THIRD_PARTY_NOTICES.md").write_text("# Retained license notices\n\n"
        "These notices are preserved from the frozen tree; inclusion is not a claim that omitted assets are distributed here.\n\n"
        + "".join("- [" + path + "](" + path.replace(" ", "%20") + ")\n" for path in licenses), encoding="utf-8")
    (output / "README.md").write_text(
        "# Spatial Wallpaper source snapshot\n\nPublic repository: " + PUBLIC_REPOSITORY + "\n\n"
        "Frozen source commit: `" + commit + "`. This directory contains no private Git history.\n\n"
        "Runtime distributions are delivered separately when their redistribution rights are verified; current public Releases include the SDK validator and original CC0 example. "
        "This source-only snapshot is for code review and development; it is not an installer or a complete Unity artwork project.\n\n"
        "SDK and authoring sources are under `Packages/com.spatialwallpaper.sdk`; shared project sources, "
        "tools and tests keep their original paths. Start with the SDK `Documentation~` directory. "
        "Creators need Unity 2022.3 and the required development toolchain. Runtime users do not need Unity.\n\n"
        "Original scenes, models, materials, textures, audio, licensed external packages and binary caches "
        "are intentionally omitted. See `OMITTED_INPUTS.json`; import each required dependency only after "
        "obtaining its license. Missing project assets and references must be restored before building the full artwork library. "
        "The exporter does not download LFS objects or install dependencies.\n\n"
        "Verify `SHA256SUMS` and `SOURCE_MANIFEST.json` before reviewing or publishing. "
        "Some source documentation links refer to private development evidence intentionally absent from this snapshot.\n\n"
        "See `SOURCE_LICENSE_STATUS.md` and `THIRD_PARTY_NOTICES.md` for the license boundary. "
        + license_notice + "\n", encoding="utf-8")
    sums = []
    for path in sorted(output.rglob("*")):
        if path.is_file():
            sums.append(hashlib.sha256(path.read_bytes()).hexdigest() + "  " + path.relative_to(output).as_posix())
    (output / "SHA256SUMS").write_text("\n".join(sums) + "\n", encoding="utf-8")
    return {"source_commit": commit, "profile": PROFILE, "output": str(output),
            "source_files": len(selected), "source_bytes": sum(len(v[1]) for v in selected.values()),
            "retained_licenses": len(licenses), "excluded_counts": dict(sorted(excluded.items())),
            "sha256sums": hashlib.sha256((output / "SHA256SUMS").read_bytes()).hexdigest(),
            "publication": "not-published", "license": project_license or "project-license-not-selected"}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2],
                        help="Source Git repository; working-tree files are never copied.")
    parser.add_argument("--commit", required=True, help="Full frozen 40-character Git commit SHA.")
    parser.add_argument("--output", type=Path, required=True, help="New empty snapshot directory to create.")
    args = parser.parse_args()
    try:
        report = prepare(args.repo.resolve(), args.commit, args.output.absolute())
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        # Do not print Git stderr or scanned source content.
        print("Public source export failed: " + str(error), file=sys.stderr)
        return 1
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
