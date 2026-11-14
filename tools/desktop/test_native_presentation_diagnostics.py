import csv
import tempfile
import unittest
from pathlib import Path

from analyze_native_presentation import inspect_capture


class PresentationDiagnosticsTests(unittest.TestCase):
    def capture(self, rows):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        path = Path(temporary.name) / 'presents.csv'
        columns = ['ProcessID', 'CPUStartQPCTimeInMs', 'SyncInterval', 'PresentMode', 'MsGPUTime', 'MsGPUBusy', 'MsGPUWait']
        with path.open('w', newline='', encoding='utf-8-sig') as output:
            writer = csv.DictWriter(output, fieldnames=columns)
            writer.writeheader()
            writer.writerows(rows)
        return path

    def test_exact_process_and_half_open_window(self):
        rows = [dict(ProcessID=process, CPUStartQPCTimeInMs=stamp, SyncInterval=1, PresentMode='Composed: Flip', MsGPUTime=16, MsGPUBusy=2, MsGPUWait=14)
                for process, stamp in [(42, 99), (43, 110), (42, 100), (42, 199), (42, 200)]]
        result = inspect_capture(self.capture(rows), 42, 100, 200)
        self.assertEqual(result['rows'], 2)
        self.assertEqual(result['sync_intervals'], [1])
        self.assertEqual(result['gpu_busy_ms']['p95'], 2)
        self.assertEqual(result['gpu_wait_ms']['p95'], 14)

    def test_missing_gpu_wait_not_invented_by_subtraction(self):
        row = dict(ProcessID=42, CPUStartQPCTimeInMs=110, SyncInterval=0, PresentMode='Composed: Flip', MsGPUTime=16, MsGPUBusy=2, MsGPUWait='NA')
        result = inspect_capture(self.capture([row]), 42, 100, 200)
        self.assertIsNone(result['gpu_wait_ms'])
        self.assertEqual(result['gpu_time_ms']['p95'], 16)

    def test_nonfinite_or_negative_metric_is_not_a_sample(self):
        rows = [dict(ProcessID=42, CPUStartQPCTimeInMs=110, SyncInterval=0, PresentMode='Composed: Flip', MsGPUTime=value, MsGPUBusy=0, MsGPUWait=0)
                for value in ['NaN', 'Infinity', '-1']]
        result = inspect_capture(self.capture(rows), 42, 100, 200)
        self.assertIsNone(result['gpu_time_ms'])
        self.assertEqual(result['gpu_busy_ms']['samples'], 3)


if __name__ == '__main__':
    unittest.main()
