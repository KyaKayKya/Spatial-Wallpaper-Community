"""Synthetic receipt corruption tests, not executions of the audio matrix."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import verify_e
from e_cases import cases

ACTIVATION=('on-time-success','GetActivateResult-failure','activation-HRESULT-failure',
            'timeout-then-late-success','timeout-then-late-GetActivateResult-failure')
BROKER=('dispose-before-late-publication','watchdog-before-late-publication')
NEGATIVES=('activation-result','activation-hresult','activation-timeout','publication-guard','terminal-guard')
SOURCES=('Supervisor/Tests/AudioActivationLateChecks.cpp','Supervisor/Interactive/ProcessAudioSampler.cpp',
         'Supervisor/Interactive/AudioFeatures.h','Supervisor/Interactive/AudioTiming.h',
         'Supervisor/Tests/AudioPublishRaceChecks.cs','Supervisor/Broker/AudioBroker.cs',
         'Supervisor/Tests/AudioFaultHelper.cpp','Supervisor/Windows/TrustedAudioProcess.cs',
         'Supervisor/Windows/Native.cs','Supervisor/Windows/RestrictedProcess.cs',
         'Supervisor/Tests/run_audio_races.py','tools/sdk/experiments/native_build.py')

class RaceReceipts(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        self.folder=Path(self.tmp.name)
        self.receipt={name:'a'*64 for name in SOURCES}

    def fixture(self,negative=None):
        status='expected-'+negative+'-fault-rejected' if negative else 'passed-controlled-audio-races'
        names=['native/'+name for name in SOURCES[:4]]+['Activation.exe']
        if negative not in NEGATIVES[:3]:names+=['InstrumentedAudioBroker.cs','Checks.exe','FaultHelper.exe']
        files={}
        for name in names:
            path=self.folder/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'never-executed-fixture');files[name]=verify_e.digest(path)
        rows=[dict(case=name,wait_ms=2000 if name.startswith('timeout') else 0,wait_parameter_ms=2000,
                   initializations=int(name=='on-time-success'),result_calls=1,objects_remaining=0,event_closed=True) for name in ACTIVATION]
        child=dict(status=status,negative=negative,source_stable=True,sources=self.receipt.copy(),compiled_inputs=files,activation_exit=0,activation_cases=rows)
        if negative in NEGATIVES[:3]:
            child['activation_exit']=1;child['activation_cases']=rows[:{'activation-result':1,'activation-hresult':2,'activation-timeout':0}[negative]]
            reason='activation-wait-parameter-exactly-2000ms' if negative=='activation-timeout' else 'failed-activation-result-must-not-initialize'
            (self.folder/'activation.log').write_text('FAIL '+reason+'\nCLEANUP activation-objects=0\n')
        else:
            child['broker_exit']=int(negative is not None)
            if negative:
                names=BROKER if negative=='terminal-guard' else BROKER[:1]
                (self.folder/'broker.log').write_text(''.join('CASE '+name+'\nCLEANUP all-owned-children-released\n' for name in names)+'System.Exception: late-validated-frame-not-published\n')
            else:
                child['broker_cases']=[dict(case=name,old_pid=100+i,old_created=200+i,late_publications=0,
                    old_kernel_exited_before_release=True,new_broker_independent=True,children_remaining=0) for i,name in enumerate(BROKER)]
        return dict(status=status,sources=self.receipt,source_stable=True,exit_code=0,observations=child,
                    process_tree=dict(job_kill_on_close=True,assignment_complete=True,handshake_released=True,
                                      active_after_termination=0,root_kernel_exited=True,unexpected_remaining_members=False))

    def validate(self,data,negative=None):
        path=self.folder/'report.json';path.write_text(json.dumps(data))
        (self.folder/'child-report.json').write_text(json.dumps(data['observations']))
        if 'broker_cases' in data['observations']:(self.folder/'broker-results.json').write_text(json.dumps(data['observations']['broker_cases']))
        case=verify_e.Case(id='audio-races'+('-negative-'+negative if negative else ''),script='unused',role='audio-races',statuses=(data['status'],))
        return verify_e.validate_report(case,path,self.folder,set(),0,self.receipt)

    def test_real_seven_case_shape_and_missing_status(self):
        data=self.fixture();self.validate(data)
        for key in ('status','source_stable','activation_cases','broker_cases'):
            bad=copy.deepcopy(data);del bad['observations'][key]
            with self.subTest(key=key),self.assertRaises(ValueError):self.validate(bad)
        for field in ('activation_cases','broker_cases'):
            for value in ([],data['observations'][field][:-1],data['observations'][field]*2):
                bad=copy.deepcopy(data);bad['observations'][field]=value
                with self.subTest(field=field),self.assertRaises(ValueError):self.validate(bad)

    def test_status_source_and_cleanup_drift(self):
        data=self.fixture()
        for section,key,value in [('observations','status','failed'),('observations','negative','publication-guard'),
                                  ('observations','source_stable',False),('observations','sources',{}),
                                  ('process_tree','active_after_termination',1),('process_tree','root_kernel_exited',False),
                                  ('process_tree','assignment_complete',False),('process_tree','unexpected_remaining_members',True)]:
            bad=copy.deepcopy(data);bad[section][key]=value
            with self.subTest(section=section,key=key),self.assertRaises(ValueError):self.validate(bad)
        bad=copy.deepcopy(data);bad['source_stable']=False
        with self.assertRaises(ValueError):self.validate(bad)

    def test_case_oracles(self):
        data=self.fixture()
        for key,value in [('wait_parameter_ms',5000),('event_closed',False),('objects_remaining',1),('initializations',0)]:
            bad=copy.deepcopy(data);bad['observations']['activation_cases'][0][key]=value
            with self.subTest(key=key),self.assertRaises(ValueError):self.validate(bad)
        for key,value in [('old_kernel_exited_before_release',False),('late_publications',1),('children_remaining',1),('new_broker_independent',False)]:
            bad=copy.deepcopy(data);bad['observations']['broker_cases'][0][key]=value
            with self.subTest(key=key),self.assertRaises(ValueError):self.validate(bad)

    def test_all_expected_negatives_and_wrong_child_status(self):
        for negative in NEGATIVES:
            # Each artifact directory is fresh; no executable inventory leftovers.
            with tempfile.TemporaryDirectory() as temp:
                self.folder=Path(temp);data=self.fixture(negative);self.validate(data,negative)
                bad=copy.deepcopy(data);bad['observations']['status']='passed-controlled-audio-races'
                with self.subTest(negative=negative),self.assertRaises(ValueError):self.validate(bad,negative)

    def test_directory_identity(self):
        data=self.fixture();self.validate(data)
        (self.folder/'Activation.exe').write_bytes(b'drift')
        with self.assertRaises(ValueError):self.validate(data)

    def test_missing_compiled_file_and_source_dependency(self):
        data=self.fixture();(self.folder/'Activation.exe').unlink()
        with self.assertRaises(ValueError):self.validate(data)
        data=self.fixture();del data['observations']['sources'][SOURCES[0]]
        with self.assertRaises(ValueError):self.validate(data)

    def test_on_disk_child_and_broker_report_drift(self):
        from audio_race_evidence import validate
        data=self.fixture();self.validate(data)
        child=self.folder/'child-report.json';child.write_text('{}')
        with self.assertRaises(ValueError):validate('audio-races',data,self.folder,self.receipt)
        self.validate(data);(self.folder/'broker-results.json').write_text('[]')
        with self.assertRaises(ValueError):validate('audio-races',data,self.folder,self.receipt)

    def test_negative_oracle_cannot_be_replaced_by_generic_failure(self):
        data=self.fixture('activation-result');(self.folder/'activation.log').write_text('FAIL unexpected\nCLEANUP activation-objects=0')
        with self.assertRaises(ValueError):self.validate(data,'activation-result')

    def test_exception_fields_rejected_in_positive_and_expected_negatives(self):
        for negative in (None,*NEGATIVES):
            with tempfile.TemporaryDirectory() as temp:
                self.folder=Path(temp);data=self.fixture(negative);self.validate(data,negative)
                for section in ('outer','observations'):
                    for key in ('failure','error','primary_failure','cleanup_error'):
                        for value in (None,'contradictory-exception'):
                            bad=copy.deepcopy(data)
                            target=bad if section=='outer' else bad[section]
                            target[key]=value
                            with self.subTest(negative=negative,section=section,key=key,value=value),self.assertRaises(ValueError):
                                self.validate(bad,negative)

    def test_catalog(self):
        catalog={case.id:case for case in cases(verify_e.Case)}
        for negative in (None,*NEGATIVES):
            key='audio-races'+('-negative-'+negative if negative else '')
            self.assertIn(key,catalog)
            case=catalog[key];self.assertEqual(case.role,'audio-races');self.assertEqual(case.resource,'background')
            self.assertEqual(case.script,'Supervisor/Tests/run_audio_races.py')
            self.assertEqual(case.args,('--negative',negative) if negative else ())

if __name__=='__main__':unittest.main()
