"""Restore the user-provided Bakery package locally without publishing third-party sources."""
import argparse,hashlib,json,tarfile
from pathlib import Path,PurePosixPath
ROOT=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--package',default='D:/Unity/Plugins/Bakery - GPU Lightmapper v1.98.unitypackage');p.add_argument('--list',action='store_true');args=p.parse_args()
package=Path(args.package)
with tarfile.open(package,'r:gz') as archive:
    members={m.name:m for m in archive.getmembers()};entries=[]
    for name,member in members.items():
        if not name.endswith('/pathname'):continue
        path=archive.extractfile(member).read().decode('utf-8-sig').splitlines()[0].strip('\0')
        rel=PurePosixPath(path)
        assert not rel.is_absolute() and '..' not in rel.parts and rel.parts[0]=='Assets',path
        entries.append((name.rsplit('/',1)[0],path))
    if args.list:
        print('\n'.join(sorted(path for _,path in entries)));raise SystemExit()
    count=0;preserved=0
    for key,path in entries:
        for source,suffix in [('asset',''),('asset.meta','.meta')]:
            member=members.get(key+'/'+source)
            if not member:continue
            dst=ROOT/'Prototype'/(path+suffix);data=archive.extractfile(member).read()
            if dst.exists():preserved+=int(dst.read_bytes()!=data)
            else:dst.parent.mkdir(parents=True,exist_ok=True);dst.write_bytes(data)
            count+=1
    print('Restored',count,'local Bakery files; package SHA256',hashlib.sha256(package.read_bytes()).hexdigest())
    print('Preserved',preserved,'locally modified plugin/settings files')
