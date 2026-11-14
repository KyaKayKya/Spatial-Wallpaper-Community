"""Runtime-owned, asynchronous TCP client. No user settings or calibration."""

import asyncio
from collections import OrderedDict
import copy
import json
import time
import uuid

from .activity import SensorActivityMonitor
from .clock import CLOCK_DOMAIN, qpc_us
from .estimator import ESTIMATOR_ID, BINOCULAR_FEATURE, stream_descriptor
from .model import ValidationError, decimal, error
from .protocol import Outbox, ProtocolError, encode_message, read_message
from .session import Session


def _uuid(value):
    if not isinstance(value, str):
        raise ValueError("Expected UUID")
    uuid.UUID(value)
    return value


def validate_bootstrap(value):
    if not isinstance(value, dict) or set(value) != {
        "host",
        "port",
        "token",
        "runtime_instance_id",
    }:
        raise ValueError("Invalid bootstrap fields")
    if value["host"] != "127.0.0.1":
        raise ValueError("Runtime must use IPv4 loopback")
    if type(value["port"]) is not int or not 1 <= value["port"] <= 65535:
        raise ValueError("Invalid Runtime port")
    if not isinstance(value["token"], str) or not 16 <= len(value["token"]) <= 1024:
        raise ValueError("Invalid Runtime token")
    _uuid(value["runtime_instance_id"])
    return copy.deepcopy(value)


