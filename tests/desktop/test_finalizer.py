import hashlib
import json
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch
from tools.desktop import finalize_native_release as final


class FinalizerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.release = self.root / 'Release'
        self.release.mkdir()
        self.stage = self.root / 'Stage'
        self.stage.mkdir()

    def file(self, name, data):
        path = self.release / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return hashlib.sha256(data).hexdigest()

    def test_overlay_copies_only_different_hashes_and_retains_unknown_data(self):
        old = {'Launch.exe': self.file('Launch.exe', b'same'), 'native-runtime/file': self.file('native-runtime/file', b'old')}
        self.file('UserData/save.json', b'private-user-data')
        overlay = final.Overlay(self.release, old)
        overlay.data('Launch.exe', b'same', self.stage)
        overlay.data('native-runtime/file', b'new', self.stage)
        overlay.data('Authoring/new-tool.py', b'tool', self.stage)
        overlay.preflight()
        self.assertEqual(overlay.skipped, ['Launch.exe'])
        self.assertEqual(len(overlay.changed), 2)
        overlay.apply()
        self.assertEqual((self.release / 'native-runtime/file').read_bytes(), b'new')
        self.assertEqual((self.release / 'UserData/save.json').read_bytes(), b'private-user-data')
        self.assertNotIn('UserData/save.json', overlay.files)

    def test_unknown_existing_new_file_is_never_adopted_or_overwritten(self):
        self.file('Authoring/local.py', b'exact desired bytes')
        overlay = final.Overlay(self.release, {})
        overlay.data('Authoring/local.py', b'exact desired bytes', self.stage)
        with self.assertRaises(ValueError):
            overlay.preflight()

    def test_owned_drift_and_changed_source_refused(self):
        old = {'Launch.exe': self.file('Launch.exe', b'old')}
        overlay = final.Overlay(self.release, old)
        overlay.data('Launch.exe', b'new', self.stage)
        overlay.preflight()
        (self.stage / 'Launch.exe').write_bytes(b'changed input')
        with self.assertRaises(ValueError):
            overlay.apply()
        self.assertEqual((self.release / 'Launch.exe').read_bytes(), b'old')
        self.file('Launch.exe', b'unknown external edit')
        with self.assertRaises(ValueError):
            final.Overlay(self.release, old).preflight()

    def test_paths_and_duplicate_json_refused(self):
        for name in ('../save', '/absolute', 'a//b', 'a/./b', 'C:/outside', 'a\\b', 'x?'):
            with self.subTest(name=name), self.assertRaises(ValueError):
                final.relative(name)
        path = self.root / 'duplicate.json'
        path.write_text('{"a":1,"a":2}', encoding='utf-8')
        with self.assertRaises(ValueError):
            final.read(path)

    def test_initial_zip_is_verified_then_renamed_without_deletion(self):
        portable = Path(str(self.release) + '.zip')
        portable.write_bytes(b'initial-owned-zip')
        sha = final.digest(portable)
        Path(str(portable) + '.sha256').write_text(sha + '  Release.zip\n', encoding='utf-8')
        with patch.object(final, 'INITIAL_ZIP', sha):
            archived = final.archive_initial(self.release)
        self.assertFalse(portable.exists())
        self.assertEqual(archived.read_bytes(), b'initial-owned-zip')
        self.assertTrue(Path(str(archived) + '.sha256').is_file())
        portable.write_bytes(b'other zip')
        with patch.object(final, 'INITIAL_ZIP', sha), self.assertRaises(ValueError):
            final.archive_initial(self.release)
        self.assertEqual(portable.read_bytes(), b'other zip')

    def test_zip_includes_only_inventory_and_hashes_exact_written_bytes(self):
        sha = self.file('Launch.exe', b'launch')
        self.file('UserData/save.json', b'not distributable')
        manifest = {'files': {'Launch.exe': sha}, 'source_commit': 'a' * 40}
        final.finish_manifest(self.release, manifest)
        portable, expected = final.zip_release(self.release, manifest)
        self.assertEqual(final.digest(portable), expected)
        with zipfile.ZipFile(portable) as archive:
            self.assertEqual(set(archive.namelist()), {'Release/Launch.exe','Release/release-manifest.json','Release/SHA256SUMS.txt'})
            self.assertEqual(archive.read('Release/Launch.exe'), b'launch')

    def test_zip_refuses_asset_changed_since_inventory(self):
        sha = self.file('Launch.exe', b'launch')
        manifest = {'files': {'Launch.exe': sha}}
        final.finish_manifest(self.release, manifest)
        self.file('Launch.exe', b'changed')
        with self.assertRaises(ValueError):
            final.zip_release(self.release, manifest)
        self.assertFalse(Path(str(self.release) + '.zip').exists())
        self.assertFalse(Path(str(self.release) + '.zip.finalize.tmp').exists())

    def report(self, external=False):
        return {'passed': True, 'release': str(self.release), 'source_commit':'a'*40, 'release_manifest_sha256':'b'*64, 'tested_critical_inputs_unchanged':True,
                'job':{'assigned_before_release':True,'forced_termination':False,'exit_code':0,'root_exit_code':0,'active_after_cleanup':0},
                'native':{'passed':True,'cases':[{'cleanly_closed':True} for _ in range(1 if external else 5)]},
                'status':'passed-final-external-native-import-render-retirement' if external else 'passed-five-final-native-packages-minimal-render-smoke',
                'gpu_retirement':{'passed':True},'not_verified':['image quality','audio experience']}

    def test_smoke_reports_bind_directory_source_manifest_and_cleanup(self):
        path = self.root / 'report.json'
        report = self.report()
        path.write_bytes(final.encoded(report))
        raw, parsed = final.check_report(path,self.release,{'source_commit':'a'*40},'b'*64)
        self.assertEqual(parsed, report)
        self.assertIn('audio experience', parsed['not_verified'])
        for field, bad in (('release',str(self.root)),('source_commit','c'*40),('release_manifest_sha256','c'*64),('passed',False)):
            changed = dict(report, **{field:bad});path.write_bytes(final.encoded(changed))
            with self.subTest(field=field), self.assertRaises(ValueError):
                final.check_report(path,self.release,{'source_commit':'a'*40},'b'*64)
        report['job']['forced_termination'] = True
        path.write_bytes(final.encoded(report))
        with self.assertRaises(ValueError):
            final.check_report(path,self.release,{'source_commit':'a'*40},'b'*64)

    def test_external_report_requires_real_retirement_gate(self):
        report = self.report(True)
        report['gpu_retirement']['passed'] = False
        path = self.root / 'external.json';path.write_bytes(final.encoded(report))
        with self.assertRaises(ValueError):
            final.check_report(path,self.release,{'source_commit':'a'*40},'b'*64,True)

    def test_windows_source_receipt_paths_are_normalized_and_bound(self):
        class Source:
            commit='a'*40
            root=self.root
            paths=['Supervisor/Test.cs']
            def source_matches(self,name,sha):
                return name=='Supervisor/Test.cs' and sha=='b'*64
        path=self.root/'build.json';path.write_bytes(final.encoded({'sources':{'Supervisor\\Test.cs':'b'*64}}))
        receipt, match = final.receipt(Source(),path)
        self.assertFalse(match['receipt_has_source_commit'])
        self.assertEqual(match['matched_source_commit'],'a'*40)
        path.write_bytes(final.encoded({'source_commit':'c'*40,'sources':{'Supervisor/Test.cs':'b'*64}}))
        with self.assertRaises(ValueError):
            final.receipt(Source(),path)


if __name__=='__main__':
    unittest.main()
