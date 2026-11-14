import pytest

from spatial_tracker import model


def valid_intent():
    return {
        "intent_revision": "1",
        "camera_id": "camera-A",
        "capture_requested": True,
        "auto_resume_allowed": True,
        "capture_preference": {"mode_id": None, "max_inference_hz": 20},
        "required_features": ["head_pose", "eye_pose"],
        "retry_generation": "0",
    }


@pytest.mark.parametrize(
    "patch",
    [
        {"capture_requested": 1},
        {"intent_revision": "-1"},
        {"intent_revision": "01"},
        {"camera_id": None},
        {"required_features": ["identity"]},
        {"capture_preference": {"mode_id": None, "max_inference_hz": 0}},
    ],
)
def test_invalid_intent_is_rejected(patch):
    value = valid_intent() | patch
    with pytest.raises(model.ValidationError):
        model.validate_intent(value)


def test_intent_does_not_alias_callers_mutable_data():
    source = valid_intent()
    result = model.validate_intent(source)
    source["required_features"].append("identity")
    assert result["required_features"] == ["head_pose", "eye_pose"]


def test_stopped_intent_allows_no_camera():
    source = valid_intent() | {"capture_requested": False, "camera_id": None}
    assert model.validate_intent(source)["capture_requested"] is False
