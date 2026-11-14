"""Candidate tooling CLI; no GitHub mutation or automatic SDK admission commands."""
import argparse
import hashlib
import json
import os
from pathlib import Path

from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey, Ed25519PublicKey

from . import catalog, community, registry
from .common import ROOT, atomic_json, canonical, read_json, require
from .github import GitHub, JsonTransport
from .pipeline import run_fixture


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    dry = sub.add_parser("dry-run", help="replay inert offline GitHub responses")
    dry.add_argument("--fixture", type=Path, default=ROOT / "tests/market/data/github.json")
    dry.add_argument("--previous", type=Path)
    dry.add_argument("--now", required=True, help="UTC timestamp ending Z")
    dry.add_argument("--output", type=Path, required=True)
    discover = sub.add_parser("discover", help="read-only live topic scan, explicit registry seeds")
    discover.add_argument("--previous", type=Path)
    discover.add_argument("--output", type=Path, required=True)
    sign = sub.add_parser("sign", help="sign a candidate catalog using an explicitly supplied 32-byte Ed25519 key")
    sign.add_argument("--catalog", type=Path, required=True)
    sign.add_argument("--private-key", type=Path, required=True)
    sign.add_argument("--key-id", required=True)
    sign.add_argument("--output", type=Path, required=True)
    verify = sub.add_parser("verify", help="verify with externally trusted public keys and optional persisted watermark")
    verify.add_argument("--catalog", type=Path, required=True)
    verify.add_argument("--signature", type=Path, required=True)
    verify.add_argument("--trusted-keys", type=Path, required=True, help="key ID to hex public key JSON")
    verify.add_argument("--watermark", type=Path)
    verify.add_argument("--now", required=True)
    verify.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "dry-run":
        fixture = read_json(args.fixture)
        result = run_fixture(fixture, read_json(args.previous) if args.previous else None, args.now)
        # A throwaway test key exercises the same signing API. Never a product trust root.
        key = Ed25519PrivateKey.generate()
        envelope = catalog.sign_snapshot(result["catalog"], key, "ephemeral-dry-run")
        catalog.verify_snapshot(result["catalog"], envelope, {"ephemeral-dry-run": key.public_key()}, None, args.now)
        result["creditsPreview"] = community.render_credits((ROOT / "README.md").read_text(encoding="utf-8"),
                                                           result["catalog"]["entries"], {int(k): v for k, v in fixture["identities"].items()})
        atomic_json(args.output, result)
        print(json.dumps({"dryRun": True, "candidateWorks": len(result["catalog"]["entries"]), "installableWorks": 0,
                          "catalogSha256": hashlib.sha256(canonical(result["catalog"])).hexdigest(),
                          "signatureRoundTrip": "ephemeral-test-key-only", "output": str(args.output)}, ensure_ascii=False))
    elif args.command == "discover":
        gh = GitHub(JsonTransport(token=os.environ.get("GITHUB_TOKEN")))
        previous = {int(k): v for k, v in read_json(args.previous).items()} if args.previous else {}
        for path in sorted((ROOT / "market/registry").glob("*.json")):
            registration = read_json(path)
            from .common import validate
            validate("registration", registration)
            require(path.name == str(registration["repositoryId"]) + ".json", "registration filename mismatch")
            repo = gh.resolve(registration["repositoryId"])
            require(repo["full_name"].lower() == registration["repository"].lower(), "registration path stale; verify rename")
            previous.setdefault(repo["id"], {"repository": repo, "manifest": gh.manifest(repo)})
        result = registry.discover(gh.search, gh.resolve, gh.manifest, previous)
        atomic_json(args.output, {str(k): v for k, v in result.items()})
        print("Read-only discovery: " + str(len(result)) + " repositories; no attachments executed or published")
    elif args.command == "sign":
        result = catalog.sign_snapshot(read_json(args.catalog), Ed25519PrivateKey.from_private_bytes(args.private_key.read_bytes()), args.key_id)
        atomic_json(args.output, result)
    elif args.command == "verify":
        trusted = {key_id: Ed25519PublicKey.from_public_bytes(bytes.fromhex(raw)) for key_id, raw in read_json(args.trusted_keys).items()}
        result = catalog.verify_snapshot(read_json(args.catalog), read_json(args.signature), trusted,
                                         read_json(args.watermark) if args.watermark else None, args.now)
        atomic_json(args.output, result)
        print("Verified candidate catalog; SDK admission remains unavailable")


if __name__ == "__main__":
    main()
