
from pathlib import Path
import sys,shutil,subprocess,uuid,json,hashlib,platform
ROOT=Path(__file__).resolve().parents[2];sys.path[:0]=[str(ROOT/'tools/sdk'),str(ROOT/'tools/sdk/experiments')]
from windows_sandbox import launch
from provenance import sources
receipt=sources()
current=json.loads((ROOT/'Validation/Content/sdk-runtime/reports/stage-C-execution.json').read_text())
assert current['sources']==receipt,'worker-build-sources-stale'
run=current['run_id'];source=ROOT/'Validation/Content/sdk-runtime/C'/run
base=ROOT/'Validation/Content/sdk-runtime/D/lpac-worker'/uuid.uuid4().hex;base.mkdir(parents=True)
root=base/'worker';shutil.copytree(source/'player',root)
out=root/'output';out.mkdir()
for name in ['scene.spatial','fields.json']:shutil.copyfile(source/name,root/name)
args=['-batchmode','-force-d3d11','-screen-width','640','-screen-height','360','-screen-fullscreen','0','-logFile',str(out/'worker.log'),'--sdk-package',str(root/'scene.spatial'),'--sdk-fields',str(root/'fields.json'),'--sdk-output',str(out),'--sdk-run',run]
report={'sources':receipt,'platform':platform.platform(),'build_run':run,'status':'failed'}
try:
 code,sid=launch(root/'StageC.exe',args,root,isolated_desktop=True,timeout=90,lpac=True,capability_names=['registryRead'],image_mitigations=(1<<52)|(1<<56))
 value=json.loads((out/'worker-result.json').read_text(encoding='utf-8-sig'))
 assert code==0 and value['run_id']==run and value['graphics']=='Direct3D11',value
 assert value['frames']==60 and value['counter']==63 and value['peer']==140,value
 digest=hashlib.sha256((out/'frame.png').read_bytes()).hexdigest()
 assert digest==current['play_frame_sha256'],'LPAC-render-drift'
 report.update(exit_code=code,worker=value,frame_sha256=digest,status='LPAC-registryRead-image-policy-render-observed')
finally:
 (base/'report.json').write_text(json.dumps(report,indent=2));print(json.dumps({key:value for key,value in report.items() if key!='sources'},indent=2) if report['status']!='failed' else str(base));print(base)
