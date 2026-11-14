"""Check changed documentation links, SDK dependency isolation and Unity GUIDs."""
from pathlib import Path
import json,re,subprocess
ROOT=Path(__file__).resolve().parents[2]
def git(*args):return subprocess.check_output(['git',*args],cwd=ROOT).decode('utf-8')
def main():
 names=set(git('diff','--name-only','-z','origin/dev/v0.0').split('\0'))|set(git('ls-files','--others','--exclude-standard','-z').split('\0'));errors=[];links=0
 def anchors(path):
  result=set();counts={};fenced=False
  for line in path.read_text(encoding='utf-8-sig').splitlines():
   if line.startswith('```'):fenced=not fenced
   if fenced or not re.match(r'^#{1,6} ',line):continue
   heading=re.sub(r'^#+ ','',line).strip().lower().replace('`','');slug=''.join(c for c in heading if c.isalnum() or c in ' _-').replace(' ','-');n=counts.get(slug,0);counts[slug]=n+1;result.add(slug+('-'+str(n) if n else ''))
  return result
 for name in names:
  path=ROOT/name
  if path.is_file() and path.suffix=='.md':
   for match in re.finditer(r'!?\[[^\]]*\]\(([^\n]+?)\)',path.read_text(encoding='utf-8-sig')):
    href=match[1].strip().strip('<>')
    if re.match(r'^[a-z]+:',href):continue
    target,_,anchor=href.partition('#')
    # Public README templates are rendered at the exported repository root.
    template=name in {'docs/public/README.md','docs/public/README.en.md'}
    link_base=ROOT if template else path.parent
    dest=(link_base/target).resolve() if target else path;links+=1
    public_doc=template or name.startswith('docs/public/')
    if template and target in {'README.md','README.en.md'}:dest=ROOT/'docs/public'/target
    generated={'SOURCE_MANIFEST.json','SHA256SUMS','OMITTED_INPUTS.json','SOURCE_LICENSE_STATUS.md','THIRD_PARTY_NOTICES.md'}
    if public_doc and dest.parent==ROOT and dest.name in generated:continue
    if not dest.exists() or anchor and dest.suffix=='.md' and anchor not in anchors(dest):errors.append(name+': '+href)
  if path.is_file() and (name.startswith('Prototype/Assets/') or name.startswith('Packages/com.spatialwallpaper.sdk/')) and path.suffix in ['.cs','.shader','.asset','.mat']:
   if not Path(str(path)+'.meta').is_file():errors.append('Missing meta: '+name)
 projection='Packages/com.spatialwallpaper.sdk/Runtime/Projection/WindowProjection.cs'
 # The reviewed physical-unit adapter is pinned; later edits still fail this guard.
 assert git('show','945497044f033c7e38015a1284697c255989c660:'+projection)==(ROOT/projection).read_text(), 'Reviewed projection source changed'
 assert git('show','origin/dev/v0.0:'+projection+'.meta')==(ROOT/(projection+'.meta')).read_text(), 'Projection GUID changed'
 package=json.loads((ROOT/'Packages/com.spatialwallpaper.sdk/package.json').read_text());assert not any('evo' in name.lower() or 'track' in name.lower() for name in package['dependencies'])
 for path in (ROOT/'Packages/com.spatialwallpaper.sdk').rglob('*.cs'):
  text=path.read_text(encoding='utf-8');assert not any(value in text for value in ['WebCamTexture','RuntimeTrackingHost','using SpatialTracking','Camera.main']),str(path)
  # Trusted Editor lifecycle adapters, Windows script bootstrap and per-renderer
  # input owner may start owned services. Input only starts the pinned trusted
  # client role and authenticates its private control peer; it runs no authors.
  # Runtime author code remains governed by the exact admission member catalog.
  if 'Process.Start' in text:assert path in [ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PreviewHostLease.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Editor/PlayCapabilities.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Editor/SandboxPreviewRun.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Native/Windows/ScriptProcessClient.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Native/Windows/NativeInputProcess.cs'],str(path)
 editor=json.loads((ROOT/'Packages/com.spatialwallpaper.sdk/Editor/SpatialWallpaper.Sdk.Editor.asmdef').read_text())
 assert editor['includePlatforms']==['Editor'],'Play broker launcher must stay Editor-only'
 if errors:raise SystemExit('\n'.join(errors))
 print('PASS',links,'changed-document links; Unity meta pairs; projection source/GUID; SDK dependency isolation')
if __name__=='__main__':main()
