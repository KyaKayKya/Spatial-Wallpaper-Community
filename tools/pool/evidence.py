"""Run acceptance workloads sequentially, keeping performance separate from export."""
from pathlib import Path
import subprocess,sys
root=Path(__file__).resolve().parents[2]
def run(*args):subprocess.run([sys.executable,*args],cwd=root,check=True)
run('tools/pool/unity.py','PoolChecks.SceneBatch','checks-acceptance')
run('tools/pool/unity.py','PoolChecks.FrontBatch','aperture-acceptance')
run('tools/pool/unity.py','PoolChecks.WaterlineBatch','waterline-acceptance')
run('tools/pool/player.py','checks','acceptance-checks')
run('tools/pool/player.py','cycle','acceptance-switching')
run('tools/pool/player.py','capture','acceptance-showcase','--scene','showcase')
run('tools/pool/player.py','capture','acceptance-views','--capture-views','-screen-width','1920','-screen-height','1080')
run('tools/pool/player.py','capture','acceptance-frames','--capture-sequence','-screen-width','1920','-screen-height','1080')
subprocess.run(['ffmpeg','-y','-framerate','30','-i',str(root/'Validation/Pool/acceptance-frames/%04d.png'),'-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(root/'Validation/Pool/pool-warm-parallax-1080p.mp4')],check=True)
for quality in ['high','low','static']:
 extra=['--always-render'] if quality=='static' else []
 run('tools/pool/player.py','benchmark','bench-'+quality+'-1080','--pool-quality',quality,'-screen-width','1920','-screen-height','1080',*extra)
run('tools/pool/summarize.py')
