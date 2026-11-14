from pathlib import Path
import subprocess
r=Path(__file__).resolve().parents[2]
out=r/'Validation/RainyCafe/light-audit';out.mkdir(exist_ok=True)
subprocess.run([str(r/'Prototype/Builds/RainyCafe/RainyCafe.exe'),'-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',str(out/'player.log'),'--cafe-capture',str(out),'--cafe-audit-lights'],cwd=r,check=True)
