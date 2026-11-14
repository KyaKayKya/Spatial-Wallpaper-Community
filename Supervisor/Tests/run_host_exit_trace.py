"""Noninteractive exit diagnostics tests. Never starts Host, Unity or desktop input."""
from pathlib import Path
import json,sys,subprocess,uuid,struct,ctypes,time
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references
from host_exit_trace import read_host_exit_trace
kernel=ctypes.WinDLL('kernel32',use_last_error=True)
kernel.GetProcessTimes.argtypes=[ctypes.c_void_p,*[ctypes.POINTER(ctypes.c_ulonglong)]*4];kernel.GetProcessTimes.restype=ctypes.c_int
def created(process):
    values=[ctypes.c_ulonglong() for _ in range(4)]
    if not kernel.GetProcessTimes(int(process._handle),*[ctypes.byref(v) for v in values]):raise ctypes.WinError(ctypes.get_last_error())
    return values[0].value
out=ROOT/'Validation/Content/sdk-runtime/E/host-exit-trace'/uuid.uuid4().hex;out.mkdir(parents=True)
assert sys.argv[1:] in [[],['--negative-frozen-clock']]
negative=bool(sys.argv[1:]);source=ROOT/'Supervisor/Core/HostExitTrace.cs'
if negative:
    text=source.read_text(encoding='utf-8-sig');needle='view.Write(at+8,Stopwatch.GetTimestamp());';assert text.count(needle)==1
    source=out/'HostExitTrace.cs';source.write_text(text.replace(needle,'view.Write(at+8,view.ReadInt64(40));'),encoding='utf-8')
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-define:SDK_SUPERVISOR_TESTS',*references(),'-out:'+str(out/'Checks.exe'),str(source),str(ROOT/'Supervisor/Core/CleanupFailures.cs'),str(ROOT/'Supervisor/Tests/HostExitTraceChecks.cs')],check=True)
process=subprocess.Popen([str(out/'Checks.exe'),str(out)],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
try:
    creation=created(process)
    stdout,stderr=process.communicate(timeout=15)
finally:
    if process.poll() is None:process.kill()
    process.wait(timeout=5)
(out/'result.log').write_text(stdout+stderr,encoding='utf-8');assert process.returncode==0,stdout+stderr
normal=read_host_exit_trace(out/'normal.trace',process.pid,creation);assert normal['status']=='stable' and normal['main_returning_observed'] and normal['kernel_exit_proven'] is False
begin,end=normal['records'][:2];delta=end['elapsed_ms']-begin['elapsed_ms']
if negative:
    assert delta<20,'negative failed to freeze clock'
    (out/'report.json').write_text(json.dumps({'expected_rejection':'controlled-pause-not-observed','observed_ms':delta,'kernel_exit_proven_by_parent':process.returncode==0},indent=2));print('EXPECTED REJECT controlled-pause-not-observed');print(out);raise SystemExit(0)
assert delta>=20,'controlled-pause-not-observed'
assert normal['records'][3]['event']=='close-requested'
fault=read_host_exit_trace(out/'fault.trace',process.pid);assert [r['event'] for r in fault['records']]==['begin','fault','begin','end','main-returning']
assert b'secret-user-path' not in (out/'fault.trace').read_bytes()
assert read_host_exit_trace(out/'write-fault.trace',process.pid)['status']=='incomplete-publication'
overflow=read_host_exit_trace(out/'overflow.trace',process.pid);assert overflow['status']=='overflow' and overflow['dropped']==3 and len(overflow['records'])==64
assert read_host_exit_trace(out/'normal.trace',process.pid+1)['status']=='identity-or-schema-mismatch'
assert read_host_exit_trace(out/'normal.trace',process.pid,creation+1)['status']=='identity-or-schema-mismatch'
data=bytearray((out/'normal.trace').read_bytes());struct.pack_into('<i',data,64+16,999);(out/'unknown.trace').write_bytes(data);assert read_host_exit_trace(out/'unknown.trace',process.pid)['status']=='invalid-record'
held=subprocess.Popen([str(out/'Checks.exe'),str(out),'--hold'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
try:
    held_creation=created(held);deadline=time.monotonic()+2
    while True:
        live=read_host_exit_trace(out/'held.trace',held.pid,held_creation)
        if live['status']=='stable' and live['records']:break
        assert time.monotonic()<deadline and held.poll() is None,'live-begin-not-published'
        time.sleep(.005)
    assert held.poll() is None and len(live['records'])==1 and live['records'][0]['event']=='begin' and not live['main_returning_observed']
finally:
    if held.poll() is None:held.kill()
    held.wait(timeout=5)
assert read_host_exit_trace(out/'held.trace',held.pid,held_creation)==live,'pending-begin-lost-on-owner-exit'
(out/'report.json').write_text(json.dumps({'scope':'fixed-exit-diagnostics-only','normal':normal,'fault':fault,'controlled_pause_ms':delta,'overflow':overflow,'live_begin_before_owner_kill':live,'diagnostic_failure_preserves_action_and_exception':True,'parent_observed_checks_kernel_exit':True,'held_process_exit_code':held.returncode},indent=2));print(stdout);print(out)