class TrackerWorker:
    def __init__(self, camera, estimator_factory, monitor_factory=SensorActivityMonitor,
                 descriptor_factory=stream_descriptor):
        self.camera = camera
        self._describe = descriptor_factory
        self._estimator_factory = estimator_factory
        self._monitor_factory = monitor_factory
        self.instance_id = str(uuid.uuid4())
        self.connection_id = None
        self._seq = 0
        self._rx_seq = 0
        self._outbox = Outbox()
        self._operations = OrderedDict()
        self._commands = set()
        self._background = set()
        self._catalog = {"catalog_revision": "0", "catalog_ready": False, "devices": []}
        self._report_revision = 0
        self._descriptor = None
        self._features = []
        self._preview_allowed = False
        self._preview_enabled = False
        self._modes_allowed = False
        self._estimator = None
        self._monitor = None
        self._closing = False
        self._catalog_task = None
        self._catalog_dirty = False
        self._last_frame_us = 0
        self._last_activity = {}
        self._activity_tasks = {}
        self.session = Session(camera, self._state, self._stream, self._frame)

    def _fail(self, exc):
        if not self._closing and not self._fatal.done():
            self._fatal.set_exception(exc)

    def _spawn(self, coro):
        task = asyncio.create_task(coro)
        self._background.add(task)

        def done(completed):
            self._background.discard(completed)
            if not completed.cancelled() and completed.exception():
                self._fail(completed.exception())

        task.add_done_callback(done)
        return task

    def _emit(self, name, payload, kind="event", request=None):
        if self._closing:
            return
        value = {"kind": kind, "type": name, "payload": payload, "request_id": request}
        try:
            if kind == "sample":
                self._outbox.put_sample(value)
            else:
                self._outbox.put_control(value)
        except Exception as exc:
            self._fail(exc)

    def _envelope(self, message):
        self._seq += 1
        return {
            "protocol_version": 1,
            "runtime_instance_id": self.config["runtime_instance_id"],
            "sender_instance_id": self.instance_id,
            "connection_id": self.connection_id,
            "seq": str(self._seq),
            "sent_at_us": str(qpc_us()),
            "state_revision": None,
            **message,
        }

    def _validate_incoming(self, message, welcome=False):
        required = {
            "protocol_version",
            "runtime_instance_id",
            "sender_instance_id",
            "connection_id",
            "seq",
            "sent_at_us",
            "state_revision",
            "kind",
            "type",
            "request_id",
            "payload",
        }
        if not required <= message.keys() or type(message["protocol_version"]) is not int:
            raise ProtocolError("Invalid message envelope")
        if (
            message["protocol_version"] != 1
            or message["runtime_instance_id"] != self.config["runtime_instance_id"]
            or message["sender_instance_id"] != self.config["runtime_instance_id"]
        ):
            raise ProtocolError("Runtime identity or protocol mismatch")
        _uuid(message["connection_id"])
        if not welcome and message["connection_id"] != self.connection_id:
            raise ProtocolError("Connection mismatch")
        sequence = decimal(message["seq"])
        decimal(message["sent_at_us"])
        if sequence != self._rx_seq + 1:
            raise ProtocolError("Nonconsecutive incoming sequence")
        if not isinstance(message["payload"], dict):
            raise ProtocolError("Payload must be an object")
        if message["kind"] == "command":
            _uuid(message["request_id"])
        elif message["request_id"] is not None:
            raise ProtocolError("Unexpected request ID")
        self._rx_seq = sequence
        self._last_rx = time.monotonic()

    async def run(self, config):
        self.config = validate_bootstrap(config)
        self._loop = asyncio.get_running_loop()
        self._fatal = self._loop.create_future()
        writer = None
        jobs = set()
        try:
            reader, writer = await asyncio.wait_for(
                asyncio.open_connection(self.config["host"], self.config["port"]), 3
            )
            hello = self._envelope(
                {
                    "kind": "handshake",
                    "type": "Hello",
                    "request_id": None,
                    "payload": {
                        "role": "tracker",
                        "protocol_versions": [1],
                        "token": self.config["token"],
                        "subscriptions": [],
                        "implementation": ESTIMATOR_ID,
                        "capabilities": {
                            "head_pose": True,
                            "eye_pose": True,
                            "camera_preview": True,
                            "capture_modes": bool(getattr(self.camera, 'supports_modes', False)),
                            "optional_features": [BINOCULAR_FEATURE],
                        },
                    },
                }
            )
            writer.write(encode_message(hello))
            await asyncio.wait_for(writer.drain(), 2)
            welcome = await asyncio.wait_for(read_message(reader), 3)
            self._validate_incoming(welcome, welcome=True)
            payload = welcome["payload"]
            if (
                welcome["kind"] != "handshake"
                or welcome["type"] != "Welcome"
                or payload.get("protocol_version") != 1
                or payload.get("connection_id") != welcome["connection_id"]
                or payload.get("clock_domain") != CLOCK_DOMAIN
            ):
                raise ProtocolError("Invalid Welcome")
            interval = payload.get("heartbeat_interval_ms")
            if type(interval) is not int or not 250 <= interval <= 2000:
                raise ProtocolError("Unsupported heartbeat interval")
            self.connection_id = welcome["connection_id"]
            self._preview_allowed = 'camera_preview_v1' in payload.get('capabilities', [])
            self._modes_allowed = 'camera_modes_v1' in payload.get('capabilities', [])
            selected = payload.get("optional_features", [])
            if (not isinstance(selected, list) or len(selected) != len(set(selected))
                    or any(x != BINOCULAR_FEATURE for x in selected)):
                raise ProtocolError("Unsupported optional feature")
            self._features = selected
            self._estimator = self._estimator_factory(self._sample)
            if self._monitor_factory:
                try:
                    self._monitor = self._monitor_factory(self._native_activity)
                    self._monitor.start()
                except (OSError, AttributeError):
                    if self._monitor:
                        self._monitor.close()
                    self._monitor = None
            self.camera.watch(self._catalog_changed)
            await self._refresh_catalog()
            self._state(self.session.state)
            jobs = {
                asyncio.create_task(self._read(reader)),
                asyncio.create_task(self._write(writer)),
                asyncio.create_task(self._heartbeat(interval / 1000)),
            }
            if hasattr(self._estimator, "failure"):
                jobs.add(self._estimator.failure)
            done, _ = await asyncio.wait(jobs | {self._fatal}, return_when=asyncio.FIRST_COMPLETED)
            for task in done:
                task.result()
        finally:
            self._closing = True
            await self.camera.stop_watch()
            for task in jobs | self._commands | self._background:
                # Let the in-flight WinRT catalog operation complete before its
                # COM wrappers are released. No new device events can enqueue work.
                if task is not self._catalog_task:
                    task.cancel()
            await asyncio.gather(
                *(jobs | self._commands | self._background), return_exceptions=True
            )
            try:
                await self.session.close()
            finally:
                if self._estimator:
                    self._estimator.close()
                if self._monitor:
                    self._monitor.close()
                if writer:
                    writer.close()
                    try:
                        await asyncio.wait_for(writer.wait_closed(), 1)
                    except (OSError, TimeoutError):
                        pass
                if not self._fatal.done():
                    self._fatal.cancel()
                elif not self._fatal.cancelled():
                    self._fatal.exception()

    async def _read(self, reader):
        while True:
            try:
                message = await asyncio.wait_for(read_message(reader), 6)
            except asyncio.IncompleteReadError:
                return
            self._validate_incoming(message)
            if message["kind"] == "command":
                self._command(message)
            elif message["kind"] == "heartbeat" and message["type"] in ("Ping", "Pong"):
                nonce = message["payload"].get("nonce")
                if not isinstance(nonce, str) or len(nonce) > 128:
                    raise ProtocolError("Invalid heartbeat nonce")
                if message["type"] == "Ping":
                    self._emit("Pong", {"nonce": nonce}, "heartbeat")
            else:
                raise ProtocolError("Unexpected Runtime message")

    async def _write(self, writer):
        while not self._closing:
            message = await self._outbox.get()
            if self._closing:
                return
            writer.write(encode_message(self._envelope(message)))
            await asyncio.wait_for(writer.drain(), 2)

    async def _heartbeat(self, interval):
        while True:
            await asyncio.sleep(interval)
            if time.monotonic() - self._last_rx >= 6:
                raise TimeoutError("Runtime heartbeat expired")
            self._emit("Ping", {"nonce": str(qpc_us())}, "heartbeat")

    def _result(self, request, status, result=None, failure=None):
        payload = {"status": status, "result": result, "error": failure}
        self._emit("OperationResult", payload, "response", request)
        return payload

    def _command(self, message):
        request = message["request_id"]
        fingerprint = json.dumps(
            [message["type"], message["payload"]], sort_keys=True, separators=(",", ":")
        )
        now = time.monotonic()
        terminal = [
            key for key, item in self._operations.items() if item[1]["status"] != "accepted"
        ]
        for key in terminal:
            if now - self._operations[key][2] > 600:
                del self._operations[key]
        terminal = [
            key for key, item in self._operations.items() if item[1]["status"] != "accepted"
        ]
        for key in terminal[:-256]:
            del self._operations[key]
        if request in self._operations:
            old, result, _ = self._operations[request]
            if fingerprint != old:
                self._result(
                    request,
                    "failed",
                    failure=error("REQUEST_ID_REUSED", "Request ID has a different payload"),
                )
            else:
                self._emit("OperationResult", result, "response", request)
            return
        if len(self._commands) >= 32:
            self._result(
                request, "failed", failure=error("SERVER_BUSY", "Too many operations", True)
            )
            return
        accepted = self._result(request, "accepted")
        self._operations[request] = (fingerprint, accepted, now)
        task = self._spawn(self._execute(message, fingerprint))
        self._commands.add(task)
        task.add_done_callback(self._commands.discard)

    async def _execute(self, message, fingerprint):
        request = message["request_id"]
        try:
            if message["type"] == "SetCameraPreview":
                enabled = message['payload'].get('enabled')
                if set(message['payload']) != {'enabled'} or type(enabled) is not bool:
                    raise ValidationError('Invalid preview request')
                if not self._preview_allowed or not hasattr(self._estimator, 'set_preview'):
                    raise ValidationError('Preview not negotiated', 'UNSUPPORTED_MESSAGE')
                self._preview_enabled = enabled
                self._estimator.set_preview(self._preview if enabled else None)
                self._outbox.clear_preview()
                payload = self._result(request, 'completed', {'preview_enabled': enabled})
            elif message["type"] == "SetTrackerIntent":
                pref = message['payload'].get('capture_preference', {})
                if isinstance(pref, dict) and pref.get('mode_id') is not None and not self._modes_allowed:
                    raise ValidationError('Mode selection not negotiated', 'UNSUPPORTED_CAPABILITY')
                state = await self.session.apply_intent(message["payload"])
                if state["intent_revision"] != message["payload"]["intent_revision"]:
                    raise ValidationError("Newer intent applied", "SUPERSEDED")
                result = {"applied_intent_revision": state["intent_revision"], "tracking": state}
                failure = state["last_error"]
                if state["camera_state"] in ("yielded", "busy") and not failure:
                    failure = error("CAMERA_BUSY", "Camera yielded to external use")
                payload = self._result(
                    request, "failed" if failure else "completed", result=result, failure=failure
                )
            elif message["type"] == "GetState":
                scopes = message["payload"].get("scopes")
                if (
                    set(message["payload"]) != {"scopes"}
                    or not isinstance(scopes, list)
                    or any(scope not in ("cameras", "tracking") for scope in scopes)
                ):
                    raise ValidationError("Invalid state scopes")
                payload = self._result(request, "completed", self.snapshot(scopes))
            else:
                raise ValidationError("Unsupported Tracker command", "UNSUPPORTED_MESSAGE")
        except ValidationError as exc:
            payload = self._result(request, "failed", failure=error(exc.code, str(exc)))
        self._operations[request] = (fingerprint, payload, time.monotonic())
        self._operations.move_to_end(request)

    def snapshot(self, scopes):
        value = {"report_revision": str(self._report_revision)}
        if "cameras" in scopes:
            value["camera_catalog"] = copy.deepcopy(self._catalog)
        if "tracking" in scopes:
            value.update(
                tracking=copy.deepcopy(self.session.state),
                stream_descriptor=copy.deepcopy(self._descriptor),
            )
        return value

    def _state(self, state):
        self._report_revision += 1
        self._emit("TrackerStateReport", {"report_revision": str(self._report_revision), **state})

    def _stream(self, stream_id, mode, intent):
        self._outbox.clear_sample()
        self._outbox.clear_preview()
        self._last_frame_us = 0
        self._estimator.set_stream(stream_id)
        self._descriptor = (
            self._describe(stream_id, mode, intent, self.instance_id) if stream_id else None
        )
        if self._descriptor is not None:
            self._descriptor["optional_features"] = list(self._features)
        self._emit("TrackerStreamChanged", {"stream_descriptor": self._descriptor})
        if stream_id and self._modes_allowed:
            self._emit('CameraModesChanged', {'camera_id': intent['camera_id'],
                                             'modes': self.camera.supported_modes})

    def _frame(self, stream_id, frame_id, captured_at_us, rgb):
        if self._closing or not self._descriptor:
            return
        mode = self._descriptor["actual_mode"]
        if rgb.shape[:2] != (mode["height_px"], mode["width_px"]):
            self._spawn(self.session.capture_error(stream_id, ValueError("Frame geometry changed")))
            return
        rate = self.session.intent["capture_preference"]["max_inference_hz"]
        if captured_at_us - self._last_frame_us < 1_000_000 / rate:
            return
        self._last_frame_us = captured_at_us
        try:
            self._estimator.submit(stream_id, frame_id, captured_at_us, rgb)
        except Exception as exc:
            self._fail(RuntimeError("MODEL_FAILED: inference submission failed"))
            self._loop.call_exception_handler({"message": "Estimator failed", "exception": exc})

    def _sample(self, payload):
        if self._closing or payload["stream_id"] != self.session.stream_id:
            return
        payload = copy.deepcopy(payload)
        payload["extensions"] = {name: payload.get("extensions", {}).get(name)
                                 for name in self._features}
        if payload["tracking_state"] != self.session.state["tracking_state"]:
            self.session._publish(tracking_state=payload["tracking_state"])
        self._emit("TrackingSample", payload, "sample")

    def _preview(self, payload):
        if (self._closing or not self._preview_enabled
                or payload['stream_id'] != self.session.stream_id):
            return
        self._outbox.put_preview({'kind': 'preview', 'type': 'CameraPreview',
                                  'request_id': None, 'payload': payload})

    def _native_activity(self, device, activity):
        if not self._closing:
            self._loop.call_soon_threadsafe(self._activity, device, activity)

    def _activity(self, device, activity):
        device = device or self.session.state["selected_camera_id"]
        if device is None or self._closing:
            return
        # Record facts now, even while the serialized coordinator awaits native I/O.
        self.session.record_external_activity(device, activity)
        self._last_activity[device] = activity
        if device not in self._activity_tasks:
            self._activity_tasks[device] = self._spawn(self._drain_activity(device))

    async def _drain_activity(self, device):
        try:
            while device in self._last_activity:
                self._last_activity.pop(device)
                await self.session.refresh()
        finally:
            self._activity_tasks.pop(device, None)

    def _catalog_changed(self):
        if self._closing:
            return
        self._catalog_dirty = True
        if self._catalog_task is None or self._catalog_task.done():
            self._catalog_task = self._spawn(self._drain_catalog())

    async def _drain_catalog(self):
        while self._catalog_dirty and not self._closing:
            self._catalog_dirty = False
            await self._refresh_catalog()
            await self.session.refresh()

    async def _refresh_catalog(self):
        devices = await self.camera.catalog()
        selected = self.session.state["selected_camera_id"]
        if selected and not any(d["camera_id"] == selected for d in devices):
            devices += [
                dict(d, present=False)
                for d in self._catalog["devices"]
                if d["camera_id"] == selected
            ]
        for device in devices:
            capabilities = device.get("capabilities")
            if capabilities and self._monitor:
                capabilities.update(
                    external_usage_detection="activity_monitor", automatic_yield="event_driven"
                )
        if self._catalog["catalog_ready"] and self._catalog["devices"] == devices:
            return
        self._catalog = {
            "catalog_revision": str(int(self._catalog["catalog_revision"]) + 1),
            "catalog_ready": True,
            "devices": devices,
        }
        self._emit("CameraCatalogChanged", self._catalog)
