"""Summarize opt-in synthetic player telemetry; no exposure-to-photon claim."""
import argparse
import bisect
import csv
import json
import math
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("folder", type=Path)
args = parser.parse_args()
rows = list(csv.DictReader((args.folder / "frames.csv").open(encoding="utf-8")))
fresh = [r for r in rows if r["fresh"] == "True" and int(r["captured_us"]) > 0]
unique = {}
for row in fresh:
    unique.setdefault((row["stream"], row["sampleFrame"]), row)
first = list(unique.values())


def percentile(values, p):
    values = sorted(values)
    return values[min(len(values) - 1, round((len(values) - 1) * p))]


def interval(a, b):
    values = [(int(r[b]) - int(r[a])) / 1000 for r in first]
    return dict(p50_ms=percentile(values, .5), p95_ms=percentile(values, .95),
                p99_ms=percentile(values, .99), max_ms=max(values))


result = dict(rendered_frames=len(rows), fresh_frames=len(fresh), distinct_rendered_samples=len(first),
              capture_to_receive=interval("captured_us", "received_us"),
              capture_to_main=interval("captured_us", "applied_us"),
              capture_to_render_submission=interval("captured_us", "render_us"),
              max_corner_error_px=max(float(r["cornerErrorPx"]) for r in rows),
              max_parallax_error_px=max(float(r["parallaxErrorPx"]) for r in rows),
              rejected=max(int(r["rejected"]) for r in rows),
              connections=max(int(r["connections"]) for r in rows),
              limitations=["Synthetic measurement processing, not live inference",
                           "frame_arrival is not camera exposure",
                           "endFrameRendering precedes GPU/OS/display presentation"])
# Estimate combined sample/Unity/filter phase delay on the smooth motion segment.
times = [int(r["captured_us"]) for r in first]
targets = [[float(r[k]) for k in ("targetX", "targetY", "targetZ")] for r in first]
if len(first) > 100 and "faults" not in str(args.folder):
    errors = []
    for delay_ms in range(151):
        error = 0
        count = 0
        for row in fresh:
            t = int(row["render_us"]) - delay_ms * 1000
            index = bisect.bisect_right(times, t) - 1
            if index < 30 or index >= len(times) - 1:
                continue
            fraction = (t - times[index]) / (times[index + 1] - times[index])
            for axis, key in enumerate(("eyeX", "eyeY", "eyeZ")):
                expected = targets[index][axis] * (1 - fraction) + targets[index + 1][axis] * fraction
                error += (float(row[key]) - expected) ** 2
            count += 3
        errors.append((error / count if count else math.inf, delay_ms))
    mse, delay = min(errors)
    result["synthetic_effective_phase_delay_ms"] = delay
    result["phase_fit_rms_mm"] = math.sqrt(mse) * 1000
(args.folder / "summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, indent=2))
if result["max_corner_error_px"] > .01 or result["max_parallax_error_px"] > .01:
    raise SystemExit("Projection instability")
if not first or result["connections"] < 1:
    raise SystemExit("No end-to-end tracking")
