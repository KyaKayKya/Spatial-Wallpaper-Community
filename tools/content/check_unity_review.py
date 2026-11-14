"""Run component-level review regressions in the real Unity Editor."""
import argparse,os,shutil,subprocess,json,hashlib
from build_apps import ROOT,OUT,PROJECT
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--baseline',action='store_true');args=parser.parse_args()
 target=PROJECT/'Assets/Editor/ReviewUnityChecks.cs';target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/'tools/content/ReviewUnityChecks.cs',target)
 # Baseline consumes the previously built generated project unchanged. The
 # green pass consumes freshly copied product sources from build_apps.py.
 report=OUT/'reports'/('review-unity-red.json' if args.baseline else 'review-unity.json')
 if report.exists():report.unlink()
 env=os.environ.copy();env['CONTENT_REVIEW_REPORT']=str(report);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
 result=subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(PROJECT),'-executeMethod','ReviewUnityChecks.Run','-logFile',str(OUT/'review-unity.log')],env=env,timeout=300)
 if report.exists():
  data=json.loads(report.read_text(encoding='utf-8-sig'));data['tested_sources']={name:hashlib.sha256((PROJECT/name).read_bytes()).hexdigest() for name in ['Packages/com.spatialwallpaper.sdk/Runtime/SignalBindings.cs','Assets/ContentManager/ContentLibraryView.cs']};report.write_text(json.dumps(data,indent=2,ensure_ascii=False)+'\n',encoding='utf-8');print(report.read_text(encoding='utf-8-sig'))
 else:print((OUT/'review-unity.log').read_text(encoding='utf-8-sig',errors='replace')[-10000:])
 raise SystemExit(result.returncode)
if __name__=='__main__':main()
