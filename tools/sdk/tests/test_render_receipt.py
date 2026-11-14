import copy
import json
import sys
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import render_profiles

class RenderReceiptTests(unittest.TestCase):
    def receipt(self):
        profile=render_profiles.catalog()['profiles'][0]
        observed={key:copy.deepcopy(profile[key]) for key in ('engine','color_space','graphics_api','settings','renderer_features','camera','capture')}
        observed.update(pipeline_type='UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset',renderer_type='UnityEngine.Rendering.Universal.UniversalRenderer',post_process_data=True)
        return dict(instance='1'*32,attempt='2'*32,generation=3,build_id='4'*64,profile=render_profiles.identity(profile),observed=observed,
                    hardware=dict(graphics_device_name='test fixture GPU',graphics_device_vendor='test fixture vendor',graphics_device_id=12,graphics_device_vendor_id=34,graphics_device_version='fixture',graphics_format='R8G8B8A8_SRGB',depth_stencil_format='D24_UNorm_S8_UInt',width=640,height=360))
    def check(self,value):
        function=getattr(render_profiles,'require_runtime_receipt',None)
        self.assertIsNotNone(function,'runtime receipt validation is missing')
        return function('SDK_RENDER_PROFILE '+json.dumps(value),dict(instance='1'*32,attempt='2'*32,generation=3,build_id='4'*64),640,360)
    def test_complete_bound_receipt(self):
        self.assertEqual(self.check(self.receipt())['hardware']['width'],640)
    def test_wrong_identity_and_actual_configuration(self):
        for group,key,value in [(None,'attempt','5'*32),(None,'generation',True),('observed','graphics_api','Vulkan'),('hardware','width',641),('hardware','graphics_device_name','')]:
            with self.subTest(group=group,key=key):
                receipt=self.receipt();(receipt if group is None else receipt[group])[key]=value
                with self.assertRaises(ValueError):self.check(receipt)
    def test_missing_or_duplicate_receipt(self):
        function=getattr(render_profiles,'require_runtime_receipt',None)
        self.assertIsNotNone(function,'runtime receipt validation is missing')
        value=self.receipt();line='SDK_RENDER_PROFILE '+json.dumps(value)
        for log in ['',line+'\n'+line]:
            with self.assertRaises(ValueError):function(log,{key:value[key] for key in ('instance','attempt','generation','build_id')},640,360)

    def test_hardware_format_must_match_observed_capture(self):
        for key,value in [('graphics_format','R8G8B8A8_UNorm'),('depth_stencil_format','D32_SFloat_S8_UInt')]:
            with self.subTest(key=key):
                receipt=self.receipt();receipt['hardware'][key]=value
                with self.assertRaisesRegex(ValueError,'render-receipt-hardware-format'):
                    self.check(receipt)

if __name__=='__main__':unittest.main()
