"""No desktop actions: prove disconnected baseline runs retain failure evidence."""
from pathlib import Path
import csv
import json
import sys
import tempfile
import types
import unittest
from unittest.mock import Mock, patch
import check_native_baseline as baseline


class TimingChecks(unittest.TestCase):
    def measure(self, missing=False):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'sample.csv'
            fields = ['ProcessID', 'SwapChainAddress', 'CPUStartQPCTimeInMs', 'FrameTime', 'CPUBusy', 'GPUTime', 'DisplayedTime']
            with path.open('w', newline='') as stream:
                writer = csv.DictWriter(stream, fieldnames=fields)
                writer.writeheader()
                for index in range(3600):
                    row = dict(zip(fields, [42, 'main', 1000 + index * 1000 / 60, 1000 / 60,
                                             0 if index % 2 else 2, 3, 1000 / 60]))
                    if missing and index:
                        row['GPUTime'] = row['DisplayedTime'] = 'NA'
                    writer.writerow(row)
                # Foreign processes, a helper swapchain, and out-of-window
                # timings must not contaminate the measured presentation.
                for pid, chain, qpc in [(99, 'main', 2000), (42, 'helper', 2000), (42, 'main', 999), (42, 'main', 61000)]:
                    writer.writerow(dict(zip(fields, [pid, chain, qpc, 9999, 9999, 9999, 9999])))
            return baseline.summarize(path, 42, 1000, 61000, 60)

    def test_process_swapchain_window_and_zero_work_are_preserved(self):
        result = self.measure()
        self.assertEqual(result['rows'], 3600)
        self.assertEqual(result['cpu_busy_ms']['samples'], 3600)
        self.assertEqual(result['cpu_busy_ms']['p50'], 0)
        self.assertEqual(result['metric_coverage']['cpu_busy_ms']['zero'], 1800)
        self.assertTrue(baseline.require_complete_timing(result, 60000, 60))
        self.assertAlmostEqual(result['displayed_time_coverage_fraction'], 1)
        self.assertAlmostEqual(result['displayed_average_fps'], 60)
        self.assertEqual(result['over_two_refresh_periods_fraction'], 0)

    def test_one_gpu_and_display_sample_does_not_pass_a_minute(self):
        result = self.measure(missing=True)
        self.assertFalse(baseline.require_complete_timing(result, 60000, 60))
        self.assertEqual(result['metric_coverage']['gpu_time_ms']['missing'], 3599)
        self.assertEqual(len(result['completeness_failures']), 2)
        self.assertLess(result['displayed_time_coverage_fraction'], .001)


class CleanupChecks(unittest.TestCase):
    def test_disconnected_runtime_preserves_report_and_original_failure(self):
        parent = baseline.ROOT / 'Validation/Content/sdk-runtime/native-render/task1/tool-tests'
        parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=parent) as temporary:
            root = Path(temporary).resolve()
            self.assertTrue(root.is_relative_to(parent.resolve()))
            seed = root / 'seed'
            (seed / 'presets').mkdir(parents=True)
            (seed / 'settings.json').write_text(json.dumps({'selected_preset_id': 'test'}))
            (seed / 'presets/test.json').write_text('{}')
            launcher = root / 'player/Launch.exe'
            assembly = launcher.parent / 'SpatialWallpaper_Data/Managed/Assembly-CSharp.dll'
            assembly.parent.mkdir(parents=True)
            assembly.write_bytes(b'fake assembly; never executed')
            presentmon = root / 'presentmon.exe'
            presentmon.write_bytes(b'fake monitor; never executed')
            client = Mock()
            client.call.side_effect = OSError('original-runtime-disconnected')

            def launch(arguments):
                desktop = Path(arguments[arguments.index('--desktop-data-root') + 1])
                descriptor = desktop / 'runtime/connection.json'
                descriptor.parent.mkdir(parents=True)
                descriptor.write_text('{}')
                return Mock(poll=lambda: 0)

            arguments = ['check_native_baseline.py', '--launcher', str(launcher), '--calibration', str(seed), '--presentmon', str(presentmon)]
            with patch.object(baseline, 'ROOT', root), patch.object(sys, 'argv', arguments), \
                 patch.object(baseline.subprocess, 'Popen', side_effect=launch), patch.object(baseline, 'Client', return_value=client), \
                 patch.object(baseline, 'owned_players', return_value=[]), patch.object(baseline.time, 'sleep'):
                with self.assertRaisesRegex(OSError, 'original-runtime-disconnected'):
                    baseline.main()
            report, = (root / 'Validation/NB').glob('*/report.json')
            data = json.loads(report.read_text())
            self.assertIn('original-runtime-disconnected', data['error'])
            self.assertIn('original-runtime-disconnected', data['quit_error'])
            self.assertEqual(data['leftover_owned_players'], [])
            self.assertEqual(data['status'], 'failed-owned-player-cleanup')
            client.close.assert_called_once()

    def test_failed_initialization_closes_only_owned_processes_and_reports_force(self):
        owned = Mock(pid=999999999)
        user = Mock()
        # No Windows API, process, window or real sleep is used by this test.
        with patch.object(baseline, 'owned_players', side_effect=[[owned], []]), \
             patch.object(baseline.time, 'sleep'), patch.object(baseline.psutil, 'wait_procs', return_value=([], [owned])), \
             patch.object(baseline.ctypes, 'windll', types.SimpleNamespace(user32=user)):
            report = {'status': 'failed', 'error': 'initialization-timeout'}
            baseline.finish(None, None, Path('fake/Launch.exe'), Path('unique-owned-root'), report)
        owned.terminate.assert_called_once()
        self.assertEqual(report['forced_owned_player_termination'], [999999999])
        self.assertEqual(report['leftover_owned_players'], [])
        self.assertEqual(report['error'], 'initialization-timeout')
        self.assertEqual(report['status'], 'failed-owned-player-cleanup')


if __name__ == '__main__':
    unittest.main()
