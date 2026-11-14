"""Preserve a verified development delivery outside worktrees; keep original works intact."""
from pathlib import Path
import argparse,hashlib,json,shutil,zipfile
ROOT=Path(__file__).resolve().parents[2]

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--candidate',required=True,type=Path);p.add_argument('--release',required=True,type=Path);p.add_argument('--trial',required=True,type=Path);p.add_argument('--packages',required=True,type=Path);a=p.parse_args()
 candidate=a.candidate.resolve();release=a.release.resolve();assert release!=candidate and (release/'AuthorProject/Assets').is_dir();assert not (release/'client').exists(),'Refuse to overwrite a prior delivery'
 shutil.copytree(candidate/'client',release/'client')
 for name in ['notices','contracts']:shutil.copytree(candidate/name,release/name)
 for name in ['com.spatialwallpaper.sdk-0.3.0-preview.3.tgz','SpatialWallpaperRuntime-windows-x64.zip','SpatialWallpaperClient-windows-x64.zip','THIRD-PARTY-NOTICES.txt']:
  source=candidate/name
  if source.is_file():shutil.copyfile(source,release/name)
 packages=json.loads(a.packages.read_text(encoding='utf-8-sig'));folder=release/'Packages';folder.mkdir();records=[]
 for item in packages:
  source=Path(item['package']);name=item.get('scene',item['name'].split()[0]);destination=folder/(name+'.spatial');shutil.copyfile(source,destination);records.append({'name':name,'package':str(destination.relative_to(release)),'sha256':digest(destination),'bytes':destination.stat().st_size})
 library=release/'TrialData/library';shutil.copytree(a.trial.resolve()/'library',library)
 index=library/'index.json';state=json.loads(index.read_text(encoding='utf-8-sig'));state['applied']={};index.write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding='utf-8')
 # Copy only the installed works; no test IPC tokens, sessions, stored game state,
 # fault journals, or deliberately altered test volume settings enter the library.
 (release/'Start-ContentManager.cmd').write_text('@echo off\r\nstart "" "%~dp0client\\manager\\ContentManager.exe" --content-data-root "%~dp0TrialData" --sdk-attempts "%~dp0..\\..\\sdk-attempts\\sdk-audio\\a"\r\n',encoding='ascii')
 docs=release/'Docs';docs.mkdir();sdkdocs=ROOT/'Packages/com.spatialwallpaper.sdk/Documentation~'
 for source in sdkdocs.iterdir():
  if source.is_file():shutil.copyfile(source,docs/source.name)
 shutil.copyfile(ROOT/'tools/wallpapers/music.json',release/'music-sources.json')
 (release/'packages.json').write_text(json.dumps(records,indent=2),encoding='utf-8')
 author=release/'AuthorProject'
 with zipfile.ZipFile(release/'WallpaperSdk-AuthorProject.zip','w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
  for source in sorted(author.rglob('*')):
   if source.is_file():archive.write(source,source.relative_to(release).as_posix())
 files={p.relative_to(release).as_posix():{'sha256':digest(p),'bytes':p.stat().st_size} for p in release.rglob('*') if p.is_file() and 'TrialData' not in p.relative_to(release).parts};(release/'delivery-files.json').write_text(json.dumps(files,indent=2),encoding='utf-8');print(release)
if __name__=='__main__':main()
