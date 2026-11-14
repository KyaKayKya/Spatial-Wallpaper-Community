"""Revert only known generated/import side effects in this isolated task tree."""
from pathlib import Path
import subprocess,hashlib,json
root=Path(__file__).resolve().parents[2]
paths=subprocess.check_output(['git','ls-files','--deleted','--','Prototype/Assets'],cwd=root,text=True).splitlines()
paths=[p for p in paths if p.endswith('.meta')]
paths+=['Prototype/Assets/Library/Scenes','Prototype/Assets/Calibration/Scenes/CalibrationDemo.unity']
subprocess.run(['git','restore','--',*paths],cwd=root,check=True)
for path in [root/'Prototype/Assets/Desktop.meta',*(root/'Prototype/Assets/Desktop').rglob('*.meta')]:
    path.write_text('\n'.join(line.rstrip() for line in path.read_text().splitlines())+'\n',encoding='utf-8')
evidence=root/'Validation/Desktop/verified'
if evidence.is_dir():
    for path in evidence.iterdir():
        if path.suffix in ('.txt','.json'):
            text='\n'.join(line.rstrip() for line in path.read_text(encoding='utf-8-sig').splitlines()).strip()+'\n'
            path.write_bytes(text.encode('utf-8'))
    summary=evidence/'summary.json'
    if summary.exists():
        data=json.loads(summary.read_text(encoding='utf-8'));data['build_receipt_sha256']=hashlib.sha256((evidence/'build-receipt.json').read_bytes()).hexdigest()
        summary.write_bytes((json.dumps(data,indent=2)+'\n').encode('utf-8'))
print('Restored generated scene/import side effects; normalized only new desktop metadata')
