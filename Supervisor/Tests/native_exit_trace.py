"""Best-effort Host phase snapshots, never process-exit acceptance evidence."""
import time
from host_exit_trace import read_host_exit_trace


def capture_exit_trace(output, host_pid, host_creation, stage):
    snapshot = {'stage': stage, 'expected_host_pid': host_pid,
                'expected_host_creation_filetime': host_creation,
                'trace': {'status': 'identity-unavailable', 'kernel_exit_proven': False, 'records': []}}
    try:
        snapshot['runner_monotonic_ms'] = time.monotonic() * 1000
        if type(host_pid) is int and host_pid > 0 and type(host_creation) is int and host_creation > 0:
            snapshot['trace'] = read_host_exit_trace(output/'state/exit.trace', host_pid, host_creation)
    except Exception as error:
        # Diagnostics must not replace an acceptance or cleanup exception.
        snapshot['trace'] = {'status': 'reader-failed', 'kernel_exit_proven': False,
                             'records': [], 'error': repr(error)}
    return snapshot
