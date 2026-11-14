"""Publish schema5 native packages after trusted byte admission; never run author code."""
import argparse
import base64
import copy
import hashlib
import json
import os
import re
import subprocess
import tempfile
import uuid
import zipfile
from datetime import timedelta
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import Request, build_opener

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from jsonschema import Draft202012Validator

from . import catalog, stats
from .common import ROOT, OFFICIAL as PRODUCT_REPOSITORY, MarketError, atomic_json, canonical, instant, loads, read_json, require, utc_now, validate
from .github import ApiNoRedirect, GitHub, JsonTransport, repository_path

MARKET_REPOSITORY = os.environ.get("MARKET_REPOSITORY", "KyaKayKya/Spatial-Wallpaper-Community")
require(re.fullmatch(r"[A-Za-z0-9-]+/[A-Za-z0-9_.-]+", MARKET_REPOSITORY), "invalid configured market repository")

DOMAIN = b"spatial-market-published-v2\0"
ALGORITHM = "RSA-PKCS1-SHA256"
SDK = "0.4.0-preview.1"
MAX_PACKAGE = 64 * 1024 * 1024
INDEX_TAG = "market-index"
VALIDATOR_TAG = "native-sdk-0.4.0-preview.1"
VALIDATOR_NAME = "NativeValidator-Windows.zip"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def signed_bytes(snapshot):
    validate("published", snapshot)
    return canonical(snapshot) + b"\n"


def sign_bytes(raw, key, key_id):
    require(isinstance(key, rsa.RSAPrivateKey) and key.key_size >= 3072, "RSA private key must have at least 3072 bits")
    require(re.fullmatch(r"[A-Za-z0-9._-]{1,64}", key_id) is not None, "invalid key ID")
    validate_published(loads(raw))
    return {"algorithm": ALGORITHM, "keyId": key_id, "sha256": digest(raw),
            "signature": base64.b64encode(key.sign(DOMAIN + raw, padding.PKCS1v15(), hashes.SHA256())).decode("ascii")}


def verify_bytes(raw, envelope, trusted_keys, now, watermark=None, allow_expired=False):
    require(set(envelope) == {"algorithm", "keyId", "sha256", "signature"}, "invalid signature envelope")
    require(envelope["algorithm"] == ALGORITHM and envelope["keyId"] in trusted_keys, "untrusted market signing key")
    require(envelope["sha256"] == digest(raw), "published byte digest mismatch")
    try:
        trusted_keys[envelope["keyId"]].verify(base64.b64decode(envelope["signature"], validate=True), DOMAIN + raw, padding.PKCS1v15(), hashes.SHA256())
    except (InvalidSignature, ValueError) as error:
        raise MarketError("invalid published signature") from error
    snapshot = validate_published(loads(raw))
    require(instant(snapshot["generatedAt"]) <= instant(now) and (allow_expired or instant(now) < instant(snapshot["expiresAt"])), "published index expired or from future")
    if watermark:
        require(snapshot["generation"] >= watermark["generation"], "published rollback")
        require(snapshot["generation"] != watermark["generation"] or digest(raw) == watermark["sha256"], "published generation equivocation")
    return snapshot


def validate_published(snapshot, previous=None):
    validate("published", snapshot)
    catalog.require_work_bindings(snapshot["entries"], previous["entries"] if previous else ())
    keys = set()
    for entry in snapshot["entries"]:
        require(entry["key"] == str(entry["repositoryId"]) + ":" + entry["workId"] and entry["key"] not in keys, "duplicate/mismatched published work identity")
        keys.add(entry["key"])
        require(str(uuid.UUID(entry["workId"])) == entry["workId"], "published work ID must be canonical stable GUID")
        for build in [entry["build"], *entry["history"]]:
            assets = build["assets"]
            require(len(assets) == 1 and assets[0]["name"].endswith(".spatial"), "exactly one .spatial package required")
            require(assets[0]["url"] == catalog.asset_url({"full_name": entry["repository"]}, build["tag"], assets[0]["name"]), "published package URL is not exact GitHub Release")
            identity = {k: build[k] for k in ("version", "releaseId", "tag", "checker")}
            identity["assets"] = [catalog.asset_binding(a) for a in assets]
            require(build["identity"] == digest(canonical({"repositoryId": entry["repositoryId"], "ownerId": entry["ownerId"], "workId": entry["workId"], **identity})), "published build identity mismatch")
        require(entry["build"] in entry["history"], "current build missing from immutable history")
        require(entry["admission"]["buildIdentity"] == entry["build"]["identity"] and entry["admission"]["packageAssetId"] == entry["build"]["assets"][0]["id"], "admission is not bound to exact build/asset")
        if "comments" in entry:
            require(entry["comments"]["url"] == "https://github.com/" + MARKET_REPOSITORY + "/issues/" + str(entry["comments"]["number"]), "foreign issue comments link")
        if "discussion" in entry:
            require(entry["discussion"]["url"] == "https://github.com/" + MARKET_REPOSITORY + "/discussions/" + str(entry["discussion"]["number"]), "foreign discussion link")
    return snapshot


