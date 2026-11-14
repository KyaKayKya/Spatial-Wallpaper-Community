"""Export in a clean SDK author project, build the independent shared loader Player."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import time

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Validation/Content/p2'
UNITY=Path('D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe')
REPORTS=OUT/'reports'

def source_receipt(role):
    sources=[]
    for p in (ROOT/'Packages/com.spatialwallpaper.sdk').rglob('*'):
        if p.is_file() and 'Samples~' not in p.parts and p.suffix in ['.cs','.meta','.shader','.asmdef','.json']:sources.append(p)
    if role=='author':sources+=list((ROOT/'Packages/com.spatialwallpaper.sdk/Samples~/StaticWindow').rglob('*'))
    else:
        for folder in ['Prototype/Assets/ContentRuntime','tools/sdk/tests/legacy-client','Prototype/Assets/Tracking/Core']:
            sources+=list((ROOT/folder).rglob('*'))
        sources += [ROOT/'Prototype/Assets/Calibration/Core/ViewportGeometry.cs',ROOT/'Prototype/Assets/Calibration/Runtime/DisplayViewport.cs']
    sources += [ROOT/'tools/content/probe-manifest.json',ROOT/'tools/content/probe-packages-lock.json',ROOT/'tools/content/build.py',ROOT/'Prototype/ProjectSettings/ProjectSettings.asset']
    return {p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(set(sources)) if p.is_file()}

def copy_tree(source,target):
    resolved=target.resolve()
    assert resolved.is_relative_to(OUT.resolve()),'Generated source copy must stay under this task output'
    if resolved.exists():shutil.rmtree('\\\\?\\'+str(resolved))
    target.mkdir(parents=True,exist_ok=True)
    for p in source.rglob('*'):
        if p.is_file():
            q=target/p.relative_to(source)
            q.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(p,q)

def prepare(role):
    project=OUT/(role+'-project')
    for name in ['Assets/Editor','Packages','ProjectSettings']:(project/name).mkdir(parents=True,exist_ok=True)
    copy_tree(ROOT/'Packages/com.spatialwallpaper.sdk',project/'Packages/com.spatialwallpaper.sdk')
    manifest=json.loads((ROOT/'tools/content/probe-manifest.json').read_text())
    manifest['dependencies']['com.spatialwallpaper.sdk']='file:com.spatialwallpaper.sdk'
    (project/'Packages/manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    shutil.copy2(ROOT/'tools/content/probe-packages-lock.json',project/'Packages/packages-lock.json')
    shutil.copy2(ROOT/'Prototype/ProjectSettings/ProjectVersion.txt',project/'ProjectSettings/ProjectVersion.txt')
    shutil.copy2(ROOT/'Prototype/ProjectSettings/ProjectSettings.asset',project/'ProjectSettings/ProjectSettings.asset')
    if role=='author':
        obsolete=project/'Assets/Editor/SdkBuild.cs'
        if obsolete.exists():obsolete.unlink()
        shutil.copy2(ROOT/'Packages/com.spatialwallpaper.sdk/Samples~/StaticWindow/Editor/StaticSampleBuild.cs',project/'Assets/Editor/StaticSampleBuild.cs')
        (project/'Assets/csc.rsp').write_text('-define:SDK_AUTHOR\n')
        shutil.copy2(ROOT/'Packages/com.spatialwallpaper.sdk/Samples~/StaticWindow/SampleExternal.shader',project/'Assets/SampleExternal.shader')
    else:
        shutil.copy2(ROOT/'tools/sdk/tests/legacy-client/SdkBuild.cs',project/'Assets/Editor/SdkBuild.cs')
        copy_tree(ROOT/'Prototype/Assets/ContentRuntime',project/'Assets/ContentRuntime')
        shutil.copy2(ROOT/'tools/sdk/tests/legacy-client/ContentPlayerBootstrap.cs',project/'Assets/ContentPlayerBootstrap.cs')
        copy_tree(ROOT/'Prototype/Assets/Tracking/Core',project/'Assets/Tracking/Core')
        for source in ['Calibration/Core/ViewportGeometry.cs','Calibration/Runtime/DisplayViewport.cs']:
            target=project/'Assets'/source;target.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(ROOT/'Prototype/Assets'/source,target)
    return project

def build(role):
    project=prepare(role)
    receipt=source_receipt(role)
    env=os.environ.copy();env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
    env['SDK_BUILD_ROLE']=role;env['SDK_BUILD_OUTPUT']=str(OUT)
    method='StaticSampleBuild.Run' if role=='author' else 'SdkBuild.Run'
    result=subprocess.run([str(UNITY),'-batchmode','-quit','-projectPath',str(project),'-executeMethod',method,'-logFile',str(OUT/(role+'-build.log'))],env=env,timeout=1200)
    print(role,'Unity exit',result.returncode,flush=True)
    if result.returncode:raise SystemExit(result.returncode)
    assert source_receipt(role)==receipt,'Build inputs changed during compilation; rebuild before verification'
    (OUT/(role+'-source-receipt.json')).write_text(json.dumps(receipt,indent=2)+'\n',encoding='utf-8')

def closure():
    h=hashlib.sha256()
    for p in sorted((OUT/'player').rglob('*')):
        if p.is_file():h.update(p.relative_to(OUT/'player').as_posix().encode()+b'\0');h.update(hashlib.sha256(p.read_bytes()).digest())
    return h.hexdigest()

def show_probe_window(process_id):
    """Show only this explicit validation Player; never change host behavior."""
    import ctypes
    from ctypes import wintypes
    user32=ctypes.WinDLL('user32',use_last_error=True)
    callback_type=ctypes.WINFUNCTYPE(wintypes.BOOL,wintypes.HWND,wintypes.LPARAM)
    user32.EnumWindows.argtypes=[callback_type,wintypes.LPARAM]
    user32.GetWindowThreadProcessId.argtypes=[wintypes.HWND,ctypes.POINTER(wintypes.DWORD)]
    user32.GetClassNameW.argtypes=[wintypes.HWND,wintypes.LPWSTR,ctypes.c_int]
    user32.IsIconic.argtypes=[wintypes.HWND]
    user32.ShowWindow.argtypes=[wintypes.HWND,ctypes.c_int]
    found=[]
    @callback_type
    def visit(handle,context):
        pid=wintypes.DWORD();user32.GetWindowThreadProcessId(handle,ctypes.byref(pid))
        if pid.value!=process_id:return True
        name=ctypes.create_unicode_buffer(128);user32.GetClassNameW(handle,name,len(name))
        if name.value!='UnityWndClass':return True
        minimized=bool(user32.IsIconic(handle))
        if minimized:user32.ShowWindow(handle,4) # SW_SHOWNOACTIVATE
        found.append({'own_pid':process_id,'was_minimized':minimized})
        return False
    user32.EnumWindows(visit,0)
    return found[0] if found else None

def run():
    compiled=json.loads((OUT/'player-source-receipt.json').read_text(encoding='utf-8'))
    assert source_receipt('player')==compiled,'Player is stale relative to source; run build.py player'
    before=closure()
    # Remove the entire generated author project from its expected location;
    # the Player can only receive the exported archive and its own installation.
    author=OUT/'author-project';hidden=OUT/'author-project.offline'
    if author.exists():author.rename(hidden)
    report=REPORTS/'player.json'
    if report.exists():report.unlink()
    try:
        startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=1
        process=subprocess.Popen([str(OUT/'player/ContentPlayer.exe'),'-screen-fullscreen','0','-screen-width','960','-screen-height','540','--content-package',str(OUT/'sample.spatial'),'--content-directional-package',str(OUT/'directional.spatial'),'--content-report',str(report),'--content-test-delay-viewport','true','-logFile',str(OUT/'player.log')],startupinfo=startup)
        deadline=time.monotonic()+120;window=None
        while process.poll() is None:
            if window is None:window=show_probe_window(process.pid)
            if time.monotonic()>deadline:process.kill();process.wait();raise TimeoutError('Player probe timed out')
            time.sleep(.1)
    finally:
        if hidden.exists():hidden.rename(author)
    data=json.loads(report.read_text(encoding='utf-8'))
    assert process.returncode==0 and data['passed'],data
    assert closure()==before,'Player installation changed'
    data['player_closure_sha256']=before
    data['compiled_source_receipt_sha256']=hashlib.sha256((OUT/'player-source-receipt.json').read_bytes()).hexdigest()
    data['validation_window']=window
    data['source_directory_unavailable_during_run']=True
    data['scope']='Software test on this Windows host; not a separate no-source-access Windows account/VM and not human acceptance'
    report.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('Player verified',len(data['cases']),'cases; closure',before,flush=True)

def fingerprint():
    sources=list((ROOT/'Packages/com.spatialwallpaper.sdk').rglob('*'))
    for folder in ['Prototype/Assets/ContentRuntime','tools/sdk/tests/legacy-client','tools/content']:
        sources+=list((ROOT/folder).rglob('*'))
    sources += [OUT/(role+'-project/Packages/'+name+'.json') for role in ['author','player'] for name in ['manifest','packages-lock']]
    sources += [OUT/'sample.spatial',OUT/'directional.spatial',UNITY]
    sources += [OUT/(role+'-source-receipt.json') for role in ['author','player']]
    sources += list((ROOT/'Validation/Content/resource-probe/bundles').glob('*'))
    for folder in ['Train/Shaders','Pool/Shaders','Parking/Shaders','Showcase/Shaders']:
        sources += list((ROOT/'Prototype/Assets'/folder).glob('*.shader'))
    records=[]
    for p in sorted(set(sources)):
        if p.is_file() and '__pycache__' not in str(p):
            records.append({'path':p.relative_to(ROOT).as_posix() if p.is_relative_to(ROOT) else str(p),'size':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
    document={'baseline':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'player_closure_sha256':closure(),'files':records}
    (REPORTS/'fingerprint.json').write_text(json.dumps(document,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def main():
    parser=argparse.ArgumentParser();parser.add_argument('step',nargs='?',default='all',choices=['all','author','player','run']);args=parser.parse_args()
    REPORTS.mkdir(parents=True,exist_ok=True)
    if args.step in ['all','author']:build('author')
    if args.step in ['all','player']:build('player')
    if args.step in ['all','run']:run();fingerprint()
if __name__=='__main__':main()
