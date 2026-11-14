"""Local-only key custody for official Community index publication.

Run from repository root with python -m tools.market.local_publication:
  collect --validator-config CONFIG --source-sha PUBLIC_SHA --output FRESH_COLLECTION
  sign --collection DIR --collection-sha256 PRINTED_HASH --dpapi LOCAL_BLOB --output FRESH_SIGNATURES
  upload --collection DIR --signatures DIR [--apply]

Only collect/upload read GITHUB_TOKEN. Sign runs without GitHub credentials as
its original Windows DPAPI user. No stage uploads secrets or changes Actions
variables. Only public index, signatures, validation reports and counters leave
this machine. Signing keys are never written as plaintext files.
"""
import argparse
import base64
import os
import re
import stat
import tempfile
from datetime import timedelta
from pathlib import Path

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from cryptography.exceptions import InvalidSignature

from . import publish, stats
from .common import ROOT, MarketError, canonical, instant, loads, require, utc_now
from .github import GitHub, JsonTransport, repository_path
from .deploy_community import unprotect_dpapi

COMMUNITY = "KyaKayKya/Spatial-Wallpaper-Community"
TRUST = ROOT / "Prototype/Assets/Library/Resources/Library/MarketTrust.json"
TRANSFER_DOMAIN = b"spatial-market-local-transfer-v1\0"
PUBLIC_FILES = {"published.json", "validation-reports.json", "stats.json"}
SIGNATURE_FILES = {"published.sig.json", "collection.sig.json"}
MAX_FILE = 8 * 1024 * 1024
MAX_AGE = timedelta(hours=2)


def no_links(path):
    path = Path(path).absolute()
    for item in [path, *path.parents]:
        if item.exists():
            flags = getattr(item.lstat(), "st_file_attributes", 0)
            require(not item.is_symlink() and not flags & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 1024), "local publication link refused")
    return path


def read_bytes(path):
    path = no_links(path)
    require(path.is_file() and 0 < path.stat().st_size <= MAX_FILE, "missing/oversized public file: " + path.name)
    raw = path.read_bytes()
    require(len(raw) <= MAX_FILE, "public file expanded during read")
    return raw


def fresh(path):
    path = no_links(path)
    path.mkdir(parents=True, exist_ok=False)
    no_links(path)
    return path


def write(path, raw):
    require(isinstance(raw, bytes) and 0 < len(raw) <= MAX_FILE, "public output size/type")
    with Path(path).open("xb") as output:
        output.write(raw)
        output.flush()
        os.fsync(output.fileno())


def trust(path):
    require(publish.MARKET_REPOSITORY == COMMUNITY, "local publisher is restricted to the official Community repository")
    value = loads(read_bytes(path))
    require(set(value) == {"repository", "catalogUrl", "signatureUrl", "keyId", "modulus", "exponent"}, "unexpected fixed trust configuration fields")
    base = "https://github.com/" + COMMUNITY + "/releases/download/market-index/"
    require(value["repository"] == COMMUNITY and value["catalogUrl"] == base + "published.json" and value["signatureUrl"] == base + "published.sig.json", "trust URLs must pin official Community releases")
    require(isinstance(value["keyId"], str) and re.fullmatch(r"[A-Za-z0-9._-]{1,64}", value["keyId"]), "invalid trusted key ID")
    try:
        modulus = base64.b64decode(value["modulus"], validate=True)
        exponent = base64.b64decode(value["exponent"], validate=True)
        key = rsa.RSAPublicNumbers(int.from_bytes(exponent, "big"), int.from_bytes(modulus, "big")).public_key()
    except (ValueError, TypeError) as error:
        raise MarketError("invalid fixed RSA public key") from error
    require(key.key_size >= 3072 and key.public_numbers().e == 65537, "trusted RSA key strength/exponent")
    return value, key, publish.digest(canonical(value))


class PublicIdentity:
    def __init__(self, key):
        self.key = key

    def public_key(self):
        return self.key


def previous(gh, key, key_id, now):
    # Existing audit verification needs only public_key(), never private material.
    return publish.live_previous(gh, PublicIdentity(key), key_id, now)


