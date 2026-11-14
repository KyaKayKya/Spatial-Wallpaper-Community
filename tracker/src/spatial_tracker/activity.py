"""Thin COM adapter for MF sensor activity reports; no extra process or polling."""

import ctypes as ct
import os
import sys
import uuid

from .camera import camera_id

_roots = set()
CALL = getattr(ct, "WINFUNCTYPE", ct.CFUNCTYPE)
HRESULT, ULONG, PTR = ct.c_int32, ct.c_uint32, ct.c_void_p


def classify_processes(processes, own_pid):
    if any(pid != own_pid and pid != 0 and streaming for pid, streaming in processes):
        return "active"
    if any(pid == 0 and streaming for pid, streaming in processes):
        return "unknown"
    return "inactive"


def _call(pointer, index, *args, argtypes=()):
    table = ct.cast(pointer, ct.POINTER(ct.POINTER(PTR))).contents
    fn = CALL(HRESULT, PTR, *argtypes)(table[index])
    result = fn(pointer, *args)
    if result < 0:
        raise OSError(f"Media Foundation call failed: 0x{result & 0xFFFFFFFF:08x}")
    return result


def _release(pointer):
    if pointer:
        _call(pointer, 2)


class SensorActivityMonitor:
    """callback(camera_id, active/inactive/unknown) may run on an MF thread."""

    def __init__(self, callback):
        self.callback = callback
        self._monitor = PTR()
        self._refs = 1
        self._closed = False
        self._started = False
        self._mf_started = False
        self._com_started = False

    def start(self):
        if self._started:
            return
        if sys.platform != "win32":
            raise RuntimeError("Sensor activity monitor requires Windows")
        self._closed = False
        self._mf = ct.WinDLL("mfsensorgroup")
        self._platform = ct.WinDLL("mfplat")
        self._ole = ct.WinDLL("ole32")
        result = self._ole.CoInitializeEx(None, 0)
        if result < 0 and result & 0xFFFFFFFF != 0x80010106:
            raise OSError("COM initialization failed")
        self._com_started = result in (0, 1)
        result = self._platform.MFStartup(0x20070, 1)
        if result < 0:
            self.close()
            raise OSError("MFStartup failed")
        self._mf_started = True
        callback_iid = uuid.UUID("de5072ee-dbe3-46dc-8a87-b6f631194751").bytes_le
        unknown_iid = uuid.UUID("00000000-0000-0000-c000-000000000046").bytes_le

        @CALL(HRESULT, PTR, PTR, ct.POINTER(PTR))
        def query(_this, iid, out):
            if ct.string_at(iid, 16) not in (callback_iid, unknown_iid):
                out[0] = None
                return -2147467262
            out[0] = ct.addressof(self._interface)
            self._refs += 1
            return 0

        @CALL(ULONG, PTR)
        def addref(_this):
            self._refs += 1
            return self._refs

        @CALL(ULONG, PTR)
        def release(_this):
            self._refs -= 1
            if self._closed and self._refs <= 1:
                _roots.discard(self)
            return self._refs

        @CALL(HRESULT, PTR, PTR)
        def report(_this, reports):
            if self._closed:
                return 0
            try:
                count = ULONG()
                _call(reports, 3, ct.byref(count), argtypes=(ct.POINTER(ULONG),))
                for index in range(count.value):
                    item = PTR()
                    _call(reports, 4, index, ct.byref(item), argtypes=(ULONG, ct.POINTER(PTR)))
                    try:
                        path = ct.create_unicode_buffer(32768)
                        written, process_count = ULONG(), ULONG()
                        _call(
                            item,
                            4,
                            path,
                            len(path),
                            ct.byref(written),
                            argtypes=(ct.c_wchar_p, ULONG, ct.POINTER(ULONG)),
                        )
                        _call(item, 5, ct.byref(process_count), argtypes=(ct.POINTER(ULONG),))
                        processes = []
                        for process_index in range(process_count.value):
                            process = PTR()
                            _call(
                                item,
                                6,
                                process_index,
                                ct.byref(process),
                                argtypes=(ULONG, ct.POINTER(PTR)),
                            )
                            try:
                                pid, streaming = ULONG(), ct.c_int32()
                                _call(process, 3, ct.byref(pid), argtypes=(ct.POINTER(ULONG),))
                                _call(
                                    process,
                                    4,
                                    ct.byref(streaming),
                                    argtypes=(ct.POINTER(ct.c_int32),),
                                )
                                processes.append((pid.value, bool(streaming.value)))
                            finally:
                                _release(process)
                        self.callback(
                            camera_id(path.value), classify_processes(processes, os.getpid())
                        )
                    finally:
                        _release(item)
            except Exception:
                self.callback(None, "unknown")
            return 0

        self._functions = query, addref, release, report
        self._vtable = (PTR * 4)(*(ct.cast(fn, PTR).value for fn in self._functions))
        self._interface = (PTR * 1)(ct.addressof(self._vtable))
        _roots.add(self)
        try:
            create = self._mf.MFCreateSensorActivityMonitor
            create.argtypes, create.restype = [PTR, ct.POINTER(PTR)], HRESULT
            result = create(ct.addressof(self._interface), ct.byref(self._monitor))
            if result < 0:
                raise OSError(f"Sensor monitor unavailable: 0x{result & 0xFFFFFFFF:08x}")
            _call(self._monitor, 3)
            self._started = True
        except Exception:
            self.close()
            raise

    def close(self):
        if self._closed:
            return
        self._closed = True
        try:
            if self._monitor:
                try:
                    if self._started:
                        _call(self._monitor, 4)
                finally:
                    _release(self._monitor)
                    self._monitor = PTR()
        finally:
            self._started = False
            if self._mf_started:
                self._platform.MFShutdown()
                self._mf_started = False
            if self._com_started:
                self._ole.CoUninitialize()
                self._com_started = False
            if self._refs <= 1:
                _roots.discard(self)
