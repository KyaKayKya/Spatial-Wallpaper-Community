from pathlib import Path
import subprocess,sys
r=Path(__file__).resolve().parents[2];label=sys.argv[1]
out=r/'Validation/Limpool'/label;out.mkdir(parents=True,exist_ok=True)
frames=[] if '--frames' in sys.argv else ['--frames','90']
cmd=[str(r/'Prototype/Builds/Limpool/Limpool.exe'),'-force-d3d11','-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',str(out/'player.log'),'--limpool-capture',str(out),'--video','--freeze-effects']+frames+sys.argv[2:]
sys.exit(subprocess.run(cmd).returncode)
