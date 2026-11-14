import importlib.util
import ast
import json
import os
from pathlib import Path
import sys
import tempfile
import time
import types
import unittest
import zipfile
from unittest import mock

SDK = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SDK))


class VerifyEChecks(unittest.TestCase):
    def setUp(self):
        self.assertTrue((SDK / 'verify_e.py').exists(), 'E orchestrator must exist')
        import verify_e
        self.api = verify_e
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.receipt = {'a': '123'}
        self.path = self.root / 'E/probe/new/report.json'
        self.path.parent.mkdir(parents=True)
        self.path.write_text(json.dumps({'status': 'ok', 'sources': self.receipt, 'result': {'clean': True}}))
        self.case = verify_e.Case('probe', 'probe.py', folder='E/probe', statuses=('ok',), required={'result.clean': True})

    def check(self, before=(), began=None):
        return self.api.validate_report(self.case, self.path, self.root, set(before), began or time.time()-2, self.receipt)

    def test_fresh_report_accepted_and_bound(self):
        self.assertEqual(self.check()['sha256'], self.api.digest(self.path))

    def test_case_boundary_configuration_drift_rejects_successful_child(self):
        output = self.root/'logs'; output.mkdir()
        (self.root/'child.py').write_text('pass')
        case = self.api.Case('child', 'child.py')
        before = {'dpi': {'status': 'measured', 'value': 96}}
        after = {'dpi': {'status': 'measured', 'value': 144}}
        with mock.patch.object(self.api,'ROOT',self.root), mock.patch.object(self.api,'BASE',self.root), mock.patch.object(self.api,'sources',return_value=self.receipt), mock.patch.object(self.api,'collect_configuration',side_effect=[before, after]):
            row = self.api.execute(case, {}, self.receipt, output)
        self.assertEqual(row['status'], 'failed')
        self.assertEqual(row['configuration_before'], before)
        self.assertEqual(row['configuration_after'], after)
        self.assertIn('configuration changed', row['configuration_error'])

    def test_old_artifact_rejected_even_with_fresh_timestamp(self):
        with self.assertRaisesRegex(ValueError, 'old-artifact'):
            self.check([self.path.resolve()])

    def test_stale_timestamp_rejected(self):
        os.utime(self.path, (1, 1))
        with self.assertRaisesRegex(ValueError, 'stale-artifact'):
            self.check()

    def test_wrong_status_rejected(self):
        self.path.write_text(json.dumps({'status': 'partial-other', 'sources': self.receipt}))
        with self.assertRaisesRegex(ValueError, 'status'):
            self.check()

    def test_source_drift_rejected(self):
        with self.assertRaisesRegex(ValueError, 'source-drift'):
            self.api.validate_report(self.case, self.path, self.root, set(), time.time()-2, {'a':'changed'})

    def test_cleanup_missing_rejected(self):
        self.path.write_text(json.dumps({'status': 'ok', 'sources': self.receipt}))
        with self.assertRaisesRegex(ValueError, 'required-field'):
            self.check()

    def test_result_case_set_missing_or_duplicate_is_rejected(self):
        case = self.api.Case('matrix','probe.py',folder='E/probe',statuses=('ok',),
                             result_cases=('one','two'), result_key='results')
        for results in [[], [{'case':'one'}], [{'case':'one'},{'case':'one'}]]:
            self.path.write_text(json.dumps({'status':'ok','sources':self.receipt,'results':results}))
            with self.assertRaisesRegex(ValueError, 'result-case-set'):
                self.api.validate_report(case,self.path,self.root,set(),time.time()-2,self.receipt)

    def test_author_metadata_cannot_hide_wrong_manifest_build(self):
        with zipfile.ZipFile(self.path.parent/'scene.spatial','w') as archive:
            archive.writestr('manifest.json',json.dumps({'build_id':'wrong','format':1}))
        metadata={'build':'claimed'}
        (self.path.parent/'author.json').write_text(json.dumps(metadata))
        self.path.write_text(json.dumps({'status':'ok','sources':self.receipt,'author':metadata,
                                        'package_sha256':self.api.digest(self.path.parent/'scene.spatial')}))
        case=self.api.Case('author','build.py',statuses=('ok',),role='author')
        with self.assertRaisesRegex(ValueError,'package-build'):
            self.api.validate_report(case,self.path,self.root,set(),time.time()-2,self.receipt)

    def test_real_child_failure_and_timeout_leave_machine_record(self):
        output = self.root/'logs'
        output.mkdir()
        script = self.root/'child.py'
        for body, timeout in [('raise RuntimeError("fixture failure")',5), ('import time; time.sleep(10)',0.1)]:
            script.write_text(body)
            case = self.api.Case('child','child.py',timeout=timeout)
            with mock.patch.object(self.api,'ROOT',self.root), mock.patch.object(self.api,'BASE',self.root), mock.patch.object(self.api,'sources',return_value=self.receipt):
                row = self.api.execute(case,{},self.receipt,output)
            self.assertEqual(row['status'],'failed')
            self.assertIn('error',row)
            self.assertTrue(Path(row['log']).is_file())

    def test_actual_subprocess_artifact_and_upstream_path_binding(self):
        output=self.root/'logs';output.mkdir()
        parent={'path':str(self.path),'files':self.api.inventory(self.path.parent)}
        script=self.root/'child.py'
        script.write_text('import sys,json\nfrom pathlib import Path\np=Path(sys.argv[1])\nassert (p/"report.json").is_file()\nout=Path("E/child/new");out.mkdir(parents=True)\n(out/"report.json").write_text(json.dumps('+repr({'status':'ok','sources':self.receipt})+'))\n')
        case=self.api.Case('child','child.py',args=('{parent}',),dependencies=('parent',),folder='E/child',statuses=('ok',))
        with mock.patch.object(self.api,'ROOT',self.root),mock.patch.object(self.api,'BASE',self.root),mock.patch.object(self.api,'sources',return_value=self.receipt):
            row=self.api.execute(case,{'parent':parent},self.receipt,output)
        self.assertEqual(row['status'],'passed',row)
        self.assertEqual(row['inputs']['parent'],str(self.path.parent))
        self.path.write_text('changed')
        with self.assertRaisesRegex(ValueError,'upstream-artifact-drift'):
            self.api.execute(case,{'parent':parent},self.receipt,output)

    def test_fixed_legacy_report_is_rejected_if_not_replaced(self):
        output=self.root/'logs';output.mkdir()
        (self.root/'child.py').write_text('print("no report replacement")')
        case=self.api.Case('child','child.py',report_file='E/probe/new/report.json',statuses=('ok',))
        with mock.patch.object(self.api,'ROOT',self.root),mock.patch.object(self.api,'BASE',self.root),mock.patch.object(self.api,'sources',return_value=self.receipt):
            row=self.api.execute(case,{},self.receipt,output)
        self.assertEqual(row['status'],'failed')
        self.assertIn('old-artifact',row['error'])

    def cleanup_fixture(self, folder, exit_code=0):
        output=self.root/'logs';output.mkdir(exist_ok=True)
        script=self.root/'cleanup_child.py'
        script.write_text('import json\nfrom pathlib import Path\np=Path('+repr(folder+'/fresh')+')\np.mkdir(parents=True)\n(p/"report.json").write_text(json.dumps('+repr({'status':'ok','sources':self.receipt})+'))\n(p/"external-input-cleanup.json").write_text("[]")\nraise SystemExit('+str(exit_code)+')\n')
        return output,self.api.Case('cleanup','cleanup_child.py',folder=folder,statuses=('ok',))

    def test_success_inventory_is_frozen_after_outer_cleanup_and_consumable(self):
        for folder in ['E/native-desktop-input-host','E/owned-input-timeout']:
            with self.subTest(folder=folder):
                output,case=self.cleanup_fixture(folder)
                def cleanup(directory):
                    path=directory/'external-input-cleanup.json';rows=json.loads(path.read_text());rows.append({'released':True});path.write_text(json.dumps(rows))
                module=types.SimpleNamespace(cleanup=cleanup)
                with mock.patch.dict(sys.modules,{'owned_input_cleanup':module}),mock.patch.object(self.api,'ROOT',self.root),mock.patch.object(self.api,'BASE',self.root),mock.patch.object(self.api,'sources',return_value=self.receipt):
                    row=self.api.execute(case,{},self.receipt,output)
                    self.assertEqual(row['status'],'passed',row)
                    self.assertEqual(row['artifact']['files'],self.api.inventory(Path(row['artifact']['path']).parent))
                    (self.root/'consume.py').write_text('pass')
                    consumer=self.api.Case('consume','consume.py',dependencies=('cleanup',),args=('{cleanup}',))
                    consumed=self.api.execute(consumer,{'cleanup':row['artifact']},self.receipt,output)
                    self.assertEqual(consumed['status'],'passed',consumed)

    def test_failed_case_binds_final_cleanup_files(self):
        output,case=self.cleanup_fixture('E/native-desktop-input-host',1)
        def cleanup(directory):(directory/'external-input-cleanup.json').write_text('[{"final":true}]')
        with mock.patch.dict(sys.modules,{'owned_input_cleanup':types.SimpleNamespace(cleanup=cleanup)}),mock.patch.object(self.api,'ROOT',self.root),mock.patch.object(self.api,'BASE',self.root),mock.patch.object(self.api,'sources',return_value=self.receipt):
            row=self.api.execute(case,{},self.receipt,output)
        self.assertEqual(row['status'],'failed')
        final=row['final_outputs'][0]
        self.assertEqual(final['files'],self.api.inventory(Path(final['path'])))
        self.assertEqual(final['files']['external-input-cleanup.json'],self.api.digest(Path(final['path'])/'external-input-cleanup.json'))

    def test_unproven_owner_never_calls_input_release(self):
        output,case=self.cleanup_fixture('E/native-desktop-input-host')
        original=self.api.CaseProcess.close
        def unproven(owner):
            original(owner) # Real fixture is cleaned before injecting proof loss.
            owner.evidence['active_after_termination']=None
            owner.evidence['root_kernel_exited']=False
            raise RuntimeError('controlled missing owner proof')
        cleanup=mock.Mock()
        with mock.patch.dict(sys.modules,{'owned_input_cleanup':types.SimpleNamespace(cleanup=cleanup)}),mock.patch.object(self.api,'ROOT',self.root),mock.patch.object(self.api,'BASE',self.root),mock.patch.object(self.api,'sources',return_value=self.receipt),mock.patch.object(self.api.CaseProcess,'close',unproven):
            row=self.api.execute(case,{},self.receipt,output)
        cleanup.assert_not_called()
        self.assertEqual(row['status'],'failed')
        self.assertIn('release-skipped-owner-live-or-unproven',row['cleanup_error'])
        self.assertEqual(row['final_outputs'][0]['files'],self.api.inventory(self.root/'E/native-desktop-input-host/fresh'))

    def test_cleanup_failure_still_binds_its_last_receipt(self):
        output,case=self.cleanup_fixture('E/native-desktop-input-host')
        def cleanup(directory):
            (directory/'external-input-cleanup.json').write_text('[{"failure":"controlled"}]')
            raise RuntimeError('controlled cleanup failure')
        with mock.patch.dict(sys.modules,{'owned_input_cleanup':types.SimpleNamespace(cleanup=cleanup)}),mock.patch.object(self.api,'ROOT',self.root),mock.patch.object(self.api,'BASE',self.root),mock.patch.object(self.api,'sources',return_value=self.receipt):
            row=self.api.execute(case,{},self.receipt,output)
        self.assertEqual(row['status'],'failed')
        self.assertIn('controlled cleanup failure',row['cleanup_error'])
        self.assertNotIn('artifact',row)
        final=row['final_outputs'][0]
        self.assertEqual(final['files'],self.api.inventory(Path(final['path'])))

    def test_log_digest_failure_preserves_unproven_cleanup(self):
        output,case=self.cleanup_fixture('E/native-desktop-input-host')
        original_close=self.api.CaseProcess.close
        original_digest=self.api.digest
        def unproven(owner):
            original_close(owner)
            owner.evidence['active_after_termination']=None
            owner.evidence['root_kernel_exited']=False
            raise RuntimeError('controlled missing owner proof')
        def digest(path):
            if Path(path)==output/'cleanup.log':raise OSError('controlled case log digest failure')
            return original_digest(path)
        cleanup=mock.Mock()
        with mock.patch.dict(sys.modules,{'owned_input_cleanup':types.SimpleNamespace(cleanup=cleanup)}),mock.patch.object(self.api,'ROOT',self.root),mock.patch.object(self.api,'BASE',self.root),mock.patch.object(self.api,'sources',return_value=self.receipt),mock.patch.object(self.api.CaseProcess,'close',unproven),mock.patch.object(self.api,'digest',digest):
            row=self.api.execute(case,{},self.receipt,output)
        cleanup.assert_not_called()
        self.assertEqual(row['status'],'failed')
        self.assertIn('release-skipped-owner-live-or-unproven',row['cleanup_error'])
        self.assertIn('case log digest failure',row['log_binding_error'])

    def test_unknown_execute_failure_stops_scheduler(self):
        (self.root/'tools/sdk').mkdir(parents=True)
        (self.root/'tools/sdk/release-policy.json').write_text('{}')
        cases=[self.api.Case('first','first.py'),self.api.Case('second','second.py')]
        execute=mock.Mock(side_effect=OSError('unknown execute boundary failure'))
        with mock.patch.object(self.api,'ROOT',self.root),mock.patch.object(self.api,'BASE',self.root),mock.patch.object(self.api,'sources',return_value=self.receipt),mock.patch.object(self.api,'registry',return_value=cases),mock.patch.object(self.api,'execute',execute),mock.patch.object(sys,'argv',['verify_e.py']):
            self.assertEqual(self.api.main(),1)
        self.assertEqual(execute.call_count,1)
        report=json.loads((self.root/'reports/stage-E.json').read_text())
        self.assertIn('cleanup_error',report['steps'][0])
        self.assertEqual(report['steps'][1]['status'],'blocked')

    def test_log_binding_keyboard_interrupt_is_retained(self):
        output,case=self.cleanup_fixture('E/native-desktop-input-host')
        original=self.api.digest
        def digest(path):
            if Path(path)==output/'cleanup.log':raise KeyboardInterrupt('controlled final binding interrupt')
            return original(path)
        with mock.patch.dict(sys.modules,{'owned_input_cleanup':types.SimpleNamespace(cleanup=lambda directory:None)}),mock.patch.object(self.api,'ROOT',self.root),mock.patch.object(self.api,'BASE',self.root),mock.patch.object(self.api,'sources',return_value=self.receipt),mock.patch.object(self.api,'digest',digest):
            row=self.api.execute(case,{},self.receipt,output)
        self.assertEqual(row['status'],'failed')
        self.assertTrue(row['interrupted'])
        self.assertTrue(row['process_tree']['root_kernel_exited'])
        self.assertIn('KeyboardInterrupt',row['log_binding_error'])

    def test_missing_and_duplicate_cases_rejected(self):
        for rows in [[], [{'id':'probe','status':'passed'}]*2]:
            with self.assertRaisesRegex(ValueError, 'case-set'):
                self.api.require_case_set([self.case], rows)

    def test_no_partial_selection_can_pass_stage(self):
        result = self.api.verdict([self.case], [{'id':'probe','status':'passed'}])
        self.assertEqual(result['status'], 'failed')
        self.assertIn('interaction-1800', result['unimplemented_gates'])

    def test_complete_validated_catalog_can_finish_but_one_failed_case_cannot(self):
        cases=self.api.registry();rows=[{'id':case.id,'status':'passed'} for case in cases]
        artifact={'path':str(self.path),'cycle_matrix':{'status':'verified-by-cycle-validator'}}
        cycle=next(row for row in rows if row['id']=='input-cycles');cycle.update(started_at_unix=2,artifact=artifact)
        with mock.patch.object(self.api,'configuration_coverage',return_value={'status':self.api.CONFIGURATION_COMPLETE}),mock.patch('historical_failure_closure.evaluate_current',return_value={'status':'passed-historical-closure'}),mock.patch('input_cycles.owner_proof'),mock.patch.object(self.api,'validate_report',return_value=artifact):
            result=self.api.verdict(cases,rows,{}, {},self.receipt,1)
            self.assertEqual(result['status'],'passed-stage-E-capabilities-only')
            rows[0]['status']='failed'
            self.assertEqual(self.api.verdict(cases,rows,{}, {},self.receipt,1)['status'],'failed')

    def test_fixed_worker_keeps_all_cases_thresholds_and_fresh_cycle_requirements(self):
        original=self.api.registry();configured=self.api.with_fixed_worker(original,self.root/'fixed-worker')
        self.assertEqual([c.id for c in configured],[c.id for c in original])
        for old,new in zip(original,configured):
            self.assertEqual(old.timeout,new.timeout);self.assertEqual(old.required,new.required);self.assertEqual(old.dependencies,new.dependencies)
            if old.id=='worker':self.assertEqual(new.script,'tools/sdk/reuse_worker.py')
            elif old.id=='input-cycles':self.assertEqual(new.args,old.args+('--worker',str((self.root/'fixed-worker').resolve())))
            else:self.assertEqual(old,new)

    def test_registry_has_no_old_artifact_paths_and_resolves_dependencies(self):
        cases = self.api.registry()
        seen = set()
        for case in cases:
            self.assertNotIn(case.id, seen)
            self.assertTrue(set(case.dependencies) <= seen, case.id)
            self.assertNotIn('Validation/Content', ' '.join(case.args))
            seen.add(case.id)
        for name in ['storage-matrix', 'storage-evidence-negatives', 'd-regression', 'desktop-idle-1800', 'desktop-continuous-unmapped']:
            self.assertIn(name, seen)

    def test_registry_status_and_folder_match_available_runner_sources(self):
        root=SDK.parents[1]
        expected_missing={'Supervisor/Tests/run_keyboard_heartbeat.py','Supervisor/Tests/run_hit_trace.py'}
        for case in self.api.registry():
            path=root/case.script
            if not path.is_file():
                self.assertIn(case.script,expected_missing)
                continue
            constants=[node.value for node in ast.walk(ast.parse(path.read_text(encoding='utf-8-sig'))) if isinstance(node,ast.Constant) and isinstance(node.value,str)]
            for status in case.statuses:
                self.assertIn(status,constants,case.id)
            if case.folder:
                self.assertTrue(any(case.folder.split('/')[-1] in value for value in constants),case.id+':'+case.folder)

    def test_fixed_B_and_D_report_schema_contracts(self):
        cases={case.id:case for case in self.api.registry()}
        self.assertEqual(cases['compile-package'].report_file,'reports/stage-B-unity.json')
        self.assertEqual(cases['compile-package'].source_field,'source_hashes')
        self.assertEqual(cases['compile-package'].statuses,())
        self.assertEqual(cases['compile-package'].required,{'engine':'2022.3.62f3','rejected':True})
        self.assertEqual(cases['d-regression'].folder,'D/acceptance')
        self.assertEqual(cases['d-regression'].source_field,'sources')
        self.assertEqual(cases['d-regression'].statuses,('passed-stage-D-resource-supervision-only',))


if __name__ == '__main__':
    unittest.main()
