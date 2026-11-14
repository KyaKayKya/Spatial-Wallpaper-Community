"""Observe DPI in a fresh, windowless process without changing the parent awareness."""
import ctypes
import json
from sdk_environment import _dpi_current_process, unmeasured


def main():
    try:
        library = ctypes.WinDLL('shcore', use_last_error=True)
        library.SetProcessDpiAwareness.argtypes = [ctypes.c_int]
        library.SetProcessDpiAwareness.restype = ctypes.c_long
        result = library.SetProcessDpiAwareness(2)
        if result != 0:
            raise OSError('SetProcessDpiAwareness failed: ' + str(result))
        value = _dpi_current_process()
        if value.get('status') == 'measured':
            value['source'] = 'isolated windowless process; ' + value['source']
    except Exception as error:
        value = unmeasured('isolated awareness initialization failed: ' + repr(error))
    print(json.dumps(value))


if __name__ == '__main__':
    main()