def check_source(gh, source):
    require(isinstance(source, str) and re.fullmatch(r"[0-9a-f]{40}", source), "exact public source SHA required")
    repository = gh.get(repository_path(COMMUNITY))[0]
    require(repository["full_name"] == COMMUNITY and repository["private"] is False, "official Community must remain public")
    commit = gh.get(repository_path(COMMUNITY) + "/commits/" + source)[0]
    require(commit["sha"] == source, "source commit absent from official public Community")


def check_reports(snapshot, reports):
    require(set(reports) == {"sourceCommit", "reports"} and isinstance(reports["reports"], list), "invalid collected validation reports")
    by_key = {}
    for report in reports["reports"]:
        require(isinstance(report, dict) and report.get("key") not in by_key, "duplicate collected admission report")
        by_key[report["key"]] = report
    require(set(by_key) == {entry["key"] for entry in snapshot["entries"]}, "collected admission report set differs from index")
    for entry in snapshot["entries"]:
        report = by_key[entry["key"]]
        if report.get("revoked") is True:
            require(entry["status"] == "revoked", "tombstone admission used for installable work")
        else:
            require(set(report) == {"key", "buildIdentity", "assetId", "report"}, "unknown admission report field")
            require(report["buildIdentity"] == entry["build"]["identity"] and report["assetId"] == entry["build"]["assets"][0]["id"], "collected report build/asset differs")
            publish.require_admission(report["report"], entry, entry["build"]["assets"][0])


def check_collection(directory, trust_path, now, expected=None):
    directory = no_links(directory)
    require(directory.is_dir() and {p.name for p in directory.iterdir()} == PUBLIC_FILES | {"collection.json"}, "collection must contain exactly the fixed public files")
    raw_manifest = read_bytes(directory / "collection.json")
    if expected is not None:
        require(re.fullmatch(r"[0-9a-f]{64}", expected) and publish.digest(raw_manifest) == expected, "collection manifest differs from reviewed collect hash")
    manifest = loads(raw_manifest)
    require(set(manifest) == {"schemaVersion", "kind", "repository", "sourceCommit", "collectedAt", "trustSha256", "previous", "files"}, "invalid collection fields")
    require(type(manifest["schemaVersion"]) is int and manifest["schemaVersion"] == 1 and manifest["kind"] == "local-market-collection" and manifest["repository"] == COMMUNITY, "invalid collection identity")
    require(re.fullmatch(r"[0-9a-f]{40}", manifest["sourceCommit"]), "invalid collection source SHA")
    config, key, fingerprint = trust(trust_path)
    require(manifest["trustSha256"] == fingerprint, "collection trust configuration changed")
    elapsed = instant(now) - instant(manifest["collectedAt"])
    require(timedelta(0) <= elapsed <= MAX_AGE, "collection stale or from future; collect again")
    require(set(manifest["files"]) == PUBLIC_FILES, "unexpected collection file inventory")
    files = {}
    for name in sorted(PUBLIC_FILES):
        raw = read_bytes(directory / name)
        record = manifest["files"][name]
        require(set(record) == {"size", "sha256"} and type(record["size"]) is int and record["size"] == len(raw) and record["sha256"] == publish.digest(raw), "collected public file digest mismatch: " + name)
        files[name] = raw
    snapshot = publish.validate_published(loads(files["published.json"]))
    require(snapshot["generatedAt"] == manifest["collectedAt"] and instant(now) < instant(snapshot["expiresAt"]) <= instant(snapshot["generatedAt"]) + timedelta(days=7), "invalid collected index timestamps")
    prior = manifest["previous"]
    require(prior is None or isinstance(prior, dict) and set(prior) == {"generation", "sha256"} and type(prior["generation"]) is int and prior["generation"] > 0 and re.fullmatch(r"[0-9a-f]{64}", prior["sha256"]), "invalid previous audit binding")
    require(snapshot["generation"] == (prior["generation"] + 1 if prior else 1), "collection generation does not extend prior audit")
    reports = loads(files["validation-reports.json"])
    require(reports.get("sourceCommit") == manifest["sourceCommit"], "validation source mismatch")
    check_reports(snapshot, reports)
    counters = loads(files["stats.json"])
    require(isinstance(counters, dict) and type(counters.get("schemaVersion")) is int and counters.get("schemaVersion") == 1 and counters.get("latestDay") == snapshot["generatedAt"][:10], "invalid collected statistics date/schema")
    ranks = publish.popularity_rows(counters, snapshot["generatedAt"][:10])
    for entry in snapshot["entries"]:
        require(entry.get("popularity") == {"downloads7d": ranks.get(entry["key"])}, "published popularity differs from collected statistics")
    return manifest, raw_manifest, files, snapshot, config, key


