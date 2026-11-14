"""Check encoded video dimensions, deterministic timing and Player error logs."""
from pathlib import Path
import csv
import json
import subprocess

root = Path(__file__).resolve().parents[2] / 'Validation/Pool'
results = {}
for name in ['pool-warm-parallax-1080p.mp4', 'pool-waterline-fixed-eye-1080p.mp4']:
    data = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-select_streams', 'v:0',
        '-show_entries', 'stream=width,height,nb_frames,r_frame_rate,duration', '-of', 'json', str(root/name)]))['streams'][0]
    assert (data['width'], data['height'], int(data['nb_frames']), data['r_frame_rate']) == (1920, 1080, 240, '30/1'), data
    results[name] = data
for name in ['acceptance-frames', 'waterline-fixed-frames']:
    rows = list(csv.DictReader((root/name/'sequence-timing.csv').open()))
    steps = [float(b['content_time'])-float(a['content_time']) for a,b in zip(rows,rows[1:])]
    assert len(rows)==240 and max(abs(s-1/30) for s in steps)<.0001
    results[name] = dict(frames=len(rows), min_step=min(steps), max_step=max(steps))
for directory in ['acceptance-checks','acceptance-switching','acceptance-showcase','acceptance-views',
                  'acceptance-frames','waterline-fixed-frames']:
    lines=(root/directory/'player.log').read_text(errors='replace').splitlines()
    errors=[line for line in lines if any(token in line for token in ['Exception:', 'Shader error', 'NullReferenceException'])]
    assert not errors, (directory, errors)
results['player_errors']=[]
(root/'video-checks.json').write_text(json.dumps(results,indent=2))
print(json.dumps(results,indent=2))
