import unittest
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from process_output import artifact_line
class ProcessOutputChecks(unittest.TestCase):
 def test_bom_only_trailer_is_not_an_artifact(self):
  self.assertEqual(artifact_line('\ufeffbuild\r\n\ufeffevidence\r\n\ufeff\ufeff'), 'evidence')
  with self.assertRaises(StopIteration):artifact_line('\ufeff\r\n')
