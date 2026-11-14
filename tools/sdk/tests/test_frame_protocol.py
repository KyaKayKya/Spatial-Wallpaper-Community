import importlib.util
import struct
import sys
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'experiments'))
from run_frame_ipc import decode_header
class FrameProtocolTests(unittest.TestCase):
 def test_exact_frame_size(self):
  self.assertEqual(691200,decode_header(struct.pack('<iiii',0x53574631,640,360,691200)))
 def test_invalid_dimensions_and_lengths_rejected_before_payload_allocation(self):
  for values in [(0,640,360,691200),(0x53574631,0,360,0),(0x53574631,640,360,-1),(0x53574631,2147483647,2147483647,691200),(0x53574631,640,360,691201)]:
   with self.subTest(values=values),self.assertRaises(ValueError):decode_header(struct.pack('<iiii',*values))
 def test_truncated_header_rejected(self):
  with self.assertRaises(struct.error):decode_header(b'bad')
if __name__=='__main__':unittest.main()
