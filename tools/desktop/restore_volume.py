"""Restore the URP child package from the user's installed licensed package."""
from pathlib import Path, PurePosixPath
import tarfile, io
root=Path(__file__).resolve().parents[2]
package=Path('D:/Unity/Plugins/Volumetric Lights 2 v20.2.1.unitypackage')
with tarfile.open(package) as archive:
    members={m.name:m for m in archive.getmembers()}
    found=[]
    for key in members:
        if not key.endswith('/pathname'):continue
        name=archive.extractfile(members[key]).read().decode('utf-8-sig').splitlines()[0].rstrip('\x00')
        if name.endswith('.unitypackage') and '/URP/' in name:
            found.append(archive.extractfile(members[key.rsplit('/',1)[0]+'/asset']).read())
    assert len(found)==1,'Expected one URP payload'
with tarfile.open(fileobj=io.BytesIO(found[0])) as archive:
    members={m.name:m for m in archive.getmembers()}
    for key in members:
        if not key.endswith('/pathname'):continue
        name=archive.extractfile(members[key]).read().decode('utf-8-sig').splitlines()[0].rstrip('\x00')
        path=PurePosixPath(name)
        if path.is_absolute() or '..' in path.parts or path.parts[0]!='Assets':raise ValueError(name)
        prefix=key.rsplit('/',1)[0];target=root/'Prototype'/path
        for suffix,dest in [('/asset',target),('/asset.meta',Path(str(target)+'.meta'))]:
            member=members.get(prefix+suffix)
            if member:
                data=archive.extractfile(member).read()
                if dest.exists() and dest.read_bytes()!=data:raise RuntimeError('Conflicting local dependency: '+str(dest))
                dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(data)
        if prefix+'/asset' not in members:target.mkdir(parents=True,exist_ok=True)
print('Restored licensed Volumetric Lights URP dependency')
