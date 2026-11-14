"""Preserve the verified desktop build before removing this task worktree."""
from pathlib import Path
import argparse,hashlib,json,shutil,subprocess
from verify_receipt import validate
root=Path(__file__).resolve().parents[2]
receipt=validate()
parser=argparse.ArgumentParser();parser.add_argument('--name',default='Desktop-Integrated');parser.add_argument('--update-entry',action='store_true');args=parser.parse_args()
assert args.name and all(c.isalnum() or c in '-_' for c in args.name),'Use a single delivery directory name'
main=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip()).parent
target=main/'Prototype/Builds'/args.name
assert target.resolve().is_relative_to((main/'Prototype/Builds').resolve()) and not target.exists(),'Refusing to overwrite an existing delivery'
source=root/'Prototype/Builds/Library'
shutil.copytree(source,target)
for name,digest in receipt['files'].items():assert hashlib.sha256((target/name).read_bytes()).hexdigest()==digest,name
shutil.copytree(root/'tracker/src',target/'Tracker/src')
for name in ['pyproject.toml','uv.lock']:shutil.copy2(root/'tracker'/name,target/'Tracker'/name)
models=next((p for p in [root/'tracker/.cache/models',main/'tracker/.cache/models'] if p.is_dir()),None)
if models:shutil.copytree(models,target/'Tracker/.cache/models')
python=main/'tracker/.venv/Scripts/python.exe';assert python.is_file()
launcher='@echo off\r\nstart "" /d "%~dp0" "%~dp0Launch.exe" %*\r\nexit /b\r\n'
(target/'Launch.cmd').write_bytes(launcher.encode('utf-8'))
(target/'delivery.json').write_text(json.dumps({'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),'build':receipt},indent=2),encoding='utf-8')
shutil.copytree(root/'Validation/Desktop',target/'Evidence/Desktop',ignore=shutil.ignore_patterns('*.exe','*.dll','runtime.lock','manager.lock','connection.json','settings.json','presets','calibration','core','binary','visible-*','*-local.png'))
summary=json.loads((root/'Validation/Desktop/verified/summary.json').read_text())
shutil.copytree(root/'Validation/Library'/summary['library_run'],target/'Evidence/Library',ignore=shutil.ignore_patterns('data'))
docs=target/'Documentation';docs.mkdir()
for path in [root/'docs/guides/Windows桌面运行.md',root/'docs/reference/evidence/desktop/验证说明.md']:
    shutil.copy2(path,docs/path.name)
if args.update_entry:
    assert args.name!='Desktop-Integrated','Entry must not call itself'
    for old in ['Desktop-Integrated','Desktop-Experience','Desktop-VerticalFit','Desktop-Interaction']:
        entry=main/'Prototype/Builds'/old
        assert (entry/'delivery.json').is_file(),'No prior managed delivery: '+old
        backup=entry/('Launch-Before-'+args.name+'.cmd')
        if not backup.exists():shutil.copy2(entry/'Launch.cmd',backup)
        (entry/'Launch.cmd').write_bytes(('@echo off\r\nstart "" /d "%~dp0..\\'+args.name+'" "%~dp0..\\'+args.name+'\\Launch.exe" %*\r\nexit /b\r\n').encode('utf-8'))
        previous=json.loads((entry/'delivery.json').read_text());previous['launcher_target']=args.name;previous['launcher_source_commit']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip()
        (entry/'delivery.json').write_text(json.dumps(previous,indent=2),encoding='utf-8')
print('Preserved verified desktop Player:',target)
