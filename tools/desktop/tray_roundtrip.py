"""Exercise our native tray callback and close only the manager it launches."""
import ctypes as c
from ctypes import wintypes as w
import time,psutil
u=c.WinDLL('user32',use_last_error=True)
visit=c.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)
u.EnumWindows.argtypes=[visit,w.LPARAM]
u.GetWindowThreadProcessId.argtypes=[w.HWND,c.POINTER(w.DWORD)]
u.GetClassNameW.argtypes=[w.HWND,w.LPWSTR,c.c_int]
u.IsWindowVisible.argtypes=[w.HWND]
u.PostMessageW.argtypes=[w.HWND,w.UINT,w.WPARAM,w.LPARAM]
def windows(pid,prefix):
    result=[]
    def inspect(hwnd,unused):
        owner=w.DWORD();u.GetWindowThreadProcessId(hwnd,c.byref(owner))
        if owner.value==pid:
            cls=c.create_unicode_buffer(256);u.GetClassNameW(hwnd,cls,256)
            if cls.value.startswith(prefix):result.append(hwnd)
        return True
    u.EnumWindows(visit(inspect),0)
    return result
def check(runtime_pid):
    shell=windows(runtime_pid,'SpatialWallpaper.Desktop.')
    assert len(shell)==1,'Missing own native tray window'
    # WM_LBUTTONDBLCLK through the same tray callback used by Shell_NotifyIcon.
    assert u.PostMessageW(shell[0],0x8001,0,0x0203)
    manager=None
    try:
        deadline=time.monotonic()+20
        while time.monotonic()<deadline:
            candidates=[p for p in psutil.Process(runtime_pid).children(recursive=True) if p.name().lower()=='spatialwallpaper.exe']
            if candidates:
                manager=candidates[0]
                render=windows(manager.pid,'UnityWndClass')
                if any(u.IsWindowVisible(hwnd) for hwnd in render):break
            time.sleep(.1)
        else:raise AssertionError('Tray-open did not produce a visible manager window')
        for hwnd in windows(manager.pid,'UnityWndClass'):u.PostMessageW(hwnd,0x0010,0,0)
        manager.wait(timeout=15)
    finally:
        if manager and manager.is_running():
            try:manager.terminate();manager.wait(timeout=15)
            except psutil.NoSuchProcess:pass
