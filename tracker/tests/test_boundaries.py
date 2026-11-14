import numpy as np
import pytest

from spatial_tracker import cli, estimator, session
from test_session import setup
from test_model import valid_intent


def test_cli_invalid_bootstrap_does_not_echo_secret(capsys, monkeypatch):
    import io

    monkeypatch.setattr("sys.stdin", io.StringIO('{"token":"sensitive-example"}\n'))
    assert cli.main(["run"]) == 2
    assert "sensitive-example" not in capsys.readouterr().err


@pytest.mark.asyncio
async def test_mode_change_rejected_without_disturbing_running_capture():
    worker, camera, _ = setup()
    await worker.apply_intent(valid_intent())
    with pytest.raises(session.ValidationError) as failure:
        await worker.apply_intent(
            valid_intent()
            | {
                "intent_revision": "2",
                "capture_preference": {"mode_id": "unsupported", "max_inference_hz": 20},
            }
        )
    assert failure.value.code == "UNSUPPORTED_CAPABILITY"
    assert worker.intent["intent_revision"] == "1"
    assert camera.is_open
    await worker.close()


@pytest.mark.asyncio
async def test_camera_switch_does_not_keep_previous_camera_activity():
    worker, camera, _ = setup()
    await worker.apply_intent(valid_intent())
    await worker.external_activity("camera-A", "active")
    await worker.apply_intent(valid_intent() | {"intent_revision": "2", "camera_id": "camera-B"})
    assert worker.state["external_usage"] == "unknown"
    assert camera.opened == ["camera-A", "camera-B"]
    await worker.close()


def test_pnp_rotation_uses_upright_head_and_camera_bases():
    points = np.array(
        [
            [0, 0, 0],
            [-0.03, -0.03, 0.03],
            [0.03, -0.03, 0.03],
            [-0.025, 0.025, 0.02],
            [0.025, 0.025, 0.02],
            [0, 0.07, 0.04],
            [-0.06, 0, 0.06],
            [0.06, 0, 0.06],
        ],
        dtype=np.float64,
    )
    angle = np.pi / 6
    c, s = np.cos(angle), np.sin(angle)
    # Positive rotation about x in CV's y-down frame becomes negative about x in C'.
    rotation = np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
    transformed = points @ rotation.T + [0, 0, 0.6]
    pixels = transformed[:, :2] / transformed[:, 2:] * 640 + [320, 240]
    pose, _, rms = estimator.solve_pose(points, pixels, 640, 480, [0, -0.03, 0.035])
    assert np.allclose(
        pose["orientation_xyzw"], [-np.sin(angle / 2), 0, 0, np.cos(angle / 2)], atol=1e-5
    )
    assert rms < 0.001
