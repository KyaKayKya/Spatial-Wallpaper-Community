"""Extract pinned original Unity triangles for visible cafe details."""
from pathlib import Path
import argparse,os,shutil,subprocess
ROOT=Path(__file__).resolve().parents[2]
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve();assert out.is_relative_to(ROOT/'Validation') and not out.exists();out.mkdir(parents=True)
 source=Path(__file__).with_name('CafeNativeDetails.cs');target=ROOT/'Prototype/Assets/Editor/CafeNativeDetails.cs';assert not target.exists();shutil.copy2(source,target)
 try:
  env=os.environ.copy();env.update(SDK_CAFE_DETAILS_OUTPUT=str(out),SDK_CAFE_DETAILS_CONFIG=str(Path(__file__).with_name('cafe-details.json')));env.setdefault('ALLUSERSPROFILE',env.get('PROGRAMDATA','C:/ProgramData'))
  subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(ROOT/'Prototype'),'-executeMethod','CafeNativeDetails.Run','-logFile',str(out/'unity.log')],env=env,timeout=180,check=True)
 finally:
  assert target.read_bytes()==source.read_bytes();target.unlink();target.with_suffix('.cs.meta').unlink(missing_ok=True)
if __name__=='__main__':main()
