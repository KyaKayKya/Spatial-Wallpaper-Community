from pathlib import Path
import urllib.request,hashlib,json
r=Path(__file__).resolve().parents[2];p=r/'Prototype/Assets/Limpool/Generated/kloppenheim_05_puresky_4k.hdr'
url='https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/4k/kloppenheim_05_puresky_4k.hdr'
if not p.exists():
    with urllib.request.urlopen(url,timeout=120) as response:p.write_bytes(response.read())
assert hashlib.md5(p.read_bytes()).hexdigest()=='7e626c52b9566fea733485bcf12bd5e3'
(r/'Validation/Limpool/sky-source.json').write_text(json.dumps({'url':url,'page':'https://polyhaven.com/a/kloppenheim_05_puresky','license':'CC0','authors':['Greg Zaal','Jarod Guest'],'sha256':hashlib.sha256(p.read_bytes()).hexdigest()},indent=2))
print('Verified HDR sky',p.stat().st_size)
