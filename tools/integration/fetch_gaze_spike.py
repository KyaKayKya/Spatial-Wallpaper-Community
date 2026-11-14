"""Pinned research-only L2CS tensors; no camera, pickle, or production install."""
import hashlib
import json
import urllib.request
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]/'Validation/Gaze/local'
REV='241076966f03b197773ce75d23adc60299473ccd'
SOURCE_REV='a4d8f7fa5436a2b2b9f088471623b552a85811bd'
FILES={
    'l2cs_gaze360_resnet50.safetensors': ('https://huggingface.co/py-feat/l2cs/resolve/'+REV+'/l2cs_gaze360_resnet50.safetensors','75405bdc01f7086b3887280fe50c28a14bc62463a53fa749781e4a3cf98eca0e'),
    'LICENSE-NOTICE.md':('https://huggingface.co/py-feat/l2cs/resolve/'+REV+'/LICENSE-NOTICE.md','72ab2446fc37e68dde70b5f53fd7fc9d18441e27f5a85cb52427e0933bb7b1af'),
    'official_model.py':('https://raw.githubusercontent.com/Ahmednull/L2CS-Net/'+SOURCE_REV+'/l2cs/model.py','2fcf682d880bc22a4686b22ff89d48d0a46429ce47e6025a3aeaf1e6fb6fc2c0'),
    'official_LICENSE':('https://raw.githubusercontent.com/Ahmednull/L2CS-Net/'+SOURCE_REV+'/LICENSE','fee4412d8f04aa84eb629e9631d1c067fee9454247ef016e8667768b6bc7488c'),
}
def main():
    ROOT.mkdir(parents=True,exist_ok=True); records=[]
    for name,(url,expected) in FILES.items():
        target=ROOT/name
        if not target.exists():
            part=target.with_suffix('.part')
            with urllib.request.urlopen(url,timeout=60) as source,part.open('wb') as dest:
                while chunk:=source.read(1024*1024):dest.write(chunk)
            digest=hashlib.sha256(part.read_bytes()).hexdigest()
            if expected and digest!=expected:raise ValueError('Hash mismatch: '+name)
            part.replace(target)
        digest=hashlib.sha256(target.read_bytes()).hexdigest()
        if expected and digest!=expected:raise ValueError('Existing hash mismatch: '+name)
        records.append({'file':name,'url':url,'bytes':target.stat().st_size,'sha256':digest})
    report={'dataset':'Gaze360','license_scope':'research only; no commercial bundling cleared','files':records,'accuracy_verified':False}
    (ROOT/'model-manifest.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))
if __name__=='__main__':main()
