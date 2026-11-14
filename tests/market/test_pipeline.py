import copy
import unittest

from tools.market.common import MarketError, ROOT, read_json
from tools.market.pipeline import run_fixture


class PipelineTests(unittest.TestCase):
    def test_repository_rename_with_historical_release_is_not_a_replacement(self):
        data = self.data(); old = run_fixture(data, None, "2026-10-26T00:00:00Z")
        record = data["repositories"][0]; record["repository"]["full_name"] = "alice/renamed"
        record["releases"][0]["assets"][0]["browser_download_url"] = "https://github.com/alice/renamed/releases/download/v1/rain.zip"
        renamed = run_fixture(data, old, "2026-10-27T00:00:00Z")
        self.assertEqual(renamed["catalog"]["entries"][0]["build"]["identity"], old["catalog"]["entries"][0]["build"]["identity"])
        # A later new version must also be able to recheck the renamed historical v1.
        record["manifest"]["works"][0].update(version="2", releaseTag="v2")
        release = copy.deepcopy(record["releases"][0]); release.update(id=302, tag_name="v2")
        release["assets"][0].update(id=402, browser_download_url="https://github.com/alice/renamed/releases/download/v2/rain.zip")
        record["releases"].append(release); record["attachments"]["402"] = "c2FmZQ=="
        result = run_fixture(data, renamed, "2026-10-28T00:00:00Z")
        self.assertEqual(len(result["catalog"]["entries"][0]["history"]), 2)

    def test_revoked_old_build_is_removed_from_existing_and_new_downloads(self):
        data = self.data(); old = run_fixture(data, None, "2026-10-26T00:00:00Z")
        old_build = old["catalog"]["entries"][0]["build"]["identity"]
        record = data["repositories"][0]
        record["manifest"]["works"][0].update(version="2", releaseTag="v2")
        release = copy.deepcopy(record["releases"][0]); release.update(id=302, tag_name="v2")
        release["assets"][0].update(id=402, download_count=1, browser_download_url="https://github.com/alice/windows/releases/download/v2/rain.zip")
        record["releases"].append(release); record["attachments"]["402"] = "c2FmZQ=="
        record["releases"][0]["assets"][0]["download_count"] = 1000
        data["revocations"]["builds"] = [old_build]
        result = run_fixture(data, old, "2026-10-27T00:00:00Z")
        row = next(row for row in result["technicalPopular7"] if row["work"] == "101:rain")
        self.assertEqual(row["downloads"], 1)
        self.assertEqual(result["stats"]["assets"]["101:401"]["lastCount"], 10)

    def data(self):
        return read_json(ROOT / "tests/market/data/github.json")

    def test_two_authors_search_and_registration_end_to_end_dry_run(self):
        result = run_fixture(self.data(), None, "2026-10-26T00:00:00Z")
        self.assertEqual(len(result["catalog"]["entries"]), 2)
        self.assertEqual(result["publicPopular7"], [])
        self.assertEqual(result["publicPopular30"], [])
        self.assertEqual(len(result["technicalPopular7"]), 2)
        self.assertTrue(result["registrationPlans"][0]["dry_run"])
        self.assertEqual(len(result["community"]["operations"]), 2)
        self.assertNotIn("installable", [e["status"] for e in result["catalog"]["entries"]])
        retry = run_fixture(self.data(), result, "2026-10-26T00:00:00Z")
        self.assertEqual(result, retry)

    def test_good_bad_update_and_revocation_retains_previous_success(self):
        data = self.data(); result = run_fixture(data, None, "2026-10-26T00:00:00Z")
        updated = copy.deepcopy(data)
        item = updated["repositories"][0]
        item["manifest"]["works"][0].update(version="2", releaseTag="v2")
        release = copy.deepcopy(item["releases"][0]); release.update(id=302, tag_name="v2")
        release["assets"][0].update(id=402, browser_download_url="https://github.com/alice/windows/releases/download/v2/rain.zip")
        item["releases"].append(release); item["attachments"]["402"] = "c2FmZQ=="
        good = run_fixture(updated, result, "2026-10-27T00:00:00Z")
        self.assertEqual(len(good["catalog"]["entries"][0]["history"]), 2)
        broken = copy.deepcopy(updated); broken["repositories"][0]["attachments"]["402"] = "ZXZpbA=="
        before = copy.deepcopy(good)
        with self.assertRaises(MarketError): run_fixture(broken, good, "2026-10-28T00:00:00Z")
        self.assertEqual(before, good)
        updated["revocations"]["works"] = ["101:rain"]
        revoked = run_fixture(updated, good, "2026-10-28T00:00:00Z")
        self.assertEqual(revoked["catalog"]["entries"][0]["status"], "revoked")
        self.assertEqual([x["work"] for x in revoked["technicalPopular7"]], ["202:sun"])

    def test_failure_does_not_become_empty_index(self):
        data = self.data(); good = run_fixture(data, None, "2026-10-26T00:00:00Z")
        data["searchPages"][0]["incomplete_results"] = True
        with self.assertRaises(MarketError): run_fixture(data, good, "2026-10-27T00:00:00Z")
        self.assertEqual(len(good["catalog"]["entries"]), 2)


if __name__ == "__main__": unittest.main()
