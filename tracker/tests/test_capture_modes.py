from types import SimpleNamespace as Obj

import pytest

from spatial_tracker.camera import describe_format, select_format, CaptureError
from spatial_tracker.session import Session
from test_model import valid_intent
from test_session import Camera


def fmt(w, h, n, d, subtype='MJPG'):
    return Obj(video_format=Obj(width=w, height=h), frame_rate=Obj(numerator=n, denominator=d),
               subtype=subtype)


def test_modes_preserve_native_geometry_rate_and_exact_selection():
    source = Obj(info=Obj(id='color-A'), supported_formats=[fmt(640,480,30,1),fmt(1920,1080,30000,1001)])
    high = describe_format(source, source.supported_formats[1])
    assert high['width_px'] == 1920 and high['height_px'] == 1080
    assert high['fps_numerator'] == 30000 and high['fps_denominator'] == 1001
    assert high['fps'] == pytest.approx(29.97003)
    chosen, native = select_format([source],high['mode_id'])
    assert chosen is source and native is source.supported_formats[1]
    with pytest.raises(CaptureError, match='unsupported'):
        select_format([source],'removed-mode')


@pytest.mark.asyncio
async def test_mode_change_reopens_same_device_and_retains_external_yield():
    class ModesCamera(Camera):
        supports_modes = True

        async def open(self, device, frame, error, mode_id=None):
            result = await super().open(device,frame,error)
            result['mode_id'] = mode_id
            return result

    camera=ModesCamera()
    streams=[]
    s=Session(camera,lambda _: None,lambda *x:streams.append(x),lambda *x:None)
    try:
        await s.apply_intent(valid_intent())
        await s.apply_intent(valid_intent() | {'intent_revision':'2', 'capture_preference':{'mode_id':'HD-30','max_inference_hz':20}})
        assert camera.opened == ['camera-A','camera-A']
        assert streams[-1][1]['mode_id'] == 'HD-30'
        await s.external_activity('camera-A','active')
        assert not camera.is_open and s.state['camera_state'] == 'yielded'
    finally:
        await s.close()
