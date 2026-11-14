"""Import the minimal local Mita scene into the shared library, preserving asset GUIDs."""
from pathlib import Path
import re, shutil, subprocess, sys
from PIL import Image

root=Path(__file__).resolve().parents[2]
project=root/'Validation/Mita/project'
if not (project/'Assets/Generated/Mita-Nod.anim').exists():
    subprocess.run([sys.executable,str(root/'tools/mita/run.py'),'build'],cwd=root,check=True)
source=project/'Assets'
target=root/'Prototype/Assets/Mita'
# Shared types already belong to the host. Rewrite their serialized script references.
mapping={}
for meta in (source/'Shared').rglob('*.cs.meta'):
    matches=list((root/'Prototype/Assets').rglob(meta.name))+list((root/'Packages').rglob(meta.name))
    matches=[p for p in matches if target not in p.parents]
    if not matches:continue
    if len(matches)!=1:raise RuntimeError(f'Ambiguous shared script: {meta.name}')
    guid=lambda p:re.search(r'^guid: (\w+)',p.read_text(),re.M)[1]
    mapping[guid(meta)]=guid(matches[0])
for path in source.rglob('*'):
    if not path.is_file() or path.relative_to(source).parts[0] in ('Shared','Editor'):continue
    dest=target/path.relative_to(source);dest.parent.mkdir(parents=True,exist_ok=True)
    if path.suffix in ('.unity','.asset','.prefab'):
        data=path.read_text(encoding='utf-8')
        for old,new in mapping.items():data=data.replace('guid: '+old,'guid: '+new)
        dest.write_text(data,encoding='utf-8')
    else:shutil.copy2(path,dest)
# Code is owned here, not in the generated author project.
for path in (root/'tools/mita/Runtime').glob('*.cs'):
    shutil.copy2(path,target/'Runtime'/path.name)
front=root/'Validation/Mita/capture/front.png'
if not front.exists():
    main=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip()).parent
    front=main/'.local-assets/mita-delivery/e9bef91/Evidence/front.png'
image=Image.open(front).convert('RGB');image.thumbnail((768,432))
cover=root/'Prototype/Assets/Library/Resources/Library/mita.jpg'
cover.parent.mkdir(parents=True,exist_ok=True);image.save(cover,quality=90)
print('Prepared minimal Mita scene, shared script references and real Player cover')
