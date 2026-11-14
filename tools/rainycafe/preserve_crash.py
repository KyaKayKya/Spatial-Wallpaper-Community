from pathlib import Path
import shutil
r=Path(__file__).resolve().parents[2]/'Validation/RainyCafe'
p=r/'stills-wide/player.log'
shutil.copy2(p,r/'wide-startup-crash.log')
print(p.read_text(errors='replace')[:5000])
