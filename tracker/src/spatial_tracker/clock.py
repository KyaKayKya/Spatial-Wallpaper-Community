"""Native same-boot clock, shared with C#: never subtract a process epoch."""

import ctypes
import sys
import time

if sys.platform == "win32":
    _kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    _frequency = ctypes.c_int64()
    _kernel.QueryPerformanceFrequency(ctypes.byref(_frequency))

elif sys.platform == "darwin":
    class _Timebase(ctypes.Structure):
        _fields_ = [("numer", ctypes.c_uint32), ("denom", ctypes.c_uint32)]
    _system = ctypes.CDLL("/usr/lib/libSystem.B.dylib")
    _system.mach_absolute_time.restype = ctypes.c_uint64
    _base = _Timebase()
    if _system.mach_timebase_info(ctypes.byref(_base)) or not _base.denom:
        raise RuntimeError("Mach clock unavailable")

CLOCK_DOMAIN = ("macos_mach_absolute_us_v1" if sys.platform == "darwin"
                else "windows_qpc_us_v1" if sys.platform == "win32"
                else "unsupported_monotonic_us_test_only")

def qpc_us():
    if sys.platform == "darwin":
        return _system.mach_absolute_time() * _base.numer // (_base.denom * 1000)
    if sys.platform != "win32":
        return time.monotonic_ns() // 1000
    counter = ctypes.c_int64()
    if not _kernel.QueryPerformanceCounter(ctypes.byref(counter)):
        raise ctypes.WinError(ctypes.get_last_error())
    return counter.value * 1_000_000 // _frequency.value
