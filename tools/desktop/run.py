"""Actual desktop Runtime acceptance, isolated from the user's presets and SDK."""
from pathlib import Path
import argparse, json, os, subprocess, sys, time, uuid, copy
import psutil
root = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(root / 'tools/content'))
from run_e2e import Client

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--exe', type=Path, default=root / 'Prototype/Builds/Library/SpatialWallpaper.exe')
    parser.add_argument('--blueprint-data-root',type=Path)
    parser.add_argument('--additional-artwork',action='append',default=[])
    args = parser.parse_args()
    if args.exe.resolve()==(root/'Prototype/Builds/Library/SpatialWallpaper.exe').resolve():
        from verify_receipt import validate
        validate()
    out = root / 'Validation/Desktop' / ('run-' + uuid.uuid4().hex[:10])
    out.mkdir(parents=True)
    report = {'passed': False, 'cases': [], 'run': str(out)}
    report['process_memory_samples']=[]
    (out/'result.json').write_text(json.dumps(report))
    process = None
    client = None
    try:
        main_root = Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip()).parent
        command = [str(args.exe), '--desktop-runtime', 'true', '--desktop-data-root', str(out/'desktop'),
                   '--data-root', str(out/'calibration'), '--tracking-python', str(main_root/'tracker/.venv/Scripts/python.exe'),
                   '--tracking-root', str(root/'tracker'), '-screen-fullscreen','0','-logFile',str(out/'runtime.log')]
        if args.blueprint_data_root:command+=['--blueprint-data-root',str(args.blueprint_data_root.resolve())]
        process = subprocess.Popen(command)
        path = out/'desktop/runtime/connection.json'
        deadline = time.monotonic()+30
        while time.monotonic()<deadline:
            if process.poll() is not None: raise RuntimeError('Runtime exited before discovery')
            if path.exists():
                try:
                    data=json.loads(path.read_text(encoding='utf-8-sig'))
                    if data['pid']==process.pid: break
                except (OSError,ValueError): pass
            time.sleep(.1)
        else: raise RuntimeError('Desktop discovery timeout')
        client=Client(data)
        state=client.call('GetDesktopState',{})
        assert not state['camera_started'] and state['active_artwork'] is None
        report['cases'].append('startup-no-camera-no-wallpaper')
        assert state['displays']
        # The real synthetic camera is enabled explicitly, never via ordinary launch.
        state=client.call('ConfigureDesktopCamera',{'camera_id':'simulation','camera_name':'合成追踪','simulate':True,'mode_id':None})
        deadline=time.monotonic()+20
        while time.monotonic()<deadline:
            sample=client.call('GetDesktopTracking',{'preview':False,'consume':True})
            if sample.get('tracking'): break
            time.sleep(.05)
        else: raise RuntimeError('No real synthetic Tracker sample')
        descriptor=sample['tracking']['descriptor']
        # A synthetic calibration fixture lives only in the isolated test store.
        preset_id=uuid.uuid4().hex
        source={k:descriptor[k] for k in ['camera_id','head_model_id','estimator_id','intrinsics_id','geometry_id','actual_mode']}
        saved={'schema_version':2,'mapping_type':'five_parameter_v1','id':preset_id,'revision':1,'name':'Desktop synthetic',
               'manual':dict(offsetX=0,offsetY=0,scaleX=4.38,scaleY=4.38,scaleZ=6.7,input_type='normalized_unmirrored_iris_v1',source=source,display_id=state['displays'][0]['id'],display_width=.53),
               'reference_ready':True,'display_units':'nominal_m'}
        presets=out/'calibration/presets';presets.mkdir(parents=True,exist_ok=True)
        (presets/(preset_id+'.json')).write_text(json.dumps(saved),encoding='utf-8')
        request={'artwork_id':'showcase','display_id':state['displays'][0]['id'],'preset_id':saved['id'],'preset_revision':saved['revision']}
        invalid=dict(request,display_id='offline-display')
        rejected=client.call('ApplyDesktop',invalid)
        assert not rejected['succeeded'] and rejected['active_artwork'] is None
        report['cases'].append('offline-target-rejected')
        for artwork in ['showcase','pool','train','parking','minishotgame','limpool','rainycafe','mita']+args.additional_artwork:
            request['artwork_id']=artwork
            result=client.call('ApplyDesktop',request)
            assert result['succeeded'] and result['active_artwork']==artwork and result['attached'],result
            assert result['corner_error_px'] < .05,result
            target=next(d for d in result['displays'] if d['id']==request['display_id'])
            assert (result['render_width'],result['render_height'])==(target['width'],target['height']),'HWND resize did not update the Unity render buffer'
            owner=psutil.Process(process.pid)
            report['process_memory_samples'].append({'artwork':artwork,'runtime':owner.memory_info()._asdict(),
                'children':[{'name':child.name(),'memory':child.memory_info()._asdict()} for child in owner.children(recursive=True) if child.is_running()]})
            report['cases'].append('actual-apply-'+artwork)
        client.call('GetDesktopTracking',{'preview':False,'consume':False})
        paused=client.call('PauseDesktop',{'paused':True})
        assert paused['paused'] and paused['pause_reason']=='manual' and paused['scene_loaded'] and paused['attached'] and paused['audio_paused'] and not paused['camera_started'],paused
        resumed=client.call('PauseDesktop',{'paused':False})
        assert not resumed['paused'] and resumed['scene_loaded'] and resumed['attached'] and not resumed['audio_paused'] and resumed['camera_started'],resumed
        report['cases'].append('pause-keeps-native-frame-and-scene-releases-tracking-resume-restores-playback')
        problems=[]
        other=copy.deepcopy(saved);other['id']=uuid.uuid4().hex;other['manual']['offsetX']=.5
        selected=client.call('DesktopCalibrationSelection',{'selected':other['id'],'applied':other,'pause_when_invisible':False})
        if selected['render_preset_id']!=preset_id:problems.append('manager-selection-overwrote-active-calibration')
        client.call('ApplyDesktop',request)
        wrong=copy.deepcopy(saved);wrong['id']=uuid.uuid4().hex;wrong['manual']['source']['camera_id']='other-camera'
        (presets/(wrong['id']+'.json')).write_text(json.dumps(wrong),encoding='utf-8')
        bad_request=dict(request,preset_id=wrong['id'])
        mismatch=client.call('ApplyDesktop',bad_request)
        if mismatch['succeeded']:problems.append('incompatible-camera-calibration-accepted')
        client.call('ApplyDesktop',request)
        client.call('BeginDesktopPreview',{})
        time.sleep(1.3)
        state=client.call('GetDesktopState',{})
        assert state['preview'] and not state['scene_loaded']
        client.call('EndDesktopPreview',{})
        state=client.call('GetDesktopState',{})
        assert state['active_artwork']==request['artwork_id'] and state['attached']
        if state['audio_paused']:problems.append('preview-return-left-audio-paused')
        report['cases'].append('preview-release-and-restore')
        client.close();client=None
        time.sleep(1)
        from tray_roundtrip import check as check_tray
        check_tray(process.pid)
        report['cases'].append('native-tray-opens-visible-manager-and-close-keeps-runtime')
        client=Client(data)
        state=client.call('GetDesktopState',{})
        assert state['active_artwork']==request['artwork_id'] and state['attached']
        report['cases'].append('manager-disconnect-and-reconnect-keeps-wallpaper')
        result=client.call('StopDesktop',{})
        assert result['succeeded'] and result['active_artwork'] is None and not result['scene_loaded'] and not result['camera_started'] and not result['paused'],result
        report['cases'].append('stop-reveals-original-wallpaper')
        if problems:raise AssertionError('; '.join(problems))
        report['cases']+=['active-calibration-snapshot-isolated','incompatible-camera-calibration-rejected','preview-audio-restored']
        report['passed']=True
    except Exception as error:
        report['error']=repr(error)
        raise
    finally:
        if client: client.close()
        if process and process.poll() is None:
            process.terminate();process.wait(timeout=15)
        (out/'result.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
        print(json.dumps({k:v for k,v in report.items() if k!='process_memory_samples'},ensure_ascii=False,indent=2))

if __name__=='__main__': main()
