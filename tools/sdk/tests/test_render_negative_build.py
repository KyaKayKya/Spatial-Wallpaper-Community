import sys
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(ROOT/'tools/sdk'))
sys.path.insert(0,str(ROOT/'Supervisor/Tests'))
from run_production import require_production_build
from build_render_negative import MODES, mutated_capture

class RenderNegativeBoundary(unittest.TestCase):
    def test_late_callback_uses_machine_clock_across_unity_and_host(self):
        original=(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Unity/WorkerFrameCapture.cs').read_text()
        changed=mutated_capture(original,'stop-before-late-readback')
        self.assertIn('WindowsMachineClock.Qpc100ns()',changed)
        self.assertNotIn('{"qpc",System.Diagnostics.Stopwatch.GetTimestamp()}',changed)
        host=(ROOT/'Supervisor/Tests/RenderNegativeChecks.cs').read_text()
        self.assertIn('WindowsMachineClock.Qpc100ns()',host)

    def test_diagnostic_worker_never_enters_production_runner(self):
        require_production_build({'status':'production-Worker-built-not-acceptance'})
        for receipt in ({},{'status':'diagnostic-render-negative-only'},
                        {'status':'production-Worker-built-not-acceptance','release_candidate':False}):
            with self.assertRaisesRegex(ValueError,'diagnostic-build-rejected'):
                require_production_build(receipt)

    def test_mutations_include_zero_output_and_post_success_boundaries(self):
        self.assertEqual(MODES['camera-before-first'][4],0)
        self.assertEqual(MODES['data-during-warmup'][4],0)
        for name in ('camera-after-frames','renderer-after-frames','camera-during-readback'):
            self.assertEqual(MODES[name][4],5)

if __name__=='__main__':unittest.main()
