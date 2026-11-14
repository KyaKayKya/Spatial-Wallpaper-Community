"""Restore the local Mita author project without redistributing source assets."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
COMMON = Path(subprocess.check_output(['git', 'rev-parse', '--path-format=absolute', '--git-common-dir'], cwd=ROOT, text=True).strip()).parent
PROJECT = ROOT / 'Validation/Mita/project'

def restore_tracking_models():
    import importlib.util
    spec=importlib.util.spec_from_file_location('mita_tracker_assets',ROOT/'tracker/src/spatial_tracker/assets.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    destination=ROOT/'tracker/.cache/models';destination.mkdir(parents=True,exist_ok=True)
    for name in module.ASSETS:
        target=destination/name
        if target.exists():module.verify_asset(target,name)
        else:shutil.copy2(module.verify_asset(COMMON/'tracker/.cache/models'/name,name),target)
    print('Verified existing MediaPipe model cache; no download')

def prepare(audio=None):
    assets = PROJECT / 'Assets'
    demo=COMMON/'.local-assets/mita-demo'
    pinned=json.loads((ROOT/'tools/mita/demo-assets.json').read_text(encoding='utf8'))
    for name,item in pinned['files'].items():
        if hashlib.sha256((demo/name).read_bytes()).hexdigest()!=item['sha256']:raise ValueError('Run fetch_demo.py; digest mismatch: '+name)
    xlmy=COMMON/'.local-assets/mita-xlmy/minimal'
    xlmy_manifest=json.loads((xlmy/'manifest.json').read_text(encoding='utf8'))
    for name,item in xlmy_manifest['files'].items():
        if hashlib.sha256((xlmy/name).read_bytes()).hexdigest()!=item['sha256']:raise ValueError('Run recover_xlmy.py; digest mismatch: '+name)
    assets.mkdir(parents=True,exist_ok=True)
    for folder in ['Mesh','Texture2D','AnimationClip','AnimatorController','Hero','Material','Shader','Xlmy','Generated','Editor','Runtime','Shared']:
        generated=assets/folder
        if generated.resolve().parent!=assets.resolve():raise ValueError('Unexpected generated asset path')
        if generated.exists():shutil.rmtree(generated)
    for folder in ['Texture2D','AnimationClip','AnimatorController','Material','Shader']:(assets/folder).mkdir()
    for name,item in pinned['files'].items():
        category='Shader' if '.shader' in name else 'Material' if '.mat' in name else 'AnimationClip' if '.anim' in name else 'Texture2D'
        shutil.copy2(demo/name,assets/category/name)
    manifest = {}
    import re
    for name in ['Body', 'Cloth', 'Face_0', 'Hair_0']:
        material = demo / (name + '.mat')
        shader = assets / 'Shader' / (name + '.shader')
        guid = re.search(r'm_Shader:.*guid: (\w+)', material.read_text(encoding='utf8')).group(1)
        shader.with_suffix('.shader.meta').write_text('fileFormatVersion: 2\nguid: ' + guid + '\nShaderImporter:\n  externalObjects: {}\n', encoding='utf8')
    for folder in ['Editor', 'Runtime']:
        shutil.copytree(ROOT / 'tools/mita' / folder, assets / folder, dirs_exist_ok=True)
    for shader in (ROOT/'tools/mita/Shaders').glob('*.shader'):shutil.copy2(shader,assets/'Shader'/shader.name)
    if audio is not None:
        if audio.suffix.lower() not in ['.mp3','.wav','.ogg','.mp4']:raise ValueError('Audio must be mp3, wav, ogg or an mp4 audio container')
        audio_cache=COMMON/'.local-assets/mita-audio';audio_cache.mkdir(exist_ok=True)
        source_hash=hashlib.sha256(audio.read_bytes()).hexdigest()
        preserved=audio_cache/('source-'+source_hash[:12]+audio.suffix.lower());shutil.copy2(audio,preserved)
        selected=audio_cache/('Bgm'+('.ogg' if audio.suffix.lower()=='.mp4' else audio.suffix.lower()))
        if audio.suffix.lower()=='.mp4':subprocess.run(['ffmpeg','-y','-hide_banner','-loglevel','error','-i',str(audio),'-vn','-c:a','libvorbis','-q:a','5',str(selected)],check=True)
        else:shutil.copy2(audio,selected)
        (audio_cache/'selected.json').write_text(json.dumps({'file':selected.name,'source':preserved.name,'source_sha256':source_hash},indent=2))
        audio=selected
    elif (COMMON/'.local-assets/mita-audio/selected.json').exists():
        selected=json.loads((COMMON/'.local-assets/mita-audio/selected.json').read_text())
        audio=COMMON/'.local-assets/mita-audio'/selected['file']
    if audio is not None:
        audio_folder=assets/'Audio';audio_folder.mkdir(exist_ok=True)
        for previous in audio_folder.glob('Bgm.*'):previous.unlink()
        shutil.copy2(audio,audio_folder/('Bgm'+audio.suffix.lower()))
    camera_source=COMMON/'.local-assets/mita-camera'
    camera_source.mkdir(exist_ok=True)
    if not (camera_source/'CameraMenu Start.anim').exists():
        raise FileNotFoundError('Run python tools/mita/fetch_camera.py once to recover pinned camera animation files')
    for path in camera_source.glob('CameraMenu*'):
        category='AnimationClip' if '.anim' in path.name else 'AnimatorController'
        shutil.copy2(path,assets/category/path.name)
    shutil.copytree(xlmy,assets/'Xlmy',dirs_exist_ok=True)
    shutil.copytree(ROOT/'Prototype/Assets/Tracking/Core',assets/'Shared/Tracking',dirs_exist_ok=True)
    shutil.copy2(ROOT/'Prototype/Assets/StreamingAssets/tracking-calibration-synthetic.json',PROJECT/'tracking-calibration-synthetic.json')
    shared = assets / 'Shared'; shared.mkdir(exist_ok=True)
    for path in ['Packages/com.spatialwallpaper.sdk/Runtime/Projection/WindowProjection.cs',
                 'Packages/com.spatialwallpaper.sdk/Runtime/Data/Framing.cs',
                 'Prototype/Assets/Showcase/Runtime/DisplaySceneVolume.cs',
                 'Prototype/Assets/Showcase/Runtime/NativeProcessCounters.cs',
                 'Prototype/Assets/Desktop/Windows/WindowsDesktopWindow.cs',
                 'Prototype/Assets/Desktop/Core/DesktopDisplay.cs']:
        shutil.copy2(ROOT / path, shared / Path(path).name)
    for name in ['CalibrationProfile.cs','FiveParameterMapping.cs','ViewportGeometry.cs']:
        shutil.copy2(ROOT/'Prototype/Assets/Calibration/Core'/name,shared/name)
    shutil.copy2(ROOT/'Prototype/Assets/Calibration/Runtime/DisplayViewport.cs',shared/'DisplayViewport.cs')
    for path in assets.rglob('*'):
        if path.is_file() and not path.name.endswith('.meta') and 'Generated' not in path.parts:
            manifest[str(path.relative_to(assets))] = {'bytes': path.stat().st_size, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
    (PROJECT / 'Packages').mkdir(exist_ok=True)
    modules = ['animation', 'audio', 'imageconversion', 'screencapture', 'physics', 'particlesystem', 'jsonserialize', 'imgui']
    dependencies={'com.unity.modules.' + n: '1.0.0' for n in modules}
    dependencies['com.unity.nuget.newtonsoft-json']=json.loads((ROOT/'Prototype/Packages/manifest.json').read_text())['dependencies']['com.unity.nuget.newtonsoft-json']
    (PROJECT / 'Packages/manifest.json').write_text(json.dumps({'dependencies':dependencies}, indent=2))
    (PROJECT / 'ProjectSettings').mkdir(exist_ok=True)
    (PROJECT/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2022.3.62f3\n')
    (ROOT / 'Validation/Mita/source-manifest.json').write_text(json.dumps({'source': 'https://github.com/yenichw/MiSide-Demo-Unity-Project', 'commit': pinned['commit'], 'xlmy_source':xlmy_manifest['source_video'], 'xlmy_archive_sha256':xlmy_manifest['archive_sha256'], 'assets': manifest}, indent=2), encoding='utf8')
    print('Restored', len(manifest), 'files to', PROJECT)

if __name__ == '__main__':
    prepare()
