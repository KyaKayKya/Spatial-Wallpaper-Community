"""Exercise external recovery without modifying the real Unity project."""
import json,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import bakery_recovery as recovery

class RecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.project=Path(self.temp.name)
        self.root=self.project/'Assets/Parking';self.root.mkdir(parents=True)
        self.backup=self.project/'Library/ParkingBakeryBackup/test'
        self.saved=self.backup/'Assets/Parking/scene.unity';self.saved.parent.mkdir(parents=True);self.saved.write_text('original')
        self.manifest=self.project/'Library/ParkingBakeryRecovery.json'
        self.manifest.write_text(json.dumps(dict(project=str(self.project),backup=str(self.backup),files=['Assets/Parking/scene.unity'])))
        self.patches=[patch.object(recovery,'PROJECT',self.project),patch.object(recovery,'MANIFEST',self.manifest),patch.object(recovery.psutil,'process_iter',return_value=[])]
        for p in self.patches:p.start()
    def tearDown(self):
        for p in reversed(self.patches):p.stop()
        self.temp.cleanup()
    def test_restore_and_preserve_source(self):
        (self.root/'scene.unity').write_text('changed');(self.root/'new.asset').write_text('new')
        (self.root/'Generated').mkdir();(self.root/'Generated/source.png').write_bytes(b'source')
        recovery.recover()
        self.assertEqual((self.root/'scene.unity').read_text(),'original')
        self.assertFalse((self.root/'new.asset').exists());self.assertFalse(self.manifest.exists())
        self.assertEqual((self.root/'Generated/source.png').read_bytes(),b'source')
    def test_reject_escape_before_mutation(self):
        data=json.loads(self.manifest.read_text());data['files']=['../outside'];self.manifest.write_text(json.dumps(data))
        with self.assertRaises(AssertionError):recovery.recover()
        self.assertTrue(self.manifest.exists())
    def test_reject_running_project_executable(self):
        class Process:info={'name':'ftraceRTX.exe','cmdline':[],'exe':str(self.project/'Assets/Bakery/ftraceRTX.exe')}
        with patch.object(recovery.psutil,'process_iter',return_value=[Process()]):
            with self.assertRaises(RuntimeError):recovery.recover()
        self.assertTrue(self.manifest.exists())

if __name__=='__main__':unittest.main()
