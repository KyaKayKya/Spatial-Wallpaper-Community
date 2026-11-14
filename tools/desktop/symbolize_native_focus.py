"""Resolve owned native diagnostic offsets with local symbols only; never attach or download."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
import struct
from pathlib import Path


class Symbol(c.Structure):
    _fields_ = [('size', w.ULONG), ('type', w.ULONG), ('reserved', c.c_ulonglong * 2),
                ('index', w.ULONG), ('bytes', w.ULONG), ('module', c.c_ulonglong),
                ('flags', w.ULONG), ('value', c.c_ulonglong), ('address', c.c_ulonglong),
                ('register', w.ULONG), ('scope', w.ULONG), ('tag', w.ULONG),
                ('length', w.ULONG), ('capacity', w.ULONG), ('name', w.WCHAR * 1)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--image', type=Path, required=True)
    parser.add_argument('--symbols', type=Path, required=True)
    parser.add_argument('--offset', action='append', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dbghelp', type=Path)
    parser.add_argument('--dia', type=Path)
    parser.add_argument('--pdb', type=Path)
    args = parser.parse_args()
    image, symbols = args.image.resolve(), args.symbols.resolve()
    assert image.is_file() and symbols.is_dir()
    assert not str(symbols).startswith('\\\\')
    dia = c.WinDLL(str(args.dia.resolve()), winmode=0x1100) if args.dia else None
    dbg = c.WinDLL(str(args.dbghelp.resolve()) if args.dbghelp else 'dbghelp.dll', use_last_error=True, winmode=0x1100)
    kernel = c.WinDLL('kernel32.dll', use_last_error=True)
    kernel.GetCurrentProcess.restype = w.HANDLE
    process = kernel.GetCurrentProcess()
    dbg.SymSetOptions.argtypes = [w.DWORD]
    dbg.SymInitializeW.argtypes = [w.HANDLE, w.LPCWSTR, w.BOOL]
    dbg.SymLoadModuleExW.argtypes = [w.HANDLE, w.HANDLE, w.LPCWSTR, w.LPCWSTR, c.c_ulonglong, w.DWORD, c.c_void_p, w.DWORD]
    dbg.SymLoadModuleExW.restype = c.c_ulonglong
    dbg.SymFromAddrW.argtypes = [w.HANDLE, c.c_ulonglong, c.POINTER(c.c_ulonglong), c.POINTER(Symbol)]
    dbg.SymCleanup.argtypes = [w.HANDLE]
    dbg.SymSetOptions(0x1000 | 0x200 | 4 | 2)
    assert dbg.SymInitializeW(process, str(symbols), False), c.get_last_error()
    report = {'scope': __doc__, 'image': str(image), 'image_sha256': hashlib.sha256(image.read_bytes()).hexdigest(),
              'symbol_directory': str(symbols), 'offsets': [], 'network_symbol_lookup': False}
    if args.dbghelp:
        report['dbghelp'] = str(args.dbghelp.resolve())
        report['dbghelp_sha256'] = hashlib.sha256(args.dbghelp.read_bytes()).hexdigest()
    if dia:
        report['dia'] = str(args.dia.resolve())
        report['dia_sha256'] = hashlib.sha256(args.dia.read_bytes()).hexdigest()
    if args.pdb:
        report['pdb'] = str(args.pdb.resolve())
        report['pdb_sha256'] = hashlib.sha256(args.pdb.read_bytes()).hexdigest()
    try:
        data = image.read_bytes()
        image_size = struct.unpack_from('<I', data, struct.unpack_from('<I', data, 0x3c)[0] + 24 + 56)[0]
        base = dbg.SymLoadModuleExW(process, None, str(args.pdb.resolve() if args.pdb else image), None, 0x180000000, image_size, None, 0)
        assert base, c.get_last_error()
        for offset in args.offset:
            storage = c.create_string_buffer(c.sizeof(Symbol) + 2048)
            value = c.cast(storage, c.POINTER(Symbol))
            value.contents.size, value.contents.capacity = c.sizeof(Symbol), 1024
            displacement = c.c_ulonglong()
            resolved = bool(dbg.SymFromAddrW(process, base + int(offset, 16), c.byref(displacement), value))
            report['offsets'].append({'offset': offset, 'resolved': resolved,
                                      'symbol': c.wstring_at(c.addressof(storage) + Symbol.name.offset, value.contents.length) if resolved else None,
                                      'displacement': displacement.value, 'error': None if resolved else c.get_last_error()})
    finally:
        assert dbg.SymCleanup(process), c.get_last_error()
        args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    return 0 if all(item['resolved'] for item in report['offsets']) else 1


if __name__ == '__main__':
    raise SystemExit(main())
