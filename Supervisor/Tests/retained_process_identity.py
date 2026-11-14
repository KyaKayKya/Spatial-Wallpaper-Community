"""Read identity from the caller's retained original process handle, never PID lookup."""
import ctypes


def creation_filetime(handle):
    if type(handle) is not int or handle <= 0:
        raise ValueError('invalid retained process handle')
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    query = kernel.GetProcessTimes
    query.argtypes = [ctypes.c_void_p] + [ctypes.POINTER(ctypes.c_uint64)] * 4
    query.restype = ctypes.c_int
    values = [ctypes.c_uint64() for _ in range(4)]
    if not query(handle, *(ctypes.byref(value) for value in values)):
        raise ctypes.WinError(ctypes.get_last_error())
    if not 0 < values[0].value <= 0x7fffffffffffffff:
        raise ValueError('invalid process creation FILETIME')
    return values[0].value
