"""Prepare an external schema5 package for review using the author's rights declaration.

Requires Python 3.10+ and the matching trusted NativeValidator distribution.
Does not establish copyright ownership, execute author code or publish anything.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import tempfile
import zipfile

if __package__:
    from .package_native import canonical
else:
    from package_native import canonical

MAX_PACKAGE_BYTES = 64 * 1024 * 1024  # Current market transfer budget.
MAX_EXPANDED_BYTES = 1024 * 1024 * 1024  # Existing PackageValidator.MaxTotal.
MAX_FILE_BYTES = 64 * 1024 * 1024
MAX_TEXTURE_BYTES = 128 * 1024 * 1024 + 44
EXTERNAL = "spatial-external-native-wallpaper"
POLICY = "native-schema5-external-data-v1"


def require(ok, message):
    if not ok:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def loads(data):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "duplicate JSON key: " + key)
            result[key] = value
        return result
    def invalid(value):
        raise ValueError("non-finite JSON number: " + value)
    return json.loads(data, object_pairs_hook=unique, parse_constant=invalid)


def no_links(path):
    for item in (path, *path.parents):
        if item.exists() or item.is_symlink():
            require(not item.is_symlink() and not getattr(item.lstat(), "st_file_attributes", 0) & 0x400,
                    "symlink/reparse path is not accepted")


def entry_path(name):
    # Match existing PackageValidator.CheckPath, without extracting any files.
    require(isinstance(name, str) and 0 < len(name) <= 200 and not name.startswith("/"), "invalid archive path")
    require(all(32 < ord(c) < 127 and c not in '\\:%<>"|?*' for c in name), "invalid archive path character")
    for part in name.split("/"):
        require(part not in {"", ".", ".."} and not part.endswith("."), "invalid archive path segment")
        stem = part.split(".")[0].upper()
        require(stem not in {"CON", "PRN", "AUX", "NUL", "CLOCK$"}
                and not (len(stem) == 4 and stem[:3] in {"COM", "LPT"} and stem[3].isdigit()), "reserved archive path")


def read_zip(data, outer=False):
    require(0 < len(data) <= MAX_PACKAGE_BYTES, "compressed archive exceeds market 64 MiB budget")
    result, aliases, total = {}, set(), 0
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        require(0 < len(archive.infolist()) <= (3 if outer else 8192), "archive entry budget")
        for entry in archive.infolist():
            name = entry.filename
            entry_path(name)
            require(name.casefold() not in aliases, "duplicate/case alias archive entry")
            aliases.add(name.casefold())
            kind = stat.S_IFMT(entry.external_attr >> 16)
            require(kind in (0, stat.S_IFREG) and entry.external_attr & 0x410 == 0
                    and not entry.flag_bits & 1, "archive link/directory/encrypted entry")
            require(entry.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED), "unsupported ZIP compression")
            limit = (1048576 if outer and name == "manifest.json" else 4194304 if outer and name == "script.json"
                     else MAX_EXPANDED_BYTES if outer else MAX_TEXTURE_BYTES if name.endswith(".stex") else MAX_FILE_BYTES)
            require(0 < entry.file_size <= limit and entry.file_size <= MAX_EXPANDED_BYTES - total, "expanded archive budget")
            with archive.open(entry) as source:
                raw = source.read(entry.file_size + 1)
                require(len(raw) == entry.file_size and not source.read(1), "archive expansion differs from declaration")
            total += len(raw)
            result[name] = raw
    return result


def identity(manifest):
    value = copy.deepcopy(manifest)
    value.pop("build_id", None)
    return sha(canonical(value))


def archive_bytes(files):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, raw in sorted(files.items()):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            archive.writestr(info, raw)
    require(stream.tell() <= MAX_PACKAGE_BYTES, "output compressed archive exceeds market 64 MiB budget")
    return stream.getvalue()


def read_external(raw):
    outer = read_zip(raw, True)
    require(set(outer) == {"manifest.json", "script.json", "assets.spatial"}, "external archive must contain exactly manifest/script/assets")
    manifest = loads(outer["manifest.json"])
    require(isinstance(manifest, dict) and manifest.get("format") == EXTERNAL
            and type(manifest.get("schema")) is int and manifest["schema"] == 5
            and manifest.get("sdk") == "0.4.0-preview.1" and type(manifest.get("command_abi")) is int and manifest["command_abi"] == 1,
            "requires external schema5 SDK 0.4.0-preview.1; platform packages are never accepted")
    inner = read_zip(outer["assets.spatial"])
    require("manifest.json" in inner, "canonical assets manifest missing")
    assets = loads(inner["manifest.json"])
    require(isinstance(assets, dict) and assets.get("schema") == 2 and isinstance(manifest.get("assets"), dict)
            and manifest["assets"].get("format") == "canonical-static-data-v1",
            "requires admitted canonical external assets")
    return outer, manifest, inner, assets


def resource_inventory(outer, inner):
    return {"script.json": sha(outer["script.json"]),
            **{"assets/" + name: sha(raw) for name, raw in sorted(inner.items()) if name != "manifest.json"}}


def license_records(value, resources):
    require(isinstance(value, dict) and set(value) == {"schemaVersion", "records"}
            and type(value["schemaVersion"]) is int and value["schemaVersion"] == 1, "rights declaration must have schemaVersion 1 and records")
    records = value["records"]
    require(isinstance(records, list) and 0 < len(records) <= 4096, "rights record budget")
    fields = {"name", "source", "license", "redistribution", "attribution"}
    admitted, covered = [], set()
    for record in records:
        require(isinstance(record, dict) and set(record) == fields | {"resources"}, "unknown/missing rights record field")
        require(all(isinstance(record[k], str) and record[k].strip() and len(record[k]) <= 8192 and "\0" not in record[k] for k in fields), "rights record text is empty/invalid")
        require(record["redistribution"] == "verified", "every rights declaration must explicitly say verified")
        names = record["resources"]
        require(isinstance(names, list) and 0 < len(names) <= 8193 and all(isinstance(n, str) for n in names)
                and len(set(names)) == len(names) and set(names) <= set(resources), "rights resources contain unknown/duplicate paths")
        covered.update(names)
        admitted.append({k: record[k] for k in sorted(fields)})
    require(covered == set(resources), "rights declarations must cover every script/asset payload")
    return admitted


def run_validator(validator, package):
    result = subprocess.run([str(validator), "--package", str(package), "--platform-index", str(package.parent / "unused-external-platform-index.json")],
                            capture_output=True, timeout=30, check=False, cwd=str(validator.parent))
    require(len(result.stdout) <= 1048576 and len(result.stderr) <= 65536, "validator output budget")
    require(result.returncode == 0, "NativeValidator rejected package: " + (result.stdout + result.stderr).decode("utf-8", errors="replace")[:2048])
    report = loads(result.stdout.decode("utf-8-sig"))
    _, manifest, _, _ = read_external(package.read_bytes())
    require(report.get("valid") is True and report.get("policy") == POLICY and report.get("schema") == 5
            and report.get("sdk") == "0.4.0-preview.1" and report.get("command_abi") == 1
            and report.get("sha256") == sha(package.read_bytes()) and report.get("build_id") == manifest["build_id"]
            and report.get("work_id") == manifest["work_id"] and report.get("revision") == manifest["revision"], "validator identity/policy does not match external bytes")
    return report


def prepare(package, declarations, confirm_rights, validator, output=None, list_only=False):
    package, validator = Path(package).absolute(), Path(validator).absolute()
    no_links(package); no_links(validator)
    require(package.is_file() and validator.is_file(), "package/trusted validator file missing")
    require(package.stat().st_size <= MAX_PACKAGE_BYTES, "input exceeds market 64 MiB budget")
    if not list_only:
        require(confirm_rights is True, "--confirm-rights is required; this is your declaration, not proof of copyright")
        require(output is not None, "--output required")
        output = Path(output).absolute()
        no_links(output)
        require(output.suffix == ".spatial" and not output.exists(), "output must be a new .spatial file")
    raw = package.read_bytes()
    outer, manifest, inner, assets = read_external(raw)
    inventory = resource_inventory(outer, inner)
    records = None if list_only else license_records(declarations, inventory)
    validator_sha = sha(validator.read_bytes())
    with tempfile.TemporaryDirectory(prefix="SpatialShare-") as directory:
        pinned = Path(directory) / "input.spatial"
        pinned.write_bytes(raw)
        before = run_validator(validator, pinned)
        if list_only:
            return {"package_sha256": sha(raw), "work_id": manifest["work_id"], "revision": manifest["revision"], "resources": inventory, "validation": before}
        license = {"mode": "share", "records": records}
        assets.update(license=copy.deepcopy(license), reshare=True)
        assets["build_id"] = identity(assets)
        inner["manifest.json"] = canonical(assets)
        outer["assets.spatial"] = archive_bytes(inner)
        manifest["license"] = license
        manifest["assets"]["build_id"] = assets["build_id"]
        manifest["files"]["assets.spatial"] = {"size": len(outer["assets.spatial"]), "sha256": sha(outer["assets.spatial"])}
        manifest["build_id"] = identity(manifest)
        outer["manifest.json"] = canonical(manifest)
        candidate = Path(directory) / "shared.spatial"
        shared = archive_bytes(outer)
        candidate.write_bytes(shared)
        after = run_validator(validator, candidate)
        require(after.get("license_share") is True, "validator did not admit sharing permission")
        require(after.get("license_records") == records, "validator license records differ")
        check_outer, _, check_inner, _ = read_external(shared)
        require(resource_inventory(check_outer, check_inner) == inventory, "script/asset bytes changed during preparation")
        require(sha(validator.read_bytes()) == validator_sha and sha(package.read_bytes()) == sha(raw), "input/validator changed during preparation")
        output.parent.mkdir(parents=True, exist_ok=True)
        no_links(output)
        with output.open("xb") as target:
            target.write(shared)
            target.flush()
            os.fsync(target.fileno())
    return {"package": str(output), "sha256": sha(shared), "source_sha256": sha(raw), "validator_sha256": validator_sha,
            "confirmed_rights": True, "rights_declaration_sha256": sha(canonical(declarations)), "rights_declaration": declarations,
            "unchanged_payload_sha256": inventory, "before": before, "after": after,
            "scope": "Author rights declaration and byte admission only; not copyright proof, author-code execution, visual/GPU safety certification or market approval."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--license-records", type=Path, help="Versioned rights records including exact per-payload resources.")
    parser.add_argument("--confirm-rights", action="store_true", help="Explicitly declare you have permission to redistribute all listed payloads.")
    parser.add_argument("--validator", required=True, type=Path, help="Trusted matching NativeValidator.exe, never supplied by a contributor package.")
    parser.add_argument("--output", type=Path, help="New shared .spatial file; the local source is never overwritten.")
    parser.add_argument("--list-resources", action="store_true", help="Read-only validated resource path/SHA inventory to prepare your declaration.")
    args = parser.parse_args()
    require(args.list_resources or args.license_records is not None, "--license-records is required")
    if args.license_records:
        no_links(args.license_records.absolute())
        require(args.license_records.stat().st_size <= 1048576, "rights declaration exceeds 1 MiB")
    records = loads(args.license_records.read_bytes()) if args.license_records else None
    print(json.dumps(prepare(args.package, records, args.confirm_rights, args.validator, args.output, args.list_resources), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
