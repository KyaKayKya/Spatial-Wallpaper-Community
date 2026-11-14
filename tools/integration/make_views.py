"""Write a clearly synthetic recording in camera_upright_v1 for repeatable view tests."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
views = [(0, 0, -.6), (-.2, 0, -.6), (.2, 0, -.6), (0, .12, -.6),
         (0, -.12, -.6), (0, 0, -.35), (0, 0, -.95), (0, 0, -.6)]
samples = []
for index, eye in enumerate(views):
    x, y, z = eye
    for tick in range(10):
        sample = dict(stream_id="00000000-0000-4000-8000-000000000001",
                      frame_id=str(index * 10 + tick + 1),
                      captured_at_us="0", processed_at_us="0", tracking_state="tracking",
                      validity=dict(head_pose=False, eye_position=True), head_pose=None,
                      eye_position=[-x, y + .03, .035 - z],
                      quality=dict(flags=["intrinsics_prior"], reprojection_error_px=None),
                      extensions={})
        samples.append(dict(offset_us=(index * 10 + tick) * 200000, sample=sample))
result = dict(descriptor=dict(estimator_id="synthetic-eye-v1"), samples=samples)
path = root / "Validation/Integration/synthetic-views.json"
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(json.dumps(result, separators=(",", ":")) + "\n", encoding="utf-8")
print(path, path.stat().st_size, "bytes")