def collect_stage(args, gh=None, now=None):
    out = fresh(args.output)
    config, key, fingerprint = trust(args.trust)
    now = now or utc_now()
    gh = gh or GitHub(JsonTransport(os.environ.get("GITHUB_TOKEN")))
    check_source(gh, args.source_sha)
    prior, prior_stats = previous(gh, key, config["keyId"], now)
    candidate, observations, presentation = publish.collect(gh, prior, now, loads(read_bytes(ROOT / "market/revocations.json")))
    validator = loads(read_bytes(args.validator_config))
    require(set(validator) == {"validator", "validatorSha256", "platformIndex", "platformIndexSha256", "runtimeFiles"}, "unexpected trusted validator config fields")
    admission = publish.NativeAdmission(validator["validator"], validator["validatorSha256"], validator["platformIndex"], validator["platformIndexSha256"], validator["runtimeFiles"])
    details_path = args.details or (ROOT / "market/details.json" if (ROOT / "market/details.json").exists() else None)
    details = loads(read_bytes(details_path)) if details_path else {}
    with tempfile.TemporaryDirectory(prefix="market-local-admission-") as directory:
        # NativeAdmission constructs an explicit environment allowlist before the
        # trusted validator executes; neither GitHub tokens nor signing keys pass.
        snapshot, reports = publish.promote(candidate, gh.asset, admission, directory, {k: {**v, **details.get(k, {})} for k, v in presentation.items()}, prior)
    counters = stats.observe(prior_stats, now[:10], observations, {e["key"]: "revoked" if e["status"] == "revoked" else "installable" for e in snapshot["entries"]}, snapshot["revocations"]["builds"])
    ranks = publish.popularity_rows(counters, now[:10])
    for entry in snapshot["entries"]:
        entry["popularity"] = {"downloads7d": ranks.get(entry["key"])}
    publish.validate_previews(snapshot)
    publish.verify_live_bindings(gh, snapshot)
    files = {"published.json": publish.signed_bytes(snapshot), "validation-reports.json": canonical({"sourceCommit": args.source_sha, "reports": reports}) + b"\n", "stats.json": canonical(counters) + b"\n"}
    manifest = {"schemaVersion": 1, "kind": "local-market-collection", "repository": COMMUNITY, "sourceCommit": args.source_sha, "collectedAt": now, "trustSha256": fingerprint,
                "previous": {"generation": prior["generation"], "sha256": publish.digest(publish.signed_bytes(prior))} if prior else None,
                "files": {name: {"size": len(raw), "sha256": publish.digest(raw)} for name, raw in files.items()}}
    for name, raw in files.items():
        write(out / name, raw)
    write(out / "collection.json", canonical(manifest) + b"\n")
    check_collection(out, args.trust, now)
    return {"collected": True, "generation": snapshot["generation"], "works": len(snapshot["entries"]), "collectionSha256": publish.digest(read_bytes(out / "collection.json")), "output": str(out), "privateKeyAccessed": False, "uploaded": False}


def sign_stage(args, now=None):
    require(not os.environ.get("GITHUB_TOKEN"), "run offline signing without GitHub credentials")
    now = now or utc_now()
    manifest, raw_manifest, files, snapshot, config, public = check_collection(args.collection, args.trust, now, args.collection_sha256)
    out = fresh(args.output)
    plaintext = unprotect_dpapi(no_links(args.dpapi))
    try:
        key = serialization.load_pem_private_key(bytes(plaintext), password=None)
        require(isinstance(key, rsa.RSAPrivateKey) and key.public_key().public_numbers() == public.public_numbers(), "DPAPI signing key differs from fixed product public key")
        envelope = publish.sign_bytes(files["published.json"], key, config["keyId"])
        transfer = {"algorithm": publish.ALGORITHM, "keyId": config["keyId"], "sha256": publish.digest(raw_manifest), "signature": base64.b64encode(key.sign(TRANSFER_DOMAIN + raw_manifest, padding.PKCS1v15(), hashes.SHA256())).decode("ascii")}
        publish.verify_bytes(files["published.json"], envelope, {config["keyId"]: public}, now)
        write(out / "published.sig.json", canonical(envelope) + b"\n")
        write(out / "collection.sig.json", canonical(transfer) + b"\n")
    finally:
        for index in range(len(plaintext)):
            plaintext[index] = 0
    return {"signed": True, "output": str(out), "privateKeyWritten": False, "uploaded": False, "generation": snapshot["generation"]}


