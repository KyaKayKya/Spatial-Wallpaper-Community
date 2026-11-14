import json,math,sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from frame_timings import TimingSeries

IDENTITY={'attempt':'1'*32,'instance':'2'*32,'generation':1,'build':'3'*64,'width':1280,'height':720,'target_fps':60}
def batch(rows,**change):return '\n'.join('SDK_FRAME_TIMINGS '+json.dumps({'format':1,**IDENTITY,'cpu_timer_frequency':1000000,'rows':rows[offset:offset+32],**change}) for offset in range(0,len(rows),32))
def row(sequence,main=2,render=3,gpu=4,wait=12):return [sequence,sequence*16667,max(16.667,main+wait),main,render,wait,gpu]

class FrameTimingsTests(unittest.TestCase):
 def test_thread_durations_do_not_prove_critical_path(self):
  series=TimingSeries(IDENTITY);series.read(batch([row(i,wait=100) for i in range(1,101)]));result=series.summary()
  self.assertEqual(result['status'],'unmeasured');self.assertEqual(result['thread_max_ms']['p95'],3);self.assertEqual(result['gpu_frame_ms']['p95'],4);self.assertIsNone(result['grade']);self.assertIn('cpu-critical-path-unproven',result['limitations'])
 def test_zero_render_thread_cannot_be_hidden_by_positive_main_thread(self):
  series=TimingSeries(IDENTITY);series.read(batch([row(1,render=0)]));result=series.summary()
  self.assertEqual(result['status'],'unmeasured');self.assertEqual(result['missing_render_thread_samples'],1);self.assertIsNone(result['grade'])
 def test_same_log_poll_is_deduplicated_but_conflicting_sample_refused(self):
  series=TimingSeries(IDENTITY);text=batch([row(1),row(2)]);series.read(text);series.read(text);self.assertEqual(series.summary()['samples'],2)
  with self.assertRaisesRegex(ValueError,'conflicting'):series.read(batch([row(2,main=7)]))
 def test_missing_gpu_is_unmeasured_not_fast(self):
  series=TimingSeries(IDENTITY);series.read(batch([row(1),row(2,gpu=0)]));result=series.summary();self.assertEqual(result['status'],'unmeasured');self.assertIsNone(result['grade']);self.assertEqual(result['missing_gpu_samples'],1)
 def test_invalid_identity_unknown_fields_and_nonfinite_values_refused(self):
  for change in [{'attempt':'4'*32},{'target_fps':30},{'untrusted_extra':True}]:
   with self.subTest(change=change),self.assertRaises(ValueError):TimingSeries(IDENTITY).read(batch([row(1)],**change))
  for value in [float('nan'),float('inf'),-1]:
   with self.subTest(value=value),self.assertRaises(ValueError):TimingSeries(IDENTITY).read(batch([row(1,gpu=value)]))
 def test_frame_order_and_missing_samples_are_not_silently_trimmed(self):
  with self.assertRaisesRegex(ValueError,'sequence'):TimingSeries(IDENTITY).read(batch([row(1),row(3)]))
  series=TimingSeries(IDENTITY);series.read('Unity startup noise\nSDK_FRAME_TIMINGS {incomplete');self.assertEqual(series.summary()['status'],'unmeasured')
 def test_raw_percentiles_are_retained_without_performance_grade(self):
  identity={**IDENTITY,'target_fps':30};series=TimingSeries(identity);series.read(batch([row(i,main=i,render=1,gpu=1) for i in range(1,101)],target_fps=30));result=series.summary()
  self.assertEqual(result['main_thread_ms'],{'p95':95,'p99':99,'max':100});self.assertIsNone(result['grade']);self.assertNotIn('frame_budget_fraction',result);self.assertNotIn('observed_seconds',result)

if __name__=='__main__':unittest.main()
