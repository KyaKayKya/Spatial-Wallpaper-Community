"""Restore pinned menu materials, shader code, Blink and background; no game project."""
import hashlib,json
import requests
from requests.adapters import HTTPAdapter
from urllib3.util.retry import Retry
from prepare import COMMON,ROOT

def fetch():
    manifest=json.loads((ROOT/'tools/mita/demo-assets.json').read_text(encoding='utf8'))
    target=COMMON/'.local-assets/mita-demo';target.mkdir(exist_ok=True)
    session=requests.Session();session.mount('https://',HTTPAdapter(max_retries=Retry(total=3,backoff_factor=1)))
    base='https://raw.githubusercontent.com/yenichw/MiSide-Demo-Unity-Project/'+manifest['commit']+'/'
    for name,item in manifest['files'].items():
        path=target/name
        previous=path.read_bytes() if path.exists() else None
        payload=previous
        if payload is not None and not name.endswith('.png'):payload=payload.replace(b'\r\n',b'\n')
        if payload is None or hashlib.sha256(payload).hexdigest()!=item['sha256']:
            response=session.get(base+item['source'],timeout=60);response.raise_for_status();payload=response.content
            if not name.endswith('.png'):payload=payload.replace(b'\r\n',b'\n')
        if hashlib.sha256(payload).hexdigest()!=item['sha256']:raise ValueError('Source digest mismatch: '+name)
        if payload!=previous:
            staged=path.with_name(path.name+'.download');staged.write_bytes(payload);staged.replace(path)
    print('Verified',len(manifest['files']),'pinned files:',target)

if __name__=='__main__':fetch()
