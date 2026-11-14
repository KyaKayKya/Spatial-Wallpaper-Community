"""Receipt corruption tests; these never capture audio or launch a child process."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
import hashlib
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import verify_e
from e_cases import cases
from audio_evidence import AUTHOR_PATHS, validate_inputs, dependencies
from input_continuity_inputs import WORKER_PATHS


class AudioReports(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.folder = Path(self.tmp.name)
        self.catalog = {case.id: case for case in cases(verify_e.Case)}
        self.receipt = {key: 'a' * 64 for key in AUTHOR_PATHS | WORKER_PATHS |
                        {'Packages/com.spatialwallpaper.sdk/Runtime/current.cs'}}
        self.builds = AudioInputs()
        self.builds.setUp()
        self.addCleanup(self.builds.doCleanups)
        self.receipt = self.builds.receipt
        self.upstream = {name: dict(path=str(folder/'report.json'), sha256=verify_e.digest(folder/'report.json'),
                                   files=verify_e.inventory(folder), inventory_scope='directory')
                         for name, folder in [('worker', self.builds.worker), ('audio-author', self.builds.author)]}

    def validate(self, name, data):
        path = self.folder / 'report.json'
        path.write_text(json.dumps(data))
        if 'result' in data:
            (self.folder / 'result.json').write_text(json.dumps(data['result']))
        return verify_e.validate_report(self.catalog[name], path, self.folder, set(), 0, data.get('sources', {}), upstream=self.upstream)

    def host(self, tree=False, pause=False):
        boundary = 'sampler-pause' if pause else 'source-exit'
        data = dict(status='partial-configured-audio-Host', sources=self.receipt, childrenExited=True,
                    samplerExited=True, cleanupErrors=[], treeSource=tree, boundary=boundary,
                    negativeNoPause=False,
                    result=dict(configured_independent_host=True, boundary=boundary,
                                author_heard_before_boundary=True, revision=3,
                                stable_zero_author_updates=120,
                                author_observed_nonzero_then_zero=True, stop_exit=True))
        data.update(negativeStabilityVerdict=False, worker_report_sha256='a'*64,
                    author_report_sha256='b'*64, author_package_sha256='c'*64, author_build_id='d'*64,
                    author_metadata_sha256='e'*64)
        data['input_binding'] = dict(worker_report_sha256='a'*64, author_report_sha256='b'*64,
                                    author_metadata_sha256='e'*64, package_sha256='c'*64, build_id='d'*64,
                                    worker_dependencies=dependencies(self.receipt, 'worker'),
                                    author_dependencies=dependencies(self.receipt, 'author', AUTHOR_PATHS),
                                    worker_runtime_files={'Worker.exe':'f'*64})
        _, _, data['input_binding'] = self.builds.check()
        for key, top in {'worker_report_sha256':'worker_report_sha256', 'author_report_sha256':'author_report_sha256',
                         'author_metadata_sha256':'author_metadata_sha256', 'package_sha256':'author_package_sha256',
                         'build_id':'author_build_id'}.items():
            data[top] = data['input_binding'][key]
        if tree:
            data.update(sourceChildAliveAfterAuthorCleared=True, sourceChildExited=True)
        if pause:
            data.update(samplerFallbackUsed=False, sourceAndHostAliveAfterSamplerExitAndAuthorClear=True,
                        samplerObservation=dict(authorRevisionImmediatelyBeforeBoundary=1,
                                                pauseInjected=True, exitedBeforeFallback=True,
                                                sourceAliveAtExitCheck=True, hostAliveAtExitCheck=True))
        data['samplerIdentity'] = dict(host_pid=100, host_creation_filetime=1000, pid=200, creation_filetime=2000)
        data.setdefault('samplerObservation', {})['pid'] = 200
        return data

    def test_host_positive_and_corruptions(self):
        for mode in ('direct', 'tree', 'pause'):
            data = self.host(mode == 'tree', mode == 'pause')
            self.validate('audio-host-' + mode, data)
            changes = [('revision', 2), ('stable_zero_author_updates', 119),
                       ('author_heard_before_boundary', False), ('stop_exit', False),
                       ('configured_independent_host', False), ('boundary', 'other')]
            for key, value in changes:
                bad = copy.deepcopy(data)
                bad['result'][key] = value
                with self.subTest(mode=mode, key=key), self.assertRaises(ValueError):
                    self.validate('audio-host-' + mode, bad)
            for key in ('result', 'treeSource', 'boundary', 'negativeNoPause'):
                bad = copy.deepcopy(data)
                del bad[key]
                with self.subTest(mode=mode, missing=key), self.assertRaises(ValueError):
                    self.validate('audio-host-' + mode, bad)

    def test_tree_cleanup_and_pause_boundary(self):
        for mode, keys in [('tree', ['sourceChildAliveAfterAuthorCleared', 'sourceChildExited']),
                           ('pause', ['sourceAndHostAliveAfterSamplerExitAndAuthorClear'])]:
            for key in keys:
                bad = self.host(mode == 'tree', mode == 'pause')
                bad[key] = False
                with self.subTest(key=key), self.assertRaises(ValueError):
                    self.validate('audio-host-' + mode, bad)
        for key in self.host(pause=True)['samplerObservation']:
            bad = self.host(pause=True)
            bad['samplerObservation'][key] = 0 if key == 'authorRevisionImmediatelyBeforeBoundary' else False
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.validate('audio-host-pause', bad)

    def test_result_file_disagreement(self):
        data = self.host()
        path = self.folder / 'report.json'
        path.write_text(json.dumps(data))
        (self.folder / 'result.json').write_text('{}')
        with self.assertRaisesRegex(ValueError, 'result-drift'):
            verify_e.validate_report(self.catalog['audio-host-direct'], path, self.folder, set(), 0, self.receipt)

    def test_new_binding_and_positive_mode_required(self):
        for key in ('negativeStabilityVerdict', 'input_binding'):
            bad = self.host(); del bad[key]
            with self.subTest(missing=key), self.assertRaises(ValueError):
                self.validate('audio-host-direct', bad)

    def test_positive_receipt_rejects_exception_fields(self):
        for key in ('failure', 'error', 'primary_failure'):
            bad = self.host()
            bad[key] = {'message': 'original failure'}
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, 'audio-failure-field'):
                self.validate('audio-host-direct', bad)
        bad = self.host(); bad['negativeStabilityVerdict'] = True
        with self.assertRaises(ValueError):
            self.validate('audio-host-direct', bad)
        for key in self.host()['input_binding']:
            bad = self.host(); del bad['input_binding'][key]
            with self.subTest(missing=key), self.assertRaises(ValueError):
                self.validate('audio-host-direct', bad)
        for key in ('worker_report_sha256', 'author_report_sha256', 'author_metadata_sha256', 'package_sha256', 'build_id'):
            bad = self.host(); bad['input_binding'][key] = '0'*64
            with self.subTest(drift=key), self.assertRaises(ValueError):
                self.validate('audio-host-direct', bad)
        for key in ('worker_dependencies', 'author_dependencies'):
            for change in ('missing', 'deleted', 'changed'):
                bad = self.host(); deps = bad['input_binding'][key]
                if change == 'missing': deps.clear()
                elif change == 'deleted': deps['Packages/com.spatialwallpaper.sdk/Runtime/deleted.cs'] = 'a'*64
                else: deps[next(iter(deps))] = '0'*64
                with self.subTest(key=key, change=change), self.assertRaises(ValueError):
                    self.validate('audio-host-direct', bad)
        for key, value in [('worker_runtime_files', {}), ('worker_runtime_files', {'Worker.exe':'bad'}),
                           ('author_metadata_sha256', 'not-a-digest')]:
            bad = self.host(); bad['input_binding'][key] = value
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                self.validate('audio-host-direct', bad)

    def test_positive_author_requires_positive_flags(self):
        case = self.catalog['audio-author']
        self.assertEqual(case.required, {'stableZeroPackage': True, 'negativeStabilityVerdictPackage': False})
        self.assertIn('--negative-stability-verdict', self.catalog['audio-host-verdict-negative'].args)

    def test_self_consistent_foreign_worker_and_inventory_rejected(self):
        bad = self.host()
        bad['input_binding']['worker_report_sha256'] = bad['worker_report_sha256'] = '0'*64
        bad['input_binding']['worker_runtime_files'] = {'unrelated.exe': '0'*64}
        with self.assertRaisesRegex(ValueError, 'audio-upstream-binding'):
            self.validate('audio-host-direct', bad)

    def test_upstream_artifact_replaced_after_execution_rejected(self):
        data = self.host()
        (self.builds.worker/'player/Worker.exe').write_bytes(b'replaced-after-run')
        with self.assertRaisesRegex(ValueError, 'audio-upstream-artifact-drift'):
            self.validate('audio-host-direct', data)

    def test_missing_current_upstream_rejected(self):
        self.upstream = {}
        with self.assertRaisesRegex(ValueError, 'audio-current-upstream-required'):
            self.validate('audio-host-direct', self.host())

    def test_negative_case_allows_expected_failure_field(self):
        data = dict(status='not-complete', sources={}, childrenExited=True, samplerExited=True, cleanupErrors=[],
                    samplerObservation=dict(pauseInjected=False, exitedBeforeFallback=False),
                    failure=dict(type='AssertionError', message='sampler-watchdog-kernel-exit-missing'))
        self.validate('audio-host-pause-negative', data)

    def test_sampler_identity_required_and_matches_observed_process(self):
        data = self.host()
        del data['samplerIdentity']
        with self.assertRaisesRegex(ValueError, 'audio-sampler-identity'):
            self.validate('audio-host-direct', data)
        for key in ('host_pid', 'host_creation_filetime', 'pid', 'creation_filetime'):
            data = self.host(); data['samplerIdentity'][key] = True
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, 'audio-sampler-identity'):
                self.validate('audio-host-direct', data)
        data = self.host(); data['samplerObservation']['pid'] = 201
        with self.assertRaisesRegex(ValueError, 'audio-field:pid'):
            self.validate('audio-host-direct', data)

    def test_lifecycle_exact_modes_and_cleanup(self):
        data = dict(status='partial-real-audio-owner-lifecycle', sources={}, sourceStable=True, childrenExited=True,
                    samplersExited=True, cases=[dict(mode=mode, samplerPid=i + 100,
                    samplerExited=True, sourceStillAlive=True, ownerStopToSamplerExitObservedMs=12.5)
                    for i, mode in enumerate(('dispose', 'crash'))])
        self.validate('audio-lifecycle', data)
        variants = []
        for rows in ([], data['cases'][:1], data['cases'] * 2, [data['cases'][0]] * 2):
            bad = copy.deepcopy(data); bad['cases'] = rows; variants.append(bad)
        for key in ('childrenExited', 'samplersExited'):
            bad = copy.deepcopy(data); bad[key] = False; variants.append(bad)
        for key in ('samplerExited', 'sourceStillAlive'):
            bad = copy.deepcopy(data); bad['cases'][0][key] = False; variants.append(bad)
        for bad in variants:
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                self.validate('audio-lifecycle', bad)

    def test_tree_each_required_observation(self):
        data = dict(status='partial-production-audio-tree', sources={}, lateChildIncluded=True,
                    childSurvivedParentAtRevoke=True, childrenExited=True, descendantsExited=True,
                    readerStopped=True, sourceStable=True)
        self.validate('audio-tree', data)
        for key in set(data) - {'status', 'sources'}:
            bad = copy.deepcopy(data)
            del bad[key]
            with self.subTest(missing=key), self.assertRaises(ValueError):
                self.validate('audio-tree', bad)


class AudioInputs(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.worker = self.root / 'worker'
        self.author = self.root / 'author'
        (self.worker / 'player').mkdir(parents=True)
        self.author.mkdir()
        (self.worker / 'player' / 'Worker.exe').write_bytes(b'fixture-only-not-executed')
        self.receipt = {key: 'current' for key in AUTHOR_PATHS | WORKER_PATHS |
                        {'Packages/com.spatialwallpaper.sdk/Runtime/current.cs',
                         'Packages/com.spatialwallpaper.sdk/Editor/current.cs'}}
        manifest = {'format': 'identity-fixture'}
        build = hashlib.sha256(json.dumps(manifest, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
        manifest['build_id'] = build
        with zipfile.ZipFile(self.author / 'scene.spatial', 'w') as archive:
            archive.writestr('manifest.json', json.dumps(manifest))
        self.metadata = {'build': build, 'engine': 'fixture'}
        self.ar = dict(status='partial-audio-CSharp-Play-only', sources=self.receipt.copy(),
                       stableZeroPackage=True, negativeStabilityVerdictPackage=False,
                       author=self.metadata.copy(), package_sha256=verify_e.digest(self.author / 'scene.spatial'))
        self.wr = dict(status='production-Worker-built-not-acceptance', sources=self.receipt.copy(),
                       runtime_files=verify_e.inventory(self.worker / 'player'))
        self.save()

    def save(self):
        (self.author / 'author.json').write_text(json.dumps(self.metadata))
        (self.author / 'report.json').write_text(json.dumps(self.ar))
        (self.worker / 'report.json').write_text(json.dumps(self.wr))

    def check(self):
        return validate_inputs(self.worker, self.author, self.receipt)

    def test_valid_and_non_dependency_edits(self):
        self.check()
        self.receipt['docs/ignored.md'] = 'changed'
        self.check()

    def test_each_required_dependency_and_deleted_file(self):
        for report, paths in ((self.ar, AUTHOR_PATHS | {'Packages/com.spatialwallpaper.sdk/Editor/current.cs'}),
                              (self.wr, WORKER_PATHS | {'Packages/com.spatialwallpaper.sdk/Runtime/current.cs'})):
            original = report['sources'].copy()
            for key in paths:
                report['sources'] = {k: v for k, v in original.items() if k != key}
                self.save()
                with self.subTest(missing=key), self.assertRaisesRegex(ValueError, 'dependency-drift'):
                    self.check()
            report['sources'] = dict(original, **{'Packages/com.spatialwallpaper.sdk/Runtime/deleted.cs': 'old'})
            self.save()
            with self.assertRaisesRegex(ValueError, 'dependency-drift'):
                self.check()
            report['sources'] = original

    def test_metadata_flags_package_and_worker_drift(self):
        for key, value in [('stableZeroPackage', False), ('negativeStabilityVerdictPackage', True),
                           ('status', 'failed'), ('package_sha256', 'wrong'), ('author', {})]:
            old = self.ar[key]; self.ar[key] = value; self.save()
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.check()
            self.ar[key] = old
        self.metadata['build'] = 'wrong'
        self.ar['author'] = self.metadata.copy()
        self.save()
        with self.assertRaisesRegex(ValueError, 'package-build'):
            self.check()

    def test_old_worker_and_empty_inventory(self):
        for key, value in [('status', 'diagnostic-worker'), ('release_candidate', False)]:
            original = self.wr.copy()
            self.wr[key] = value
            self.save()
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.check()
            self.wr = original
        self.wr['sources']['Packages/com.spatialwallpaper.sdk/Runtime/current.cs'] = 'old'
        self.save()
        with self.assertRaisesRegex(ValueError, 'dependency-drift'):
            self.check()
        self.wr['sources'] = self.receipt.copy()
        (self.worker / 'player' / 'Worker.exe').unlink()
        self.wr['runtime_files'] = {}
        self.save()
        with self.assertRaisesRegex(ValueError, 'worker-inventory'):
            self.check()


if __name__ == '__main__':
    unittest.main()
