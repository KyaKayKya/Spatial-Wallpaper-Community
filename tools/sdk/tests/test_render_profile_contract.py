"""Real C# admission checks; fixtures mutate a valid package, never bypass parsing."""
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import check_contracts
import fixtures
import render_profiles
import copy

class RenderProfileAdmission(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if check_contracts.compile_checks()!=0:raise RuntimeError('contract compilation failed')

    def admit(self,edit=None,observation=None):
        program,content=fixtures.sample()
        with tempfile.TemporaryDirectory() as directory:
            package=Path(directory)/'scene.spatial';result=Path(directory)/'result.json'
            package.write_bytes(fixtures.archive(program,content,manifest_edit=edit))
            command=[str(check_contracts.DATA/'MonoBleedingEdge/bin/mono.exe'),str(check_contracts.OUT/'ContractChecks.exe'),str(package),str(result)]
            if observation is not None:
                observed=Path(directory)/'observed.json';observed.write_text(json.dumps(observation),encoding='utf-8');command.append(str(observed))
            process=subprocess.run(command,capture_output=True,text=True,timeout=20)
            value=json.loads(result.read_text(encoding='utf-8-sig'))
            self.assertEqual(process.returncode,0 if value['accepted'] else 1)
            return value

    def rejected(self,edit,reason):
        value=self.admit(edit);self.assertFalse(value['accepted']);self.assertIn(reason,value['diagnostic'])

    def test_legacy_schema_cannot_gain_implicit_profile(self):
        def legacy(m):m['schema']=3;m.pop('render_profile',None)
        self.rejected(legacy,'manifest-schema')

    def test_explicit_profile_roundtrip(self):
        value=self.admit();self.assertTrue(value['accepted']);self.assertTrue(value['roundtrip'])

    def test_missing_profile(self):
        self.rejected(lambda m:m.pop('render_profile'),'render-profile-required')

    def test_unknown_profile(self):
        self.rejected(lambda m:m['render_profile'].__setitem__('id','untrusted.renderer'),'render-profile-unknown')

    def test_wrong_profile_version(self):
        self.rejected(lambda m:m['render_profile'].__setitem__('version','2.0.0'),'render-profile-version')
        self.rejected(lambda m:m['render_profile'].__setitem__('version','1.0.0'),'render-profile-version')

    def test_wrong_profile_format(self):
        self.rejected(lambda m:m['render_profile'].__setitem__('format_version',2),'render-profile-format')

    def test_wrong_descriptor_digest(self):
        self.rejected(lambda m:m['render_profile'].__setitem__('descriptor_sha256','0'*64),'render-profile-digest')

    def observation(self):
        profile=render_profiles.catalog()['profiles'][0]
        return {key:copy.deepcopy(profile[key]) for key in ('engine','color_space','graphics_api','settings','renderer_features','camera','capture')}|{
            'pipeline_type':'UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset',
            'renderer_type':'UnityEngine.Rendering.Universal.UniversalRenderer','post_process_data':True}

    def test_observed_runtime_matches_profile(self):
        result=self.admit(observation=self.observation())
        self.assertTrue(result['accepted'],result['diagnostic'] if not result['accepted'] else '')

    def test_runtime_mismatch_is_rejected(self):
        for key,value in [('graphics_api','Vulkan'),('color_space','Gamma'),('engine','2022.3.61f1'),
                          ('renderer_features',['UnknownFeature']),('renderer_type','OtherRenderer'),('post_process_data',False)]:
            with self.subTest(key=key):
                observation=self.observation();observation[key]=value
                result=self.admit(observation=observation)
                self.assertFalse(result['accepted']);self.assertIn('render-profile-observation-'+key,result['diagnostic'])

    def test_actual_render_mode_mismatch_is_rejected(self):
        observation=self.observation();observation['settings']['rendering_mode']='Forward'
        result=self.admit(observation=observation)
        self.assertFalse(result['accepted']);self.assertIn('render-profile-observation-settings',result['diagnostic'])

    def test_capture_and_camera_cannot_inherit_pipeline_capabilities(self):
        for group,key,value in [('capture','samples',4),('capture','srgb',False),('capture','format','ARGBHalf'),('camera','post_processing',False),('camera','resolved_renderer_index',1),('capture','graphics_format','B8G8R8A8_SRGB'),('capture','depth_stencil_format','D32_SFloat')]:
            with self.subTest(group=group,key=key):
                observation=self.observation();observation[group][key]=value
                result=self.admit(observation=observation)
                self.assertFalse(result['accepted']);self.assertIn('render-profile-observation-'+group,result['diagnostic'])

    def test_only_single_base_camera_is_in_supported_profile(self):
        for key,value in [('camera_stack_count',1),('render_type','Overlay')]:
            observation=self.observation();observation['camera'][key]=value
            result=self.admit(observation=observation)
            self.assertFalse(result['accepted']);self.assertIn('render-profile-observation-camera',result['diagnostic'])

if __name__=='__main__':unittest.main()
