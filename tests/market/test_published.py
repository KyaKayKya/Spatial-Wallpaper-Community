import base64
import copy
import hashlib
import tempfile
import unittest
from pathlib import Path
from cryptography.hazmat.primitives.asymmetric import rsa
from tools.market import catalog, publish
from tools.market.common import ROOT, MarketError, canonical
from fixtures import manifest, releases, repo

NOW = "2026-11-14T00:00:00Z"
WORK = "11111111-2222-4333-8444-555555555555"
BODY = b"inert package fixture; trusted admission callback is injected only in tests"


def candidate():
    m, r = manifest(), releases()
    m["works"][0].update(id=WORK, sdk=publish.SDK, capabilities=["view-data"], assets=["rain.spatial"])
    r[0]["assets"][0].update(name="rain.spatial", size=len(BODY), browser_download_url="https://github.com/alice/windows/releases/download/v1/rain.spatial")
    return catalog.build_snapshot(repo(), m, r, lambda a: (a["browser_download_url"], [BODY]), None, 1, NOW, {"works": [], "builds": []})


def report(path):
    return {"valid": True, "sha256": hashlib.sha256(Path(path).read_bytes()).hexdigest(), "schema": 5,
            "sdk": publish.SDK, "command_abi": 1, "policy": "native-schema5-platform-index-v1",
            "build_id": "b" * 64, "work_id": WORK, "revision": "1.0.0", "license_share": True,
            "capabilities": ["view-data"], "license_records": [{"license": "CC0-1.0", "redistribution": "verified"}]}


def promote(value=None, mutate=None):
    value = value or candidate()
    def admission(path):
        result = report(path)
        if mutate:
            mutate(result)
        return result
    with tempfile.TemporaryDirectory() as directory:
        return publish.promote(value, lambda a: (a["browser_download_url"], [BODY]), admission, directory)


class PublishedTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.key = rsa.generate_private_key(public_exponent=65537, key_size=3072)

    def test_actual_bytes_and_admission_build_binding(self):
        snapshot, reports = promote()
        entry = snapshot["entries"][0]
        self.assertEqual(snapshot["kind"], "published")
        self.assertEqual(entry["status"], "published")
        self.assertEqual(entry["admission"]["buildIdentity"], entry["build"]["identity"])
        self.assertEqual(entry["admission"]["packageAssetId"], 401)
        self.assertEqual(reports[0]["report"]["sha256"], hashlib.sha256(BODY).hexdigest())

    def test_raw_utf8_signature_not_parsed_json_signature(self):
        snapshot, _ = promote()
        raw = publish.signed_bytes(snapshot)
        envelope = publish.sign_bytes(raw, self.key, "test")
        self.assertEqual(envelope["algorithm"], "RSA-PKCS1-SHA256")
        self.assertEqual(publish.verify_bytes(raw, envelope, {"test": self.key.public_key()}, NOW), snapshot)
        for changed in (raw[:-1], b" " + raw, raw.replace(b"published", b"revoked", 1)):
            with self.assertRaises(MarketError):
                publish.verify_bytes(changed, envelope, {"test": self.key.public_key()}, NOW)
        with self.assertRaises(MarketError):
            publish.verify_bytes(raw, envelope, {}, NOW)

    def test_replay_and_expired_previous_are_distinct_from_client_acceptance(self):
        snapshot, _ = promote()
        raw = publish.signed_bytes(snapshot)
        envelope = publish.sign_bytes(raw, self.key, "test")
        for watermark in ({"generation": 2, "sha256": "f" * 64}, {"generation": 1, "sha256": "f" * 64}):
            with self.assertRaises(MarketError):
                publish.verify_bytes(raw, envelope, {"test": self.key.public_key()}, NOW, watermark)
        with self.assertRaises(MarketError):
            publish.verify_bytes(raw, envelope, {"test": self.key.public_key()}, "2027-01-01T00:00:00Z")
        publish.verify_bytes(raw, envelope, {"test": self.key.public_key()}, "2027-01-01T00:00:00Z", allow_expired=True)

    def test_reports_cannot_claim_pass_for_wrong_bytes_or_identity(self):
        for key, wrong in (("sha256", "0" * 64), ("schema", 4), ("sdk", "old"), ("command_abi", 2),
                           ("work_id", "22222222-2222-4333-8444-555555555555"), ("revision", "1.0.1"),
                           ("license_share", False), ("capabilities", ["private-storage"]),
                           ("license_records", [{"license": "proprietary", "redistribution": "verified"}])):
            with self.subTest(key=key), self.assertRaises(MarketError):
                promote(mutate=lambda result: result.update({key: wrong}))

    def test_no_renamed_legacy_or_multiple_assets(self):
        value = candidate()
        value["entries"][0]["build"]["assets"].append(copy.deepcopy(value["entries"][0]["build"]["assets"][0]))
        with self.assertRaises(MarketError):
            promote(value)
        value = candidate()
        value["entries"][0]["build"]["assets"][0]["name"] = "rain.zip"
        with self.assertRaises(MarketError):
            promote(value)

    def test_download_must_match_collected_release_bytes(self):
        with tempfile.TemporaryDirectory() as directory, self.assertRaises(MarketError):
            publish.promote(candidate(), lambda a: (a["browser_download_url"], [BODY[:-1]]), report, directory)

    def test_revoked_previous_does_not_need_deleted_release_download(self):
        previous, _ = promote()
        value = publish.candidate_previous(previous)
        value["entries"][0]["status"] = "revoked"
        def forbidden(*args):
            self.fail("revoked tombstone must not download/execute")
        with tempfile.TemporaryDirectory() as directory:
            result, reports = publish.promote(value, forbidden, forbidden, directory, previous=previous)
        self.assertEqual(result["entries"][0]["status"], "revoked")
        self.assertEqual(result["entries"][0]["admission"], previous["entries"][0]["admission"])

    def test_comments_only_exact_configured_market_issue(self):
        value, _ = promote()
        value["entries"][0]["comments"] = {"kind": "issues", "url": "https://github.com/" + publish.MARKET_REPOSITORY + "/issues/12", "number": 12}
        publish.validate_published(value)
        value["entries"][0]["comments"]["url"] = "https://github.com/attacker/repo/issues/12"
        with self.assertRaises(MarketError):
            publish.validate_published(value)

    def test_workflow_privilege_separation_and_base_checkout(self):
        import yaml
        check = yaml.load((ROOT / ".github/workflows/market-check.yml").read_text(), Loader=yaml.BaseLoader)
        release = yaml.load((ROOT / ".github/workflows/market-publish.yml").read_text(), Loader=yaml.BaseLoader)
        self.assertEqual(check["permissions"], {})
        job = check["jobs"]["check"]
        self.assertEqual(job["permissions"], {"contents": "read", "pull-requests": "read"})
        self.assertIn("base.sha", job["steps"][0]["with"]["ref"])
        self.assertNotIn("head.sha", job["steps"][0]["with"]["ref"])
        self.assertNotIn("secrets.", str(check))
        self.assertEqual(release["jobs"]["publish"]["environment"], "market-publish")
        self.assertNotIn("pull_request_target", release["on"])
        for workflow in (check, release):
            for job in workflow["jobs"].values():
                self.assertEqual(job["runs-on"], "windows-latest")
                for step in job["steps"]:
                    if "uses" in step:
                        self.assertRegex(step["uses"], r"@[0-9a-f]{40}$")
                    self.assertNotIn("$" + "{{", step.get("run", ""))

    def test_first_lifetime_sample_never_becomes_seven_day_popularity(self):
        from tools.market import stats
        state = None
        for day in range(14, 21):
            state = stats.observe(state, "2026-11-" + str(day), [{"repositoryId": 101, "assetId": 401, "work": "101:" + WORK,
                "buildId": "b" * 64, "downloadCount": 10000 + day - 14, "status": "installable", "volumeGroup": None}])
            if day < 20:
                self.assertEqual(publish.popularity_rows(state, "2026-11-" + str(day)), {})
        self.assertEqual(publish.popularity_rows(state, "2026-11-20")["101:" + WORK], 6)

    def test_pr_api_read_never_needs_contributor_checkout(self):
        m, r = manifest(), releases()
        m["works"][0].update(id=WORK, sdk=publish.SDK, capabilities=["view-data"], assets=["rain.spatial"])
        r[0]["assets"][0].update(name="rain.spatial", size=len(BODY), browser_download_url="https://github.com/alice/windows/releases/download/v1/rain.spatial")
        pull = {"state": "open", "draft": False, "head": {"sha": "a" * 40, "repo": {"full_name": "alice/market-fork"}},
                "base": {"repo": {"full_name": publish.MARKET_REPOSITORY}, "ref": "dev/v0.0"}, "user": {"id": 11}, "changed_files": 1}
        files = [{"filename": "market/registry/101.json", "status": "added"}]
        class Fake:
            def get(self, path): return copy.deepcopy(pull), {}
            def pages(self, path): return files
            def file_at(self, repository, filename, head):
                self.filename = filename
                return {"schemaVersion": 1, "repositoryId": 101, "repository": "alice/windows"}
            def resolve(self, rid): return repo()
            def manifest(self, repository): return m
            def releases(self, repository): return r
            def asset(self, asset): return asset["browser_download_url"], [BODY]
        fake = Fake()
        self.assertEqual(publish.check_pr(fake, 1, "a" * 40, NOW)["repositoryId"], 101)
        for filename in ("tools/market/pwn.py", "market/registry/../pwn.json"):
            files[0]["filename"] = filename
            with self.assertRaises(MarketError):
                publish.check_pr(fake, 1, "a" * 40, NOW)
        files[0]["filename"] = "market/registry/101.json"
        pull["head"]["sha"] = "b" * 40
        with self.assertRaises(MarketError):
            publish.check_pr(fake, 1, "a" * 40, NOW)

    def test_preview_origin_png_and_budget(self):
        import struct
        value, _ = promote()
        value["entries"][0]["preview"] = "https://raw.githubusercontent.com/alice/windows/" + "a" * 40 + "/preview.png"
        image = bytes((137, 80, 78, 71, 13, 10, 26, 10)) + struct.pack(">I", 13) + b"IHDR" + struct.pack(">II", 2048, 512) + b"\x08\x06\x00\x00\x00" + b"crc!"
        publish.validate_previews(value, lambda url: image)
        for bad in (image[:20], image[:16] + struct.pack(">II", 2049, 512) + image[24:], b"bad" * 800000):
            with self.assertRaises(MarketError):
                publish.validate_previews(value, lambda url: bad)
        value["entries"][0]["preview"] = "https://raw.githubusercontent.com/alice/windows/main/preview.png"
        with self.assertRaises(MarketError):
            publish.validate_previews(value, lambda url: image)

    def test_draft_audit_completes_before_stable_pair_is_replaced(self):
        value, _ = promote()
        events = []
        class Writer(publish.ReleaseWriter):
            def __init__(self): pass
            def release(self, tag, source_sha, draft=False):
                events.append(("release", tag, draft))
                return {"id": 1 if draft else 2, "draft": draft, "assets": []}
            def upload(self, release, name, data, replace=False):
                events.append(("upload", release["id"], name, replace))
            def request(self, method, path, body=None, raw=False):
                events.append((method, path, body))
        Writer().publish(value, {"published.json": b"data", "published.sig.json": b"sig", "stats.json": b"stats"}, "a" * 40)
        patch = next(i for i, e in enumerate(events) if e[0] == "PATCH")
        stable = next(i for i, e in enumerate(events) if e[:2] == ("release", publish.INDEX_TAG))
        self.assertLess(patch, stable)
        self.assertEqual(events[0], ("release", "market-published-1", True))

    def test_source_asset_replacement_after_admission_rejected(self):
        value, _ = promote()
        class Fake:
            def resolve(self, rid): return repo()
            def releases(self, name): return releases()
        with self.assertRaises(MarketError):
            publish.verify_live_bindings(Fake(), value)

if __name__ == "__main__":
    unittest.main()
