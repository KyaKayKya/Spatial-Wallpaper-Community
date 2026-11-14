"""Strict predicates for observed acceptance evidence; never a security certificate."""
from pathlib import Path
import hashlib
import json
def require_render(cases):
 modes={'full-trust','appcontainer','private-station'}
 if set(cases)!=modes:raise ValueError('missing-render-mode')
 hashes=set()
 for mode,case in cases.items():
  if case.get('exit_code')!=0 or case.get('frames')!=60 or case.get('graphics')!='Direct3D11' or not 60<=case.get('interpreted_count',0)<=61:
   raise ValueError('render-execution-failed:'+mode)
  if case.get('width')!=640 or case.get('height')!=360 or case.get('png_valid') is not True:raise ValueError('missing-or-invalid-frame:'+mode)
  digest=case.get('frame_sha256','')
  if len(digest)!=64 or any(c not in '0123456789abcdef' for c in digest):raise ValueError('missing-frame-digest')
  hashes.add(digest)
 if len(hashes)!=1:raise ValueError('render-images-differ')
def require_permissions(cases):
 if set(cases)!={'full-trust','interactive-desktop','private-desktop'}:raise ValueError('missing-permission-mode')
 positive=['outside_file_read','loopback_connect','spawn_child','foreign_process_access','inherited_parent_secret','f24_marker']
 control=cases['full-trust']
 if control.get('app_container')!=0 or any(control.get(k) is not True for k in positive):raise ValueError('invalid-positive-control')
 for name in ['interactive-desktop','private-desktop']:
  actual=cases[name]
  if actual.get('exit_code')!=0 or actual.get('app_container')!=1:raise ValueError('not-an-appcontainer')
  if any(actual.get(k) is not False for k in positive[:-1]):raise ValueError('permission-probe-escape:'+name)
 private=cases['private-desktop']
 for key in ['input_desktop_opened','input_desktop_attached','escaped_station','f24_marker','f24_after_switch']:
  if private.get(key) is not False:raise ValueError('input-isolation-failed:'+key)
def require_build_result(result,run_id):
 if result.get('result')!='Succeeded' or result.get('errors')!=0 or result.get('run_id')!=run_id:raise ValueError('missing-or-stale-build-marker')
def require_production_permissions(control,restricted):
 positive=['outside_file_read','loopback_connect','spawn_child','input_desktop_opened','input_desktop_attached','f24_marker','escaped_station','foreign_process_access','inherited_parent_secret']
 if control.get('app_container')!=0 or any(control.get(k) is not True for k in positive):raise ValueError('invalid-production-positive-control')
 if restricted.get('app_container')!=1 or any(restricted.get(k) is not False for k in positive+['f24_after_switch']):raise ValueError('production-permission-escape-or-missing-observation')
def require_source_bytes(expected,actual):
 if set(expected)!=set(actual):raise ValueError('source-file-set-mismatch')
 for name,data in actual.items():
  if hashlib.sha256(data).hexdigest()!=expected[name]:raise ValueError('source-hash-mismatch:'+name)
def load_current_build(root):
 base=Path(root)/'Validation/Content/sdk-runtime/A/unity'
 pointer=json.loads((base/'current-build.json').read_text(encoding='utf-8'))
 run_id=pointer['run_id']
 if len(run_id)!=32 or any(c not in '0123456789abcdef' for c in run_id):raise ValueError('invalid-build-id')
 attempt=base/'attempts'/run_id
 require_build_result(json.loads((attempt/'build-result.json').read_text()),run_id)
 if not (attempt/'player/StageA.exe').is_file() or (attempt/'player/StageA_Data/Managed/StageAAuthor.dll').exists():raise ValueError('invalid-independent-player')
 for name in ['source-receipt.json','removed-author-assembly.json']:
  if not (attempt/name).is_file():raise ValueError('missing-fresh-receipt:'+name)
 return attempt