class NativeAdmission:
    """Invoke a platform-supplied, digest-pinned validator with no contributor code."""
    def __init__(self, validator, validator_sha256, platform_index, platform_index_sha256, runtime_files=None):
        self.validator = Path(validator).resolve(strict=True)
        self.platform_index = Path(platform_index).resolve(strict=True)
        require(digest(self.validator.read_bytes()) == validator_sha256, "trusted validator digest mismatch")
        require(digest(self.platform_index.read_bytes()) == platform_index_sha256, "trusted platform index digest mismatch")
        self.runtime_files = runtime_files or {}
        require(set(self.runtime_files) == {"NativeValidator.exe", "NativeValidator.exe.config", "Newtonsoft.Json.dll"}, "trusted validator runtime file inventory required")
        for name, expected in self.runtime_files.items():
            require(digest((self.validator.parent / name).read_bytes()) == expected, "validator runtime digest mismatch: " + name)
        self.validator_sha256, self.platform_index_sha256 = validator_sha256, platform_index_sha256

    def __call__(self, package):
        # Recheck immediately before execution; a submitted path never selects the executable.
        require(digest(self.validator.read_bytes()) == self.validator_sha256 and digest(self.platform_index.read_bytes()) == self.platform_index_sha256, "trusted validator/index drift")
        for name, expected in self.runtime_files.items():
            require(digest((self.validator.parent / name).read_bytes()) == expected, "validator dependency drift: " + name)
        result = subprocess.run([str(self.validator), "--package", str(Path(package).resolve()), "--platform-index", str(self.platform_index)],
                                capture_output=True, timeout=20, check=False, cwd=str(self.validator.parent),
                                env={name: value for name, value in os.environ.items() if name.upper() in {"SYSTEMROOT", "WINDIR", "PATH", "TEMP", "TMP", "COMSPEC", "PATHEXT", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS", "USERPROFILE", "LOCALAPPDATA"}})
        require(len(result.stdout) <= 1024 * 1024 and len(result.stderr) <= 65536, "validator output budget exceeded")
        require(result.returncode == 0, "native admission failed: " + result.stdout.decode("utf-8", errors="replace")[:512])
        report = loads(result.stdout.decode("utf-8-sig"))
        require(report.get("valid") is True, "validator did not admit native package")
        report["validator_sha256"] = self.validator_sha256
        report["platform_index_sha256"] = self.platform_index_sha256
        return report


def require_admission(report, entry, asset):
    require(report.get("valid") is True and report.get("sha256") == asset["sha256"], "native admission not bound to downloaded bytes")
    require(report.get("schema") == 5 and report.get("sdk") == SDK and report.get("command_abi") == 1 and report.get("policy") in ("native-schema5-platform-index-v1", "native-schema5-external-data-v1"), "native admission version/policy mismatch")
    require(report.get("license_share") is True, "package lacks verified redistribution permission")
    require(report.get("work_id") == entry["workId"] and report.get("revision") == entry["build"]["version"], "native stable GUID/revision differs from listing")
    require(re.fullmatch(r"[0-9a-f]{64}", report.get("build_id", "")) is not None, "missing native package build identity")
    require(sorted(report.get("capabilities", [])) == sorted(entry["capabilities"]) and "capabilities" in report, "declared capabilities differ from actual admitted bytecode")
    require(entry["sdk"] == SDK, "listing SDK differs from admitted package")
    records = report.get("license_records")
    require(isinstance(records, list) and records and all(r.get("redistribution") == "verified" for r in records), "missing verified license records")
    declared = {a["license"] for a in entry["attribution"]}
    require({r["license"] for r in records}.issubset(declared), "listing omits package license attribution")


def promote(candidate, fetch_asset, admit, directory, details=None, previous=None):
    """Re-download exact asset bytes and admit them before a candidate can publish."""
    validate("catalog", candidate)
    result = copy.deepcopy(candidate)
    result.update(schemaVersion=2, kind="published")
    reports = []
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    prior = {e["key"]: e for e in previous["entries"]} if previous else {}
    for entry in result["entries"]:
        if entry["status"] == "revoked" and entry["key"] in prior and prior[entry["key"]]["build"] == entry["build"]:
            entry["admission"] = copy.deepcopy(prior[entry["key"]]["admission"])
            for field in ("description", "preview", "discussion", "comments"):
                if field in prior[entry["key"]]:
                    entry[field] = copy.deepcopy(prior[entry["key"]][field])
            reports.append({"key": entry["key"], "revoked": True, "reason": "retained prior admission only for uninstallable tombstone; unavailable bytes need not be fetched"})
            continue
        assets = entry["build"]["assets"]
        require(len(assets) == 1 and assets[0]["name"].endswith(".spatial"), "one .spatial package required; split/multiple attachments are not native packages")
        asset = assets[0]
        # Metadata candidate signatures and author admission claims are deliberately ignored.
        path = directory / (str(asset["id"]) + ".spatial")
        final, chunks = fetch_asset({"id": asset["id"], "name": asset["name"], "size": asset["size"], "browser_download_url": asset["url"]})
        from urllib.parse import urlsplit
        target = urlsplit(final)
        require(final == asset["url"] or target.scheme == "https" and target.hostname in catalog.CDN_HOSTS and target.port in (None, 443) and target.username is None and target.password is None, "untrusted package redirect")
        size, sha = 0, hashlib.sha256()
        with path.open("xb") as output:
            for chunk in chunks:
                size += len(chunk)
                require(size <= min(MAX_PACKAGE, asset["size"]), "package transfer budget exceeded")
                output.write(chunk)
                sha.update(chunk)
        require(size == asset["size"] and sha.hexdigest() == asset["sha256"], "package bytes changed after Release collection")
        report = admit(path)
        require_admission(report, entry, asset)
        entry["admission"] = {"schemaVersion": 5, "sdkVersion": SDK, "sceneCommandAbi": 1,
                              "policy": "native-admission-v1", "buildIdentity": entry["build"]["identity"], "packageAssetId": asset["id"]}
        if entry["status"] != "revoked":
            entry["status"] = "published"
        if details and entry["key"] in details:
            metadata = details[entry["key"]]
            require(set(metadata).issubset({"description", "preview", "discussion", "comments"}), "unknown reviewed detail field")
            entry.update(copy.deepcopy(metadata))
        reports.append({"key": entry["key"], "buildIdentity": entry["build"]["identity"], "assetId": asset["id"], "report": report})
    return validate_published(result, previous), reports


def validate_previews(snapshot, fetch=None):
    from urllib.parse import urlsplit
    import struct
    def download(url):
        with build_opener(ApiNoRedirect()).open(Request(url, headers={"User-Agent": "Spatial-Wallpaper-market/2"}), timeout=20) as response:
            return response.read(2 * 1024 * 1024 + 1)
    for entry in snapshot["entries"]:
        if entry["status"] == "revoked" or "preview" not in entry:
            continue
        url = entry["preview"]
        target = urlsplit(url)
        require(target.scheme == "https" and target.port in (None, 443) and target.username is None and target.password is None and not target.query and not target.fragment, "unsafe preview URL")
        if target.hostname == "raw.githubusercontent.com":
            require(re.fullmatch(r"/[A-Za-z0-9-]+/[A-Za-z0-9_.-]+/[0-9a-f]{40}/[^?#]+", target.path), "raw preview must pin a source commit")
        else:
            require(target.hostname == "user-images.githubusercontent.com" or target.hostname == "github.com" and target.path.startswith("/user-attachments/assets/"), "untrusted preview origin")
        data = (fetch or download)(url)
        require(33 <= len(data) <= 2 * 1024 * 1024 and data[:8] == bytes((137, 80, 78, 71, 13, 10, 26, 10)) and data[12:16] == b"IHDR", "preview must be bounded PNG")
        width, height = struct.unpack(">II", data[16:24])
        require(0 < width <= 2048 and 0 < height <= 2048, "preview dimensions exceed client budget")


def verify_live_bindings(gh, snapshot):
    repositories = {}
    for entry in snapshot["entries"]:
        if entry["status"] == "revoked":
            continue
        repository = gh.resolve(entry["repositoryId"])
        require(repository["owner"]["id"] == entry["ownerId"] and repository["full_name"] == entry["repository"], "repository changed during native admission")
        releases = repositories.setdefault(entry["repository"], gh.releases(entry["repository"]))
        build = entry["build"]
        exact = [r for r in releases if r["id"] == build["releaseId"] and r["tag_name"] == build["tag"] and not r["draft"] and not r["prerelease"]]
        require(len(exact) == 1, "Release changed during native admission")
        asset = build["assets"][0]
        require(any(a["id"] == asset["id"] and a["name"] == asset["name"] and a["size"] == asset["size"] and a["browser_download_url"] == asset["url"] for a in exact[0]["assets"]), "asset replaced during native admission")


def candidate_previous(previous):
    if not previous:
        return None
    validate_published(previous)
    result = copy.deepcopy(previous)
    result.update(schemaVersion=1, kind="candidate")
    for entry in result["entries"]:
        for field in ("admission", "description", "preview", "discussion", "comments", "popularity"):
            entry.pop(field, None)
        if entry["status"] == "published":
            entry["status"] = "awaiting-sdk-validation"
    return validate("catalog", result)


def submissions():
    """Only records in this trusted checkout approve exact work/revision bytes."""
    found = {}
    for path in sorted((ROOT / "market/submissions").glob("*/*/*.json")):
        value = validate("submission", read_json(path))
        require(path.relative_to(ROOT / "market").as_posix() == submission_path(value), "submission filename mismatch")
        key = (value["repositoryId"], value["workId"], value["revision"])
        require(key not in found, "duplicate approved submission")
        found[key] = value
    return found


def registrations():
    found = {}
    for path in sorted((ROOT / "market/registry").glob("*.json")):
        value = validate("registration", read_json(path))
        require(path.name == str(value["repositoryId"]) + ".json", "registration filename mismatch")
        found[value["repositoryId"]] = value
    for value in submissions().values():
        prior = found.setdefault(value["repositoryId"], {k: value[k] for k in ("schemaVersion", "repositoryId", "repository")})
        require(prior["repository"] == value["repository"], "submission repository identity conflict")
    return found


def require_approved_package(entry, approval):
    assets = entry["build"]["assets"]
    require(len(assets) == 1 and assets[0]["name"].endswith(".spatial"), "approved revision requires exactly one .spatial asset")
    require(entry["repositoryId"] == approval["repositoryId"] and entry["workId"] == approval["workId"]
            and entry["build"]["version"] == approval["revision"] and assets[0]["sha256"] == approval["packageSha256"],
            "downloaded package SHA256 or revision differs from approved submission")


def normalize_revocations(value):
    # Repository state has a versioned envelope; catalog/published snapshots
    # deliberately embed only its works/builds payload. Internal callers may
    # already supply that payload, but neither form may hide unknown fields.
    require(isinstance(value, dict) and set(value) in ({"works", "builds"}, {"schemaVersion", "works", "builds"}), "invalid revocations fields")
    if "schemaVersion" in value:
        require(type(value["schemaVersion"]) is int and value["schemaVersion"] == 1, "unsupported revocations schemaVersion")
    result = {key: copy.deepcopy(value[key]) for key in ("works", "builds")}
    schema = read_json(ROOT / "market/schemas/catalog.schema.json")["properties"]["revocations"]
    errors = list(Draft202012Validator(schema).iter_errors(result))
    require(not errors, "revocations: " + "; ".join(error.message for error in errors))
    return result


def collect(gh, previous, now, revocations):
    revocations = normalize_revocations(revocations)
    old = candidate_previous(previous)
    generation = old["generation"] + 1 if old else 1
    # A registered author may change its default-branch manifest at any time.
    # Preserve signed revisions until an exact new submission is approved;
    # registration alone never approves all future works or revisions.
    entries = {e["key"]: copy.deepcopy(e) for e in old["entries"]} if old else {}
    fields = ("description", "preview", "discussion", "comments")
    presentation = {e["key"]: {f: copy.deepcopy(e[f]) for f in fields if f in e}
                    for e in previous["entries"]} if previous else {}
    approvals = submissions()
    releases_by_repository = {}
    for repository_id, registration in sorted(registrations().items()):
        repository = gh.resolve(repository_id)
        require(repository["full_name"].lower() == registration["repository"].lower(), "registered repository renamed; review path rebind")
        from .registry import validate_manifest
        manifest = validate_manifest(repository, gh.manifest(repository))
        releases = gh.releases(repository["full_name"])
        releases_by_repository[repository_id] = releases
        approved = [work for work in manifest["works"] if (repository_id, work["id"], work["version"]) in approvals]
        if not approved:
            continue
        selected = copy.deepcopy(manifest)
        selected["works"] = approved
        snapshot = catalog.build_snapshot(repository, selected, releases, gh.asset, old, generation, now, revocations)
        selected_ids = {work["id"] for work in approved}
        for entry in snapshot["entries"]:
            if entry["repositoryId"] != repository_id or entry["workId"] not in selected_ids:
                continue
            approval = approvals[(repository_id, entry["workId"], entry["build"]["version"])]
            require(approval["repository"].lower() == repository["full_name"].lower(), "approved repository identity changed")
            require_approved_package(entry, approval)
            current_work = next(w for w in approved if w["id"] == entry["workId"])
            presentation.setdefault(entry["key"], {}).update({f: copy.deepcopy(current_work[f]) for f in ("description", "preview") if f in current_work})
            entries[entry["key"]] = entry
    observations = []
    for entry in entries.values():
        # Official withdrawal is independent of whether any new revision was
        # approved or the author still exposes this work in its manifest.
        if entry["key"] in revocations["works"] or entry["build"]["identity"] in revocations["builds"]:
            entry["status"] = "revoked"
        presentation.setdefault(entry["key"], {})
        releases = releases_by_repository.get(entry["repositoryId"], [])
        for build in entry["history"]:
            for asset in build["assets"]:
                matches = [a for r in releases if r["id"] == build["releaseId"] for a in r["assets"] if a["id"] == asset["id"] and a["name"] == asset["name"] and a["size"] == asset["size"]]
                if len(matches) == 1:
                    observations.append({"repositoryId": entry["repositoryId"], "assetId": asset["id"], "work": entry["key"], "buildId": build["identity"], "downloadCount": matches[0]["download_count"], "status": "revoked" if entry["status"] == "revoked" else "installable", "volumeGroup": None})
    catalog.require_work_bindings(entries.values(), old["entries"] if old else ())
    return validate("catalog", {"schemaVersion": 1, "kind": "candidate", "generation": generation, "generatedAt": now,
            "expiresAt": (instant(now) + timedelta(days=7)).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "entries": [entries[k] for k in sorted(entries)], "revocations": copy.deepcopy(revocations)}), observations, presentation


def popularity_rows(state, day):
    # Legacy statistics keep an initial lifetime sample for diagnostics. It is
    # never evidence of a time-bounded download increment in the published feed.
    bounded = copy.deepcopy(state)
    for asset in bounded["assets"].values():
        for event in asset["events"]:
            if event["from"] is None:
                event["delta"] = 0
    rows = {}
    for row in stats.rank(bounded, day, 7):
        if row["complete"] and row["unallocatedDownloads"] == 0:
            rows[row["work"]] = row["downloads"]
    return rows


def submission_path(value):
    return "submissions/" + str(value["repositoryId"]) + "/" + value["workId"] + "/" + value["revision"] + ".json"


def check_pr(gh, number, checked_head, now, admit=None, directory=None):
    """API-read data only; checkout and execution always use the trusted base SHA."""
    require(type(number) is int and number > 0 and re.fullmatch(r"[0-9a-f]{40}", checked_head), "invalid PR identity")
    path = repository_path(MARKET_REPOSITORY) + "/pulls/" + str(number)
    pull = gh.get(path)[0]
    require(pull["state"] == "open" and not pull["draft"] and pull["head"]["sha"] == checked_head, "PR not ready or head changed")
    require(pull["base"]["repo"]["full_name"] == MARKET_REPOSITORY and re.fullmatch(r"dev/v[0-9]+\.[0-9]+", pull["base"]["ref"]), "submission must target active reviewed dev branch")
    files = gh.pages(path + "/files")
    require(len(files) == pull["changed_files"] == 1 and files[0]["status"] == "added" and "previous_filename" not in files[0], "submission PR must add exactly one metadata file")
    filename = files[0]["filename"]
    require(re.fullmatch(r"market/registry/[1-9][0-9]*\.json|market/submissions/[1-9][0-9]*/[0-9a-f-]{36}/[A-Za-z0-9][A-Za-z0-9._+-]{0,99}\.json", filename), "unsafe/non-submission PR path")
    value = gh.file_at(pull["head"]["repo"]["full_name"], filename, checked_head)
    validate("submission" if filename.startswith("market/submissions/") else "registration", value)
    expected = "market/" + submission_path(value) if "workId" in value else "market/registry/" + str(value["repositoryId"]) + ".json"
    require(filename == expected, "submission path does not match identity")
    repository = gh.resolve(value["repositoryId"])
    require(not repository["private"] and repository["full_name"].lower() == value["repository"].lower(), "submission repository mismatch/private")
    # Automatic owner verification is deliberately narrow. Organization/collaborator
    # requests require a separately reviewed default-branch control proof; no PAT.
    require(repository["owner"]["type"] == "User" and repository["owner"]["id"] == pull["user"]["id"], "organization/collaborator requires maintainer control-proof review")
    from .registry import validate_manifest
    manifest = validate_manifest(repository, gh.manifest(repository))
    if "workId" in value:
        require(any(w["id"] == value["workId"] and w["version"] == value["revision"] for w in manifest["works"]), "submitted revision absent from author default-branch manifest")
        manifest = copy.deepcopy(manifest)
        manifest["works"] = [w for w in manifest["works"] if w["id"] == value["workId"]]
        snapshot = catalog.build_snapshot(repository, manifest, gh.releases(repository["full_name"]), gh.asset, None, 1, now, {"works": [], "builds": []})
        require_approved_package(snapshot["entries"][0], value)
        if admit is not None:
            promote(snapshot, gh.asset, admit, directory)
    else:
        # Repository registration is discovery only, never revision approval.
        snapshot = {"entries": []}
    require(gh.get(path)[0]["head"]["sha"] == checked_head, "PR changed during metadata check")
    return {"checkedHead": checked_head, "repositoryId": repository["id"], "works": len(snapshot["entries"]),
            "packageSha256": value.get("packageSha256"), "scope": "source registration only; no revision approved" if "workId" not in value else "read-only metadata and native-byte admission" if admit is not None else "metadata and actual package SHA256; native admission not run"}


def prepare_validator(gh, output, expected_zip_sha256):
    repository = gh.get(repository_path(MARKET_REPOSITORY))[0]
    release = gh.get(repository_path(MARKET_REPOSITORY) + "/releases/tags/" + VALIDATOR_TAG)[0]
    matches = [a for a in release["assets"] if a["name"] == VALIDATOR_NAME]
    require(len(matches) == 1, "trusted validator Release asset unavailable; configure deployment first")
    require(matches[0]["browser_download_url"] == catalog.asset_url({"full_name": MARKET_REPOSITORY}, VALIDATOR_TAG, VALIDATOR_NAME), "unexpected validator source URL")
    final, chunks = gh.asset(matches[0])
    with tempfile.TemporaryDirectory(prefix="market-validator-") as temporary:
        archive = Path(temporary) / "validator.zip"
        with archive.open("xb") as stream:
            total = 0
            for chunk in chunks:
                total += len(chunk)
                require(total <= 32 * 1024 * 1024, "validator archive budget")
                stream.write(chunk)
        require(digest(archive.read_bytes()) == expected_zip_sha256, "trusted validator archive digest mismatch")
        output = Path(output)
        output.mkdir(parents=True, exist_ok=False)
        allowed = {"NativeValidator.exe", "NativeValidator.exe.config", "Newtonsoft.Json.dll", "native-platform-library.json", "build.json"}
        with zipfile.ZipFile(archive) as package:
            require(len(package.infolist()) == len(allowed) and {i.filename for i in package.infolist()} == allowed, "unexpected validator archive layout")
            for item in package.infolist():
                require(item.file_size <= 16 * 1024 * 1024 and item.file_size > 0, "validator entry budget")
                (output / item.filename).write_bytes(package.read(item))
        build = read_json(output / "build.json")
        for name in allowed - {"build.json"}:
            require(build["files"].get(name) == digest((output / name).read_bytes()), "validator distribution file digest mismatch: " + name)
    return {"validator": str(output / "NativeValidator.exe"), "validatorSha256": digest((output / "NativeValidator.exe").read_bytes()),
            "platformIndex": str(output / "native-platform-library.json"), "platformIndexSha256": digest((output / "native-platform-library.json").read_bytes()),
            "runtimeFiles": {name: digest((output / name).read_bytes()) for name in ("NativeValidator.exe", "NativeValidator.exe.config", "Newtonsoft.Json.dll")}}


class ReleaseWriter:
    def __init__(self, token):
        require(bool(token), "publication token required only in protected publish job")
        self.token = token
        self.read = GitHub(JsonTransport(token))

    def request(self, method, path, body=None, raw=False):
        require(path.startswith("/repos/" + MARKET_REPOSITORY + "/"), "write outside official market repository")
        url = ("https://uploads.github.com" if raw else "https://api.github.com") + path
        data = body if raw else None if body is None else canonical(body)
        headers = {"Authorization": "Bearer " + self.token, "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "Spatial-Wallpaper-market/2", "Content-Type": "application/json"}
        response = build_opener(ApiNoRedirect()).open(Request(url, data=data, headers=headers, method=method), timeout=30)
        with response:
            data = response.read(8 * 1024 * 1024 + 1)
            require(len(data) <= 8 * 1024 * 1024, "GitHub write reply budget")
            return loads(data) if data else None

    def release(self, tag, source_sha, draft=False):
        path = repository_path(MARKET_REPOSITORY) + "/releases"
        releases = self.read.pages(path)
        matches = [r for r in releases if r["tag_name"] == tag]
        require(len(matches) <= 1, "ambiguous market release")
        return matches[0] if matches else self.request("POST", path, {"tag_name": tag, "target_commitish": source_sha, "name": tag, "body": "Signed native wallpaper market index; each package still requires local admission.", "draft": draft, "prerelease": False, "make_latest": "false"})

    def upload(self, release, name, data, replace=False):
        matches = [a for a in release["assets"] if a["name"] == name]
        require(len(matches) <= 1, "ambiguous published asset")
        if matches and replace and release["draft"]:
            self.request("DELETE", repository_path(MARKET_REPOSITORY) + "/releases/assets/" + str(matches[0]["id"]))
            matches = []
        if matches:
            actual = catalog.hash_asset({"full_name": MARKET_REPOSITORY}, release, matches[0], self.read.asset, 8 * 1024 * 1024)
            if actual["sha256"] == digest(data):
                return
            require(replace, "immutable market generation already contains different bytes")
            self.request("DELETE", repository_path(MARKET_REPOSITORY) + "/releases/assets/" + str(matches[0]["id"]))
        self.request("POST", repository_path(MARKET_REPOSITORY) + "/releases/" + str(release["id"]) + "/assets?name=" + quote(name, safe=""), data, raw=True)

    def publish(self, snapshot, files, source_sha):
        require(re.fullmatch(r"[0-9a-f]{40}", source_sha), "publication source commit required")
        immutable = self.release("market-published-" + str(snapshot["generation"]), source_sha, draft=True)
        for name, data in files.items():
            self.upload(immutable, name, data, replace=immutable["draft"])
        if immutable["draft"]:
            self.request("PATCH", repository_path(MARKET_REPOSITORY) + "/releases/" + str(immutable["id"]), {"draft": False, "make_latest": "false"})
        stable = self.release(INDEX_TAG, source_sha)
        for name in ("published.json", "published.sig.json"):
            self.upload(stable, name, files[name], replace=True)


def live_previous(gh, key, key_id, now):
    releases = gh.pages(repository_path(MARKET_REPOSITORY) + "/releases")
    complete = [r for r in releases if re.fullmatch(r"market-published-[1-9][0-9]*", r["tag_name"]) and not r["draft"]]
    if not complete:
        require(not any(r["tag_name"] == INDEX_TAG for r in releases), "stable index exists without audited immutable generation")
        return None, None
    # Immutable signed audit releases are authoritative after a partial stable update.
    latest = max(complete, key=lambda r: int(r["tag_name"].rsplit("-", 1)[1]))
    def download(release, name):
        matches = [a for a in release["assets"] if a["name"] == name]
        require(len(matches) == 1 and 0 < matches[0]["size"] <= 8 * 1024 * 1024, "missing/oversized prior publication asset: " + name)
        require(matches[0]["browser_download_url"] == catalog.asset_url({"full_name": MARKET_REPOSITORY}, release["tag_name"], name), "unexpected prior asset URL")
        final, chunks = gh.asset(matches[0])
        body = bytearray()
        for chunk in chunks:
            body.extend(chunk)
            require(len(body) <= matches[0]["size"], "prior publication transfer exceeded budget")
        require(len(body) == matches[0]["size"], "truncated previous publication")
        return bytes(body)
    raw = download(latest, "published.json")
    previous = verify_bytes(raw, loads(download(latest, "published.sig.json")), {key_id: key.public_key()}, now, allow_expired=True)
    require(latest["tag_name"] == "market-published-" + str(previous["generation"]), "immutable release generation mismatch")
    return previous, loads(download(latest, "stats.json"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    check = commands.add_parser("check-pr")
    check.add_argument("--number", type=int, required=True)
    check.add_argument("--head", required=True)
    check.add_argument("--output", type=Path, required=True)
    check.add_argument("--validator-config", type=Path, required=True)
    prepare = commands.add_parser("prepare-validator")
    prepare.add_argument("--sha256", required=True)
    prepare.add_argument("--output", type=Path, required=True)
    publish = commands.add_parser("build")
    publish.add_argument("--validator-config", type=Path, required=True)
    publish.add_argument("--private-key", type=Path, required=True)
    publish.add_argument("--key-id", required=True)
    publish.add_argument("--previous", type=Path)
    publish.add_argument("--previous-signature", type=Path)
    publish.add_argument("--previous-stats", type=Path)
    publish.add_argument("--details", type=Path)
    publish.add_argument("--output", type=Path, required=True)
    publish.add_argument("--source-sha", required=True)
    publish.add_argument("--upload", action="store_true")
    args = parser.parse_args()
    gh = GitHub(JsonTransport(os.environ.get("GITHUB_TOKEN")))
    if args.command == "check-pr":
        config = read_json(args.validator_config)
        admission = NativeAdmission(config["validator"], config["validatorSha256"], config["platformIndex"], config["platformIndexSha256"], config["runtimeFiles"])
        with tempfile.TemporaryDirectory(prefix="market-pr-admission-") as directory:
            atomic_json(args.output, check_pr(gh, args.number, args.head, utc_now(), admission, directory))
        return
    if args.command == "prepare-validator":
        config = prepare_validator(gh, args.output, args.sha256)
        atomic_json(args.output / "validator-config.json", config)
        return
    require(re.fullmatch(r"[0-9a-f]{40}", args.source_sha), "exact source SHA required")
    key = serialization.load_pem_private_key(args.private_key.read_bytes(), password=None)
    previous = None
    now = utc_now()
    require((args.previous is None) == (args.previous_signature is None), "previous index and signature must be supplied together")
    if args.previous:
        previous = verify_bytes(args.previous.read_bytes(), read_json(args.previous_signature), {args.key_id: key.public_key()}, now, allow_expired=True)
    prior_stats = read_json(args.previous_stats) if args.previous_stats else None
    if not args.previous:
        previous, prior_stats = live_previous(gh, key, args.key_id, now)
    candidate, observations, presentation = collect(gh, previous, now, read_json(ROOT / "market/revocations.json"))
    config = read_json(args.validator_config)
    admission = NativeAdmission(config["validator"], config["validatorSha256"], config["platformIndex"], config["platformIndexSha256"], config["runtimeFiles"])
    details = args.details or (ROOT / "market/details.json" if (ROOT / "market/details.json").exists() else None)
    with tempfile.TemporaryDirectory(prefix="market-packages-") as directory:
        snapshot, reports = promote(candidate, gh.asset, admission, directory, {k: {**v, **(read_json(details).get(k, {}) if details else {})} for k, v in presentation.items()}, previous)
    stat = stats.observe(prior_stats, now[:10], observations,
                         {e["key"]: "revoked" if e["status"] == "revoked" else "installable" for e in snapshot["entries"]}, snapshot["revocations"]["builds"])
    ranks = popularity_rows(stat, now[:10])
    for entry in snapshot["entries"]:
        entry["popularity"] = {"downloads7d": ranks.get(entry["key"])}
    validate_previews(snapshot)
    verify_live_bindings(gh, snapshot)
    raw = signed_bytes(snapshot)
    envelope = sign_bytes(raw, key, args.key_id)
    verify_bytes(raw, envelope, {args.key_id: key.public_key()}, now)
    files = {"published.json": raw, "published.sig.json": canonical(envelope) + b"\n", "validation-reports.json": canonical({"sourceCommit": args.source_sha, "reports": reports}) + b"\n", "stats.json": canonical(stat) + b"\n"}
    args.output.mkdir(parents=True, exist_ok=False)
    for name, data in files.items():
        (args.output / name).write_bytes(data)
    if args.upload:
        ReleaseWriter(os.environ.get("GITHUB_TOKEN")).publish(snapshot, files, args.source_sha)
    print(json.dumps({"generation": snapshot["generation"], "works": len(snapshot["entries"]), "sha256": digest(raw), "uploaded": args.upload}))

if __name__ == "__main__":
    main()