def verify_transfer(raw, envelope, key, key_id):
    require(set(envelope) == {"algorithm", "keyId", "sha256", "signature"} and envelope["algorithm"] == publish.ALGORITHM and envelope["keyId"] == key_id and envelope["sha256"] == publish.digest(raw), "invalid local transfer envelope")
    try:
        key.verify(base64.b64decode(envelope["signature"], validate=True), TRANSFER_DOMAIN + raw, padding.PKCS1v15(), hashes.SHA256())
    except (InvalidSignature, ValueError) as error:
        raise MarketError("invalid local transfer signature") from error


def upload_stage(args, gh=None, writer=None, now=None):
    now = now or utc_now()
    manifest, raw_manifest, files, snapshot, config, key = check_collection(args.collection, args.trust, now)
    signatures = no_links(args.signatures)
    require(signatures.is_dir() and {p.name for p in signatures.iterdir()} == SIGNATURE_FILES, "signature directory contains unexpected files")
    signed = {name: read_bytes(signatures / name) for name in SIGNATURE_FILES}
    publish.verify_bytes(files["published.json"], loads(signed["published.sig.json"]), {config["keyId"]: key}, now)
    verify_transfer(raw_manifest, loads(signed["collection.sig.json"]), key, config["keyId"])
    result = {"repository": COMMUNITY, "generation": snapshot["generation"], "sourceCommit": manifest["sourceCommit"], "sha256": publish.digest(files["published.json"]), "applied": False, "uploadsSecrets": False, "changesActionsVariables": False}
    if not args.apply:
        return result
    gh = gh or GitHub(JsonTransport(os.environ.get("GITHUB_TOKEN")))
    check_source(gh, manifest["sourceCommit"])
    prior, _ = previous(gh, key, config["keyId"], now)
    if prior and prior["generation"] == snapshot["generation"]:
        require(publish.signed_bytes(prior) == files["published.json"], "published generation already has different bytes")
    else:
        binding = {"generation": prior["generation"], "sha256": publish.digest(publish.signed_bytes(prior))} if prior else None
        require(manifest["previous"] == binding, "remote audit advanced since collection; collect again")
    publish.verify_live_bindings(gh, snapshot)
    files.update(signed)
    files["collection.json"] = raw_manifest
    (writer or publish.ReleaseWriter(os.environ.get("GITHUB_TOKEN"))).publish(snapshot, files, manifest["sourceCommit"])
    result["applied"] = True
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trust", type=Path, default=TRUST, help="Operator-selected fixed product public trust resource; never an index-supplied key")
    commands = parser.add_subparsers(dest="command", required=True)
    collect = commands.add_parser("collect", help="Admin: read GitHub and validate exact package bytes; never open DPAPI")
    collect.add_argument("--validator-config", type=Path, required=True)
    collect.add_argument("--source-sha", required=True)
    collect.add_argument("--details", type=Path)
    collect.add_argument("--output", type=Path, required=True)
    sign = commands.add_parser("sign", help="Original DPAPI user, no credentials/network: sign only into fresh public-signature directory")
    sign.add_argument("--collection", type=Path, required=True)
    sign.add_argument("--collection-sha256", required=True, help="Exact hash printed by the reviewed collect stage")
    sign.add_argument("--dpapi", type=Path, required=True)
    sign.add_argument("--output", type=Path, required=True)
    upload = commands.add_parser("upload", help="Admin: verify public signatures and audit; default local dry-run, --apply uploads only public assets")
    upload.add_argument("--collection", type=Path, required=True)
    upload.add_argument("--signatures", type=Path, required=True)
    upload.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    result = {"collect": collect_stage, "sign": sign_stage, "upload": upload_stage}[args.command](args)
    print(canonical(result).decode("utf-8"))


if __name__ == "__main__":
    main()
