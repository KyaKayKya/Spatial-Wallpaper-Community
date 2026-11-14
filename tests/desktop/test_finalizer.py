import hashlib
import json
import tempfile
import subprocess
from types import SimpleNamespace
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

    def pending_fixture(self):
        repo = self.root / 'Repo'
        repo.mkdir()
        subprocess.run(['git','init','-q',str(repo)],check=True,capture_output=True)
        def git(*args, data=None):
            return subprocess.check_output(['git','-C',str(repo),'-c','user.name=Fixture','-c','user.email=fixture@example.invalid',*args],input=data).decode().strip()
        tree = git('mktree', data=b'')
        base = git('commit-tree',tree,data=b'old pending\n')
        child = git('commit-tree',tree,'-p',base,data=b'reviewed correction\n')
        unrelated = git('commit-tree',tree,data=b'unrelated lineage\n')
        anchor = self.file('Provenance/InitialReleaseManifest.json',b'initial-owned-manifest')
        old = {'format':'spatial-wallpaper-portable-release','version':final.VERSION,'status':final.PENDING,
               'source_commit':base,'initial_manifest_sha256':anchor,
               'files':{'Launch.exe':self.file('Launch.exe',b'owned-player'),'Provenance/InitialReleaseManifest.json':anchor}}
        final.finish_manifest(self.release,old)
        self.anchor_patch = patch.object(final,'INITIAL_MANIFEST',anchor)
        self.anchor_patch.start()
        self.addCleanup(self.anchor_patch.stop)
        return old, SimpleNamespace(root=repo,commit=child), unrelated

    def transition(self, old, source, enabled=False, reason=None):
        release, loaded, initial = final.load_owned(self.release)
        self.assertEqual(old,loaded)
        return final.prepare_transition(SimpleNamespace(supersede_pending=enabled,supersede_reason=reason),release,loaded,initial,source)

    def test_different_pending_freeze_requires_explicit_flag(self):
        old, source, _ = self.pending_fixture()
        with self.assertRaises(ValueError):
            self.transition(old,source)
        with self.assertRaises(ValueError):
            self.transition(old,source,reason='review correction')

    def test_explicit_pending_supersession_binds_old_manifest_and_source(self):
        old, source, _ = self.pending_fixture()
        before = (self.release/'release-manifest.json').read_bytes()
        result = self.transition(old,source,True,' real reviewer gate fix ')
        self.assertEqual(result['old_source_commit'],old['source_commit'])
        self.assertEqual(result['old_manifest_sha256'],hashlib.sha256(before).hexdigest())
        self.assertEqual(result['new_source_commit'],source.commit)
        self.assertEqual(result['reason'],'real reviewer gate fix')
        self.assertEqual((self.release/'release-manifest.json').read_bytes(),before)
        # The unchanged overlay preflight still protects every known input.
        self.file('Launch.exe',b'unknown local drift')
        with self.assertRaises(ValueError):
            final.Overlay(self.release,old['files']).preflight()

    def test_pending_supersession_rejects_unrelated_git_source(self):
        old, source, unrelated = self.pending_fixture()
        source.commit = unrelated
        with self.assertRaises(ValueError):
            self.transition(old,source,True,'review correction')

    def test_pending_supersession_rejects_sealed_release_and_final_zip(self):
        old, source, _ = self.pending_fixture()
        old['status'] = final.SEALED
        final.finish_manifest(self.release,old)
        with self.assertRaises(ValueError):
            self.transition(old,source,True,'review correction')
        old['status'] = final.PENDING
        final.finish_manifest(self.release,old)
        Path(str(self.release)+'.zip').write_bytes(b'final archive must be retained')
        with self.assertRaises(ValueError):
            self.transition(old,source,True,'review correction')

    def test_pending_supersession_requires_reason_and_is_one_time(self):
        old, source, _ = self.pending_fixture()
        for reason in (None,'','   ','line\nbreak'):
            with self.subTest(reason=reason), self.assertRaises(ValueError):
                self.transition(old,source,True,reason)
        old['pending_supersession']={'old_manifest_sha256':'b'*64}
        final.finish_manifest(self.release,old)
        with self.assertRaises(ValueError):
            self.transition(old,source,True,'another correction')

    def test_pending_supersession_preserves_initial_anchor_requirement(self):
        old, source, _ = self.pending_fixture()
        self.file('Provenance/InitialReleaseManifest.json',b'untrusted replacement')
        with self.assertRaises(ValueError):
            self.transition(old,source,True,'review correction')


    def test_pending_supersession_retains_exact_old_provenance_before_replacement(self):
        old, source, _ = self.pending_fixture()
        for name in ('Provenance/Player.json','Provenance/Player.SourceMatch.json','Provenance/ScriptWorker.json','Provenance/Supervisor.json','Provenance/NativeValidator.json','Provenance/NativeArtworks.json','Provenance/CreatorExample.json','Provenance/PlatformIndex.json','Provenance/Finalization.json'):
            old['files'][name] = self.file(name,('old '+name).encode())
        self.file('Provenance/unlisted-local.json',b'private local evidence')
        final.finish_manifest(self.release,old)
        supersession = self.transition(old,source,True,'review correction')
        overlay = final.Overlay(self.release,old['files'])
        overlay.data('Provenance/Player.json',b'new Player evidence',self.stage)
        final.retain_pending_provenance(overlay,self.release,old,supersession,self.stage)
        overlay.preflight()
        overlay.apply()
        prefix = 'Provenance/Pending-'+supersession['old_manifest_sha256']+'/'
        for name, sha in old['files'].items():
            if name.startswith('Provenance/'):
                retained = prefix + name[len('Provenance/'):]
                self.assertEqual(final.digest(self.release/retained),sha)
                self.assertEqual(overlay.files[retained],sha)
        self.assertEqual((self.release/'Provenance/Player.json').read_bytes(),b'new Player evidence')
        self.assertFalse((self.release/prefix/'unlisted-local.json').exists())
        self.assertEqual(final.digest(self.release/supersession['retained_manifest']),supersession['old_manifest_sha256'])

    def test_pending_retention_rejects_old_hash_drift_and_unknown_archive_target(self):
        old, source, _ = self.pending_fixture()
        name = 'Provenance/Player.json'
        old['files'][name] = self.file(name,b'old player receipt')
        final.finish_manifest(self.release,old)
        supersession = self.transition(old,source,True,'review correction')
        self.file(name,b'changed receipt')
        with self.assertRaises(ValueError):
            final.retain_pending_provenance(final.Overlay(self.release,old['files']),self.release,old,supersession,self.stage)
        self.file(name,b'old player receipt')
        overlay = final.Overlay(self.release,old['files'])
        final.retain_pending_provenance(overlay,self.release,old,supersession,self.stage)
        self.file('Provenance/Pending-'+supersession['old_manifest_sha256']+'/Player.json',b'unrelated existing file')
        with self.assertRaises(ValueError):
            overlay.preflight()



if __name__=='__main__':
    unittest.main()
