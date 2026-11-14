"""Governance plans only. This module has no GitHub write transport."""
import re

from .common import OFFICIAL, require


def product_build_required(paths):
    return not paths or any(re.fullmatch(r"market/registry/[1-9][0-9]*\.json", path) is None for path in paths)


def publish_plan(catalog_sha256, generation):
    require(re.fullmatch(r"[0-9a-f]{64}", catalog_sha256) is not None and type(generation) is int and generation > 0, "invalid candidate publication identity")
    return {"dry_run": True, "repository": OFFICIAL, "releaseTag": "market-candidate-" + str(generation),
            "makeLatest": False, "catalogSha256": catalog_sha256,
            "assets": ["catalog.json", "catalog.sig.json", "revocations.json", "stats.json"],
            "idempotencyKey": str(generation) + ":" + catalog_sha256,
            "blockedBy": ["trusted branch and head recheck", "protected signing environment", "immutable generation/digest check", "rollout review"]}
