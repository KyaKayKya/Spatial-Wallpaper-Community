"""Topic discovery and registration proofs; all merge operations are plans."""
import copy
import re
from datetime import date, timedelta

from .common import OFFICIAL, instant, require, utc_now, validate


def validate_manifest(repository, manifest):
    validate("manifest", manifest)
    require(not repository["private"], "private repository cannot be listed")
    require(manifest["repositoryId"] == repository["id"], "repository ID mismatch")
    ids, assets = set(), set()
    for work in manifest["works"]:
        require(work["id"] not in ids, "duplicate stable work ID")
        ids.add(work["id"])
        require(not assets.intersection(work["assets"]), "asset shared across works")
        assets.update(work["assets"])
    return copy.deepcopy(manifest)


def discover(search, resolve_id, get_manifest, previous, start="2008-01-01", end=None):
    """Abort incomplete scans; caller retains last snapshot. Refetch known IDs too."""
    end = end or date.today().isoformat()
    discovered = {}

    def scan(query, lower, upper):
        first = search(query, 1)
        require(not first["incomplete_results"], "incomplete search; retry without publishing")
        if first["total_count"] > 1000:
            lo, hi = date.fromisoformat(lower), date.fromisoformat(upper)
            require(lo < hi, "search cap exceeded in a single day; manual partition required")
            mid = lo + (hi - lo) // 2
            for a, b in [(lo, mid), (mid + timedelta(days=1), hi)]:
                scan("topic:gameai-wallpaper created:" + a.isoformat() + ".." + b.isoformat(), a.isoformat(), b.isoformat())
            return
        page, response, count = 1, first, 0
        while True:
            require(not response["incomplete_results"], "incomplete search page")
            for repo in response["items"]:
                discovered[repo["id"]] = repo
                count += 1
            if not response["has_next"]:
                break
            page += 1
            require(page <= 10, "search pagination exceeds cap")
            response = search(query, page)
        require(count >= first["total_count"], "missing search pages")

    scan("topic:gameai-wallpaper", start, end)
    result = {}
    for repository_id in sorted(set(discovered) | set(previous)):
        repo = resolve_id(repository_id)
        require(repo["id"] == repository_id, "resolved repository ID mismatch")
        if repository_id in previous:
            require(repo["owner"]["id"] == previous[repository_id]["repository"]["owner"]["id"], "owner changed; rebind required")
        manifest = validate_manifest(repo, get_manifest(repo))
        result[repository_id] = {"repository": repo, "manifest": manifest}
    return result


def registration_plan(pull, checked_head, current_head, files, registration, repository,
                      declaration=None, challenge=None, now=None):
    validate("registration", registration)
    require(pull["state"] == "open" and not pull["draft"], "PR must be open and ready")
    require(pull["base"]["repo"]["full_name"] == OFFICIAL and pull["base"]["ref"] == "main", "wrong registration base")
    require(re.fullmatch(r"[0-9a-f]{40}", checked_head) is not None, "invalid head SHA")
    require(checked_head == current_head == pull["head"]["sha"], "PR head changed; recheck all files")
    expected = "market/registry/" + str(registration["repositoryId"]) + ".json"
    require(len(files) == 1 and files[0]["filename"] == expected and files[0]["status"] == "added"
            and "previous_filename" not in files[0], "only one new exact registry path allowed")
    require(repository["id"] == registration["repositoryId"] and not repository["private"], "repository identity mismatch")
    require(repository["full_name"].lower() == registration["repository"].lower(), "repository path mismatch; resolve ID")
    actor = pull["user"]["id"]
    owner = repository["owner"]
    if owner["type"] != "User" or actor != owner["id"]:
        require(challenge is not None and declaration is not None, "default branch control proof required")
        require(not challenge["used"] and instant(challenge["expiresAt"]) > instant(now or utc_now()), "challenge expired or used")
        require(re.fullmatch(r"[0-9a-f]{64}", challenge["nonce"]) is not None, "invalid trusted challenge")
        required = {"repositoryId": repository["id"], "request": pull["number"], "actorId": actor, "nonce": challenge["nonce"]}
        require(all(challenge[k] == v for k, v in required.items()), "challenge binding mismatch")
        require(declaration["sourceRepositoryId"] == repository["id"] and declaration["sourceRef"] == repository["default_branch"], "proof not from target default branch")
        require(declaration["value"] == required, "declaration mismatch")
    return {"dry_run": True, "repository": OFFICIAL, "number": pull["number"], "sha": checked_head,
            "merge_method": "merge", "path": expected,
            "blockedBy": ["user-approved registration exception", "live head/path/control recheck", "effective protection rules"]}
