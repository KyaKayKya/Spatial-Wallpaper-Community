"""Opt-in camera-free source; all control and transport stay in TrackerWorker."""
import asyncio
import copy
import json
import math
from pathlib import Path

import numpy as np

from .clock import qpc_us
from .estimator import stream_descriptor
from .protocol import encode_message
from .preview import encode_preview
from .worker import TrackerWorker


class SimulatedCamera:
    supports_modes = True

    def __init__(self, mode=None):
        self.task = None
        self.mode = copy.deepcopy(mode)
        self.supported_modes = [dict(mode_id='synthetic-640-480-30', width_px=640,height_px=480,
                                     fps=30,fps_numerator=30,fps_denominator=1,pixel_format='RGB8'),
                                dict(mode_id='synthetic-1920-1080-30',width_px=1920,height_px=1080,
                                     fps=30,fps_numerator=30,fps_denominator=1,pixel_format='RGB8')]

    async def catalog(self):
        return [dict(camera_id="simulation", display_name="Synthetic measurement source",
                     present=True, backend="simulation", modes=[], capabilities={})]

    def watch(self, callback):
        pass

    async def stop_watch(self):
        pass

    async def open(self, camera_id, on_frame, on_error, mode_id=None):
        if camera_id != "simulation":
            raise ValueError("Simulation requires its explicit device ID")
        mode = self.mode or dict(mode_id=None, source_width_px=640, source_height_px=480,
                                 width_px=640, height_px=480, fps=60, pixel_format="RGB8",
                                 rotation_deg=0, crop_rect=[0, 0, 640, 480], mirrored=False)
        if mode_id is not None:
            requested = next((m for m in self.supported_modes if m['mode_id']==mode_id),None)
            if requested is None:
                raise ValueError('Unsupported simulation mode')
            mode = dict(mode, mode_id=mode_id, width_px=requested['width_px'],
                        height_px=requested['height_px'],source_width_px=requested['width_px'],
                        source_height_px=requested['height_px'],fps=requested['fps'],
                        crop_rect=[0,0,requested['width_px'],requested['height_px']])
        width, height = mode["width_px"], mode["height_px"]
        if type(width) is not int or type(height) is not int or not (1 <= width <= 4096 and 1 <= height <= 4096):
            raise ValueError("Recording geometry exceeds replay bounds")
        image = np.zeros((height, width, 3), dtype=np.uint8)

        async def frames():
            while True:
                on_frame(image, qpc_us())
                await asyncio.sleep(1 / (mode['fps'] or 60))

        self.task = asyncio.create_task(frames())
        return mode

    async def close(self):
        if self.task:
            self.task.cancel()
            await asyncio.gather(self.task, return_exceptions=True)
            self.task = None


class MeasurementReplay:
    def __init__(self, callback, recording=None, scenario="motion"):
        self.callback = callback
        self.recording = recording
        self.scenario = scenario
        self.stream = None
        self.preview = None
        self.preview_at = 0

    def set_preview(self, callback):
        self.preview = callback
        self.preview_at = 0

    def set_stream(self, stream):
        self.stream = stream
        self.start = qpc_us()
        self.index = 0

    def submit(self, stream, frame, captured, image):
        elapsed = (captured - self.start) / 1_000_000
        if self.recording:
            rows = self.recording["samples"]
            if self.index >= len(rows) or elapsed * 1_000_000 < rows[self.index]["offset_us"]:
                return
            # Skip superseded recording rows just as the live estimator skips old input.
            while self.index + 1 < len(rows) and rows[self.index + 1]["offset_us"] <= elapsed * 1_000_000:
                self.index += 1
            row = rows[self.index]
            sample = copy.deepcopy(row["sample"])
            sample.update(stream_id=stream, frame_id=str(frame), captured_at_us=str(captured),
                          processed_at_us=str(qpc_us()))
            self.index += 1
        else:
            if self.scenario == "fixed":
                elapsed = 0
            if self.scenario == "faults" and 5 <= elapsed < 6:
                return  # Healthy TCP heartbeats cannot refresh the last measurement.
            eye = [-.16 * math.sin(elapsed * .65), .03 + .065 * math.sin(elapsed * .47),
                   .635 - .10 * math.sin(elapsed * .31)]
            sample = dict(stream_id=stream, frame_id=str(frame), captured_at_us=str(captured),
                          processed_at_us=str(qpc_us()), tracking_state="tracking",
                          validity=dict(head_pose=False, eye_position=True),
                          head_pose=None, eye_position=eye,
                          quality=dict(flags=["intrinsics_prior"], reprojection_error_px=None),
                          extensions={"binocular_center_v1": {
                              "center_uv": [.36*math.sin(elapsed*.65), .16*math.sin(elapsed*.47)],
                              "ipd_px": 100+18*math.sin(elapsed*.31)}})
            if self.scenario == "faults":
                if 4 <= elapsed < 5:
                    sample.update(tracking_state="lost", eye_position=None,
                                  validity=dict(head_pose=False, eye_position=False))
                    sample["extensions"]["binocular_center_v1"] = None
                elif frame % 97 == 0:
                    sample["captured_at_us"] = str(captured - 500_000)
                elif frame % 89 == 0:
                    sample["captured_at_us"] = str(captured + 500_000)
                    sample["processed_at_us"] = str(captured + 500_000)
                elif frame % 83 == 0:
                    sample["frame_id"] = str(max(1, frame - 5))
        self.callback(sample)
        if self.preview is not None and captured - self.preview_at >= 125000:
            self.preview_at = captured
            preview = encode_preview(image, [])
            preview.update(stream_id=stream, frame_id=str(frame), captured_at_us=str(captured),
                           inference_ms=0, ipd_px=None)
            self.preview(preview)

    def close(self):
        pass


def load_recording(path):
    value = json.loads(Path(path).read_text(encoding="utf-8"))
    encode_message(value)  # Bounded, finite JSON; this developer input is never a camera recording.
    if not value.get("samples") or len(value["samples"]) > 2000:
        raise ValueError("Recording needs 1..2000 rows")
    previous = -1
    for row in value["samples"]:
        offset = row["offset_us"]
        if type(offset) is not int or offset < 0 or offset <= previous:
            raise ValueError("Recording offsets must increase")
        previous = offset
    return value


async def run_simulation(config, recording=None, scenario="motion"):
    record = load_recording(recording) if recording else None

    def describe(stream, mode, intent, instance):
        descriptor = stream_descriptor(stream, mode, intent, instance)
        if record:
            descriptor.update(record["descriptor"])
            descriptor.update(stream_id=stream, tracker_instance_id=instance,
                              camera_id="simulation", intent_revision=intent["intent_revision"])
        else:
            descriptor["estimator_id"] = "synthetic-eye-v1"
        return descriptor

    worker = TrackerWorker(SimulatedCamera(record["descriptor"].get("actual_mode") if record else None),
                           lambda callback: MeasurementReplay(callback, record, scenario),
                           monitor_factory=None, descriptor_factory=describe)
    await worker.run(config)
