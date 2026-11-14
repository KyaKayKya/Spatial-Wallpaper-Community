"""Check the trusted preview and desktop surfaces with an already received frame."""
from pathlib import Path
import subprocess,sys,json
ROOT=Path(__file__).resolve().parents[3]
frame=Path(sys.argv[1]).resolve()
if frame.name!='frame.rgb' or not frame.is_relative_to(ROOT/'Validation/Content/sdk-runtime'):raise SystemExit('generated-frame-required')
out=frame.parent
data=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
exe=out/'PresentationProbe.exe'
cmd=[str(data/'MonoBleedingEdge/bin/mono.exe'),str(data/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:System.Windows.Forms.dll','-r:System.Drawing.dll','-out:'+str(exe),str(Path(__file__).with_name('PresentationProbe.cs')),str(ROOT/'Prototype/Assets/Desktop/Windows/WindowsDesktopWindow.cs'),str(ROOT/'Prototype/Assets/Desktop/Core/DesktopDisplay.cs')]
subprocess.run(cmd,check=True)
failed=False
for mode in ['preview','desktop']:
 dest=out/(mode+'-presentation.json')
 result=subprocess.run([str(exe),mode,str(frame),str(dest)],timeout=15)
 print(dest.read_text() if dest.exists() else 'no result',flush=True)
 failed|=result.returncode!=0
raise SystemExit(1 if failed else 0)
