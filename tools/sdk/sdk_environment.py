"""Read-only environment observations. Missing configuration is never inferred."""
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
import os
from pathlib import Path
import platform
import sys
import subprocess
import time


DIMENSIONS = tuple(json.loads((Path(__file__).with_name('release-policy.json')).read_text())['coverage']['identity_dimensions'])
_render_observation=None


def require_render_probe_binding(observed,current,observed_files,selected_files):
    for key in ['os','display_topology','dpi']:
        if observed[key].get('status')!='measured' or current[key].get('status')!='measured' or observed[key]['value']!=current[key]['value']:raise ValueError('render-probe-configuration-mismatch:'+key)
    if not observed_files or observed_files!=selected_files:raise ValueError('render-probe-Worker-inventory-mismatch')


def bind_render_probe(path,selected_worker=None):
    """Bind a revalidated real Worker observation for this acceptance process.

    This identifies the fixed rendering configuration, not visual fidelity,
    performance or platform certification. OS/display/DPI are still reread.
    """
    global _render_observation
    from release_identity import ROOT,identity as release_identity
    from verify_delivery import component
    from render_profiles import require_runtime_receipt
    path=Path(path).resolve()
    if not path.is_relative_to(ROOT/'Validation/Content/sdk-runtime/G/frame-timing') or path.name!='report.json':raise ValueError('render-probe-owned-report-required')
    report=json.loads(path.read_text())
    if report.get('status')!='passed-development-real-worker-diagnostics-performance-unmeasured' or report.get('sources_unchanged') is not True or report['result'].get('retained_worker_and_host_exit_zero') is not True:raise ValueError('render-probe-incomplete')
    worker=Path(report['worker_build']['path'])
    if digest(worker/'report.json')!=report['worker_build']['receipt_sha256']:raise ValueError('render-probe-Worker-receipt-drift')
    expected=release_identity();expected.pop('package_sha256');build=component(worker,'worker',expected)
    value=report['render_profile'];session={key:report['result']['identity'][key] for key in ['attempt','instance','generation']};session['build_id']=value['build_id']
    value=require_runtime_receipt('SDK_RENDER_PROFILE '+json.dumps(value),session,640,360)
    if value['profile']!=build['build']['render_profile']['identity']:raise ValueError('render-probe-build-profile-mismatch')
    before,after=report['configuration_before'],report['configuration_after']
    if before!=after or any(before[key].get('status')!='measured' for key in ['os','display_topology','dpi']):raise ValueError('render-probe-configuration-unmeasured-or-drift')
    selected=component(Path(selected_worker),'worker',expected) if selected_worker else build
    require_render_probe_binding(before,configuration_identity(),build['runtime_files'],selected['runtime_files'])
    _render_observation={'profile':value['profile'],'configuration':before,'source':'real Worker SDK_RENDER_PROFILE; '+str(path)+'; sha256='+digest(path)}
    return _render_observation


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def unmeasured(reason):
    return {'status':'unmeasured','reason':reason}


def measured(value, source):
    return {'status':'measured','source':source,'value':value}


def observe(operation, source):
    try:
        return measured(operation(), source)
    except Exception as error:
        return unmeasured(source+': '+repr(error))


def binary(path):
    path=Path(path).resolve()
    return {'path':str(path),'sha256':digest(path),'bytes':path.stat().st_size}


def cpu():
    value={'logical_processors':os.cpu_count(),'machine':platform.machine()}
    if os.name=='nt':
        import winreg
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,r'HARDWARE\DESCRIPTION\System\CentralProcessor\0') as key:
            for name in ('ProcessorNameString','Identifier','VendorIdentifier'):
                value[name]=winreg.QueryValueEx(key,name)[0].strip()
    else:
        value['processor']=platform.processor()
    return value


def memory():
    if os.name!='nt':
        raise RuntimeError('Windows GlobalMemoryStatusEx probe only')
    class Memory(c.Structure):
        _fields_=[('length',w.DWORD),('load',w.DWORD),('total',c.c_ulonglong),
                  ('available',c.c_ulonglong),('page_total',c.c_ulonglong),('page_available',c.c_ulonglong),
                  ('virtual_total',c.c_ulonglong),('virtual_available',c.c_ulonglong),('extended',c.c_ulonglong)]
    kernel=c.WinDLL('kernel32',use_last_error=True)
    kernel.GlobalMemoryStatusEx.argtypes=[c.POINTER(Memory)]
    kernel.GlobalMemoryStatusEx.restype=w.BOOL
    value=Memory();value.length=c.sizeof(value)
    if not kernel.GlobalMemoryStatusEx(c.byref(value)):
        raise c.WinError(c.get_last_error())
    return {'total_physical_bytes':value.total,'available_physical_bytes':value.available}


