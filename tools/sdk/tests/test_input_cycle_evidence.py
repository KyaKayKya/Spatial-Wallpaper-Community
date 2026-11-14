import copy
from dataclasses import asdict, replace
import json
from pathlib import Path
import sys
import tempfile
import time
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import verify_e as engine
import input_cycles
import input_cycle_evidence as proof
from tools.sdk.tests import test_input_cycles as fixtures


class CycleEvidence(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(); cls.addClassCleanup(cls.temp.cleanup)
        cls.base = Path(cls.temp.name); cls.output = cls.base/'E/input-cycles'/('a'*32); cls.output.mkdir(parents=True)
        cls.path = cls.output/'report.json'
        cls.policy = engine.read(engine.ROOT/'tools/sdk/release-policy.json')
        # Build-role admission has its own tests; this fixture exercises 802 real report/log bindings.
        cls.catalog = [replace(c, role='') if c.role in ('worker', 'author') else c for c in engine.registry()]
        cls.byid = {c.id:c for c in cls.catalog}
        cls.receipt = {'controlled-fixture-source': 'a'*64}
        cls.plan = input_cycles.schedule(cls.policy)
        fixture = fixtures.InputCycles(); fixture.setUp()
        cls.environment = {'configuration_identity': {'controlled': 'same'}}
        for name in ('environment.json', 'environment-final.json'):
            (cls.output/name).write_text(json.dumps(cls.environment))
        cls.started = time.time()-2
        artifacts = {}; serial = 0
        def row(case, step=None):
            nonlocal serial
            serial += 1
            folder = cls.base/case.folder/f'{serial:032x}'; folder.mkdir(parents=True)
            path = folder/'report.json'
            if case.id.startswith('owned-timeout-'):
                data = {key:True for key in ('outer_job_assigned','client_kernel_exit_observed','without_outer_cleanup_held',
                                             'held_before_kill','native_released','outer_job_active_zero')}
                timing = fixture.forced_timing(); timing['timing_clock']='runner-monotonic-ms'
                timing['client_kernel_exited_at_ms']=timing.pop('worker_kernel_exited_at_ms')
                data.update(ledger_after_cleanup=0,published_injector_identities=[[10000+serial,20000+serial]],forced_lifecycle=timing)
                prepared=Path(artifacts['desktop-base']['path'])
                data.update(compiled_report=engine.read(prepared),client_sha256=engine.digest(prepared.parent/'Client.exe'))
            else:
                data = fixture.native(); life=data['result']['lifecycle']
                life.update(attempt=f'{serial:032x}',worker_pid=1000+serial,worker_creation_filetime=30000+serial,retained_worker_creation_filetime=30000+serial)
                (folder/'Client.exe').write_bytes(b'controlled client image')
                if step:data.update(worker_report_sha256=artifacts['worker']['sha256'],author_report_sha256=artifacts['input-author']['sha256'])
            data.update(status=case.statuses[0],sources=cls.receipt)
            for key,value in case.required.items():
                target=data;parts=key.split('.')
                for part in parts[:-1]: target=target.setdefault(part,{})
                target[parts[-1]]=value
            if case.id=='desktop-cleanup-negative': (folder/'client.log').write_text('PASS author-storage-revision-3')
            path.write_text(json.dumps(data))
            logfolder=cls.output/step['id'] if step else cls.output; logfolder.mkdir(exist_ok=True)
            log=logfolder/(case.id+'.log');log.write_text('controlled successful execution')
            inputs={dep:str(Path(artifacts[dep]['path']).parent) for dep in case.dependencies}
            artifact={'path':str(path),'sha256':engine.digest(path),'status':data['status'],
                      'inventory_scope':'directory','files':engine.inventory(folder)}
            value={'id':case.id,'status':'passed','artifact':artifact,'log':str(log),'log_sha256':engine.digest(log),
                   'command':[sys.executable,str(engine.ROOT/case.script),*[a.format_map(inputs) for a in case.args]],
                   'script_sha256':engine.digest(engine.ROOT/case.script),'inputs':inputs,'exit_code':0,
                   'started_at_unix':cls.started+serial*.00001,'finished_at_unix':cls.started+serial*.00001+.000001,
                   'configuration_before':cls.environment['configuration_identity'],
                   'configuration_after':cls.environment['configuration_identity'],
                   'process_tree':{'assignment_complete':True,'handshake_released':True,'active_after_termination':0,
                                   'root_kernel_exited':True,'unexpected_remaining_members':False}}
            if step:
                value['step']=step
                value['cycle_proof']=(input_cycles.timeout_proof(data,set()) if case.id.startswith('owned-timeout-')
                                      else input_cycles.native_proof(data,cls.policy,set(),case.id=='desktop-cleanup-negative'))
            artifacts[case.id]=artifact
            return value
        dependencies=[row(c) for c in engine.selection(cls.catalog,['worker','input-author'])]
        steps=[row(cls.byid[s['case']],s) for s in cls.plan]
        cls.report={'schema':1,'run_id':cls.output.name,'status':'passed-input-cycle-gate-only','sources':cls.receipt,
                    'policy':cls.policy,'policy_sha256':engine.digest(engine.ROOT/'tools/sdk/release-policy.json'),
                    'started_at_unix':cls.started,'finished_at_unix':time.time(), 'plan':cls.plan,'steps':steps,
                    'dependencies':dependencies,'catalog':[asdict(cls.byid[n]) for n in dict.fromkeys(s['case'] for s in cls.plan)],
                    'scope':'input-cycle-gate-not-E-verdict','source_unchanged':True,'final_evidence_unchanged':True,
                    'configuration_identity_unchanged':True,
                    'environment_sha256':engine.digest(cls.output/'environment.json'),
                    'environment_final_sha256':engine.digest(cls.output/'environment-final.json')}

    def verify(self, report=None):
        self.path.write_text(json.dumps(self.report if report is None else report))
        with patch.object(engine,'BASE',self.base):
            return proof.validate_cycle_report(self.path,self.receipt,self.started-1,self.catalog)

    def test_complete_800_steps_revalidated_not_multiplied(self):
        result=self.verify()
        self.assertEqual(result['steps'],800)
        self.assertEqual(result['normal_cycles'],100)
        self.assertEqual(set(result['fault_cycles']),set(input_cycles.FAULT_CASES))
        self.assertEqual(set(result['fault_cycles'].values()),{20})

    def test_missing_duplicate_reordered_or_forged_pass_rejected(self):
        for mutate in [lambda r:r['steps'].pop(),lambda r:r['steps'].__setitem__(1,r['steps'][0]),
                       lambda r:r['steps'].reverse(),lambda r:r.update(steps=[]),
                       lambda r:r['dependencies'].pop(),lambda r:r.update(source_unchanged=False),
                       lambda r:r['plan'][0].update(cycle=99)]:
            report=copy.deepcopy(self.report);mutate(report)
            with self.assertRaises(ValueError):self.verify(report)

    def test_old_receipt_rejected_even_when_report_rewritten_now(self):
        report=copy.deepcopy(self.report);report['started_at_unix']=1
        with self.assertRaisesRegex(ValueError,'fresh'):self.verify(report)

    def test_forged_proof_duration_and_reused_report_rejected(self):
        for mutate in [lambda r:r['steps'][0]['cycle_proof'].update(normal_stop_to_kernel_exit_ms=1),
                       lambda r:r['steps'][1].update(artifact=r['steps'][0]['artifact']),
                       lambda r:r['steps'][0]['process_tree'].update(active_after_termination=1),
                       lambda r:r['steps'][0].update(configuration_after={'different':True})]:
            report=copy.deepcopy(self.report);mutate(report)
            with self.assertRaises(ValueError):self.verify(report)

    def test_actual_wrong_stop_timing_rejected_even_with_updated_hashes(self):
        report=copy.deepcopy(self.report);row=report['steps'][0];path=Path(row['artifact']['path']);raw=path.read_bytes()
        try:
            data=json.loads(raw);data['result']['lifecycle']['normal_stop_to_kernel_exit_ms']=501
            path.write_text(json.dumps(data));row['artifact'].update(sha256=engine.digest(path),files=engine.inventory(path.parent))
            row['cycle_proof']=data['result']['lifecycle']
            with self.assertRaisesRegex(ValueError,'normal-stop-grace'):self.verify(report)
        finally:path.write_bytes(raw)

    def test_reused_worker_and_wrong_build_binding_rejected_with_rehashed_artifact(self):
        for mutation, oracle in [('worker','reused-worker'),('build','worker-author-binding')]:
            report=copy.deepcopy(self.report);row=report['steps'][1];path=Path(row['artifact']['path']);raw=path.read_bytes()
            try:
                data=json.loads(raw)
                if mutation=='worker':
                    first=report['steps'][0]['cycle_proof']
                    for key in ('worker_pid','worker_creation_filetime','retained_worker_creation_filetime'):
                        data['result']['lifecycle'][key]=first[key]
                else:data['worker_report_sha256']='f'*64
                path.write_text(json.dumps(data));row['artifact'].update(sha256=engine.digest(path),files=engine.inventory(path.parent))
                row['cycle_proof']=data['result']['lifecycle']
                with self.assertRaisesRegex(ValueError,oracle):self.verify(report)
            finally:path.write_bytes(raw)

    def test_unified_role_and_verdict_revalidate_before_closing_only_two_gates(self):
        self.path.write_text(json.dumps(self.report))
        case=next(c for c in self.catalog if c.id=='input-cycles')
        with patch.object(engine,'BASE',self.base),patch.object(engine,'registry',return_value=self.catalog):
            artifact=engine.validate_report(case,self.path,self.base,set(),self.started-1,self.receipt)
            row={'id':'input-cycles','status':'passed','artifact':artifact,'started_at_unix':self.started-1,
                 'process_tree':self.report['steps'][0]['process_tree']}
            result=engine.verdict([case],[row],receipt=self.receipt,run_started_at=self.started-2)
            self.assertNotIn('start-stop-100',result['unimplemented_gates'])
            self.assertNotIn('fault-cycles-20-each',result['unimplemented_gates'])
            self.assertIn('historical-failure-closure',result['unimplemented_gates'])
            self.assertEqual(result['status'],'failed')
            row['artifact']['cycle_matrix']['normal_cycles']=10000
            rejected=engine.verdict([case],[row],receipt=self.receipt,run_started_at=self.started-2)
            self.assertIn('start-stop-100',rejected['unimplemented_gates'])
            self.assertIn('artifact-drift',rejected['input_cycle_validation']['error'])

    def test_timeout_preparation_and_separate_force_timing_rechecked(self):
        index=next(i for i,row in enumerate(self.report['steps']) if row['id'].startswith('owned-timeout-'))
        for change,oracle in [('preparation','preparation-binding'),('client','preparation-binding'),('timing','forced-termination-duration')]:
            report=copy.deepcopy(self.report);row=report['steps'][index];path=Path(row['artifact']['path']);raw=path.read_bytes()
            try:
                data=json.loads(raw)
                if change=='preparation':data['compiled_report']['result']['lifecycle']['attempt']='f'*32
                elif change=='client':data['client_sha256']='f'*64
                else:data['forced_lifecycle']['forced_to_kernel_exit_ms']=1
                path.write_text(json.dumps(data));row['artifact'].update(sha256=engine.digest(path),files=engine.inventory(path.parent))
                with self.assertRaisesRegex(ValueError,oracle):self.verify(report)
            finally:path.write_bytes(raw)

    def test_registry_and_verdict_reject_status_only_shortcut(self):
        cases=engine.registry();case=next(c for c in cases if c.id=='input-cycles')
        self.assertEqual(case.role,'input-cycles')
        result=engine.verdict([case],[{'id':case.id,'status':'passed'}])
        self.assertIn('start-stop-100',result['unimplemented_gates'])
        self.assertIn('fault-cycles-20-each',result['unimplemented_gates'])

    def test_real_nested_case_jobs_drain_without_running_input_or_unity(self):
        from case_process import CaseProcess
        directory=self.base/'nested-owner';directory.mkdir(exist_ok=True)
        leaf=directory/'leaf.py';leaf.write_text('pass')
        wrapper=directory/'wrapper.py'
        wrapper.write_text('import sys,json\nfrom pathlib import Path\nsys.path.insert(0,'+repr(str(engine.ROOT/'tools/sdk'))+')\nfrom case_process import CaseProcess\nowner=CaseProcess()\nwith open('+repr(str(directory/'inner.log'))+',"w") as log:\n try:\n  owner.start('+repr(str(leaf))+',[],'+repr(str(directory))+',log)\n  assert owner.wait(5)==0\n finally:owner.close()\nassert owner.evidence["active_after_termination"]==0 and owner.evidence["root_kernel_exited"] and not owner.evidence["unexpected_remaining_members"]\nprint("nested-owner-drained")\n')
        outer=CaseProcess()
        with (directory/'outer.log').open('w') as log:
            try:
                outer.start(str(wrapper),[],directory,log)
                self.assertEqual(outer.wait(10),0)
            finally:outer.close()
        input_cycles.owner_proof({'status':'passed','process_tree':outer.evidence})
        self.assertIn('nested-owner-drained',(directory/'outer.log').read_text())


if __name__=='__main__':unittest.main()
