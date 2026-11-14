import base64
from types import SimpleNamespace

import cv2
import numpy as np
import pytest

from spatial_tracker.preview import encode_preview
from spatial_tracker.protocol import Outbox


def test_preview_is_bounded_mirrored_and_does_not_mutate_capture():
    rgb = np.zeros((480, 640, 3), dtype=np.uint8)
    rgb[:, :320, 0] = 255
    original = rgb.copy()
    face = [SimpleNamespace(x=.25, y=.5)] * 478
    p = encode_preview(rgb, face)
    image = cv2.imdecode(np.frombuffer(base64.b64decode(p['jpeg']), np.uint8), cv2.IMREAD_COLOR)
    assert image.shape == (480, 640, 3)
    assert image[10, 600, 2] > 200 and image[10, 10, 2] < 10
    assert np.array_equal(rgb, original)
    assert len(p['jpeg']) <= 53336 and p['face_found']
    assert not encode_preview(rgb, [])['face_found']


@pytest.mark.asyncio
async def test_preview_has_replaceable_slot_and_lower_priority_than_tracking():
    q = Outbox()
    q.put_preview({'frame': 1})
    q.put_preview({'frame': 2})
    q.put_sample({'sample': 1})
    assert await q.get() == {'sample': 1}
    assert await q.get() == {'frame': 2}
    q.put_preview({'frame': 3})
    q.clear_preview()
    assert q._preview is None


def test_hd_capture_is_only_downscaled_in_diagnostic_copy_and_jpeg_is_bounded():
    rgb = np.zeros((1080,1920,3),dtype=np.uint8)
    p = encode_preview(rgb,[])
    assert (p['width'],p['height']) == (640,360)
    assert rgb.shape == (1080,1920,3)
    noisy = np.random.default_rng(7).integers(0,256,(480,640,3),dtype=np.uint8)
    assert len(base64.b64decode(encode_preview(noisy,[])['jpeg'])) <= 40000
