"""Read-only window hierarchy evidence for desktop-host compatibility."""
import ctypes as c
from ctypes import wintypes as w
import json
from pathlib import Path

u = c.WinDLL('user32', use_last_error=True)
callback = c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
u.EnumWindows.argtypes = [callback, w.LPARAM]
u.EnumChildWindows.argtypes = [w.HWND, callback, w.LPARAM]
u.GetClassNameW.argtypes = [w.HWND, w.LPWSTR, c.c_int]
u.GetWindowTextW.argtypes = [w.HWND, w.LPWSTR, c.c_int]
u.GetParent.argtypes = [w.HWND]
u.GetParent.restype = w.HWND
windows = []
def read(hwnd, unused):
    cls, title = c.create_unicode_buffer(256), c.create_unicode_buffer(256)
    u.GetClassNameW(hwnd, cls, 256)
    if cls.value not in ('Progman', 'WorkerW', 'SHELLDLL_DefView', 'SysListView32'):
        return True
    u.GetWindowTextW(hwnd, title, 256)
    windows.append(dict(hwnd=hwnd, parent=u.GetParent(hwnd), cls=cls.value, title=title.value, visible=bool(u.IsWindowVisible(hwnd))))
    return True
def top(hwnd, unused):
    read(hwnd, unused)
    u.EnumChildWindows(hwnd, callback(read), 0)
    return True
u.EnumWindows(callback(top), 0)
out = Path(__file__).resolve().parents[2] / 'Validation/Desktop/shell.json'
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text(json.dumps(windows, indent=2), encoding='utf-8')
print(out.read_text())