class DisplayDevice(c.Structure):
    _fields_=[('size',w.DWORD),('name',w.WCHAR*32),('description',w.WCHAR*128),
              ('flags',w.DWORD),('device_id',w.WCHAR*128),('device_key',w.WCHAR*128)]


class DisplayMode(c.Structure):
    # The display member of DEVMODEW's 16-byte printer/display union.
    _fields_=[('name',w.WCHAR*32),('spec',w.WORD),('driver',w.WORD),('size',w.WORD),
              ('extra',w.WORD),('fields',w.DWORD),('x',w.LONG),('y',w.LONG),
              ('orientation',w.DWORD),('fixed_output',w.DWORD),('color',w.SHORT),
              ('duplex',w.SHORT),('y_resolution',w.SHORT),('tt',w.SHORT),('collate',w.SHORT),
              ('form',w.WCHAR*32),('log_pixels',w.WORD),('bits',w.DWORD),
              ('width',w.DWORD),('height',w.DWORD),('flags',w.DWORD),('frequency',w.DWORD),
              ('icm_method',w.DWORD),('icm_intent',w.DWORD),('media',w.DWORD),('dither',w.DWORD),
              ('reserved1',w.DWORD),('reserved2',w.DWORD),('pan_width',w.DWORD),('pan_height',w.DWORD)]


def display_devices():
    if os.name!='nt':
        raise RuntimeError('Windows display enumeration probe only')
    user=c.WinDLL('user32',use_last_error=True)
    user.EnumDisplayDevicesW.argtypes=[w.LPCWSTR,w.DWORD,c.POINTER(DisplayDevice),w.DWORD]
    user.EnumDisplayDevicesW.restype=w.BOOL
    user.EnumDisplaySettingsW.argtypes=[w.LPCWSTR,w.DWORD,c.POINTER(DisplayMode)]
    user.EnumDisplaySettingsW.restype=w.BOOL
    rows=[]
    for index in range(64):
        device=DisplayDevice();device.size=c.sizeof(device)
        if not user.EnumDisplayDevicesW(None,index,c.byref(device),0):
            break
        row={'name':device.name,'description':device.description,'state_flags':device.flags,
             'device_id':device.device_id,'device_key':device.device_key}
        if device.flags & 1:
            mode=DisplayMode();mode.size=c.sizeof(mode)
            if not user.EnumDisplaySettingsW(device.name,0xffffffff,c.byref(mode)):
                row['mode']=unmeasured('EnumDisplaySettingsW failed')
            else:
                row['mode']=measured({'x':mode.x,'y':mode.y,'width':mode.width,'height':mode.height,
                    'frequency_hz':mode.frequency,'orientation':mode.orientation,'bits_per_pixel':mode.bits},'EnumDisplaySettingsW ENUM_CURRENT_SETTINGS')
        rows.append(row)
    if not rows:
        raise RuntimeError('no display devices observed')
    return rows


def _dpi_current_process():
    if os.name!='nt':
        return unmeasured('Windows per-monitor DPI probe only')
    try:
        shcore=c.WinDLL('shcore',use_last_error=True)
        shcore.GetProcessDpiAwareness.argtypes=[w.HANDLE,c.POINTER(c.c_int)]
        shcore.GetProcessDpiAwareness.restype=c.c_long
        awareness=c.c_int()
        if shcore.GetProcessDpiAwareness(None,c.byref(awareness))!=0:
            return unmeasured('GetProcessDpiAwareness failed')
        if awareness.value!=2:
            return unmeasured('caller is not per-monitor DPI aware; virtualized 96/system DPI cannot identify the tested monitor configuration')
        user=c.WinDLL('user32',use_last_error=True)
        callback_type=c.WINFUNCTYPE(w.BOOL,w.HANDLE,w.HDC,c.POINTER(w.RECT),w.LPARAM)
        user.EnumDisplayMonitors.argtypes=[w.HDC,c.POINTER(w.RECT),callback_type,w.LPARAM]
        user.EnumDisplayMonitors.restype=w.BOOL
        shcore.GetDpiForMonitor.argtypes=[w.HANDLE,c.c_int,c.POINTER(w.UINT),c.POINTER(w.UINT)]
        shcore.GetDpiForMonitor.restype=c.c_long
        rows=[];errors=[]
        def visit(monitor,dc,rect,context):
            x,y=w.UINT(),w.UINT()
            if shcore.GetDpiForMonitor(monitor,0,c.byref(x),c.byref(y))!=0:
                errors.append('GetDpiForMonitor failed')
            else:
                r=rect.contents
                rows.append({'rectangle':[r.left,r.top,r.right,r.bottom],'effective_dpi':[x.value,y.value]})
            return True
        callback=callback_type(visit)
        if not user.EnumDisplayMonitors(None,None,callback,0) or errors or not rows:
            return unmeasured('per-monitor DPI enumeration incomplete: '+repr(errors))
        return measured(rows,'GetDpiForMonitor MDT_EFFECTIVE_DPI with observed per-monitor awareness')
    except Exception as error:
        return unmeasured('DPI query unavailable: '+repr(error))


