"""Pure registration/gate checks; no Unity, Player, or input execution."""
from pathlib import Path
import sys
import unittest
import json
import tempfile
import time
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import verify_e

KEYS=('Space','A','Left','Right','Up','Down','Primary','Secondary')
REQUIRED={'continuity-author','continuity-preflight','continuity-short','continuity-drop-release','continuity-1800',*("continuity-kill-"+key.lower() for key in KEYS)}

class ContinuityRegistration(unittest.TestCase):
    def test_outer_wrapper_rechecks_rows_and_summary_not_only_status(self):
        sys.path.insert(0,str(Path(__file__).resolve().parents[3]/'Supervisor/Tests'))
        from input_continuity_evidence import NAMES,BITS
        with tempfile.TemporaryDirectory() as temp:
            folder=Path(temp);path=folder/'report.json'
            proof={'mode':'development-short-only','requested_seconds':1,'elapsed_ms':1400,'cycles':1,'author_edges':14,'source_edges':14,'dropped_edges':0,'owed':0,'epoch':7}
            rows=[{'edge':i+1,'revision':i+1,'source_edges':i+1,'cycle':0,'step':i,'ack':NAMES[i//2]+('-up' if i%2 else '-down'),'epoch':7,'obligations':0 if i%2 else BITS[i//2],'elapsed_ms':(i+1)*100} for i in range(14)]
            report={'status':'ok','interaction_seconds':1,'interaction_proof':proof,'result':{'author_durable_revision':14}}
            path.write_text(json.dumps(report))
            (folder/'interaction-continuity.json').write_text(json.dumps(proof))
            journal=folder/'interaction-edges.jsonl'
            journal.write_text('\n'.join(map(json.dumps,rows)))
            case=verify_e.Case('probe','unused',statuses=('ok',),sources_required=False,role='continuity',required={'interaction_seconds':1})
            verify_e.validate_report(case,path,folder,set(),time.time()-5,{})
            journal.write_text('\n'.join(map(json.dumps,rows[:-1])))
            with self.assertRaisesRegex(ValueError,'edge-count'):
                verify_e.validate_report(case,path,folder,set(),time.time()-5,{})
            journal.write_text('\n'.join(map(json.dumps,rows)))
            report['result']['author_durable_revision']=13;path.write_text(json.dumps(report))
            with self.assertRaisesRegex(ValueError,'continuity-proof-binding'):
                verify_e.validate_report(case,path,folder,set(),time.time()-5,{})

    def test_fresh_build_and_complete_dependency_closure(self):
        cases=verify_e.registry();by={c.id:c for c in cases}
        self.assertTrue(REQUIRED<=by.keys())
        self.assertEqual(by['continuity-author'].args,('--continuity-fixture',))
        closure={c.id for c in verify_e.selection(cases,['continuity-1800'])}
        self.assertTrue(REQUIRED|{'worker'}<=closure)
        for name in REQUIRED-{'continuity-author'}:
            self.assertNotIn('Validation/', ' '.join(by[name].args))
        formal=by['continuity-1800']
        self.assertIn('1800',formal.args)
        self.assertEqual(formal.required['interaction_proof.requested_seconds'],1800)
        self.assertEqual(formal.required['interaction_drop_release'],False)
        self.assertEqual(formal.role,'continuity')

    def test_eight_key_cases_bind_name_and_ledger(self):
        by={c.id:c for c in verify_e.registry()}
        for key,bit in zip(KEYS,[1,4,8,16,32,64,2,128]):
            case=by['continuity-kill-'+key.lower()]
            self.assertEqual(case.args,('{continuity-short}','--key',key))
            self.assertEqual(case.required['ledger_bit'],bit)
            for field in ['without_outer_cleanup_held','native_released','client_kernel_exit_observed','outer_job_active_zero']:
                self.assertIs(case.required[field],True)
            self.assertEqual(case.required['cleanup_errors'],[])

    def test_registered_cases_alone_do_not_close_gate(self):
        cases=verify_e.registry();rows=[{'id':c.id,'status':'not-run'} for c in cases]
        self.assertIn('interaction-1800',verify_e.verdict(cases,rows)['unimplemented_gates'])
        for row in rows:row['status']='passed'
        result=verify_e.verdict(cases,rows)
        self.assertNotIn('interaction-1800',result['unimplemented_gates'])
        self.assertEqual(result['status'],'failed')
        for missing in REQUIRED:
            for row in rows:row['status']='failed' if row['id']==missing else 'passed'
            self.assertIn('interaction-1800',verify_e.verdict(cases,rows)['unimplemented_gates'],missing)
        self.assertTrue({'historical-failure-closure','start-stop-100','fault-cycles-20-each','supported-configuration-catalog'}<=result['unimplemented_gates'].keys())

if __name__=='__main__':unittest.main()
