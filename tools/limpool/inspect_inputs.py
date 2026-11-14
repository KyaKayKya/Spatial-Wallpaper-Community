from pathlib import Path
import zipfile, tarfile, json, subprocess,sys
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Validation/Limpool'; OUT.mkdir(parents=True,exist_ok=True)
SOURCE=Path(sys.argv[1]) if len(sys.argv)>1 else ROOT/'art/limpool/Liminal_Pool_Project.zip'
if not SOURCE.exists():
    common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip())
    SOURCE=common.parent/'art/limpool/Liminal_Pool_Project.zip'
with zipfile.ZipFile(SOURCE) as z:
    for name in ['README_中文.txt','SOURCES.txt']:
        print(name, z.read(name).decode('utf-8-sig'))
    target=ROOT/'art/limpool/source'
    target.mkdir(parents=True,exist_ok=True)
    z.extractall(target)
for name in ['URP Water 1.2.2','Volumetric Lights 2 v20.2.1','Bakery - GPU Lightmapper v1.98']:
    with tarfile.open(Path('D:/Unity/Plugins')/(name+'.unitypackage')) as t:
        entries={}
        for m in t.getmembers():
            if m.name.endswith('/pathname'):
                entries[m.name.rsplit('/',1)[0]]=t.extractfile(m).read().decode('utf-8-sig').splitlines()[0].rstrip('\x00')
        (OUT/(name+'-index.json')).write_text(json.dumps(entries,indent=2),encoding='utf-8')
        for k,v in entries.items():
            if any(s in v.lower() for s in ['readme','.pdf','documentation','.asmdef','planar','depth.hlsl']):
                print(name,v)
                try:
                    data=t.extractfile(k+'/asset').read()
                    p=OUT/'package-docs'/name/Path(v).name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data)
                except KeyError: pass
print('BLENDER',Path('D:/Blender/blender.exe').exists())
result=subprocess.run(['D:/Blender/blender.exe','--background','--factory-startup','--python-expr','import bpy; print(bpy.app.version_string)'],capture_output=True,text=True)
print(result.returncode,result.stdout,result.stderr)
