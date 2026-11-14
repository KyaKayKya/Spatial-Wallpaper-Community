"""MediaPipe single-face tracking plus explicit canonical-model PnP measurements."""

import asyncio
import math
from pathlib import Path

import cv2
import numpy as np

from .assets import ASSETS, model_paths
from .clock import CLOCK_DOMAIN, qpc_us
from .preview import encode_preview

LANDMARKS = [1, 4, 6, 33, 61, 133, 152, 168, 197, 199, 234, 263, 291, 362, 454]
ESTIMATOR_ID = "mediapipe-1.1.1-live-single-pnp-v1"
HEAD_MODEL_ID = "canonical-face-nose1-cm-" + ASSETS["canonical_face_model.obj"][1][:16]
BINOCULAR_FEATURE = "binocular_center_v1"


def binocular_observation(face, width, height):
    """Unmirrored iris centers 468/473, [-1,1] center and full-image pixel IPD."""
    if len(face) < 474 or width <= 0 or height <= 0:
        return None
    left, right = face[468], face[473]
    values = (left.x, left.y, right.x, right.y)
    if not all(math.isfinite(v) and 0 <= v <= 1 for v in values):
        return None
    ipd = math.hypot((left.x-right.x)*width, (left.y-right.y)*height)
    if ipd < 1:
        return None
    return {"center_uv": [left.x+right.x-1, left.y+right.y-1], "ipd_px": ipd}


def solve_pose(object_points, image_points, width, height, eye_model_cv):
    objects = np.asarray(object_points, dtype=np.float64)
    pixels = np.asarray(image_points, dtype=np.float64)
    if (
        width <= 0
        or height <= 0
        or objects.shape[0] < 6
        or not np.isfinite(objects).all()
        or not np.isfinite(pixels).all()
        or np.linalg.matrix_rank(objects - objects.mean(axis=0)) < 3
    ):
        raise ValueError("Degenerate face geometry")
    intrinsics = np.array(
        [[width, 0, width / 2], [0, width, height / 2], [0, 0, 1]], dtype=np.float64
    )
    ok, rvec, tvec = cv2.solvePnP(objects, pixels, intrinsics, None, flags=cv2.SOLVEPNP_ITERATIVE)
    if not ok or not np.isfinite(tvec).all() or tvec[2, 0] <= 0:
        raise ValueError("No finite forward-facing pose")
    rotation, _ = cv2.Rodrigues(rvec)
    basis = np.diag([1.0, -1.0, 1.0])
    upright_rotation = basis @ rotation @ basis
    upright_vector, _ = cv2.Rodrigues(upright_rotation)
    angle = float(np.linalg.norm(upright_vector))
    xyz = upright_vector[:, 0] / angle * math.sin(angle / 2) if angle > 1e-12 else np.zeros(3)
    quaternion = np.r_[xyz, math.cos(angle / 2)]
    if quaternion[3] < 0:
        quaternion *= -1
    projection, _ = cv2.projectPoints(objects, rvec, tvec, intrinsics, None)
    rms = float(np.sqrt(np.mean(np.sum((projection[:, 0, :] - pixels) ** 2, axis=1))))
    position = basis @ tvec[:, 0]
    eye = basis @ (rotation @ np.asarray(eye_model_cv) + tvec[:, 0])
    return (
        {"position": position.tolist(), "orientation_xyzw": quaternion.tolist()},
        eye.tolist(),
        rms,
    )


def load_head_model(path):
    points = np.array(
        [
            [float(x) for x in line.split()[1:4]]
            for line in Path(path).read_text().splitlines()
            if line.startswith("v ")
        ]
    )
    if points.shape != (468, 3):
        raise ValueError("Unexpected canonical face model")
    # Canonical x-right/y-up/z-face-front -> PnP head x-right/y-down/z-back.
    return (points - points[1]) * [0.01, -0.01, -0.01]


