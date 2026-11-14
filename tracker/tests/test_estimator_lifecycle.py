import asyncio
from pathlib import Path

import numpy as np
import pytest

from spatial_tracker import estimator


@pytest.fixture
def fake_models(monkeypatch):
    import mediapipe as mp

    instances = []

    class Native:
        def __init__(self, options):
            self.options = options
            self.closed = False
            self.fail = False

        def detect_async(self, image, timestamp):
            if self.fail:
                raise RuntimeError("native inference failure")

        def close(self):
            self.closed = True

    def factory(options):
        model = Native(options)
        instances.append(model)
        return model

    monkeypatch.setattr(mp.tasks.vision.FaceLandmarker, "create_from_options", factory)
    monkeypatch.setattr(
        estimator,
        "model_paths",
        lambda _: {"face_landmarker.task": "unused", "canonical_face_model.obj": "unused"},
    )
    monkeypatch.setattr(estimator, "load_head_model", lambda _: np.zeros((468, 3)))
    return instances


@pytest.mark.asyncio
async def test_new_camera_stream_resets_native_tracking_history(fake_models):
    model = estimator.PoseEstimator(Path("unused"), lambda _: None)
    try:
        model.set_stream("A")
        first = fake_models[-1]
        model.submit("A", 1, 1000, np.zeros((16, 16, 3), dtype=np.uint8))
        model.set_stream("B")
        assert first.closed
        assert fake_models[-1] is not first
    finally:
        model.close()


@pytest.mark.asyncio
async def test_queued_inference_failure_is_observable(fake_models):
    import mediapipe as mp

    model = estimator.PoseEstimator(Path("unused"), lambda _: None)
    try:
        model.set_stream("A")
        native = fake_models[-1]
        frame = np.zeros((16, 16, 3), dtype=np.uint8)
        model.submit("A", 1, 1000, frame)
        model.submit("A", 2, 2000, frame)
        native.fail = True
        native.options.result_callback(mp.tasks.vision.FaceLandmarkerResult([], [], []), None, 1)
        with pytest.raises(RuntimeError, match="native inference failure"):
            await asyncio.wait_for(model.failure, 1)
    finally:
        model.close()
