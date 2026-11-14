import copy
import unittest

from fixtures import manifest, releases, repo
from test_catalog import NOW, fetch
from tools.market import catalog, community
from tools.market.common import MarketError


def entries():
    return catalog.build_snapshot(repo(), manifest(), releases(), fetch, None, 1, NOW, {"works": [], "builds": []})["entries"]


class CommunityTests(unittest.TestCase):
    def test_untrusted_marker_cannot_claim_mapping_or_block_official_creation(self):
        marker = "<!-- spatial-market:101:rain -->"
        impostor = {"id": "D_evil", "number": 99, "repository": "KyaKayKya/Spatial-Wallpaper", "categoryId": "DIC_test", "body": marker, "authorId": 999}
        result = community.discussion_plan(entries(), {}, [impostor], "DIC_test", 33)
        self.assertEqual(result["mapping"], {})
        self.assertEqual(len(result["operations"]), 1)
        forged_mapping = {"101:rain": {k: impostor[k] for k in ["id", "number", "repository", "categoryId", "authorId"]}}
        with self.assertRaises(MarketError):
            community.discussion_plan(entries(), forged_mapping, [impostor], "DIC_test", 33)

    def test_stable_id_not_title_controls_mapping_and_retry(self):
        items = entries(); items.append(dict(items[0], key="101:sun", workId="sun"))
        first = community.discussion_plan(items, {}, [], "DIC_test", 33)
        self.assertEqual(len(first["operations"]), 2)
        self.assertNotEqual(first["operations"][0]["marker"], first["operations"][1]["marker"])
        found = [{"id": "D_1", "number": 7, "repository": "KyaKayKya/Spatial-Wallpaper", "categoryId": "DIC_test",
                  "body": first["operations"][0]["marker"], "authorId": 33}]
        recovered = community.discussion_plan(items[:1], {}, found, "DIC_test", 33)
        self.assertEqual(recovered["operations"], [])
        items[0]["title"] = "new title"
        retry = community.discussion_plan(items[:1], recovered["mapping"], found, "DIC_test", 33)
        self.assertEqual(retry["mapping"]["101:rain"]["number"], 7)
        self.assertEqual(retry["operations"], [])

    def test_duplicate_or_wrong_repo_category_mapping_rejected(self):
        marker = community.discussion_plan(entries(), {}, [], "DIC_test", 33)["operations"][0]["marker"]
        found = {"id": "D_1", "number": 7, "repository": "KyaKayKya/Spatial-Wallpaper", "categoryId": "DIC_test", "body": marker, "authorId": 33}
        with self.assertRaises(MarketError): community.discussion_plan(entries(), {}, [found, dict(found, id="D_2")], "DIC_test", 33)
        mapping = {"101:rain": dict(id="D_1", number=7, categoryId="DIC_test", repository="attacker/repo")}
        before = copy.deepcopy(mapping)
        with self.assertRaises(MarketError): community.discussion_plan(entries(), mapping, [], "DIC_test", 33)
        self.assertEqual(mapping, before)

    def test_readme_only_generated_region_and_escapes_author_text(self):
        item = entries()[0]; item["title"] = "<script>[bad](evil)\n# heading"
        item["attribution"][0]["name"] = "<img> ![x](bad) | @everyone"
        source = "# Product\nkeep this\n<!-- market-creators:start -->\nold\n<!-- market-creators:end -->\nfooter\n"
        result = community.render_credits(source, [item], {11: {"id": 11, "login": "alice"}})
        self.assertTrue(result.startswith("# Product\nkeep this\n"))
        self.assertTrue(result.endswith("\nfooter\n"))
        self.assertNotIn("<script>", result)
        self.assertNotIn("<img>", result)
        self.assertIn("https://github.com/alice", result)
        self.assertEqual(community.render_credits(result, [item], {11: {"id": 11, "login": "alice"}}), result)
        self.assertIn("候选", result)

    def test_missing_markers_and_unverified_profile_fail(self):
        with self.assertRaises(MarketError): community.render_credits("no markers", entries(), {})
        source = "<!-- market-creators:start --><!-- market-creators:end -->"
        with self.assertRaises(MarketError): community.render_credits(source, entries(), {11: {"id": 12, "login": "alice"}})


if __name__ == "__main__": unittest.main()
