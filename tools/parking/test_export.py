import json, unittest
from pathlib import Path

class ExportContract(unittest.TestCase):
    def test_export_contains_complete_scene_with_budgeted_lod(self):
        p=Path(__file__).resolve().parents[2]/'Prototype/Assets/Parking/Generated/scene.json'
        self.assertTrue(p.exists(), 'Parking export not created')
        d=json.loads(p.read_text())
        self.assertEqual(len([m for m in d['meshes'] if m['name'].endswith('_Housing')]),8)
        self.assertEqual(len(d['screens']),2)
        self.assertLess(sum(len(s['indices'])//3 for s in next(m for m in d['meshes'] if m['name']=='LampLOD')['submeshes']),2000)
        # Source report includes the master; layout contains 1939 placed instances.
        self.assertEqual(len(d['lamps']),1939)
        for m in d['meshes']:
            if m['name']=='LampNear':
                self.assertAlmostEqual(min(m['vertices'][1::3]),0,delta=.005,msg='Lamp base floats above or penetrates ground')
            self.assertEqual(len(m['vertices']),len(m['normals']))
            self.assertEqual(len(m['uv'])*3,len(m['vertices'])*2)
            for s in m['submeshes']:
                self.assertLess(max(s['indices'],default=0),len(m['vertices'])//3)

if __name__=='__main__': unittest.main()
