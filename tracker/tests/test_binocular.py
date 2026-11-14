"""Reference input semantics, independent of PnP or camera hardware."""
from types import SimpleNamespace

import pytest
from spatial_tracker import estimator


def face(left=(.25, .4), right=(.75, .6)):
    points = [SimpleNamespace(x=0, y=0) for _ in range(478)]
    points[468] = SimpleNamespace(x=left[0], y=left[1])
    points[473] = SimpleNamespace(x=right[0], y=right[1])
    return points


def test_reference_binocular_input_exists_and_uses_pixel_distance():
    assert hasattr(estimator, "binocular_observation"), "Missing reference binocular input"
    value = estimator.binocular_observation(face(), 640, 480)
    assert value["center_uv"] == pytest.approx([0, 0])
    assert value["ipd_px"] == pytest.approx((320**2 + 96**2)**.5)


def test_normalization_is_not_mirrored_and_never_reuses_pnp_eye():
    assert hasattr(estimator, "binocular_observation")
    value = estimator.binocular_observation(face((.1, .2), (.3, .2)), 1000, 500)
    assert value["center_uv"] == pytest.approx([-.6, -.6])
    assert value["ipd_px"] == pytest.approx(200)


@pytest.mark.parametrize("points,width,height", [
    ([], 640, 480), (face((.5,.5),(.5,.5)),640,480),
    (face((float('nan'),.5),(.4,.5)),640,480),
    (face(),0,480), (face(),640,-1),
])
def test_invalid_observations_are_explicitly_unavailable(points,width,height):
    assert hasattr(estimator, "binocular_observation")
    assert estimator.binocular_observation(points,width,height) is None
