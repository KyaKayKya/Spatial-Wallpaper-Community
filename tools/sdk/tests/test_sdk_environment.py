import importlib.util
from pathlib import Path
import sys
import unittest
from unittest import mock

SDK=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(SDK))


class EnvironmentReceiptChecks(unittest.TestCase):
    def setUp(self):
        self.assertTrue((SDK/'sdk_environment.py').is_file(),'E needs an observed machine receipt')
        import sdk_environment
        self.api=sdk_environment

    def test_missing_render_configuration_is_not_filled_with_defaults(self):
        receipt=self.api.collect()
        identity=receipt['configuration_identity']
        for key in ('render_profile_id','render_profile_version','render_profile_format_version'):
            self.assertEqual(identity[key]['status'],'unmeasured')
            self.assertNotIn('value',identity[key])
        self.assertFalse(receipt['configuration_complete'])
        self.assertEqual(receipt['support_catalog']['status'],'unmeasured')

    def test_failed_hardware_probe_remains_explicitly_unmeasured(self):
        with mock.patch.object(self.api,'display_devices',side_effect=OSError('controlled display unavailable')):
            receipt=self.api.collect()
        self.assertEqual(receipt['hardware']['gpu']['status'],'unmeasured')
        self.assertEqual(receipt['configuration_identity']['display_topology']['status'],'unmeasured')
        self.assertIn('controlled display unavailable',receipt['hardware']['gpu']['reason'])

    def test_receipt_binds_actual_python_and_cpu_memory_observations(self):
        receipt=self.api.collect()
        python=receipt['toolchain']['python']
        self.assertEqual(python['status'],'measured')
        self.assertEqual(python['value']['sha256'],self.api.digest(Path(sys.executable)))
        self.assertGreater(receipt['hardware']['cpu']['value']['logical_processors'],0)
        if sys.platform=='win32':
            self.assertGreater(receipt['hardware']['memory']['value']['total_physical_bytes'],0)
        self.assertEqual(receipt['hardware']['worker_render_gpu']['status'],'unmeasured')

    def test_display_topology_contains_attached_displays_not_unused_drivers(self):
        displays=self.api.measured([{'name':'active','state_flags':1},{'name':'unused-virtual-driver','state_flags':0}],'fixture OS enumeration')
        value=self.api.configuration_identity(displays)['display_topology']
        self.assertEqual(value['value'],[{'name':'active','state_flags':1}])

    def test_probe_cannot_be_rebound_to_another_configuration_or_worker(self):
        configuration={key:self.api.measured(value,'fixture actual API') for key,value in [('os',{'build':'A'}),('display_topology',[{'display':'one'}]),('dpi',[96])]}
        files={'Worker.exe':'same bytes'}
        self.api.require_render_probe_binding(configuration,configuration,files,files)
        for key in configuration:
            changed={**configuration,key:self.api.measured('different','fixture actual API')}
            with self.subTest(key=key),self.assertRaisesRegex(ValueError,'configuration-mismatch'):
                self.api.require_render_probe_binding(configuration,changed,files,files)
        with self.assertRaisesRegex(ValueError,'Worker-inventory-mismatch'):
            self.api.require_render_probe_binding(configuration,configuration,files,{'Worker.exe':'different bytes'})


if __name__=='__main__':
    unittest.main()