class PoseEstimator:
    def __init__(self, model_root, on_sample):
        import mediapipe as mp

        self._mp = mp
        self._loop = asyncio.get_running_loop()
        self._callback = on_sample
        self.failure = self._loop.create_future()
        self._closed = False
        self._stream = None
        self._seen = False
        self._busy = None
        self._latest = None
        self._timestamp = -1
        self._preview_callback = None
        self._preview_at = 0
        self._inference_started = 0
        paths = model_paths(model_root)
        self._points = load_head_model(paths["canonical_face_model.obj"])
        self._eye = self._points[[33, 133, 263, 362]].mean(axis=0)
        self._options = mp.tasks.vision.FaceLandmarkerOptions(
            base_options=mp.tasks.BaseOptions(model_asset_path=str(paths["face_landmarker.task"])),
            running_mode=mp.tasks.vision.RunningMode.LIVE_STREAM,
            num_faces=1,
            result_callback=self._native_result,
        )
        self._model = None

    def set_stream(self, stream_id):
        if self._stream == stream_id:
            return
        self._stream = stream_id
        self._seen = False
        self._latest = self._busy = None
        if self._model is not None:
            self._model.close()
            self._model = None
        if stream_id is not None:
            self._model = self._mp.tasks.vision.FaceLandmarker.create_from_options(self._options)

    def set_preview(self, callback):
        self._preview_callback = callback
        self._preview_at = 0

    def submit(self, stream_id, frame_id, captured_at_us, rgb):
        if self._closed or stream_id != self._stream:
            return
        self._latest = (stream_id, frame_id, captured_at_us, rgb)
        self._start_latest()

    def _start_latest(self):
        if self._closed or self._busy is not None or self._latest is None:
            return
        item, self._latest = self._latest, None
        self._timestamp = max(self._timestamp + 1, item[2] // 1000)
        self._busy = (self._timestamp, item)
        self._inference_started = qpc_us()
        try:
            image = self._mp.Image(
                image_format=self._mp.ImageFormat.SRGB, data=np.ascontiguousarray(item[3])
            )
            self._model.detect_async(image, self._timestamp)
        except Exception:
            self._busy = None
            raise

    def _native_result(self, result, _image, timestamp):
        if not self._closed:
            self._loop.call_soon_threadsafe(self._complete, result, timestamp)

    def _complete(self, result, timestamp):
        try:
            self._finish(result, timestamp)
        except Exception as exc:
            if not self.failure.done():
                self.failure.set_exception(exc)

    def _finish(self, result, timestamp):
        if self._closed or self._busy is None or self._busy[0] != timestamp:
            return
        _, item = self._busy
        self._busy = None
        stream_id, frame_id, captured, rgb = item
        inference_ms = (qpc_us() - self._inference_started) / 1000
        if stream_id == self._stream:
            payload = {
                "stream_id": stream_id,
                "frame_id": str(frame_id),
                "captured_at_us": str(captured),
                "processed_at_us": str(qpc_us()),
                "tracking_state": "lost" if self._seen else "searching",
                "validity": {"head_pose": False, "eye_position": False},
                "head_pose": None,
                "eye_position": None,
                "quality": {"flags": ["no_face"], "reprojection_error_px": None},
                "extensions": {},
            }
            if result.face_landmarks:
                try:
                    height, width = rgb.shape[:2]
                    face = result.face_landmarks[0]
                    payload["extensions"][BINOCULAR_FEATURE] = binocular_observation(face, width, height)
                    pixels = np.array([[face[i].x * width, face[i].y * height] for i in LANDMARKS])
                    pose, eye, rms = solve_pose(
                        self._points[LANDMARKS], pixels, width, height, self._eye
                    )
                    payload["quality"] = {
                        "flags": ["intrinsics_prior"],
                        "reprojection_error_px": rms,
                    }
                    if rms <= max(8, width * 0.02):
                        self._seen = True
                        payload.update(
                            tracking_state="tracking",
                            head_pose=pose,
                            eye_position=eye,
                            validity={"head_pose": True, "eye_position": True},
                        )
                    else:
                        payload["tracking_state"] = "degraded"
                        payload["quality"]["flags"].append("low_geometry_quality")
                except (ValueError, cv2.error):
                    payload["tracking_state"] = "degraded"
                    payload["quality"]["flags"] = ["low_geometry_quality", "intrinsics_prior"]
            self._callback(payload)
            now = qpc_us()
            if self._preview_callback is not None and now - self._preview_at >= 125000:
                self._preview_at = now
                face = result.face_landmarks[0] if result.face_landmarks else []
                preview = encode_preview(rgb, face)
                observation = payload['extensions'].get(BINOCULAR_FEATURE)
                preview.update(stream_id=stream_id, frame_id=str(frame_id),
                               captured_at_us=str(captured), inference_ms=inference_ms,
                               ipd_px=observation['ipd_px'] if observation else None)
                self._preview_callback(preview)
        self._start_latest()

    def close(self):
        if self._closed:
            return
        self._closed = True
        self._latest = None
        if self._model is not None:
            self._model.close()
        self._busy = None
        if not self.failure.done():
            self.failure.cancel()
        elif not self.failure.cancelled():
            self.failure.exception()


def stream_descriptor(stream_id, mode, intent, tracker_instance_id):
    width, height = mode["width_px"], mode["height_px"]
    return {
        "stream_id": stream_id,
        "tracker_instance_id": tracker_instance_id,
        "camera_id": intent["camera_id"],
        "intent_revision": intent["intent_revision"],
        "coordinate_frame": "camera_upright_v1",
        "length_unit": "model_m",
        "scale_source": "canonical_face_prior",
        "head_model_id": HEAD_MODEL_ID,
        "estimator_id": ESTIMATOR_ID,
        "intrinsics_id": f"prior-fx-width-{width}x{height}-v1",
        "geometry_id": f"upright-full-{width}x{height}-v1",
        "intrinsics_source": "prior",
        "actual_mode": mode,
        "clock_domain": CLOCK_DOMAIN,
        "timestamp_origin": "frame_arrival",
        "optional_features": [],
    }
