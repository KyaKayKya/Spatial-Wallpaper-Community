"""Assemble actual Player backbuffers using measured frame timing."""
import argparse
import csv
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("folder", type=Path)
parser.add_argument("--ffmpeg", default="C:/Users/Administrator/miniconda3/Library/bin/ffmpeg.exe")
args = parser.parse_args()
rows = {int(row["frame"]) + 1: row for row in csv.DictReader((args.folder / "frames.csv").open())}
started = (args.folder / "conditions.txt").stat().st_mtime_ns
frames = sorted(p for p in (args.folder / "frames").glob("*.png")
                if p.stat().st_mtime_ns >= started and int(p.stem) in rows)
if not frames:
    raise SystemExit("No current-run frames")
lines = []
for index, path in enumerate(frames):
    lines.append("file '" + str(path.resolve()).replace("\\", "/").replace("'", "'\\''") + "'")
    if index + 1 < len(frames):
        a, b = int(path.stem), int(frames[index + 1].stem)
        if a in rows and b in rows:
            duration = (int(rows[b]["render_us"]) - int(rows[a]["render_us"])) / 1e6
        else:
            duration = 1 / 30
        lines.append(f"duration {max(.001, duration):.6f}")
manifest = args.folder / "local-concat.txt"
manifest.write_text("\n".join(lines), encoding="utf-8")
subprocess.run([args.ffmpeg, "-y", "-f", "concat", "-safe", "0", "-i", str(manifest),
                "-vf", "fps=30", "-c:v", "libx264", "-pix_fmt", "yuv420p",
                "-crf", "22", str(args.folder / "demo.mp4")], check=True,
               stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
print(args.folder / "demo.mp4")
