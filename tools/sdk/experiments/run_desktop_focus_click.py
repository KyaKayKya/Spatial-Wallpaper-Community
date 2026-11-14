"""Temporarily show the desktop, click verified blank Explorer, then restore windows."""
from pathlib import Path
import subprocess,uuid,json,hashlib,platform,ctypes,argparse
from native_build import compile_probe
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--negative-no-hook',action='store_true');options=parser.parse_args()
root=Path(__file__).resolve().parents[3];out=root/'Validation/Content/sdk-runtime/E/desktop-focus-click'/uuid.uuid4().hex;out.mkdir(parents=True);source=Path(__file__).with_name('desktop_focus_click.cpp');exe=out/'probe.exe';print(out,flush=True)
compile_probe(source,exe)
user=ctypes.WinDLL('user32');user.GetForegroundWindow.restype=ctypes.c_void_p;user.SetForegroundWindow.argtypes=[ctypes.c_void_p];previous=user.GetForegroundWindow()
class Point(ctypes.Structure):_fields_=[('x',ctypes.c_long),('y',ctypes.c_long)]
cursor=Point();user.GetCursorPos(ctypes.byref(cursor));report={'status':'not-complete','platform':platform.platform(),'scope':'actual visible Explorer blank focus acquisition only; not production desktop input','sha256':{str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [source,Path(__file__),exe]}}
try:
 result=subprocess.run([str(exe)]+(['--no-hook'] if options.negative_no_hook else []),capture_output=True,text=True,timeout=15);(out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');report.update(exitCode=result.returncode,stdout=result.stdout,stderr=result.stderr);print(result.stdout+result.stderr)
 observations=[json.loads(line) for line in result.stdout.splitlines()];report['observations']=observations
 if options.negative_no_hook:
  assert result.returncode==12 and observations[0]['secondBlankClick'] and not observations[0]['secondClickReceiverFocus'] and observations[0]['secondClickShellForeground'] and observations[0]['routedDown']==0 and observations[1]['receiverActuallyFocused'];report['status']='expected-unrouted-desktop-click-loses-focus'
 else:result.check_returncode();report['status']='partial-actual-Explorer-focus-mechanism'
except subprocess.TimeoutExpired:
 report['timeout']=True;restore=subprocess.run([str(exe),'--restore'],capture_output=True,text=True,timeout=5);report['emergencyRestoreExit']=restore.returncode;raise
finally:
 user.SetCursorPos(cursor.x,cursor.y)
 if previous:user.SetForegroundWindow(previous)
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
