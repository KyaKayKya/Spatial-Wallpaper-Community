"""Install an SDK copy in an independent project, create and sandbox-preview its samples."""
from pathlib import Path
import argparse,ctypes,hashlib,json,os,shutil,subprocess,sys,tarfile,threading,time,uuid
from build_execution import ROOT,UNITY
from release_identity import identity
from toolchain_process import run_toolchain

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk',type=Path,default=ROOT/'Packages/com.spatialwallpaper.sdk')
    parser.add_argument('--sdk-tgz',type=Path,help='Install the actual candidate tarball through UPM, rather than embedding a source copy.')
    parser.add_argument('--runtime',type=Path,required=True)
    parser.add_argument('--reuse-project',type=Path)
    parser.add_argument('--ordinary-play',action='store_true')
    parser.add_argument('--play-broker',type=Path)
    parser.add_argument('--sample',choices=['InputStorage','Audio','DynamicResources','ShaderPrefab','LlmReply'])
    parser.add_argument('--negative-skip-llm-write',action='store_true')
    parser.add_argument('--audio-tone-fixture',action='store_true')
    parser.add_argument('--negative-omit-audio-selection',action='store_true')
    args=parser.parse_args();sdk=args.sdk.resolve();runtime=args.runtime.resolve()
    assert not args.negative_omit_audio_selection or args.audio_tone_fixture,'missing-selection negative requires an owned tone fixture'
    out=ROOT/'Validation/Content/sdk-runtime/G/clean-samples'/uuid.uuid4().hex
    out.mkdir(parents=True);print(out,flush=True)
    report={'status':'failed','scope':__doc__,'identity':identity(),'phase':'sample-preflight'}
    tone=None;stop_observer=None;finished=threading.Event()
    try:
        if args.sdk_tgz:
            tarball=args.sdk_tgz.resolve();inspection=out/'tarball-inspection';inspection.mkdir()
            with tarfile.open(tarball,'r:gz') as archive:
                for member in archive.getmembers():
                    if member.name=='package' and member.isdir():continue
                    target=(inspection/member.name).resolve()
                    if not target.is_relative_to(inspection) or not member.name.startswith('package/') or not (member.isdir() or member.isfile()):raise ValueError('unsafe SDK tarball member')
                    if member.isfile():
                        target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(archive.extractfile(member).read())
            sdk=inspection/'package';report['installed_tarball']={'path':str(tarball),'sha256':hashlib.sha256(tarball.read_bytes()).hexdigest()}
        package=json.loads((sdk/'package.json').read_text())
        assert any(item['path']=='Samples~/StarterKit' for item in package['samples']),'clean SDK starter sample is missing from Package Manager'
        sample=sdk/'Samples~/StarterKit'
        for name in ['README.md','Editor/SdkSamples.cs','Runtime/InputStorageSample.cs',
                     'Runtime/AudioSample.cs','Runtime/DynamicResourcesSample.cs',
                     'Runtime/ShaderPrefabSample.cs','Runtime/LlmReplySample.cs','SamplePulse.shader']:
            assert (sample/name).is_file(),'clean sample input missing: '+name
        projects=(ROOT/'Validation/Sdk').resolve()
        project=args.reuse_project.resolve() if args.reuse_project else projects/('gs'+uuid.uuid4().hex[:10])
        marker=project/'clean-samples-project.json'
        expected={'owner':'sdk-clean-samples','project':str(project)}
        if args.reuse_project:
            assert project.parent==projects and marker.is_file() and json.loads(marker.read_text())==expected,'not an owned clean sample project'
        else:
            project.mkdir(parents=True);marker.write_text(json.dumps(expected))
        for target in [project/'Packages/com.spatialwallpaper.sdk',project/'Assets/ImportedSdkSamples']:
            target=target.resolve();assert target.is_relative_to(project)
            if target.exists():shutil.rmtree(target)
        if not args.sdk_tgz:shutil.copytree(sdk,project/'Packages/com.spatialwallpaper.sdk')
        if args.play_broker:
            assert not args.sdk_tgz,'tarball validation must use its own bundled broker'
            broker=args.play_broker.resolve();target=project/'Packages/com.spatialwallpaper.sdk/Editor/Windows~'
            if target.exists():shutil.rmtree(target)
            shutil.copytree(broker,target)
            report['bundled_play_broker']={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in target.iterdir() if p.is_file()}
        shutil.copytree(sample,project/'Assets/ImportedSdkSamples')
        if args.audio_tone_fixture:
            assert args.sample=='Audio','audio fixture requires single Audio sample'
            shutil.copyfile(ROOT/'tools/sdk/experiments/AudioSampleObservation.cs',project/'Assets/ImportedSdkSamples/Runtime/AudioSample.cs')
            report['fixture_mutation']='same band-driven material with test-only durable heard and subsequent zero-band observations; wiring check only, not continuous-silence or audio long-run acceptance'
        if args.negative_skip_llm_write:
            assert not args.ordinary_play and args.sample=='LlmReply','LLM write negative requires the single sandbox LLM sample'
            author=project/'Assets/ImportedSdkSamples/Runtime/LlmReplySample.cs'
            text=author.read_text();needle='storage=Storage.BeginWriteText("starter-llm-reply",reply,version);phase=3;'
            assert text.count(needle)==1;author.write_text(text.replace(needle,'storage=0;phase=5;'))
            report['fixture_mutation']='suppress current sample LLM reply persistence only'
        for folder in ['Assets/Editor','Packages','ProjectSettings']:(project/folder).mkdir(parents=True,exist_ok=True)
        shutil.copyfile(ROOT/'tools/sdk/experiments/CleanSamplesChecks.cs',project/'Assets/Editor/CleanSamplesChecks.cs')
        shutil.copyfile(ROOT/'tools/sdk/experiments/CleanSamplesPlayChecks.cs',project/'Assets/Editor/CleanSamplesPlayChecks.cs')
        shutil.copyfile(ROOT/'Prototype/ProjectSettings/ProjectVersion.txt',project/'ProjectSettings/ProjectVersion.txt')
        # A local package copy, never an absolute link back to the repository SDK.
        package_reference='file:'+tarball.as_posix() if args.sdk_tgz else 'file:com.spatialwallpaper.sdk'
        (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{'com.spatialwallpaper.sdk':package_reference}}))
        inputs={p.relative_to(sdk).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sdk.rglob('*') if p.is_file()}
        report.update(phase='installed-project-preview',project=str(project),sdk_inputs=inputs,toolchain={})
        cache=project/('v'+out.name[:8]);assert not cache.exists(),'sample run cache already exists'
        env=os.environ.copy();env.update(SDK_G_OUTPUT=str(out),SDK_G_RUNTIME=str(runtime),SDK_G_ATTEMPTS=str(project/'Attempts'),SDK_G_CACHE=str(cache))
        report['cache']=str(cache)
        if args.sample:env['SDK_G_SAMPLE']=args.sample
        else:env.pop('SDK_G_SAMPLE',None)
        env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
        if args.ordinary_play:
            for name in ['SDK_PLAY_BROKER','SDK_PLAY_LLM_CONFIG','SDK_PLAY_AUDIO_CONFIG','SDK_PLAY_STORE']:env.pop(name,None)
        if args.audio_tone_fixture:
            sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
            from native_build import compile_probe
            from timed_output import ready_line
            from process_observation import k
            compile_probe(ROOT/'tools/sdk/experiments/process_audio_capture.cpp',out/'Tone.exe')
            tone=subprocess.Popen([str(out/'Tone.exe'),'--tone-right-long','120'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
            assert json.loads(ready_line(tone,5))['ready'];times=[ctypes.c_ulonglong() for _ in range(4)]
            assert k.GetProcessTimes(int(tone._handle),*[ctypes.byref(v) for v in times])
            source={'process':tone.pid,'created':str(times[0].value)}
            selected=project/'Library/SpatialWallpaper/audio-source.json';selected.parent.mkdir(parents=True,exist_ok=True);selected.write_text(json.dumps(source))
            if args.negative_omit_audio_selection:selected.unlink()
            report['selected_audio']=source;env['SDK_G_AUDIO_FIXTURE']='1'
            def stop_after_heard():
                while not finished.wait(.025):
                    if (out/'audio-heard').exists():
                        if tone.poll() is None:tone.terminate()
                        return
            stop_observer=threading.Thread(target=stop_after_heard,daemon=True);stop_observer.start()
        else:
            env.pop('SDK_G_AUDIO_FIXTURE',None)
            selected=project/'Library/SpatialWallpaper/audio-source.json'
            if selected.is_file():selected.unlink()
        try:
            with (out/'toolchain.log').open('w') as log:
                run_toolchain([UNITY,'-batchmode','-projectPath',str(project),'-executeMethod','CleanSamplesPlayChecks.Run' if args.ordinary_play else 'CleanSamplesChecks.Run',
                               '-logFile',str(out/'unity.log')],ROOT,log,900,report['toolchain'],env=env)
        except subprocess.CalledProcessError:
            error=(out/'error.txt').read_text(encoding='utf-8-sig') if (out/'error.txt').exists() else ''
            llm_negative=args.negative_skip_llm_write and 'clean-sample-llm-reply-not-stored' in error
            audio_negative=args.negative_omit_audio_selection and 'clean-sample-selected-audio-not-heard' in error
            if not llm_negative and not audio_negative:raise
            assert inputs=={p.relative_to(sdk).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sdk.rglob('*') if p.is_file()},'sample SDK input drift'
            if audio_negative:assert tone.poll() is None and not (out/'audio-heard').exists(),'missing-selection negative lost its live audio source'
            report.update(status='passed-negative-selected-audio-required' if audio_negative else 'passed-negative-current-run-llm-write-required',expected_failure=error)
            return
        result=json.loads((out/'result.json').read_text(encoding='utf-8-sig'))
        if args.ordinary_play:assert result['ordinary_play'] and (result['audio_wiring_only'] if args.audio_tone_fixture else result['reply_stored']) and result['broker_exited_zero']
        else:assert len(result['samples'])==(1 if args.sample else 5) and all(item['resources_released'] for item in result['samples'])
        assert not args.negative_skip_llm_write,'accepted suppressed current LLM reply write using old cached persistence'
        assert not args.negative_omit_audio_selection,'accepted actual audio without selecting its source'
        if args.audio_tone_fixture:
            assert (out/'audio-heard').is_file() and tone.wait(timeout=5) is not None,'audio selected source cleanup unproven'
            report['selected_audio_heard_and_zero_observed_source_ended']=True
        assert inputs=={p.relative_to(sdk).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sdk.rglob('*') if p.is_file()},'sample SDK input drift'
        report.update(status='passed-development-clean-sdk-ordinary-play' if args.ordinary_play else 'passed-development-clean-sample-install-and-preview',result=result)
    finally:
        finished.set()
        if stop_observer:stop_observer.join(1)
        if tone:
            if tone.poll() is None:tone.terminate()
            tone.wait(timeout=5)
            if tone.stdout:tone.stdout.close()
            if tone.stderr:tone.stderr.close()
            report['tone_exited']=True
        (out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)

if __name__=='__main__':main()
