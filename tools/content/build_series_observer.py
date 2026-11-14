"""Build a separate Player with read-only frame observation over current product sources."""
import hashlib,json,os,shutil,subprocess
from build_apps import ROOT,OUT,PROJECT,validate_receipt
from fingerprint_apps import closure
def main():
 validate_receipt();receipt_path=OUT/'reports/platform-source-receipt.json';product_receipt=hashlib.sha256(receipt_path.read_bytes()).hexdigest();sources=['NaturalSeriesCapture.cs','RuntimeSeriesObserver.cs','ContentLifecycleObserver.cs','SeriesObserverBuild.cs'];inputs={name:hashlib.sha256((ROOT/'tools/content'/name).read_bytes()).hexdigest() for name in sources}
 for name in sources:
  target=PROJECT/'Assets'/('Editor' if name=='SeriesObserverBuild.cs' else 'ContentSeriesProbe')/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/'tools/content'/name,target)
 env=os.environ.copy();env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));env['CONTENT_SERIES_BUILD']=str(OUT/'series-player/ContentPlayer.exe')
 result=subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(PROJECT),'-executeMethod','SeriesObserverBuild.Run','-logFile',str(OUT/'series-build.log')],env=env,timeout=1200)
 validate_receipt();assert product_receipt==hashlib.sha256(receipt_path.read_bytes()).hexdigest();assert inputs=={name:hashlib.sha256((ROOT/'tools/content'/name).read_bytes()).hexdigest() for name in sources}
 if result.returncode==0:(OUT/'reports/series-player-source.json').write_text(json.dumps({'scope':'Current product with separate validation-only observer; not the production installation','platform_source_receipt_sha256':product_receipt,'compiled_test_sources':inputs,'player_closure_sha256':closure(OUT/'series-player')},indent=2)+'\n')
 print('Series observer build exit',result.returncode,flush=True);raise SystemExit(result.returncode)
if __name__=='__main__':main()