def dpi():
    if os.name != 'nt':
        return unmeasured('Windows per-monitor DPI probe only')
    try:
        child = subprocess.run([sys.executable, str(Path(__file__).with_name('dpi_probe.py'))],
                               capture_output=True, text=True, timeout=5,
                               creationflags=subprocess.CREATE_NO_WINDOW)
        if child.returncode != 0:
            raise ValueError('DPI probe process failed')
        value = json.loads(child.stdout)
        if not isinstance(value, dict) or value.get('status') != 'measured':
            raise ValueError('DPI probe unmeasured: ' + repr(value))
        rows = value.get('value')
        if not isinstance(rows, list) or not rows or not value.get('source'):
            raise ValueError('DPI probe incomplete')
        rectangles = set()
        for row in rows:
            rectangle, effective = row['rectangle'], row['effective_dpi']
            if (len(rectangle) != 4 or any(type(v) is not int for v in rectangle) or
                rectangle[0] >= rectangle[2] or rectangle[1] >= rectangle[3] or
                len(effective) != 2 or any(type(v) is not int or v <= 0 for v in effective) or
                tuple(rectangle) in rectangles):
                raise ValueError('DPI probe invalid values')
            rectangles.add(tuple(rectangle))
        return value
    except Exception as error:
        return unmeasured('isolated DPI query unavailable: ' + repr(error))


def configuration_identity(displays=None):
    if displays is None:
        displays = observe(display_devices, 'EnumDisplayDevicesW and EnumDisplaySettingsW')
    topology=displays
    if displays.get('status')=='measured':
        attached=[row for row in displays['value'] if row['state_flags'] & 1]
        topology=measured(attached,displays['source']+'; displays attached to desktop only') if attached else unmeasured('no display attached to desktop')
    identity = {key: unmeasured('trusted runtime render profile identity is not implemented') for key in DIMENSIONS}
    identity.update(os=observe(lambda: {'platform': platform.platform(), 'win32_ver': list(platform.win32_ver()),
                                      'machine': platform.machine()}, 'Python platform OS APIs'),
                    display_topology=topology, dpi=dpi())
    bound=_render_observation and all(identity[key].get('status')=='measured' and identity[key]['value']==_render_observation['configuration'][key]['value'] for key in ['os','display_topology','dpi'])
    if bound:
        for key,field in [('render_profile_id','id'),('render_profile_version','version'),('render_profile_format_version','format_version')]:
            identity[key]=measured(_render_observation['profile'][field],_render_observation['source'])
    return identity


def collect():
    from check_contracts import DATA
    tools={'python':observe(lambda:dict(binary(sys.executable),version=sys.version),'running Python executable'),
           'unity':observe(lambda:binary(DATA.parent/'Unity.exe'),'pinned check_contracts.DATA'),
           'mono':observe(lambda:binary(DATA/'MonoBleedingEdge/bin/mono.exe'),'pinned check_contracts.DATA'),
           'csharp_compiler':observe(lambda:binary(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'pinned check_contracts.DATA'),
           'native_compiler':unmeasured('native compiler selection/version not measured by this probe; runner build receipts remain separate')}
    displays=observe(display_devices,'EnumDisplayDevicesW and EnumDisplaySettingsW')
    if displays['status']=='measured':
        gpu=measured([{key:row[key] for key in ('description','device_id','device_key','state_flags')} for row in displays['value']],
                     'OS display adapters; does not identify the Worker rendering adapter')
    else:
        gpu=displays.copy()
    identity=configuration_identity(displays)
    return {'schema':1,'observed_at_unix':time.time(),'toolchain':tools,
            'hardware':{'cpu':observe(cpu,'OS CPU count and Windows processor registry'),
                        'memory':observe(memory,'GlobalMemoryStatusEx'),'gpu':gpu,
                        'worker_render_gpu':unmeasured('no Worker/SystemInfo rendering adapter observation in the environment probe')},
            'required_configuration_dimensions':list(DIMENSIONS),'configuration_identity':identity,
            'configuration_complete':all(entry['status']=='measured' for entry in identity.values()),
            'support_catalog':unmeasured('no approved release support catalog bound to this execution')}
