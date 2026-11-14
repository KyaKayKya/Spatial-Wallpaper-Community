import copy
import json
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import render_profiles

class RenderProfileCatalogTests(unittest.TestCase):
    def test_rejects_unimplementable_or_ambiguous_descriptors(self):
        original=json.loads(render_profiles.SOURCE.read_text(encoding='utf-8'))
        mutations=[('color_space','Unknown'),('graphics_api','Vulkan'),('version',''),
                   ('renderer_features',['untrusted.dll']),('settings',{}),('id','bad id')]
        for key,value in mutations:
            with self.subTest(key=key):
                catalog=copy.deepcopy(original);catalog['profiles'][0][key]=value
                catalog['default']=catalog['profiles'][0]['id']
                with patch.object(Path,'read_text',return_value=json.dumps(catalog)):
                    with self.assertRaises(ValueError):render_profiles.catalog()

    def test_rejects_bool_in_place_of_integer_setting(self):
        original=json.loads(render_profiles.SOURCE.read_text(encoding='utf-8'))
        original['profiles'][0]['settings']['msaa']=True
        with patch.object(Path,'read_text',return_value=json.dumps(original)):
            with self.assertRaises(ValueError):render_profiles.catalog()

    def test_checked_in_catalog_is_supported_by_builder(self):
        self.assertEqual(render_profiles.catalog()['default'],'spatial.urp.forward-plus')

if __name__=='__main__':unittest.main()
