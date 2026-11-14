"""Recover only the five pinned original camera assets and their Unity metadata."""
from pathlib import Path
import hashlib
import json
import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry
from prepare import COMMON

commit='099f7412301dd62370e4d85e4e06b50ef8706e4f'
base='https://raw.githubusercontent.com/yenichw/MiSide-Demo-Unity-Project/'+commit+'/'
paths=['Assets/AnimationClip/CameraMenu Start.anim','Assets/AnimationClip/CameraMenuAlternative Idle.anim','Assets/AnimationClip/CameraMenuAlternative Start.anim','Assets/AnimatorController/CameraMenu.controller','Assets/AnimatorController/CameraMenuAlternative.controller']
target=COMMON/'.local-assets/mita-camera';target.mkdir(exist_ok=True)
session=requests.Session();session.mount('https://',HTTPAdapter(max_retries=Retry(total=3,backoff_factor=1)))
manifest={}
for path in paths:
    for suffix in ['', '.meta']:
        output=target/(Path(path).name+suffix)
        if not output.exists():
            response=session.get(base+path+suffix,timeout=40);response.raise_for_status();output.write_bytes(response.content)
        manifest[path+suffix]=hashlib.sha256(output.read_bytes()).hexdigest()
(target/'manifest.json').write_text(json.dumps({'commit':commit,'sha256':manifest},indent=2))
print('Recovered camera animation assets:',target)
