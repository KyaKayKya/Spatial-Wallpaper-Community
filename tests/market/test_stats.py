import copy
import unittest

from tools.market import stats
from tools.market.common import MarketError


def sample(asset_id=401, count=10, key="101:rain", volume=None):
    return {"repositoryId": 101, "assetId": asset_id, "work": key, "downloadCount": count,
            "volumeGroup": volume, "status": "awaiting-sdk-validation", "buildId": "a" * 64}


class StatsTests(unittest.TestCase):
    def test_first_observation_increment_same_day_retry_and_public_filter(self):
        state = stats.observe(None, "2026-10-01", [sample()])
        self.assertEqual(stats.rank(state, "2026-10-01", 7, public=False)[0]["downloads"], 10)
        self.assertEqual(stats.rank(state, "2026-10-01", 7), [])
        self.assertEqual(stats.observe(state, "2026-10-01", [sample()]), state)
        state = stats.observe(state, "2026-10-02", [sample(count=15)])
        self.assertEqual(stats.rank(state, "2026-10-02", 7, public=False)[0]["downloads"], 15)
        state = stats.observe(state, "2026-10-02", [sample(count=17)])
        self.assertEqual(stats.rank(state, "2026-10-02", 7, public=False)[0]["downloads"], 17)

    def test_windows_missing_days_not_zero_and_no_false_full_coverage(self):
        state = stats.observe(None, "2026-10-01", [sample()])
        state = stats.observe(state, "2026-10-10", [sample(count=25)])
        short = stats.rank(state, "2026-10-10", 7, public=False)[0]
        self.assertEqual(short["downloads"], 0)  # Gap straddles window: delta cannot be assigned.
        self.assertFalse(short["complete"])
        self.assertEqual(short["unallocatedDownloads"], 15)
        long = stats.rank(state, "2026-10-10", 30, public=False)[0]
        self.assertEqual(long["downloads"], 25)
        self.assertEqual(long["observedSince"], "2026-10-01")
        self.assertFalse(long["complete"])

    def test_deleted_assets_keep_history_reupload_new_id_and_reset_recorded(self):
        state = stats.observe(None, "2026-10-01", [sample()])
        state = stats.observe(state, "2026-10-02", [])
        state = stats.observe(state, "2026-10-03", [sample(asset_id=402, count=4)])
        self.assertEqual(stats.rank(state, "2026-10-03", 7, public=False)[0]["downloads"], 14)
        state = stats.observe(state, "2026-10-04", [sample(asset_id=402, count=2)])
        self.assertEqual(stats.rank(state, "2026-10-04", 7, public=False)[0]["downloads"], 14)
        self.assertEqual(state["anomalies"][-1]["reason"], "counter-decreased")

    def test_volumes_are_not_summed_as_installs(self):
        state = stats.observe(None, "2026-10-01", [sample(volume="split"), sample(asset_id=402, count=6, volume="split")])
        row = stats.rank(state, "2026-10-01", 7, public=False)[0]
        self.assertEqual(row["downloads"], 6)
        self.assertIn("分卷", row["metric"])

    def test_cross_work_duplicate_and_api_failure_are_transactional(self):
        state = stats.observe(None, "2026-10-01", [sample()]); before = copy.deepcopy(state)
        for samples in [[sample(), sample(key="101:other")], None]:
            with self.assertRaises(MarketError): stats.observe(state, "2026-10-02", samples)
        self.assertEqual(state, before)
        with self.assertRaises(MarketError): stats.observe(state, "2026-09-30", [sample()])

    def test_installable_and_revoked_filter_and_editorial_selection(self):
        s = dict(sample(), status="installable")
        state = stats.observe(None, "2026-10-01", [s])
        self.assertEqual(len(stats.rank(state, "2026-10-01", 7)), 1)
        self.assertEqual(stats.featured(["101:rain"], state), ["101:rain"])
        state = stats.observe(state, "2026-10-02", [dict(s, status="revoked")])
        self.assertEqual(stats.rank(state, "2026-10-02", 7), [])
        self.assertEqual(stats.rank(state, "2026-10-02", 7, public=False), [])
        self.assertEqual(stats.featured(["101:rain"], state), [])


if __name__ == "__main__": unittest.main()
