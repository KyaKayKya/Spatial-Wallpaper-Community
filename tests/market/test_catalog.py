import copy
import hashlib
import tempfile
import unittest
from pathlib import Path

from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey

from fixtures import manifest, releases, repo
from tools.market import catalog
from tools.market.common import MarketError, atomic_json, read_json

NOW = "2026-10-26T00:00:00Z"


def fetch(asset):
    return asset["browser_download_url"], [b"sa", b"fe"]


class CatalogTests(unittest.TestCase):
    def test_rename_preserves_stable_build_identity(self):
        old = self.build(); renamed = repo(); renamed["full_name"] = "alice/renamed"
        rs = releases(); rs[0]["assets"][0]["browser_download_url"] = "https://github.com/alice/renamed/releases/download/v1/rain.zip"
        result = self.build(repository=renamed, releases=rs, previous=old, generation=2)
        self.assertEqual(result["entries"][0]["build"]["identity"], old["entries"][0]["build"]["identity"])
        self.assertEqual(len(result["entries"][0]["history"]), 1)
        self.assertIn("alice/renamed", result["entries"][0]["build"]["assets"][0]["url"])

    def test_total_budget_is_checked_before_any_download(self):
        m = manifest(); m["works"][0]["assets"].append("rain.part2")
        rs = releases(); rs[0]["assets"].append(dict(rs[0]["assets"][0], id=402, name="rain.part2", browser_download_url="https://github.com/alice/windows/releases/download/v1/rain.part2"))
        calls = []
        def transfer(asset): calls.append(asset["id"]); return fetch(asset)
        with self.assertRaises(MarketError): self.build(manifest=m, releases=rs, fetch_asset=transfer, max_bytes=4)
        self.assertEqual(calls, [])

    def build(self, **kwargs):
        args = dict(repository=repo(), manifest=manifest(), releases=releases(), fetch_asset=fetch,
                    previous=None, generation=1, now=NOW, revocations={"works": [], "builds": []})
        args.update(kwargs)
        return catalog.build_snapshot(**args)

    def test_metadata_and_actual_bytes_never_admit_content(self):
        snapshot = self.build(); entry = snapshot["entries"][0]
        self.assertEqual(entry["status"], "awaiting-sdk-validation")
        self.assertEqual(entry["build"]["assets"][0]["sha256"], hashlib.sha256(b"safe").hexdigest())
        self.assertEqual(entry["build"]["checker"], "market-metadata/1")
        self.assertEqual(catalog.installable_entries(snapshot), [])
        self.assertEqual((entry["repositoryId"], entry["build"]["releaseId"], entry["build"]["assets"][0]["id"]), (101, 301, 401))

    def test_no_release_and_draft_or_prerelease_fail(self):
        for rs in [[], [dict(releases()[0], draft=True)], [dict(releases()[0], prerelease=True)]]:
            with self.subTest(rs=rs), self.assertRaises(MarketError): self.build(releases=rs)

    def test_source_redirect_size_and_actual_transfer_budget(self):
        bad = releases(); bad[0]["assets"][0]["browser_download_url"] = "https://evil.test/rain.zip"
        with self.assertRaises(MarketError): self.build(releases=bad)
        for fn in [lambda a: ("https://github.com.evil.test/a", [b"safe"]),
                   lambda a: (a["browser_download_url"], [b"safe-extra"]),
                   lambda a: (a["browser_download_url"], [b"saf"])]:
            with self.assertRaises(MarketError): self.build(fetch_asset=fn)
        with self.assertRaises(MarketError): self.build(max_bytes=3)

    def test_same_version_replacement_and_removed_asset_fail_without_mutation(self):
        prior = self.build(); before = copy.deepcopy(prior)
        for fn, rs in [(lambda a: (a["browser_download_url"], [b"evil"]), releases()),
                       (fetch, [dict(releases()[0], assets=[])]),
                       (fetch, [dict(releases()[0], assets=[dict(releases()[0]["assets"][0], id=402)])])]:
            with self.assertRaises(MarketError): self.build(previous=prior, generation=2, releases=rs, fetch_asset=fn)
        self.assertEqual(prior, before)

    def test_good_update_preserves_listing_and_version_history(self):
        old = self.build(); m = manifest(); m["works"][0].update(version="2.0", releaseTag="v2")
        rs = releases(); rs[0].update(id=302, tag_name="v2"); rs[0]["assets"][0].update(id=402, browser_download_url="https://github.com/alice/windows/releases/download/v2/rain.zip")
        new = self.build(previous=old, generation=2, manifest=m, releases=rs, now="2026-10-27T00:00:00Z")
        self.assertEqual(new["entries"][0]["firstListedAt"], NOW)
        self.assertEqual(len(new["entries"][0]["history"]), 2)
        self.assertEqual(new["entries"][0]["status"], "awaiting-sdk-validation")
        # Historical version cannot be reused with a different digest either.
        with self.assertRaises(MarketError):
            self.build(previous=new, generation=3, fetch_asset=lambda a: (a["browser_download_url"], [b"evil"]))

    def test_multiple_works_and_revocations(self):
        m = manifest(); m["works"].append(dict(m["works"][0], id="sun", assets=["sun.zip"]))
        rs = releases(); rs[0]["assets"].append(dict(rs[0]["assets"][0], id=402, name="sun.zip", browser_download_url="https://github.com/alice/windows/releases/download/v1/sun.zip"))
        result = self.build(manifest=m, releases=rs, revocations={"works": ["101:rain"], "builds": []})
        self.assertEqual([x["status"] for x in result["entries"]], ["revoked", "awaiting-sdk-validation"])

    def test_sign_verify_tamper_rollback_equivocation_and_rotation(self):
        key, next_key = Ed25519PrivateKey.generate(), Ed25519PrivateKey.generate()
        roots = {"test": key.public_key(), "next": next_key.public_key()}
        snapshot = self.build(); envelope = catalog.sign_snapshot(snapshot, key, "test")
        watermark = catalog.verify_snapshot(snapshot, envelope, roots, None, NOW)
        self.assertEqual(watermark["generation"], 1)
        self.assertEqual(catalog.verify_snapshot(snapshot, envelope, roots, watermark, NOW), watermark)
        modified = copy.deepcopy(snapshot); modified["entries"][0]["status"] = "installable"
        with self.assertRaises(MarketError): catalog.verify_snapshot(modified, envelope, roots, None, NOW)
        # Stay inside the schema and update the untrusted digest: only the signature rejects this.
        from tools.market.common import canonical
        modified = copy.deepcopy(snapshot); modified["entries"][0]["title"] = "tampered"
        forged = dict(envelope, sha256=hashlib.sha256(canonical(modified)).hexdigest())
        with self.assertRaisesRegex(MarketError, "signature"):
            catalog.verify_snapshot(modified, forged, roots, None, NOW)
        with self.assertRaises(MarketError): catalog.verify_snapshot(snapshot, envelope, roots, dict(watermark, generation=2), NOW)
        with self.assertRaises(MarketError): catalog.verify_snapshot(snapshot, envelope, roots, dict(watermark, sha256="0" * 64), NOW)
        with self.assertRaises(MarketError): catalog.verify_snapshot(snapshot, envelope, {}, None, NOW)
        with self.assertRaises(MarketError): catalog.verify_snapshot(snapshot, envelope, roots, None, "2026-11-30T00:00:00Z")
        next_snapshot = self.build(generation=2)
        self.assertEqual(catalog.verify_snapshot(next_snapshot, catalog.sign_snapshot(next_snapshot, next_key, "next"), roots, watermark, NOW)["generation"], 2)

    def test_atomic_retry_retains_success_and_does_not_write_failure(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "catalog.json"; atomic_json(path, self.build())
            with self.assertRaises(MarketError):
                atomic_json(path, self.build(releases=[]))
            self.assertEqual(read_json(path)["generation"], 1)
            atomic_json(path, self.build())
            self.assertEqual(read_json(path)["generation"], 1)


if __name__ == "__main__": unittest.main()
