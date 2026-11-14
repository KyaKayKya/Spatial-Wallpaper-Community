"""Release only input owed by an exited, identity-pinned test injector."""
import ctypes
import time
from ctypes import wintypes


def owed_keys(record, pid, creation):
    if record.get('pid') != pid or record.get('creation_filetime') != creation:
        raise ValueError('input-injector-identity-mismatch')
    buttons, keys = record['buttons'], record['keys']
    if type(buttons) is not int or buttons & ~20 or not isinstance(keys, list):
        raise ValueError('invalid-owned-input-ledger')
    if any(type(key) is not int or key not in [16, 27, 32, 37, 38, 39, 40] for key in keys) or len(set(keys)) != len(keys):
        raise ValueError('invalid-owned-key-ledger')
    injected = record.get('injected', [])
    if not isinstance(injected, list) or any(type(key) is not int or key not in [1, 2, 16, 27, 32, 37, 38, 39, 40] for key in injected) or len(set(injected)) != len(injected):
        raise ValueError('invalid-injected-input-ledger')
    return sorted(set(([1] if buttons & 4 else []) + ([2] if buttons & 16 else []) + keys + injected))


class Mouse(ctypes.Structure):
    _fields_ = [('x', wintypes.LONG), ('y', wintypes.LONG), ('data', wintypes.DWORD),
                ('flags', wintypes.DWORD), ('time', wintypes.DWORD), ('extra', ctypes.c_size_t)]


class Key(ctypes.Structure):
    _fields_ = [('code', wintypes.WORD), ('scan', wintypes.WORD), ('flags', wintypes.DWORD),
                ('time', wintypes.DWORD), ('extra', ctypes.c_size_t)]


class Payload(ctypes.Union):
    _fields_ = [('mouse', Mouse), ('key', Key)]


class Input(ctypes.Structure):
    _fields_ = [('type', wintypes.DWORD), ('data', Payload)]


def wait_for_clear(any_held):
    deadline, clear_since = time.monotonic() + .5, None
    while time.monotonic() < deadline:
        if any_held():
            clear_since = None
        elif clear_since is None:
            clear_since = time.monotonic()
        elif time.monotonic() - clear_since >= .05:
            return True
        time.sleep(.005)
    return False


def release_owed(record, pid, creation):
    keys = owed_keys(record, pid, creation)
    user = ctypes.windll.user32
    user.SendInput.argtypes = [wintypes.UINT, ctypes.POINTER(Input), ctypes.c_int]
    user.SendInput.restype = wintypes.UINT
    released = []
    for code in keys:
        event = Input(type=0 if code in [1, 2] else 1)
        if code in [1, 2]:
            event.data.mouse.flags = 4 if code == 1 else 16
        else:
            event.data.key.code, event.data.key.flags = code, 2
        if user.SendInput(1, ctypes.byref(event), ctypes.sizeof(event)) != 1:
            raise RuntimeError('owned-input-release-failed:' + str(code))
        released.append(code)
    stable_clear = wait_for_clear(lambda: any(user.GetAsyncKeyState(code) & 0x8000 for code in keys))
    return {'owed': keys, 'released': released, 'stable_clear': stable_clear,
            'remaining_held': [code for code in keys if user.GetAsyncKeyState(code) & 0x8000]}
