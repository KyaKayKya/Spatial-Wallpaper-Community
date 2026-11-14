from spatial_tracker import activity


def test_activity_ignores_own_process_but_detects_external_stream():
    assert activity.classify_processes([(12, True)], own_pid=12) == "inactive"
    assert activity.classify_processes([(12, True), (13, True)], own_pid=12) == "active"
    assert activity.classify_processes([(13, False)], own_pid=12) == "inactive"
    assert activity.classify_processes([(0, True)], own_pid=12) == "unknown"
