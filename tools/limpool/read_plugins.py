from pathlib import Path
import tarfile, io, json
r=Path(__file__).resolve().parents[2];out=r/'Validation/Limpool'
index=json.loads((out/'Volumetric Lights 2 v20.2.1-index.json').read_text())
with tarfile.open('D:/Unity/Plugins/Volumetric Lights 2 v20.2.1.unitypackage') as t:
    for k,v in index.items():
        print(v)
        if v.endswith('.unitypackage') and '/URP/' in v:
            data=t.extractfile(k+'/asset').read()
            with tarfile.open(fileobj=io.BytesIO(data)) as inner:
                members={m.name:inner.extractfile(m).read() for m in inner if m.isfile()}
                for key,data in members.items():
                    if not key.endswith('/pathname'):continue
                    path=data.decode('utf-8-sig').splitlines()[0].rstrip('\x00');pre=key.rsplit('/',1)[0]
                    p=Path(path)
                    if p.is_absolute() or '..' in p.parts or p.parts[0]!='Assets':raise ValueError(path)
                    target=r/'Prototype'/p
                    if pre+'/asset' in members:
                        target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(members[pre+'/asset'])
                    else:target.mkdir(parents=True,exist_ok=True)
                    if pre+'/asset.meta' in members:Path(str(target)+'.meta').write_bytes(members[pre+'/asset.meta'])
                    if path.endswith('.pdf'):print('DOC',path)
