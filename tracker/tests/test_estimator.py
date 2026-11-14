import asyncio
from pathlib import Path

import numpy as np
import pytest

from spatial_tracker import estimator, assets


def test_pnp_recovers_known_translation_and_eye_offset():
    # Independent pinhole fixture: object frame uses OpenCV x-right/y-down.
    points = np.array(
        [
            [0, 0, 0],
            [-0.03, -0.03, 0.03],
            [0.03, -0.03, 0.03],
            [-0.025, 0.025, 0.02],
            [0.025, 0.025, 0.02],
            [0, 0.07, 0.04],
            [-0.06, 0, 0.06],
            [0.06, 0, 0.06],
        ],
        dtype=np.float64,
    )
    camera_points = points + [0.02, -0.01, 0.6]
    pixels = camera_points[:, :2] / camera_points[:, 2:] * 640 + [320, 240]
    pose, eye, rms = estimator.solve_pose(points, pixels, 640, 480, [0, -0.03, 0.035])
    assert np.allclose(pose["position"], [0.02, 0.01, 0.6], atol=1e-5)
    assert np.allclose(pose["orientation_xyzw"], [0, 0, 0, 1], atol=1e-5)
    assert np.allclose(eye, [0.02, 0.04, 0.635], atol=1e-5)
    assert rms < 0.001


def test_invalid_geometry_cannot_publish_zero_pose():
    with pytest.raises(ValueError):
        estimator.solve_pose(np.zeros((6, 3)), np.zeros((6, 2)), 640, 480, [0, 0, 0])


def test_asset_hash_rejects_corruption(tmp_path):
    p = tmp_path / "canonical_face_model.obj"
    p.write_bytes(b"not-the-model")
    with pytest.raises(ValueError, match="SHA-256"):
        assets.verify_asset(p, "canonical_face_model.obj")


@pytest.mark.asyncio
async def test_real_mediapipe_face_and_no_face():
    import cv2

    root = Path(".cache")
    if not all(
        (root / name).exists()
        for name in (
            "models/face_landmarker.task",
            "models/canonical_face_model.obj",
            "fixtures/portrait.jpg",
        )
    ):
        pytest.skip("Run spatial-tracker fetch-models and fetch the documented portrait fixture")
    rgb = cv2.cvtColor(cv2.imread(str(root / "fixtures/portrait.jpg")), cv2.COLOR_BGR2RGB)
    results = asyncio.Queue()
    model = estimator.PoseEstimator(root / "models", lambda p: results.put_nowait(p))
    previews = asyncio.Queue()
    model.set_preview(previews.put_nowait)
    try:
        model.set_stream("stream-A")
        model.submit("stream-A", 1, 1_000_000, rgb)
        face = await asyncio.wait_for(results.get(), 10)
        assert face["stream_id"] == "stream-A"
        assert face["validity"]["head_pose"] and face["validity"]["eye_position"]
        assert face["head_pose"]["position"][2] > 0
        observation = face["extensions"][estimator.BINOCULAR_FEATURE]
        assert observation is not None
        assert all(-1 <= v <= 1 for v in observation["center_uv"])
        assert observation["ipd_px"] > 1
        assert face["frame_id"] == "1" and face["captured_at_us"] == "1000000"
        preview = await asyncio.wait_for(previews.get(), 2)
        assert preview['frame_id'] == face['frame_id']
        assert preview['captured_at_us'] == face['captured_at_us']
        assert preview['face_found'] and preview['ipd_px'] == observation['ipd_px']
        assert preview['width'] <= 640 and preview['height'] <= 480
        model.set_preview(None)
        model.submit("stream-A", 2, 2_000_000, np.zeros_like(rgb))
        lost = await asyncio.wait_for(results.get(), 10)
        assert lost["tracking_state"] == "lost"
        assert lost["head_pose"] is None and lost["eye_position"] is None
        assert previews.empty()
    finally:
        model.close()


@pytest.mark.asyncio
async def test_stream_change_drops_inflight_old_result(monkeypatch):
    import mediapipe as mp

    options_seen = []

    class Model:
        def detect_async(self, image, stamp):
            self.stamp = stamp

        def close(self):
            pass

    native = Model()

    def factory(options):
        options_seen.append(options)
        return native

    monkeypatch.setattr(mp.tasks.vision.FaceLandmarker, "create_from_options", factory)
    results = []
    monkeypatch.setattr(
        estimator,
        "model_paths",
        lambda _: {"face_landmarker.task": "unused", "canonical_face_model.obj": "unused"},
    )
    monkeypatch.setattr(estimator, "load_head_model", lambda _: np.zeros((468, 3)))
    model = estimator.PoseEstimator(Path("unused"), results.append)
    try:
        model.set_stream("old")
        model.submit("old", 1, 1000, np.zeros((16, 16, 3), dtype=np.uint8))
        model.set_stream("new")
        model.submit("new", 1, 2000, np.zeros((16, 16, 3), dtype=np.uint8))
        result = mp.tasks.vision.FaceLandmarkerResult([], [], [])
        options_seen[0].result_callback(result, None, 1)
        await asyncio.sleep(0)
        assert results == []
        options_seen[0].result_callback(result, None, 2)
        await asyncio.sleep(0)
        assert len(results) == 1
        assert results[0]["stream_id"] == "new"
    finally:
        model.close()
