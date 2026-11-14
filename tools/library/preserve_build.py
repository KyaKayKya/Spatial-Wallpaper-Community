"""Preserve this verified local Player before its task worktree is removed."""
from pathlib import Path
import argparse,hashlib,json,os,re,shutil,subprocess
root=Path(__file__).resolve().parents[2]
main=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip()).parent
parser=argparse.ArgumentParser();parser.add_argument('--name',default='Library');args=parser.parse_args()
assert re.fullmatch(r'Library(?:-[A-Za-z0-9-]+)?',args.name), 'Invalid local delivery name'
target=main/'Prototype/Builds'/args.name
assert target.resolve().is_relative_to(main.resolve()) and not target.exists(), 'Refusing to overwrite an existing delivery'
source=root/'Prototype/Builds/Library'
shutil.copytree(source,target)
tracker=target/'Tracker'
shutil.copytree(root/'tracker/src',tracker/'src')
shutil.copy2(root/'tracker/pyproject.toml',tracker/'pyproject.toml')
shutil.copy2(root/'tracker/uv.lock',tracker/'uv.lock')
if (root/'tracker/.cache/models').exists():shutil.copytree(root/'tracker/.cache/models',tracker/'.cache/models')
python=main/'tracker/.venv/Scripts/python.exe'
assert python.is_file(), 'Shared Python environment is unavailable'
launcher='@echo off\r\ncd /d "%~dp0"\r\n"%~dp0SpatialWallpaper.exe" --tracking-python "'+str(python)+'" --tracking-root "%~dp0Tracker" %*\r\n'
(target/'Launch.cmd').write_bytes(launcher.encode('utf-8'))
hashes={}
for item in source.rglob('*'):
 if item.is_file():
  relative=item.relative_to(source).as_posix();digest=hashlib.sha256(item.read_bytes()).hexdigest()
  assert hashlib.sha256((target/relative).read_bytes()).hexdigest()==digest,relative
  hashes[relative]=digest
(target/'delivery.json').write_text(json.dumps({'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),'player_files':hashes},indent=2)+'\n',encoding='utf-8')
for name in ['no-wallpaper.png','library-refined.png','library-sixty.png','library-many-pages.png']:
 preview=root/'Validation/Library/verified'/name
 if preview.is_file():shutil.copy2(preview,target/name)
print('Preserved verified Player and Tracker:',target,'files=',len(hashes))
