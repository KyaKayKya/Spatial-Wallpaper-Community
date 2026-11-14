"""Verify tread clearance and capture the animated contact with a stationary eye."""
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
def run(*args):
    subprocess.run([sys.executable, *args], cwd=root, check=True)

run('tools/pool/unity.py', 'PoolChecks.WaterlineBatch', 'waterline-green')
run('tools/pool/player.py', 'capture', 'waterline-fixed-frames',
    '--capture-sequence', '--capture-fixed-eye', '-screen-width', '1920', '-screen-height', '1080')
subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-framerate', '30', '-i',
                str(root / 'Validation/Pool/waterline-fixed-frames/%04d.png'),
                '-c:v', 'libx264', '-crf', '18', '-pix_fmt', 'yuv420p', '-movflags', '+faststart',
                str(root / 'Validation/Pool/pool-waterline-fixed-eye-1080p.mp4')], check=True)
run('tools/pool/evidence.py')
