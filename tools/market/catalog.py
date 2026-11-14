"""Exact Release metadata and signed candidate snapshots. No SDK admission."""
import base64
import copy
import hashlib
from datetime import timedelta
from urllib.parse import quote, urlsplit

from cryptography.exceptions import InvalidSignature

from .common import CHECKER, MarketError, canonical, instant, require, validate, work_key
from .registry import validate_manifest

CDN_HOSTS = {"release-assets.githubusercontent.com", "objects.githubusercontent.com"}


def require_work_bindings(entries, previous=()):
    """A local work/storage GUID retains its numeric repository, even revoked."""
    bindings = {}
    for entry in [*previous, *entries]:
        repository = bindings.setdefault(entry["workId"], entry["repositoryId"])
        require(repository == entry["repositoryId"], "work GUID belongs to another repository; explicit maintainer transfer required")


def asset_binding(asset):
    # URLs are display/transport metadata and change on repository rename.
    return {key: asset[key] for key in ["id", "name", "size", "sha256"]}


def asset_url(repository, tag, name):
    return ("https://github.com/" + repository["full_name"] + "/releases/download/"
            + quote(tag, safe="") + "/" + quote(name, safe=""))


def hash_asset(repository, release, asset, fetch_asset, max_bytes):
    expected = asset_url(repository, release["tag_name"], asset["name"])
    require(asset["browser_download_url"] == expected, "asset source is not exact repository Release")
    require(type(asset["id"]) is int and asset["id"] > 0, "invalid asset ID")
    require(type(asset["size"]) is int and 0 < asset["size"] <= max_bytes, "asset size exceeds budget")
    final_url, chunks = fetch_asset(asset)
    target = urlsplit(final_url)
    require(final_url == expected or (target.scheme == "https" and target.hostname in CDN_HOSTS
            and target.port in (None, 443) and target.username is None and target.password is None), "untrusted asset redirect")
    size, digest = 0, hashlib.sha256()
    for chunk in chunks:
        size += len(chunk)
        require(size <= min(max_bytes, asset["size"]), "transfer exceeds size budget")
        digest.update(chunk)
    require(size == asset["size"], "truncated attachment")
    return {"id": asset["id"], "name": asset["name"], "size": size,
            "sha256": digest.hexdigest(), "url": expected}


def build_snapshot(repository, manifest, releases, fetch_asset, previous, generation, now,
                   revocations, max_bytes=64 * 1024 * 1024):
    validate_manifest(repository, manifest)
    instant(now)
    if previous:
        validate("catalog", previous)
        require(generation > previous["generation"], "generation must advance")
    old = {entry["key"]: entry for entry in previous["entries"]} if previous else {}
    require_work_bindings([{"workId": work["id"], "repositoryId": repository["id"]} for work in manifest["works"]], old.values())
    entries = {k: copy.deepcopy(v) for k, v in old.items() if v["repositoryId"] != repository["id"]}
    for work in manifest["works"]:
        key = work_key(repository["id"], work["id"])
        prior = old.get(key)
        require(not prior or prior["ownerId"] == repository["owner"]["id"], "repository owner changed; rebind required")
        eligible = [r for r in releases if not r["draft"] and not r["prerelease"] and r["tag_name"] == work["releaseTag"]]
        require(len(eligible) == 1, "exact stable Release missing or ambiguous: " + key)
        release = eligible[0]
        selected = []
        for name in work["assets"]:
            matches = [a for a in release["assets"] if a["name"] == name]
            require(len(matches) == 1, "attachment missing/replaced or ambiguous: " + name)
            selected.append(matches[0])
        require(all(type(a["size"]) is int and a["size"] > 0 for a in selected), "invalid attachment size")
        require(sum(a["size"] for a in selected) <= max_bytes, "work transfer budget exceeded")
        assets, remaining = [], max_bytes
        for asset in selected:
            verified = hash_asset(repository, release, asset, fetch_asset, remaining)
            assets.append(verified)
            remaining -= verified["size"]
        require(len({a["id"] for a in assets}) == len(assets), "duplicate asset ID")
        require(sum(a["size"] for a in assets) <= max_bytes, "work transfer budget exceeded")
        build = {"version": work["version"], "releaseId": release["id"], "tag": release["tag_name"],
                 "checker": CHECKER, "assets": assets}
        binding = {**build, "assets": [asset_binding(a) for a in assets]}
        build["identity"] = hashlib.sha256(canonical({"repositoryId": repository["id"], "ownerId": repository["owner"]["id"], "workId": work["id"], **binding})).hexdigest()
        history = copy.deepcopy(prior["history"]) if prior else []
        for known in history:
            require(known["version"] != build["version"] or known["identity"] == build["identity"], "same version has different attachment identity/digest")
            for asset in known["assets"]:
                asset["url"] = asset_url(repository, known["tag"], asset["name"])
        if build not in history:
            history.append(build)
        entries[key] = {"key": key, "repositoryId": repository["id"], "ownerId": repository["owner"]["id"],
                        "repository": repository["full_name"], "workId": work["id"],
                        **{k: copy.deepcopy(work[k]) for k in ["title", "sdk", "capabilities", "attribution"]},
                        "status": "awaiting-sdk-validation", "firstListedAt": prior["firstListedAt"] if prior else now,
                        "build": build, "history": history}
    # Retain removed work records as revoked, never as silently installable/stale entries.
    for key, entry in old.items():
        if key not in entries:
            entries[key] = copy.deepcopy(entry)
            entries[key]["status"] = "revoked"
    for entry in entries.values():
        if entry["key"] in revocations["works"] or entry["build"]["identity"] in revocations["builds"]:
            entry["status"] = "revoked"
    snapshot = {"schemaVersion": 1, "kind": "candidate", "generation": generation, "generatedAt": now,
                "expiresAt": (instant(now) + timedelta(days=7)).strftime("%Y-%m-%dT%H:%M:%SZ"),
                "entries": [entries[k] for k in sorted(entries)], "revocations": copy.deepcopy(revocations)}
    validate("catalog", snapshot)
    return snapshot


def installable_entries(snapshot):
    validate("catalog", snapshot)
    # Candidate schema cannot represent successful SDK validation, even if signed.
    return []


def sign_snapshot(snapshot, private_key, key_id):
    validate("catalog", snapshot)
    data = canonical(snapshot)
    return {"algorithm": "Ed25519", "keyId": key_id, "sha256": hashlib.sha256(data).hexdigest(),
            "signature": base64.b64encode(private_key.sign(b"spatial-market-candidate-v1\0" + data)).decode("ascii")}


def verify_snapshot(snapshot, envelope, trusted_keys, watermark, now):
    validate("catalog", snapshot)
    require(envelope["algorithm"] == "Ed25519" and envelope["keyId"] in trusted_keys, "untrusted signing key")
    data = canonical(snapshot); digest = hashlib.sha256(data).hexdigest()
    require(digest == envelope["sha256"], "catalog digest mismatch")
    try:
        signature = base64.b64decode(envelope["signature"], validate=True)
        trusted_keys[envelope["keyId"]].verify(signature, b"spatial-market-candidate-v1\0" + data)
    except (InvalidSignature, ValueError) as error:
        raise MarketError("invalid catalog signature") from error
    require(instant(snapshot["generatedAt"]) <= instant(now) < instant(snapshot["expiresAt"]), "catalog expired or from future")
    if watermark:
        require(snapshot["generation"] >= watermark["generation"], "catalog rollback")
        require(snapshot["generation"] != watermark["generation"] or digest == watermark["sha256"], "generation equivocation")
    return {"generation": snapshot["generation"], "sha256": digest}
