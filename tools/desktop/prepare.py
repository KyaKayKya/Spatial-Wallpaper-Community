"""Prepare licensed local inputs in this worktree using existing project tools."""
from pathlib import Path
import subprocess, sys, shutil
root=Path(__file__).resolve().parents[2]
for args in [
 ['tools/showcase/local_assets.py','restore','--group','runtime'],
 ['tools/parking/local_assets.py','restore'],
 ['tools/train/local_assets.py','restore'],
 ['tools/limpool/restore.py'],
 ['tools/rainycafe/local_assets.py','restore'],
 ['tools/rainycafe/restore_shared_dependencies.py'],
 ['tools/integration/prepare_calibration_assets.py'],
 ['tools/library/prepare.py'],
]:subprocess.run([sys.executable,*args],cwd=root,check=True)
main=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip()).parent
for origin in [main/'Prototype/Assets/VolumetricLights',main/'.worktrees/sdk-library-reference/Prototype/Assets/VolumetricLights']:
    if origin.is_dir():
        target=root/'Prototype/Assets/VolumetricLights'
        for source in origin.rglob('*'):
            if source.is_file():
                dest=target/source.relative_to(origin)
                if not dest.exists():dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,dest)
        break
else:subprocess.run([sys.executable,'tools/desktop/restore_volume.py'],cwd=root,check=True)
# Unity removes orphan .meta files for payload not needed by this build.
# Restore only deletion side effects in this task's own clean-at-start worktree.
deleted=subprocess.check_output(['git','ls-files','--deleted','--','Prototype/Assets'],cwd=root,text=True).splitlines()
metadata=[p for p in deleted if p.endswith('.meta')]
if metadata:subprocess.run(['git','restore','--',*metadata],cwd=root,check=True)
