"""Restore Parking assets only after this project's baker processes have stopped.

Bakery's global preferences are not rolled back. The immutable Generated source
textures are excluded from the snapshot.
"""
import json, shutil, time
from pathlib import Path
import psutil

PROJECT=Path(__file__).resolve().parents[2]/'Prototype'
MANIFEST=PROJECT/'Library/ParkingBakeryRecovery.json'

def recover():
    if not MANIFEST.exists():return
    for process in psutil.process_iter(['name','cmdline','exe']):
        if process.info['exe'] and Path(process.info['exe']).resolve().is_relative_to(PROJECT.resolve()):
            raise RuntimeError('Project executable still active; recovery deferred')
        if (process.info['name'] or '').lower()=='unity.exe':
            if any(str(PROJECT.resolve()).lower()==str(arg).replace('/','\\').lower() for arg in (process.info['cmdline'] or [])):
                raise RuntimeError('Close this project Unity before recovery')
    data=json.loads(MANIFEST.read_text(encoding='utf-8'))
    assert Path(data['project']).resolve()==PROJECT.resolve()
    backup=Path(data['backup']).resolve()
    assert backup.is_relative_to((PROJECT/'Library/ParkingBakeryBackup').resolve())
    root=(PROJECT/'Assets/Parking').resolve()
    original={}
    for relative in data['files']:
        target=(PROJECT/relative).resolve();source=(backup/relative).resolve()
        assert target.is_relative_to(root) and source.is_relative_to(backup)
        assert source.is_file(),source
        original[target]=source
    for target in root.rglob('*'):
        if target.is_file() and not target.is_relative_to(root/'Generated') and target.resolve() not in original:
            target.unlink()
    for target,source in original.items():
        target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
    MANIFEST.unlink()
    print('Restored Parking assets from',backup)

def wait_for_bake(process):
    """Track only this launched editor's descendants, retaining process identities."""
    parent=psutil.Process(process.pid);children={}
    while process.poll() is None:
        try:
            for child in parent.children(recursive=True):children[(child.pid,child.create_time())]=child
        except psutil.NoSuchProcess:pass
        time.sleep(.2)
    # A failed Unity exit may leave native baker children writing output.
    for child in children.values():
        if child.is_running():child.terminate()
    _,alive=psutil.wait_procs(list(children.values()),timeout=10)
    for child in alive:child.kill()
    _,alive=psutil.wait_procs(alive,timeout=10)
    if alive:raise RuntimeError('Baker processes still active; recovery deferred')
    if MANIFEST.exists():
        recover()
        return process.returncode or 1
    return process.returncode

if __name__=='__main__':recover()
