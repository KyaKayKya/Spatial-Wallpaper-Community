from pathlib import Path
import hashlib
ROOT=Path(__file__).resolve().parents[2]
def sources(root=ROOT):
 root=Path(root)
 roots=[root/'tools/sdk',root/'Packages/com.spatialwallpaper.sdk',root/'Supervisor',root/'Prototype/Assets/ContentRuntime/Core']
 return {p.relative_to(root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for base in roots for p in base.rglob('*') if p.is_file() and (p.suffix in {'.py','.cs','.shader','.json','.asmdef','.cpp','.h','.config'} or 'license' in p.name.lower() or p.name.lower().startswith(('notice','copying')))}
