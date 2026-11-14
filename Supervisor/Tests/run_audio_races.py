"""Owned background controls for real audio implementation entry points.

Activation uses COM substitutes, publication uses a framed hostile child; neither
captures a real device. This is not a real device/session event acceptance gate.
"""
from pathlib import Path
import argparse, hashlib, json, shutil, subprocess, sys, uuid
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'));sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from check_contracts import DATA
from windows_framework import references
from native_build import compile_probe
from case_process import CaseProcess
from provenance import sources

NEGATIVE_STATUS={
    'activation-result':'expected-activation-result-fault-rejected',
    'activation-hresult':'expected-activation-hresult-fault-rejected',
    'activation-timeout':'expected-activation-timeout-fault-rejected',
    'publication-guard':'expected-publication-guard-fault-rejected',
    'terminal-guard':'expected-terminal-guard-fault-rejected',
}

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def child(out,negative):
    names=['Supervisor/Tests/AudioActivationLateChecks.cpp','Supervisor/Interactive/ProcessAudioSampler.cpp',
           'Supervisor/Interactive/AudioFeatures.h','Supervisor/Interactive/AudioTiming.h',
           'Supervisor/Tests/AudioPublishRaceChecks.cs','Supervisor/Broker/AudioBroker.cs',
           'Supervisor/Tests/AudioFaultHelper.cpp','Supervisor/Windows/TrustedAudioProcess.cs',
           'Supervisor/Windows/Native.cs','Supervisor/Windows/RestrictedProcess.cs',
           'Supervisor/Tests/run_audio_races.py','tools/sdk/experiments/native_build.py']
    report=dict(status='failed',negative=negative,sources={name:digest(ROOT/name) for name in names},
                scope='real Capture/Completion and Broker parser/publication; simulated activation/framed helper only')
    def run(command,name):
        result=subprocess.run(command,capture_output=True,text=True,timeout=30,creationflags=subprocess.CREATE_NO_WINDOW)
        log=result.stdout+result.stderr;(out/name).write_text(log,encoding='utf-8');return result,log
    try:
        for name in names[:4]:
            target=out/'native'/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(ROOT/name,target)
        sampler=out/'native/Supervisor/Interactive/ProcessAudioSampler.cpp'
        if negative=='activation-result':
            text=sampler.read_text();needle='result=operation->GetActivateResult(&activated,&unknown);';assert text.count(needle)==1
            sampler.write_text(text.replace(needle,'(void)operation->GetActivateResult(&activated,&unknown);result=S_OK;'))
        if negative=='activation-hresult':
            text=sampler.read_text();needle='if(SUCCEEDED(result))result=activated;';assert text.count(needle)==1
            sampler.write_text(text.replace(needle,'/* controlled ignored activation HRESULT */'))
        if negative=='activation-timeout':
            text=sampler.read_text();needle='WaitForSingleObject(completion->event.value,2000)';assert text.count(needle)==1
            sampler.write_text(text.replace(needle,'WaitForSingleObject(completion->event.value,5000)'))
        compile_probe(out/'native/Supervisor/Tests/AudioActivationLateChecks.cpp',out/'Activation.exe')
        result,log=run([str(out/'Activation.exe')],'activation.log')
        report['activation_exit']=result.returncode
        report['activation_cases']=[json.loads(line) for line in result.stdout.splitlines() if line.startswith('{')]
        if negative=='activation-timeout':
            assert result.returncode==1 and 'FAIL activation-wait-parameter-exactly-2000ms' in log and 'CLEANUP activation-objects=0' in log
            report['status']=NEGATIVE_STATUS[negative];return
        if negative in ('activation-result','activation-hresult'):
            assert result.returncode==1 and 'FAIL failed-activation-result-must-not-initialize' in log and 'CLEANUP activation-objects=0' in log
            preceding=['on-time-success'] if negative=='activation-result' else ['on-time-success','GetActivateResult-failure']
            assert [row['case'] for row in report['activation_cases']]==preceding
            report['status']=NEGATIVE_STATUS[negative];return
        result.check_returncode()
        expected={'on-time-success','GetActivateResult-failure','activation-HRESULT-failure','timeout-then-late-success','timeout-then-late-GetActivateResult-failure'}
        assert len(report['activation_cases'])==len(expected) and {row['case'] for row in report['activation_cases']}==expected
        broker=(ROOT/'Supervisor/Broker/AudioBroker.cs').read_text()
        for before,after in [('sequence=incoming;lastSent=sent;','sequence=incoming;lastSent=sent;AudioRaceControl.Parsed(this,incoming);'),
                             ('Volatile.Write(ref received,1);if(terminal)','Volatile.Write(ref received,1);AudioRaceControl.Published(this);if(terminal)')]:
            assert broker.count(before)==1;broker=broker.replace(before,after)
        if negative in ('publication-guard','terminal-guard'):
            needle='if(Volatile.Read(ref closed)!=0||terminal)break;';assert broker.count(needle)==1
            broker=broker.replace(needle,'if(Volatile.Read(ref closed)!=0)break;' if negative=='terminal-guard' else 'if(terminal)break;')
        compiled=out/'InstrumentedAudioBroker.cs';compiled.write_text(broker)
        report['instrumentation']='barrier after full frame validation, witness after actual publication; production deadlines unchanged'
        files=[ROOT/name for name in names if name.endswith('.cs') and name!='Supervisor/Broker/AudioBroker.cs']+[compiled]
        shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
        result,log=run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],'broker-compile.log');result.check_returncode()
        compile_probe(ROOT/'Supervisor/Tests/AudioFaultHelper.cpp',out/'FaultHelper.exe')
        result,log=run([str(out/'Checks.exe'),str(out/'FaultHelper.exe'),str(out)],'broker.log');report['broker_exit']=result.returncode
        if negative in ('publication-guard','terminal-guard'):
            assert result.returncode==1 and 'System.Exception: late-validated-frame-not-published' in log and 'CLEANUP all-owned-children-released' in log
            assert ('CASE watchdog-before-late-publication' in log)==(negative=='terminal-guard')
            assert log.count('CLEANUP all-owned-children-released')==(2 if negative=='terminal-guard' else 1)
            report['status']=NEGATIVE_STATUS[negative]
        else:
            result.check_returncode();report['broker_cases']=json.loads((out/'broker-results.json').read_text())
            assert {row['case'] for row in report['broker_cases']}=={'dispose-before-late-publication','watchdog-before-late-publication'}
            assert len(report['broker_cases'])==2 and all(row['children_remaining']==0 and row['late_publications']==0 for row in report['broker_cases'])
            report['status']='passed-controlled-audio-races'
    finally:
        report['source_stable']=all(digest(ROOT/name)==value for name,value in report['sources'].items())
        report['compiled_inputs']={p.relative_to(out).as_posix():digest(p) for p in out.rglob('*') if p.is_file() and p.suffix in ('.cs','.cpp','.h','.exe')}
        (out/'child-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
        assert report['source_stable']

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--negative',choices=['activation-result','activation-hresult','activation-timeout','publication-guard','terminal-guard']);parser.add_argument('--child',type=Path);args=parser.parse_args()
    if args.child:child(args.child,args.negative);return
    out=ROOT/'Validation/Content/sdk-runtime/E/audio-races'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
    process=CaseProcess();report={'status':'failed','scope':'background-controls-only-no-real-device-events','sources':sources()}
    try:
        with (out/'execution.log').open('w') as log:
            process.start(Path(__file__),['--child',str(out),*(['--negative',args.negative] if args.negative else [])],ROOT,log)
            code=process.wait(120);report['exit_code']=code
            if (out/'child-report.json').exists():report['observations']=json.loads((out/'child-report.json').read_text())
            assert code==0
        report['status']=report['observations']['status']
    finally:
        try:process.close()
        except BaseException as error:report['cleanup_error']=repr(error);report['status']='cleanup-error';raise
        finally:
            report['process_tree']=process.evidence
            if process.evidence.get('unexpected_remaining_members'):report['status']='cleanup-error'
            report['source_stable']=sources()==report['sources']
            if not report['source_stable']:report['status']='source-drift'
            (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    assert report['status'] not in ('cleanup-error','source-drift')

if __name__=='__main__':main()
