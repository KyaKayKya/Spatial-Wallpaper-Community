"""Wait for a build's complete owned tree within one total build deadline."""
from pathlib import Path
import subprocess
import time
from case_process import CaseProcess


def run_toolchain(command, cwd, stdout, timeout, evidence, env=None):
    started = time.monotonic()
    deadline = started + timeout
    evidence.update(natural_tree_exit=False, timeout_seconds=timeout)
    owner = CaseProcess()
    evidence['process_tree'] = owner.evidence
    def remaining():
        value = deadline - time.monotonic()
        if value <= 0:
            raise subprocess.TimeoutExpired(command, timeout)
        return value
    try:
        owner.start(Path(__file__).with_name('toolchain_command.py'),
                    [str(value) for value in command], cwd, stdout, env=env)
        remaining()
        code = owner.wait(remaining())
        evidence['exit_code'] = code
        evidence['root_exit_seconds'] = time.monotonic() - started
        evidence['members_after_root_exit'] = owner.job.members()
        if code:
            raise subprocess.CalledProcessError(code, command)
        # The command launcher can exit before UPM/conhost. They still belong
        # to this build; success cannot escape while they remain alive.
        while True:
            left = remaining()
            if owner.job.active() == 0:
                break
            time.sleep(min(.01, left))
        evidence['tree_exit_seconds'] = time.monotonic() - started
        evidence['natural_tree_exit'] = True
    except BaseException as error:
        evidence['error'] = repr(error)
        raise
    finally:
        # Forced cleanup never sets natural_tree_exit. Cleanup failure overrides
        # success and propagates to the outer acceptance scheduler.
        try:
            owner.close()
        except BaseException as error:
            evidence['cleanup_error'] = repr(error)
            raise
        finally:
            evidence['total_seconds'] = time.monotonic() - started
