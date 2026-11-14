"""Trusted runner observations of its retained Client process, not a Worker."""
import math
import time


class ForcedClientTiming:
    def __init__(self, clock=time.monotonic):
        self.clock = clock
        self.data = {'timing_clock': 'runner-monotonic-ms',
                     'forced_started_at_ms': None, 'client_kernel_exited_at_ms': None,
                     'cleanup_completed_at_ms': None, 'forced_to_kernel_exit_ms': None,
                     'kernel_exit_to_cleanup_complete_ms': None, 'cleanup_completed': False}

    def observe(self, previous=None):
        value = self.clock() * 1000
        if not math.isfinite(value) or value < 0 or previous is not None and value < previous:
            raise ValueError('invalid-runner-monotonic-observation')
        return value

    def begin_force(self):
        if self.data['forced_started_at_ms'] is None:
            self.data['forced_started_at_ms'] = self.observe()

    def client_exited(self):
        began = self.data['forced_started_at_ms']
        if began is None:
            raise ValueError('client-exit-without-force-observation')
        if self.data['client_kernel_exited_at_ms'] is None:
            exited = self.observe(began)
            self.data.update(client_kernel_exited_at_ms=exited, forced_to_kernel_exit_ms=exited-began)

    def cleanup_finished(self):
        exited = self.data['client_kernel_exited_at_ms']
        if exited is None:
            raise ValueError('cleanup-without-client-kernel-exit')
        if self.data['cleanup_completed_at_ms'] is None:
            completed = self.observe(exited)
            self.data.update(cleanup_completed_at_ms=completed,
                             kernel_exit_to_cleanup_complete_ms=completed-exited,
                             cleanup_completed=True)
