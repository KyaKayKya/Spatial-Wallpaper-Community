import json
from pathlib import Path
import sys
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from build_render_negative import MODES,mutated_capture
from render_negative_evidence import require_negative

class RenderNegativeEvidence(unittest.TestCase):
    def fixture(self,mode):
        stage,_,_,error,frames=MODES[mode]
        marker=dict(mode=mode,stage=stage,attempt='a'*32,completed_sequence=frames,next_sequence=frames+1,mutation_count=1,unity_frame=7)
        if mode=='renderer-after-frames':marker.update(renderer_changed=True,pipeline_same=True,data_same=True)
        if mode=='data-during-warmup':marker.update(data_changed=True,selected_index=0)
        result=dict(received_frames=frames,last_sequence=frames,attempt='a'*32)
        result.update(clock_frequency=10000000,clock_origin_qpc=1000,stop_evidence={'forced_started_at_ms':2000})
        if mode=='stop-before-late-readback':marker.update(qpc=1500,frequency=10000000)
        return marker,result
    def log(self,mode,marker):
        error=MODES[mode][3];frames=MODES[mode][4]
        lines=['SDK_RENDER_PROFILE '+json.dumps({'attempt':'a'*32})] if frames else []
        lines.append('SDK_RENDER_NEGATIVE '+json.dumps(marker))
        lines.append('SDK_RENDER_NEGATIVE_LATE_READBACK '+json.dumps(dict(reason='capture-stopped-late-readback',attempt='a'*32,request_sequence=6,completed_sequence=5,stopped=True,has_error=False,qpc=2000,frequency=10000000)) if mode=='stop-before-late-readback' else 'InvalidDataException: '+error)
        return '\n'.join(lines)
    def test_all_exact_modes(self):
        for mode in MODES:
            marker,result=self.fixture(mode)
            self.assertEqual(require_negative(self.log(mode,marker),mode,'a'*32,result),marker)
    def test_wrong_attempt_stage_count_or_request(self):
        mode='camera-during-readback'
        for key,value in [('attempt','b'*32),('stage','before'),('mutation_count',2),('next_sequence',5),('completed_sequence',True)]:
            marker,result=self.fixture(mode);marker[key]=value
            with self.assertRaises(ValueError):require_negative(self.log(mode,marker),mode,'a'*32,result)
    def test_renderer_call_without_actual_replacement_is_rejected(self):
        for key in ('renderer_changed','pipeline_same','data_same'):
            mode='renderer-after-frames';marker,result=self.fixture(mode);marker[key]=False
            with self.assertRaises(ValueError):require_negative(self.log(mode,marker),mode,'a'*32,result)
    def test_prior_fault_duplicate_marker_and_missing_late_callback(self):
        for mode in MODES:
            marker,result=self.fixture(mode);log=self.log(mode,marker)
            for bad in ('InvalidOperationException: unrelated\n'+log,log+'\nSDK_RENDER_NEGATIVE '+json.dumps(marker)):
                with self.assertRaises(ValueError):require_negative(bad,mode,'a'*32,result)
        mode='stop-before-late-readback';marker,result=self.fixture(mode)
        with self.assertRaises(ValueError):require_negative(self.log(mode,marker).split('\nSDK_RENDER_NEGATIVE_LATE_READBACK')[0],mode,'a'*32,result)
    def test_count_without_contiguous_final_sequence_rejected(self):
        mode='camera-after-frames';marker,result=self.fixture(mode);result['last_sequence']=6
        with self.assertRaises(ValueError):require_negative(self.log(mode,marker),mode,'a'*32,result)
    def test_late_callback_must_be_stopped_same_attempt_and_before_fault(self):
        mode='stop-before-late-readback';marker,result=self.fixture(mode);log=self.log(mode,marker)
        for old,new in [('"stopped": true','"stopped": false'),('"has_error": false','"has_error": true'),('"request_sequence": 6','"request_sequence": 7'),('"qpc": 2000','"qpc": 30000000')]:
            with self.assertRaises(ValueError):require_negative(log.replace(old,new),mode,'a'*32,result)
    def test_same_millisecond_and_invalid_clock_fields_are_rejected(self):
        mode='stop-before-late-readback';marker,result=self.fixture(mode);log=self.log(mode,marker)
        for value in (20001000,20001001,20010999,True,2000.0,-1):
            with self.subTest(qpc=value),self.assertRaises(ValueError):
                require_negative(log.replace('"qpc": 2000','"qpc": '+json.dumps(value)),mode,'a'*32,result)
        for key in ('qpc','frequency'):
            for value in (None,True,-1,1.0):
                changed=dict(marker);changed[key]=value
                with self.subTest(key=key,value=value),self.assertRaises(ValueError):
                    require_negative(self.log(mode,changed),mode,'a'*32,result)
        changed=dict(marker);changed['qpc']=2001
        with self.assertRaises(ValueError):require_negative(self.log(mode,changed),mode,'a'*32,result)
    def test_mutation_keeps_all_original_capture_lines(self):
        source=(Path(__file__).resolve().parents[3]/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Unity/WorkerFrameCapture.cs').read_text()
        for mode in MODES:
            changed=mutated_capture(source,mode)
            self.assertEqual(changed.count('int diagnosticMutationCount;'),1)
            self.assertEqual(changed.count('++diagnosticMutationCount'),1)
            for anchor in ('writer.Publish(slot,next);CompletedSequence=next;','profileBinding.RequireSame(completedBinding);','SpatialWallpaper.Sdk.Security.RenderProfiles.RequireObservation(renderProfile,completedObservation);'):
                self.assertEqual(changed.count(anchor),source.count(anchor))

if __name__=='__main__':unittest.main()
