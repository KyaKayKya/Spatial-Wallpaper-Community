"""Serial camera ownership; intent updates take effect even during an awaited open."""

import asyncio
import copy
import uuid
import time

from .clock import qpc_us

from .model import ValidationError, error, validate_intent


class Session:
    def __init__(self, camera, on_state, on_stream, on_frame):
        self.camera = camera
        self.on_state, self.on_stream, self.on_frame = on_state, on_stream, on_frame
        self.intent = None
        self.stream_id = None
        self._active = None
        self._active_mode_id = None
        self._owned = False
        self._closed = False
        self._lock = asyncio.Lock()
        self._external = {}
        self._external_changed_at = {}
        self._yielded = set()
        self._retry_override = False
        self._retry_task = None
        self._attempts = 0
        self._stream_started_at = 0
        self.state = {
            "camera_state": "closed",
            "tracking_state": "suspended",
            "selected_camera_id": None,
            "active_camera_id": None,
            "intent_revision": "0",
            "stream_id": None,
            "external_usage": "unknown",
            "external_usage_observed_at_us": None,
            "external_usage_source": "unavailable",
            "blocked_reasons": ["no_demand"],
            "last_error": None,
        }

    def _publish(self, **changes):
        self.state.update(changes)
        self.on_state(copy.deepcopy(self.state))

    async def apply_intent(self, value):
        new = validate_intent(value)
        if (new["capture_preference"]["mode_id"] is not None
                and not getattr(self.camera, 'supports_modes', False)):
            raise ValidationError(
                "Shared read-only mode cannot be selected", "UNSUPPORTED_CAPABILITY"
            )
        if self._closed:
            raise ValidationError("Session is closed")
        if self.intent and int(new["intent_revision"]) <= int(self.intent["intent_revision"]):
            if new == self.intent:
                return copy.deepcopy(self.state)
            raise ValidationError("Intent has been superseded", "SUPERSEDED")
        self._retry_override = bool(
            self.intent and int(new["retry_generation"]) > int(self.intent["retry_generation"])
        )
        self.intent = new
        self._cancel_retry()
        self._attempts = 0
        self._publish(intent_revision=new["intent_revision"], selected_camera_id=new["camera_id"])
        async with self._lock:
            await self._reconcile()
        return copy.deepcopy(self.state)

    def record_external_activity(self, device_id, activity):
        if activity not in ("active", "inactive", "unknown"):
            raise ValueError("Unknown activity value")
        if self._external.get(device_id) != activity:
            self._external_changed_at[device_id] = time.monotonic()
        self._external[device_id] = activity
        if activity == "active":
            self._yielded.add(device_id)
        if not self.intent or device_id != self.intent["camera_id"] or self._closed:
            return
        self._publish(
            external_usage=activity,
            external_usage_observed_at_us=str(qpc_us()),
            external_usage_source="activity_monitor",
        )

    async def external_activity(self, device_id, activity):
        self.record_external_activity(device_id, activity)
        if not self.intent or device_id != self.intent["camera_id"] or self._closed:
            return
        await self.refresh()

    async def refresh(self):
        if self._closed:
            return
        async with self._lock:
            await self._reconcile()

    async def capture_error(self, stream_id, exc):
        if stream_id != self.stream_id or self._closed:
            return
        async with self._lock:
            if stream_id != self.stream_id:
                return
            if not await self._stop():
                return
            code = getattr(exc, "code", "CAPTURE_FAILED")
            if code == "CAMERA_BUSY":
                self._yielded.add(self.intent["camera_id"])
                self._publish(
                    camera_state="yielded",
                    blocked_reasons=["external_use"],
                    last_error=error(code, str(exc)),
                )
            else:
                self._publish(
                    camera_state="error",
                    blocked_reasons=["backend_error"],
                    last_error=error(code, str(exc), True),
                )
                self._schedule_retry()

    async def _stop(self):
        old_stream, self.stream_id = self.stream_id, None
        if old_stream is not None:
            self.on_stream(None, None, None)
        if self._owned:
            self._publish(camera_state="releasing", tracking_state="suspended", stream_id=None)
            try:
                await asyncio.wait_for(self.camera.close(), timeout=3)
            except Exception:
                self._publish(
                    camera_state="error",
                    tracking_state="suspended",
                    last_error=error("RELEASE_TIMEOUT", "Camera release not confirmed"),
                    blocked_reasons=["backend_error"],
                )
                return False
        self._owned = False
        self._active = None
        self._active_mode_id = None
        self._publish(active_camera_id=None, stream_id=None, tracking_state="suspended")
        return True

    def _capture_allowed(self, device_id, current, manual_retry):
        activity = self._external.get(device_id, "unknown")
        return activity != "active" and (
            device_id not in self._yielded
            or manual_retry
            or (activity == "inactive" and current["auto_resume_allowed"])
        )

    async def _yield_to_external(self, device_id):
        if await self._stop():
            self._publish(
                camera_state="yielded",
                blocked_reasons=["external_use"],
                external_usage=self._external.get(device_id, "unknown"),
            )

    async def _reconcile(self):
        current = self.intent
        # A retry grants one attempt, including an attempt rejected as busy.
        manual_retry, self._retry_override = self._retry_override, False
        if self._closed or not current or not current["capture_requested"]:
            if await self._stop():
                self._publish(camera_state="closed", blocked_reasons=["no_demand"], last_error=None)
            return
        device_id = current["camera_id"]
        activity = self._external.get(device_id, "unknown")
        if self.state["external_usage"] != activity:
            self._publish(
                external_usage=activity,
                external_usage_observed_at_us=None,
                external_usage_source="unavailable",
            )
        if not self._capture_allowed(device_id, current, manual_retry):
            await self._yield_to_external(device_id)
            return
        # Even a brief active report must end the old capture session.
        if device_id in self._yielded and self._owned and not await self._stop():
            return
        while (
            device_id in self._yielded
            and not manual_retry
            and self._external.get(device_id) == "inactive"
        ):
            delay = self._external_changed_at[device_id] + 0.15 - time.monotonic()
            if delay <= 0:
                break
            await asyncio.sleep(delay)
            if self._closed or current is not self.intent:
                return
        if not self._capture_allowed(device_id, current, manual_retry):
            await self._yield_to_external(device_id)
            return
        devices = await self.camera.catalog()
        if current is not self.intent or self._closed:
            return
        if not self._capture_allowed(device_id, current, manual_retry):
            await self._yield_to_external(device_id)
            return
        if device_id not in {device["camera_id"] for device in devices}:
            if await self._stop():
                self._publish(
                    camera_state="unavailable",
                    blocked_reasons=["device_missing"],
                    last_error=error("CAMERA_UNAVAILABLE", "Selected camera unavailable"),
                )
            return
        if (self._active == device_id and self.stream_id is not None
                and self._active_mode_id == current['capture_preference']['mode_id']):
            return
        if not await self._stop():
            return
        if self._closed or current is not self.intent:
            return
        if not self._capture_allowed(device_id, current, manual_retry):
            await self._yield_to_external(device_id)
            return
        stream_id = str(uuid.uuid4())
        self._publish(camera_state="opening", blocked_reasons=[], last_error=None)
        self._owned = True  # Includes partially initialized native resources.
        frame_id = 0
        opening_failure = None

        def frame(rgb, captured_at_us):
            nonlocal frame_id
            frame_id += 1
            if (
                self.stream_id == stream_id
                and self.state["camera_state"] == "streaming"
                and self.intent["capture_requested"]
                and self._external.get(device_id) != "active"
            ):
                if time.monotonic() - self._stream_started_at >= 5:
                    self._attempts = 0
                self.on_frame(stream_id, frame_id, captured_at_us, rgb)

        def failed(exc):
            nonlocal opening_failure
            if self.stream_id == stream_id:
                asyncio.create_task(self.capture_error(stream_id, exc))
            elif self.state["camera_state"] == "opening" and current is self.intent:
                opening_failure = exc

        try:
            requested_mode = current['capture_preference']['mode_id']
            opening = (self.camera.open(device_id, frame, failed, mode_id=requested_mode)
                       if requested_mode is not None else self.camera.open(device_id, frame, failed))
            mode = await asyncio.wait_for(opening, timeout=10)
            if opening_failure is not None:
                raise opening_failure
        except Exception as exc:
            if not await self._stop():
                return
            code = getattr(exc, "code", "CAPTURE_FAILED")
            target = {
                "CAMERA_BUSY": "busy",
                "PERMISSION_DENIED": "permission_denied",
                "CAMERA_UNAVAILABLE": "unavailable",
            }.get(code, "error")
            reason = {
                "CAMERA_BUSY": "external_use",
                "PERMISSION_DENIED": "permission_denied",
                "CAMERA_UNAVAILABLE": "device_missing",
            }.get(code, "backend_error")
            self._publish(
                camera_state=target,
                blocked_reasons=[reason],
                last_error=error(code, str(exc), code == "CAPTURE_FAILED"),
            )
            if code == "CAMERA_BUSY":
                self._yielded.add(device_id)
            if code == "CAPTURE_FAILED":
                self._schedule_retry()
            return
        if self._closed or current is not self.intent:
            await self._stop()
            return
        if not self._capture_allowed(device_id, current, manual_retry):
            await self._yield_to_external(device_id)
            return
        self._active, self.stream_id = device_id, stream_id
        self._active_mode_id = current['capture_preference']['mode_id']
        self._yielded.discard(device_id)
        self._stream_started_at = time.monotonic()
        self.on_stream(stream_id, mode, copy.deepcopy(current))
        self._publish(
            camera_state="streaming",
            tracking_state="searching",
            active_camera_id=device_id,
            stream_id=stream_id,
            blocked_reasons=[],
            last_error=None,
        )

    def _cancel_retry(self):
        if self._retry_task and self._retry_task is not asyncio.current_task():
            self._retry_task.cancel()
        self._retry_task = None

    def _schedule_retry(self):
        if self._closed or self._attempts >= 5:
            return
        self._attempts += 1
        delay = min(2 ** (self._attempts - 1), 30)

        async def retry():
            await asyncio.sleep(delay)
            await self.refresh()

        self._retry_task = asyncio.create_task(retry())

    async def close(self):
        self._closed = True
        self._cancel_retry()
        async with self._lock:
            if await self._stop():
                self._publish(camera_state="closed", blocked_reasons=["no_demand"])
