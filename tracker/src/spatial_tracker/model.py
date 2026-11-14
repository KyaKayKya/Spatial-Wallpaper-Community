"""Validation at the Runtime/Tracker boundary."""

import copy
import math
import re


class ValidationError(ValueError):
    def __init__(self, message, code="INVALID_ARGUMENT"):
        super().__init__(message)
        self.code = code


def decimal(value):
    if not isinstance(value, str) or not re.fullmatch(r"0|[1-9][0-9]{0,19}", value):
        raise ValidationError("Expected unsigned decimal string")
    number = int(value)
    if number > 2**64 - 1:
        raise ValidationError("Integer exceeds uint64")
    return number


def validate_intent(value):
    if not isinstance(value, dict):
        raise ValidationError("Intent must be an object")
    keys = {
        "intent_revision",
        "camera_id",
        "capture_requested",
        "auto_resume_allowed",
        "capture_preference",
        "required_features",
        "retry_generation",
    }
    if set(value) != keys:
        raise ValidationError("Intent fields do not match schema")
    decimal(value["intent_revision"])
    decimal(value["retry_generation"])
    for name in ("capture_requested", "auto_resume_allowed"):
        if type(value[name]) is not bool:
            raise ValidationError("Intent flags must be boolean")
    camera_id = value["camera_id"]
    if camera_id is not None and (not isinstance(camera_id, str) or not camera_id):
        raise ValidationError("Invalid camera ID")
    if value["capture_requested"] and camera_id is None:
        raise ValidationError("Capture requires an explicit camera")
    pref = value["capture_preference"]
    if not isinstance(pref, dict) or set(pref) != {"mode_id", "max_inference_hz"}:
        raise ValidationError("Invalid capture preference")
    if pref["mode_id"] is not None and not isinstance(pref["mode_id"], str):
        raise ValidationError("Invalid mode ID")
    rate = pref["max_inference_hz"]
    if type(rate) not in (int, float) or not math.isfinite(rate) or not 1 <= rate <= 60:
        raise ValidationError("Inference frequency must be between 1 and 60 Hz")
    features = value["required_features"]
    if not isinstance(features, list) or any(x not in ("head_pose", "eye_pose") for x in features):
        raise ValidationError("Unsupported required feature", "UNSUPPORTED_CAPABILITY")
    return copy.deepcopy(value)


def error(code, message, retryable=False):
    return {
        "code": code,
        "message": message,
        "retryable": retryable,
        "retry_after_ms": None,
        "details": {},
    }
