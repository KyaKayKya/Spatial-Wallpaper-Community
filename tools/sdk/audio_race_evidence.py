"""Fail-closed receipts for controlled audio races, never real-device claims."""
import json
import math
from pathlib import Path
from audio_evidence import exact
from input_continuity_inputs import digest, need

ACTIVATION_CASES=('on-time-success','GetActivateResult-failure','activation-HRESULT-failure',
                  'timeout-then-late-success','timeout-then-late-GetActivateResult-failure')
BROKER_CASES=('dispose-before-late-publication','watchdog-before-late-publication')
NEGATIVES=('activation-result','activation-hresult','activation-timeout','publication-guard','terminal-guard')
SOURCE_PATHS=('Supervisor/Tests/AudioActivationLateChecks.cpp','Supervisor/Interactive/ProcessAudioSampler.cpp',
              'Supervisor/Interactive/AudioFeatures.h','Supervisor/Interactive/AudioTiming.h',
              'Supervisor/Tests/AudioPublishRaceChecks.cs','Supervisor/Broker/AudioBroker.cs',
              'Supervisor/Tests/AudioFaultHelper.cpp','Supervisor/Windows/TrustedAudioProcess.cs',
              'Supervisor/Windows/Native.cs','Supervisor/Windows/RestrictedProcess.cs',
              'Supervisor/Tests/run_audio_races.py','tools/sdk/experiments/native_build.py')

def status(negative=None):
    need(negative is None or negative in NEGATIVES,'audio-race-mode')
    return 'expected-'+negative+'-fault-rejected' if negative else 'passed-controlled-audio-races'

def case_rows(rows,expected):
    need(isinstance(rows,list) and all(isinstance(row,dict) for row in rows),'audio-race-rows')
    names=[row.get('case') for row in rows]
    need(len(names)==len(expected) and set(names)==set(expected),'audio-race-case-set')
    return rows

def validate(case_id,data,folder,current):
    negative=None if case_id=='audio-races' else case_id.removeprefix('audio-races-negative-')
    expected=status(negative)
    exact(data,'status',expected);exact(data,'source_stable',True);exact(data,'exit_code',0)
    for key in ('failure','error','primary_failure','cleanup_error'):
        need(key not in data,'audio-race-outer-exception:'+key)
    tree=data.get('process_tree');need(isinstance(tree,dict),'audio-race-process-tree')
    for key,value in dict(job_kill_on_close=True,assignment_complete=True,handshake_released=True,
                          active_after_termination=0,root_kernel_exited=True,unexpected_remaining_members=False).items():exact(tree,key,value)
    child=data.get('observations');need(isinstance(child,dict),'audio-race-child-report')
    for key in ('failure','error','primary_failure','cleanup_error'):
        need(key not in child,'audio-race-child-exception:'+key)
    need(json.loads((folder/'child-report.json').read_text(encoding='utf-8-sig'))==child,'audio-race-child-report-drift')
    exact(child,'status',expected);exact(child,'negative',negative);exact(child,'source_stable',True)
    need(set(SOURCE_PATHS)<=current.keys(),'audio-race-current-source-set')
    exact(child,'sources',{name:current[name] for name in SOURCE_PATHS})
    inventory={p.relative_to(folder).as_posix():digest(p) for p in folder.rglob('*') if p.is_file() and p.suffix in ('.cs','.cpp','.h','.exe')}
    required={'native/'+name for name in SOURCE_PATHS[:4]}|{'Activation.exe'}
    early=negative in NEGATIVES[:3]
    if not early:required|={'InstrumentedAudioBroker.cs','Checks.exe','FaultHelper.exe'}
    need(required<=inventory.keys(),'audio-race-compiled-inputs-missing')
    exact(child,'compiled_inputs',inventory)
    before={'activation-result':ACTIVATION_CASES[:1],'activation-hresult':ACTIVATION_CASES[:2],'activation-timeout':()}
    rows=case_rows(child.get('activation_cases'),before[negative] if early else ACTIVATION_CASES)
    for row in rows:
        for key,value in dict(wait_parameter_ms=2000,initializations=int(row['case']=='on-time-success'),
                              result_calls=1,objects_remaining=0,event_closed=True).items():exact(row,key,value)
        elapsed=row.get('wait_ms')
        need(type(elapsed) in (float,int) and math.isfinite(elapsed) and elapsed>=0,'audio-race-wait-observation')
        if row['case'].startswith('timeout'):need(elapsed>=1900,'audio-race-real-wait-missing')
    exact(child,'activation_exit',1 if early else 0)
    if early:
        need('broker_cases' not in child and 'broker_exit' not in child,'audio-race-unexecuted-broker-claim')
        reason='activation-wait-parameter-exactly-2000ms' if negative=='activation-timeout' else 'failed-activation-result-must-not-initialize'
        log=(folder/'activation.log').read_text(encoding='utf-8-sig')
        need('FAIL '+reason in log and 'CLEANUP activation-objects=0' in log,'audio-race-negative-activation-oracle')
        return
    exact(child,'broker_exit',1 if negative else 0)
    if negative:
        need('broker_cases' not in child,'audio-race-negative-success-claim')
        log=(folder/'broker.log').read_text(encoding='utf-8-sig')
        expected_cases=BROKER_CASES if negative=='terminal-guard' else BROKER_CASES[:1]
        need([line[5:] for line in log.splitlines() if line.startswith('CASE ')]==list(expected_cases),'audio-race-negative-stage')
        need('System.Exception: late-validated-frame-not-published' in log and
             log.count('CLEANUP all-owned-children-released')==len(expected_cases),'audio-race-negative-publication-oracle')
        return
    rows=case_rows(child.get('broker_cases'),BROKER_CASES)
    need(json.loads((folder/'broker-results.json').read_text(encoding='utf-8-sig'))==rows,'audio-race-broker-result-drift')
    identities=[]
    for row in rows:
        for key,value in dict(late_publications=0,old_kernel_exited_before_release=True,new_broker_independent=True,children_remaining=0).items():exact(row,key,value)
        for key in ('old_pid','old_created'):need(type(row.get(key)) is int and row[key]>0,'audio-race-process-identity')
        identities.append((row['old_pid'],row['old_created']))
    need(len(set(identities))==2,'audio-race-reused-process-observation')
