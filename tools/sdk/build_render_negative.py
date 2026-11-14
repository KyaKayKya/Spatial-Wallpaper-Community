"""Build an isolated mutated Worker; never eligible for production acceptance."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import uuid
from build_execution import ROOT, UNITY, create
from provenance import sources

MODES = {
    'camera-before-first': ('before', 'CompletedSequence==0', 'view.allowHDR=false;', 'render-profile-observation-camera', 0),
    'camera-after-frames': ('before', 'CompletedSequence==5', 'view.allowHDR=false;', 'render-profile-observation-camera', 5),
    'renderer-after-frames': ('before', 'CompletedSequence==5', 'var p=(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;var f=typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);((UnityEngine.Rendering.Universal.ScriptableRendererData[])f.GetValue(p))[0].SetDirty();', 'render-profile-object-binding-changed', 5),
    'data-during-warmup': ('submitted', 'CompletedSequence==0', 'var p=(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;var f=typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var rows=(UnityEngine.Rendering.Universal.ScriptableRendererData[])f.GetValue(p);rows[0]=UnityEngine.Object.Instantiate(rows[0]);', 'render-profile-object-binding-changed', 0),
    'camera-during-readback': ('readback', 'CompletedSequence==5', 'view.allowHDR=false;', 'render-profile-observation-camera', 5),
    'stop-before-late-readback': ('requested', 'CompletedSequence==5', 'Stop();', 'capture-stopped-late-readback', 5),
}

def mutated_capture(original,mode):
    location,condition,mutation,expected,frames=MODES[mode]
    anchors={
        'before':'var before=UnityRenderObservation.Read(view,target,TextureFormat.RGBA32,out var beforeBinding);',
        'submitted':'var observation=UnityRenderObservation.Read(view,target,TextureFormat.RGBA32,out var afterBinding);',
        'readback':'var completedObservation=UnityRenderObservation.Read(view,target,TextureFormat.RGBA32,out var completedBinding);',
        'requested':'\n   }catch(Exception error){reading=false;',
    }
    anchor=anchors[location];assert original.count(anchor)==1
    field='  int diagnosticMutationCount;\n'
    assert original.count('  bool initialized,')==1
    body=''
    if mode in ('renderer-after-frames','data-during-warmup'):
        body+='var dp=(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;var df=typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var dd=((UnityEngine.Rendering.Universal.ScriptableRendererData[])df.GetValue(dp))[0];var dr=dp.GetRenderer(0);'
    body+=mutation
    body+='var diagnosticArgs=System.Environment.GetCommandLineArgs();int diagnosticAttemptIndex=System.Array.IndexOf(diagnosticArgs,"--sdk-attempt");if(diagnosticAttemptIndex<0)throw new System.Exception("diagnostic-attempt-missing");'
    body+='var diagnosticMarker=new Newtonsoft.Json.Linq.JObject{{"mode","'+mode+'"},{"attempt",diagnosticArgs[diagnosticAttemptIndex+1]},{"stage","'+location+'"},{"completed_sequence",CompletedSequence},{"next_sequence",CompletedSequence+1},{"mutation_count",++diagnosticMutationCount},{"unity_frame",Time.frameCount}};'
    if mode=='renderer-after-frames':
        body+='diagnosticMarker["renderer_changed"]=!object.ReferenceEquals(dr,dp.GetRenderer(0));diagnosticMarker["pipeline_same"]=object.ReferenceEquals(dp,UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline);diagnosticMarker["data_same"]=object.ReferenceEquals(dd,((UnityEngine.Rendering.Universal.ScriptableRendererData[])df.GetValue(dp))[0]);'
    if mode=='data-during-warmup':
        body+='diagnosticMarker["data_changed"]=!object.ReferenceEquals(dd,((UnityEngine.Rendering.Universal.ScriptableRendererData[])df.GetValue(dp))[0]);diagnosticMarker["selected_index"]=(int)typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_DefaultRendererIndex",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(dp);'
    if mode=='stop-before-late-readback':
        body+='diagnosticMarker["qpc"]=SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Qpc100ns();diagnosticMarker["frequency"]=10000000L;'
    body+='Debug.Log("SDK_RENDER_NEGATIVE "+diagnosticMarker.ToString(Newtonsoft.Json.Formatting.None));'
    injected='if('+condition+'){'+body+'}\n    '
    changed=original.replace('  bool initialized,',field+'  bool initialized,').replace(anchor,injected+anchor)
    if mode=='stop-before-late-readback':
        guard='if(stopped||Error!=null||capturedProjection!=ProjectionRevision){writer.CancelCapture(slot);return;}'
        assert changed.count(guard)==1
        changed=changed.replace(guard,'if(stopped||Error!=null||capturedProjection!=ProjectionRevision){if(stopped||Error!=null){var lateArgs=System.Environment.GetCommandLineArgs();var late=new Newtonsoft.Json.Linq.JObject{{"reason","capture-stopped-late-readback"},{"attempt",lateArgs[System.Array.IndexOf(lateArgs,"--sdk-attempt")+1]},{"request_sequence",CompletedSequence+1},{"completed_sequence",CompletedSequence},{"stopped",stopped},{"has_error",Error!=null},{"qpc",SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Qpc100ns()},{"frequency",10000000L}};Debug.Log("SDK_RENDER_NEGATIVE_LATE_READBACK "+late.ToString(Newtonsoft.Json.Formatting.None));}writer.CancelCapture(slot);return;}')
    return changed

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mode',choices=MODES,required=True)
    args=parser.parse_args();mode=args.mode
    run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/render-negative-worker'/run
    out.mkdir(parents=True);print(out,flush=True)
    project=ROOT/'Validation/Sdk'/('rn'+run[:10])
    create(project,[('SdkWorkerBuild.cs',True),('StageCPipeline.cs',True)])
    sdk=project/'Packages/com.spatialwallpaper.sdk'
    shutil.copytree(ROOT/'Packages/com.spatialwallpaper.sdk',sdk)
    manifest=project/'Packages/manifest.json';value=json.loads(manifest.read_text())
    value['dependencies']['com.spatialwallpaper.sdk']='file:com.spatialwallpaper.sdk'
    manifest.write_text(json.dumps(value))
    target=sdk/'Runtime/Hosting/Unity/WorkerFrameCapture.cs';original=target.read_text()
    location,condition,mutation,expected,frames=MODES[mode]
    changed=mutated_capture(original,mode);target.write_text(changed)
    (out/'WorkerFrameCapture.cs').write_text(changed)
    receipt=sources();env=os.environ.copy();env.update(SDK_WORKER_OUTPUT=str(out),SDK_WORKER_RUN=run)
    env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
    report=dict(status='failed',release_candidate=False,mode=mode,expected_error=expected,expected_frames=frames,sources=receipt,
                original_capture_sha256=hashlib.sha256(original.encode()).hexdigest(),mutated_capture_sha256=hashlib.sha256(changed.encode()).hexdigest())
    original_sdk=ROOT/'Packages/com.spatialwallpaper.sdk'
    changed_files=[p.relative_to(sdk).as_posix() for p in sdk.rglob('*') if p.is_file() and p.read_bytes()!=(original_sdk/p.relative_to(sdk)).read_bytes()]
    assert changed_files==['Runtime/Hosting/Unity/WorkerFrameCapture.cs'],'unexpected diagnostic SDK mutation'
    report['changed_sdk_files']=changed_files
    try:
        subprocess.run([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','SdkWorkerBuild.Run','-logFile',str(out/'build.log')],env=env,timeout=900,check=True)
        value=json.loads((out/'worker-build.json').read_text(encoding='utf-8-sig'))
        assert value['run_id']==run and value['result']=='Succeeded' and value['errors']==0
        for debug in (out/'player').iterdir():
            if debug.is_dir() and debug.name.endswith('_DoNotShip'):shutil.move(str(debug),str(out/debug.name))
        assert sources()==receipt,'render negative build source drift'
        assert target.read_text()==changed,'diagnostic capture changed during build'
        after_files=[p.relative_to(sdk).as_posix() for p in sdk.rglob('*') if p.is_file() and (not (original_sdk/p.relative_to(sdk)).is_file() or p.read_bytes()!=(original_sdk/p.relative_to(sdk)).read_bytes())]
        assert after_files==changed_files,'diagnostic SDK changed during build'
        report.update(status='diagnostic-render-negative-only',build=value,
            runtime_files={p.relative_to(out/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (out/'player').rglob('*') if p.is_file()})
    finally:
        (out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)

if __name__=='__main__':main()
