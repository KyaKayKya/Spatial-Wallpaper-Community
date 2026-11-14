"""Exercise the actual exporter with two local keyword sets on one shader."""
import argparse,os,shutil,subprocess
from build_apps import ROOT,OUT
def main():
 p=argparse.ArgumentParser();p.add_argument('--baseline',action='store_true');args=p.parse_args();project=OUT/'pool-author';output=OUT/('keyword-red' if args.baseline else 'keyword-checks');output.mkdir(exist_ok=True)
 if not args.baseline:
  for name in ['ExportShaderVariants.cs','ScenePackageExporter.cs']:shutil.copy2(ROOT/'Packages/com.spatialwallpaper.sdk/Editor'/name,project/'Packages/com.spatialwallpaper.sdk/Editor'/name)
 shutil.copy2(ROOT/'tools/content/LocalKeywordExportChecks.cs',project/'Assets/Editor/LocalKeywordExportChecks.cs')
 env=os.environ.copy();env['CONTENT_KEYWORD_CHECK_OUTPUT']=str(output);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
 r=subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(project),'-executeMethod','LocalKeywordExportChecks.Run','-logFile',str(output/'unity.log')],env=env,timeout=600)
 if (output/'result.json').exists():print((output/'result.json').read_text())
 print('Keyword check exit',r.returncode);raise SystemExit(r.returncode)
if __name__=='__main__':main()
