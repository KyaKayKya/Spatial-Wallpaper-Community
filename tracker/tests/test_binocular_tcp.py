import numpy as np
import pytest
from spatial_tracker.clock import qpc_us
from spatial_tracker.estimator import BINOCULAR_FEATURE
from test_model import valid_intent
from test_tcp_boundaries import connected
from test_worker import Estimator


class Inference(Estimator):
    def submit(self, stream, frame, stamp, rgb):
        self.callback(dict(
            stream_id=stream, frame_id=str(frame), captured_at_us=str(stamp),
            processed_at_us=str(qpc_us()), tracking_state="tracking",
            head_pose=None, eye_position=None,
            validity=dict(head_pose=False, eye_position=False),
            quality=dict(flags=[], reprojection_error_px=None),
            extensions={BINOCULAR_FEATURE: dict(center_uv=[.1,-.2],ipd_px=84)},
        ))


@pytest.mark.asyncio
@pytest.mark.parametrize("features", [None, [], [BINOCULAR_FEATURE]])
async def test_same_frame_extension_is_only_sent_after_negotiation(features):
    async with connected(Inference, features) as (peer,camera,instance,task):
        request = await peer.send("SetTrackerIntent", valid_intent())
        await peer.result(request)
        descriptor = instance.snapshot(["tracking"])["stream_descriptor"]
        assert descriptor["optional_features"] == (features or [])
        camera.on_frame(np.zeros((480,640,3),dtype=np.uint8),qpc_us())
        messages = await peer.until(lambda m:m["type"]=="TrackingSample")
        value = messages[-1]["payload"]
        if features:
            assert value["extensions"][BINOCULAR_FEATURE]["ipd_px"] == 84
            assert value["extensions"][BINOCULAR_FEATURE]["center_uv"] == [.1,-.2]
            assert value["frame_id"] == "1"
        else:
            assert value["extensions"] == {}
