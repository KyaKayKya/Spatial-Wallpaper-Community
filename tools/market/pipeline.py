"""Transactional offline integration of the SDK-independent market stages."""
import base64
import copy
import hashlib
from datetime import timedelta

from . import catalog, community, registry, stats
from .common import canonical, instant, require, validate
from .workflow import publish_plan


def run_fixture(data, previous, now):
    """No filesystem/network mutations. Publish only a complete successful result."""
    instant(now)
    input_digest = hashlib.sha256(canonical(data)).hexdigest()
    if previous:
        validate("catalog", previous["catalog"])
        if previous["inputSha256"] == input_digest and previous["observedAt"] == now:
            return copy.deepcopy(previous)
    records = {record["repository"]["id"]: record for record in data["repositories"]}
    require(len(records) == len(data["repositories"]), "duplicate repository fixture")
    old_registry = {int(k): v for k, v in previous["registry"].items()} if previous else {}
    seeds = copy.deepcopy(old_registry)
    registration_plans = []
    for request in data["registrations"]:
        request = copy.deepcopy(request)
        record = records[request.pop("repositoryId")]
        plan = registry.registration_plan(**request, repository=record["repository"], now=now)
        registration_plans.append(plan)
        rid = record["repository"]["id"]
        seeds.setdefault(rid, {"repository": record["repository"], "manifest": record["manifest"]})
    candidates = registry.discover(lambda q, p: data["searchPages"][p - 1],
                                   lambda rid: records[rid]["repository"],
                                   lambda r: records[r["id"]]["manifest"], seeds)
    prior_catalog = previous["catalog"] if previous else None
    generation = prior_catalog["generation"] + 1 if prior_catalog else 1
    entries = {}
    for rid, candidate in candidates.items():
        record = records[rid]
        def fetch(asset):
            body = base64.b64decode(record["attachments"][str(asset["id"])], validate=True)
            return asset["browser_download_url"], [body]
        snapshot = catalog.build_snapshot(candidate["repository"], candidate["manifest"], record["releases"],
                                          fetch, prior_catalog, generation, now, data["revocations"])
        for entry in snapshot["entries"]:
            if entry["repositoryId"] == rid:
                entries[entry["key"]] = entry
    snapshot = {"schemaVersion": 1, "kind": "candidate", "generation": generation, "generatedAt": now,
                "expiresAt": (instant(now) + timedelta(days=7)).strftime("%Y-%m-%dT%H:%M:%SZ"),
                "entries": [entries[k] for k in sorted(entries)], "revocations": copy.deepcopy(data["revocations"])}
    validate("catalog", snapshot)
    observations, asset_keys = [], set()
    for entry in snapshot["entries"]:
        record = records[entry["repositoryId"]]
        for build in entry["history"]:
            if build["identity"] in data["revocations"]["builds"]:
                continue
            matched_releases = [r for r in record["releases"] if r["id"] == build["releaseId"] and not r["draft"] and not r["prerelease"]]
            if not matched_releases:
                continue  # Deleted historical Release: retain its previous counter history.
            require(len(matched_releases) == 1, "duplicate Release ID")
            release = matched_releases[0]
            for expected in build["assets"]:
                matches = [a for a in release["assets"] if a["id"] == expected["id"]]
                if not matches:
                    continue  # Deleted asset is not a zero sample.
                require(len(matches) == 1, "duplicate Release asset")
                asset = matches[0]
                def fetch_history(a):
                    return a["browser_download_url"], [base64.b64decode(record["attachments"][str(a["id"])], validate=True)]
                actual = catalog.hash_asset(record["repository"], release, asset, fetch_history, 64 * 1024 * 1024)
                require(catalog.asset_binding(actual) == catalog.asset_binding(expected),
                        "historical asset replaced; cannot reuse previous check")
                asset_key = (entry["repositoryId"], asset["id"])
                require(asset_key not in asset_keys, "duplicate historical asset reference")
                asset_keys.add(asset_key)
                observations.append({"repositoryId": entry["repositoryId"], "assetId": asset["id"], "work": entry["key"],
                                     "buildId": build["identity"],
                                     "downloadCount": asset["download_count"], "status": entry["status"],
                                     "volumeGroup": build["identity"] if len(build["assets"]) > 1 else None})
    stat_state = stats.observe(previous["stats"] if previous else None, now[:10], observations,
                               {e["key"]: e["status"] for e in snapshot["entries"]}, data["revocations"]["builds"])
    old_mapping = previous["community"]["mapping"] if previous else {}
    discussions = community.discussion_plan(snapshot["entries"], old_mapping, data["discussions"], data["categoryId"], data["discussionActorId"])
    return {"dryRun": True, "inputSha256": input_digest, "observedAt": now,
            "publicationPlan": publish_plan(hashlib.sha256(canonical(snapshot)).hexdigest(), generation),
            "registry": {str(k): v for k, v in candidates.items()}, "catalog": snapshot, "stats": stat_state,
            "registrationPlans": registration_plans, "community": discussions,
            "technicalPopular7": stats.rank(stat_state, now[:10], 7, public=False),
            "technicalPopular30": stats.rank(stat_state, now[:10], 30, public=False),
            "publicPopular7": stats.rank(stat_state, now[:10], 7),
            "publicPopular30": stats.rank(stat_state, now[:10], 30),
            "featured": stats.featured(data["featured"], stat_state)}
