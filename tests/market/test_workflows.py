import unittest

from tools.market.common import ROOT, MarketError
from tools.market.workflow import product_build_required, publish_plan


class WorkflowTests(unittest.TestCase):
    def test_registry_only_skips_unity_but_any_source_change_builds(self):
        self.assertFalse(product_build_required(["market/registry/101.json"]))
        self.assertTrue(product_build_required(["market/registry/101.json", "Prototype/Assets/pwn.cs"]))
        self.assertTrue(product_build_required(["market/registry/../pwn.cs"]))

    def test_publish_remains_candidate_dry_run_and_no_latest_product_release(self):
        plan = publish_plan("f" * 64, 1)
        self.assertTrue(plan["dry_run"])
        self.assertEqual(plan["repository"], "KyaKayKya/Spatial-Wallpaper")
        self.assertEqual(plan["releaseTag"], "market-candidate-1")
        self.assertFalse(plan["makeLatest"])
        self.assertEqual(plan, publish_plan("f" * 64, 1))
        with self.assertRaises(MarketError): publish_plan("invalid", 0)

    def test_draft_has_no_write_token_schedule_or_pr_code_with_privileges(self):
        import yaml
        # BaseLoader preserves the YAML 1.2 spelling 'on'.
        workflow = yaml.load((ROOT / ".github/workflows/market-dry-run.yml").read_text(), Loader=yaml.BaseLoader)
        self.assertEqual(set(workflow["on"]), {"workflow_dispatch"})
        self.assertEqual(workflow["permissions"], {})
        job = workflow["jobs"]["offline"]
        self.assertEqual(job["permissions"], {"contents": "read"})
        self.assertEqual(job["runs-on"], "ubuntu-latest")
        for step in job["steps"]:
            if "uses" in step:
                self.assertRegex(step["uses"], r"^[A-Za-z0-9-]+/[A-Za-z0-9-]+@[0-9a-f]{40}$")
            self.assertNotIn("${{", step.get("run", ""))
            self.assertNotIn("secrets.", str(step))
        self.assertEqual(job["steps"][0]["with"]["persist-credentials"], "false")


if __name__ == "__main__": unittest.main()
