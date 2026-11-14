"""Observe a Host-owned sampler only after retained-handle identity validation."""
import ctypes
from ctypes import wintypes as w
from process_observation import ObservedProcess, k

k.GetProcessId.argtypes = [w.HANDLE]
k.GetProcessId.restype = w.DWORD


def process_identity(handle):
    pid = k.GetProcessId(handle)
    if not pid:
        raise ctypes.WinError(ctypes.get_last_error())
    times = [ctypes.c_ulonglong() for _ in range(4)]
    if not k.GetProcessTimes(handle, *[ctypes.byref(value) for value in times]):
        raise ctypes.WinError(ctypes.get_last_error())
    if times[0].value == 0:
        raise ValueError('audio-process-creation-unavailable')
    return pid, times[0].value


def observe_audio_sampler(host, identity, allow_terminate=False, allow_suspend=False):
    keys = {'host_pid', 'host_creation_filetime', 'pid', 'creation_filetime'}
    if not isinstance(identity, dict) or set(identity) != keys or any(
            type(identity[key]) is not int or identity[key] <= 0 for key in keys):
        raise ValueError('audio-process-identity-required')
    if identity['pid'] == identity['host_pid'] or identity['creation_filetime'] < identity['host_creation_filetime']:
        raise ValueError('audio-process-identity-order')
    if (host.pid != identity['host_pid'] or host.poll() is not None or
            process_identity(int(host._handle)) != (identity['host_pid'], identity['host_creation_filetime'])):
        raise ValueError('audio-host-identity-mismatch')
    # Obtaining rights is not permission to act: no suspend/terminate operation
    # occurs until the actual retained handle matches the Host's creation tuple.
    sampler = ObservedProcess(identity['pid'], allow_terminate=allow_terminate, allow_suspend=allow_suspend)
    try:
        if process_identity(sampler.handle) != (identity['pid'], identity['creation_filetime']):
            raise ValueError('audio-sampler-identity-mismatch')
        if host.poll() is not None or sampler.exited():
            raise ValueError('audio-identity-owner-or-sampler-exited')
        return sampler
    except BaseException:
        sampler.close()
        raise
