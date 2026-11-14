"""Preserve a verified, machine-local Player and evidence outside the task worktree."""
import argparse,hashlib,json,re,shutil,subprocess
from pathlib import Path
from prepare import ROOT,COMMON,PROJECT,restore_tracking_models

parser=argparse.ArgumentParser()
parser.add_argument('--output',type=Path,required=True)
parser.add_argument('--calibration',type=Path)
args=parser.parse_args()
receipt=json.loads((ROOT/'Validation/Mita/build-receipt.json').read_text())
compiled={Path(k).as_posix():v for k,v in receipt['compiled_sources'].items()}
for folder in ['Runtime','Editor']:
    for source in (ROOT/'tools/mita'/folder).glob('*.cs'):
        if hashlib.sha256(source.read_bytes()).hexdigest()!=compiled[f'Assets/{folder}/{source.name}']:raise ValueError('Rebuild before preserving: '+str(source))
for name,digest in receipt['products'].items():
    if hashlib.sha256((PROJECT/name).read_bytes()).hexdigest()!=digest:raise ValueError('Changed build product: '+name)
output=args.output.resolve();output.mkdir(parents=True,exist_ok=False)
player=output/'Player';player.mkdir()
source=PROJECT/'Builds/Mita'
for name in ['Mita.exe','UnityPlayer.dll','UnityCrashHandler64.exe']:shutil.copy2(source/name,player/name)
for name in ['Mita_Data','MonoBleedingEdge']:shutil.copytree(source/name,player/name)
evidence=output/'Evidence';evidence.mkdir()
capture=ROOT/'Validation/Mita/capture'
for path in capture.iterdir():
    if path.is_file() and path.suffix.lower() in ['.json','.csv','.png','.mp4','.txt']:shutil.copy2(path,evidence/path.name)
for name in ['history','gestures','five-parameter-frames','real-camera-frames']:
    if (capture/name).exists():shutil.copytree(capture/name,evidence/name)
for name in ['build-receipt.json','source-manifest.json']:shutil.copy2(ROOT/'Validation/Mita'/name,evidence/name)
shutil.copytree(ROOT/'Validation/Mita/research',evidence/'Research',ignore=shutil.ignore_patterns('__pycache__'))
restore_tracking_models()
shutil.copytree(ROOT/'tracker/src',output/'Tracker/src',ignore=shutil.ignore_patterns('__pycache__'))
shutil.copytree(ROOT/'tracker/.cache/models',output/'Tracker/.cache/models')
shutil.copy2(PROJECT/'tracking-calibration-synthetic.json',output/'Calibration-Synthetic.json')
python=COMMON/'tracker/.venv/Scripts/python.exe'
if not python.exists():raise FileNotFoundError(python)
tracking=rf'--tracking --tracking-python "{python}" --tracking-root "%~dp0Tracker"'
launchers={
    'Preview.cmd':'',
    'Desktop.cmd':'--desktop --display 0',
    'Preview-TrackingDemo.cmd':tracking+r' --tracking-mode simulate --tracking-calibration "%~dp0Calibration-Synthetic.json"'
}
if args.calibration:
    value=json.loads(args.calibration.read_text(encoding='utf-8-sig'));profile=value.get('applied_profile',value)
    if profile.get('schema_version')!=2:raise ValueError('Local camera launcher requires a schema 2 profile')
    camera=profile['manual']['source']['camera_id']
    if not re.fullmatch(r'[A-Za-z0-9_-]+',camera):raise ValueError('Unsupported local camera identifier')
    (output/'LocalCalibration.json').write_text(json.dumps(profile,ensure_ascii=False,indent=2),encoding='utf8')
    launchers['Preview-Camera.cmd']=tracking+rf' --tracking-mode camera --tracking-camera {camera} --tracking-calibration "%~dp0LocalCalibration.json"'
for name,options in launchers.items():
    body=f'''@echo off
setlocal
set "MITA_OUTPUT=%~dp0Logs"
if not exist "%MITA_OUTPUT%" mkdir "%MITA_OUTPUT%"
start "" /D "%~dp0Player" "%~dp0Player\\Mita.exe" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "%MITA_OUTPUT%\\{name[:-4]}.log" {options}
'''
    (output/name).write_bytes(body.replace('\n','\r\n').encode('utf8'))
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
(output/'README.md').write_text(f'''# 米塔本机开发候选

双击 Preview.cmd 预览，Desktop.cmd 挂到主显示器桌面；默认不打开摄像头。Preview-TrackingDemo.cmd 使用合成眼位。若存在 Preview-Camera.cmd，它使用此目录 LocalCalibration.json 的已应用预设；本次验证时未枚举到真实相机，不能视为已验收。

单击脸部眨眼回望，0.6 秒内连续点击触发原点击反应。数字 1–6 触发六组完整动作；空格暂停，M 静音，R 重播开场，Esc 退出。桌面模式用 Ctrl+Alt+1–6、Ctrl+Alt+P、Ctrl+Alt+Q。退出不会修改系统静态壁纸设置。

Player 可离开任务 worktree 运行。追踪复用本机已有 Python：{python}；此交付不包含新的 Python 安装。Tracker 源码、校验模型与校准快照已独立保存。Logs 为运行输出，Evidence 为构建与验证快照。不能把合成测试视为真人自然度或完整 SDK 内容包验收。

来源提交：{head}。完整指南：https://github.com/KyaKayKya/Spatial-Wallpaper/blob/{head}/docs/guides/米塔空间壁纸运行.md
''',encoding='utf8')
manifest={'source_commit':head,'source_dirty':bool(subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).strip()),'files':{}}
for path in sorted(output.rglob('*')):
    if path.is_file():manifest['files'][path.relative_to(output).as_posix()]={'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
(output/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
print('Preserved',len(manifest['files']),'files at',output)
