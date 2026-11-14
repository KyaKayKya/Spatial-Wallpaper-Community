import copy
import unittest

from fixtures import manifest, pr, repo
from tools.market import registry
from tools.market.common import MarketError, canonical, loads


class RegistryTests(unittest.TestCase):
    def test_no_consent_and_claimed_identity_rejected(self):
        for field, value in [("listingConsent", False), ("repositoryId", 102)]:
            m = manifest(); m[field] = value
            with self.assertRaises(MarketError):
                registry.validate_manifest(repo(), m)

    def test_multiple_stable_works_and_duplicate_rejection(self):
        m = manifest(); m["works"].append(dict(m["works"][0], id="sun", assets=["sun.zip"]))
        self.assertEqual(len(registry.validate_manifest(repo(), m)["works"]), 2)
        m["works"][1]["id"] = "rain"
        with self.assertRaises(MarketError): registry.validate_manifest(repo(), m)

    def test_external_sources_unknown_fields_and_shared_assets_rejected(self):
        for patch in [{"downloadUrl": "https://evil.test/a"}, {"assets": ["../rain.zip"]}]:
            m = manifest(); m["works"][0].update(patch)
            with self.assertRaises(MarketError): registry.validate_manifest(repo(), m)
        m = manifest(); m["works"].append(dict(m["works"][0], id="sun"))
        with self.assertRaises(MarketError): registry.validate_manifest(repo(), m)

    def test_discovery_paginates_deduplicates_keeps_missing_and_renames(self):
        calls = []
        renamed = repo(); renamed["full_name"] = "alice/renamed"
        def search(query, page):
            calls.append(page)
            return {"items": [repo()] if page == 1 else [renamed], "total_count": 2,
                    "incomplete_results": False, "has_next": page == 1}
        old = {202: {"repository": dict(repo(), id=202), "manifest": dict(manifest(), repositoryId=202)}}
        result = registry.discover(search, lambda rid: renamed if rid == 101 else old[rid]["repository"],
                                   lambda r: manifest() if r["id"] == 101 else old[202]["manifest"], old)
        self.assertEqual(calls, [1, 2])
        self.assertEqual(set(result), {101, 202})
        self.assertEqual(result[101]["repository"]["full_name"], "alice/renamed")

    def test_discovery_failure_does_not_mutate_previous(self):
        old = {101: {"repository": repo(), "manifest": manifest()}}; before = copy.deepcopy(old)
        for response in [{"items": [], "total_count": 0, "incomplete_results": True, "has_next": False}]:
            with self.assertRaises(MarketError):
                registry.discover(lambda q, p: response, lambda _: repo(), lambda _: manifest(), old)
        self.assertEqual(old, before)
        def failed(q, p): raise MarketError("rate limited; retry after 60")
        with self.assertRaisesRegex(MarketError, "retry after"):
            registry.discover(failed, lambda _: repo(), lambda _: manifest(), old)

    def test_search_over_cap_is_partitioned(self):
        queries = []
        def search(q, p):
            queries.append(q)
            if "created:" not in q:
                return {"total_count": 1001, "items": [], "incomplete_results": False, "has_next": False}
            return {"total_count": 0, "items": [], "incomplete_results": False, "has_next": False}
        self.assertEqual(registry.discover(search, None, None, {}, start="2026-10-01", end="2026-10-04"), {})
        self.assertEqual(len(queries), 3)

    def test_transfer_requires_rebinding(self):
        old = {101: {"repository": repo(), "manifest": manifest()}}
        moved = repo(); moved["owner"]["id"] = 12
        with self.assertRaisesRegex(MarketError, "owner"):
            registry.discover(lambda q, p: {"items": [], "total_count": 0, "incomplete_results": False, "has_next": False},
                              lambda _: moved, lambda _: manifest(), old)

    def plan(self, **kwargs):
        defaults = dict(pull=pr(), checked_head="a" * 40, current_head="a" * 40,
                        files=[{"filename": "market/registry/101.json", "status": "added"}],
                        registration={"schemaVersion": 1, "repositoryId": 101, "repository": "alice/windows"},
                        repository=repo(), declaration=None, challenge=None)
        defaults.update(kwargs)
        return registry.registration_plan(**defaults)

    def test_registration_is_bound_to_head_path_base_and_numeric_owner(self):
        self.assertEqual(self.plan()["merge_method"], "merge")
        self.assertTrue(self.plan()["dry_run"])
        for patch in [dict(current_head="b" * 40), dict(files=[]),
                      dict(files=[{"filename": ".github/workflows/pwn.yml", "status": "added"}]),
                      dict(files=[{"filename": "market/registry/../pwn.json", "status": "added"}]),
                      dict(files=[{"filename": "market/registry/101.json", "status": "renamed", "previous_filename": "a.py"}]),
                      dict(pull=dict(pr(), user={"id": 13})),
                      dict(pull=dict(pr(), base={"ref": "dev/v0.0", "repo": {"full_name": "KyaKayKya/Spatial-Wallpaper"}}))]:
            with self.subTest(patch=patch), self.assertRaises(MarketError): self.plan(**patch)

    def test_org_claim_needs_trusted_default_branch_one_time_challenge(self):
        r = repo(); r["owner"]["type"] = "Organization"
        with self.assertRaises(MarketError): self.plan(repository=r)
        challenge = {"repositoryId": 101, "request": 9, "actorId": 11, "nonce": "f" * 64,
                     "expiresAt": "2026-10-27T00:00:00Z", "used": False}
        declaration = {"sourceRepositoryId": 101, "sourceRef": "main", "value": {k: challenge[k] for k in ["repositoryId", "request", "actorId", "nonce"]}}
        self.assertTrue(self.plan(repository=r, challenge=challenge, declaration=declaration, now="2026-10-26T00:00:00Z")["dry_run"])
        for change in [dict(used=True), dict(expiresAt="2026-10-25T00:00:00Z"), dict(actorId=12)]:
            with self.assertRaises(MarketError):
                self.plan(repository=r, challenge=dict(challenge, **change), declaration=declaration, now="2026-10-26T00:00:00Z")

    def test_strict_json_and_canonical_profile(self):
        for raw in ['{"a":1,"a":2}', '{"a":NaN}', '{"a":1.2}']:
            with self.assertRaises(MarketError): loads(raw)
        self.assertEqual(canonical({"b": 2, "a": "雨"}), '{"a":"雨","b":2}'.encode())


if __name__ == "__main__": unittest.main()
