"""Preserve local Player and raw evidence in the main checkout before worktree cleanup.

Run from the integrated task worktree. Archives differing prior delivery files.
"""
import hashlib,shutil,subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[2]
common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip())
main=common.parent
assert root.resolve()!=main.resolve(),'Run this from the task worktree'
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip()
subprocess.run(['git','merge-base','--is-ancestor',head,'HEAD'],cwd=main,check=True)
subprocess.run(['git','diff','--quiet'],cwd=main,check=True)
tracked=set(subprocess.check_output(['git','ls-files','-z'],cwd=root).decode().split('\0'))
count=0
for directory in ['Prototype/Builds/Parking','Validation/Parking','Prototype/Assets/Bakery','Prototype/Assets/Editor/x64']:
    for src in (root/directory).rglob('*'):
        if not src.is_file():continue
        dst=main/src.relative_to(root)
        if src.relative_to(root).as_posix() in tracked:
            assert dst.exists(),f'Integrated file missing: {dst}'
            continue
        if dst.exists():
            digest=hashlib.sha256(dst.read_bytes()).hexdigest()
            if hashlib.sha256(src.read_bytes()).hexdigest()!=digest:
                archive=main/'.local-assets/parking-deliveries'/digest/src.relative_to(root)
                archive.parent.mkdir(parents=True,exist_ok=True)
                if archive.exists():assert hashlib.sha256(archive.read_bytes()).hexdigest()==digest
                else:shutil.copy2(dst,archive)
                shutil.copy2(src,dst)
        else:
            dst.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src,dst)
        count+=1
for name in ['Prototype/Assets/Bakery.meta','Prototype/Assets/Editor/x64.meta']:
    src=root/name;dst=main/name
    if src.exists():
        if dst.exists():assert src.read_bytes()==dst.read_bytes()
        else:dst.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src,dst)
print(f'Preserved {count} files under {main}')
