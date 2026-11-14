"""Emergency pairing for this fixture's durable obligations after Client kernel exit."""
import ctypes as c
from ctypes import wintypes as w
import json,time

RELEASES={1:(1,32,2),2:(0,0,4),4:(1,65,2),8:(1,37,2),16:(1,39,2),32:(1,38,2),64:(1,40,2),128:(0,0,16)}

def cleanup(folder):
    folder=folder.resolve();ledger=folder/'input-obligations.bin'
    if not ledger.exists():return
    data=ledger.read_bytes()
    if len(data)!=1 or data[0]&~255:raise RuntimeError('invalid-input-obligation-ledger')
    if data[0]==0:
        # Historical PIDs may now identify unrelated protected processes.
        # No release is owed, so no native operation or exit claim is needed.
        evidence=folder/'external-input-cleanup.json'
        attempts=json.loads(evidence.read_text())['attempts'] if evidence.exists() else []
        attempts.append({'initial_obligations':0,'remaining_obligations':0,'children':[],
                         'child_observation':'not-required-zero-obligations-unverified',
                         'failures':[],'time':time.time()})
        evidence.write_text(json.dumps({'attempts':attempts[-16:]},indent=2))
        return
    kernel=c.WinDLL('kernel32',use_last_error=True)
    kernel.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD];kernel.OpenProcess.restype=w.HANDLE
    kernel.WaitForSingleObject.argtypes=[w.HANDLE,w.DWORD];kernel.WaitForSingleObject.restype=w.DWORD
    kernel.GetProcessTimes.argtypes=[w.HANDLE,*([c.POINTER(c.c_longlong)]*4)];kernel.GetProcessTimes.restype=w.BOOL
    kernel.CloseHandle.argtypes=[w.HANDLE];kernel.CloseHandle.restype=w.BOOL
    observed=[]
    # Client has exited, closing its noninherited Job handles. Observe original children
    # before pairing releases. PID reuse is distinguished by kernel creation time.
    for record in folder.glob('inject-*.request.pid'):
        pid,expected=map(int,record.read_text().split());handle=kernel.OpenProcess(0x100000|0x1000,False,pid)
        if not handle:
            error=c.get_last_error()
            if error!=87:raise c.WinError(error)
            observed.append({'pid':pid,'exited':True});continue
        try:
            times=[c.c_longlong() for _ in range(4)]
            if not kernel.GetProcessTimes(handle,*map(c.byref,times)):raise c.WinError(c.get_last_error())
            reused=times[0].value!=expected
            if not reused and kernel.WaitForSingleObject(handle,5000)!=0:raise RuntimeError('owned-injector-kernel-not-exited')
            observed.append({'pid':pid,'exited':True,'pid_reused':reused})
        finally:kernel.CloseHandle(handle)
    class Mouse(c.Structure):_fields_=[('x',w.LONG),('y',w.LONG),('data',w.DWORD),('flags',w.DWORD),('time',w.DWORD),('extra',c.c_size_t)]
    class Key(c.Structure):_fields_=[('key',w.WORD),('scan',w.WORD),('flags',w.DWORD),('time',w.DWORD),('extra',c.c_size_t)]
    class Payload(c.Union):_fields_=[('mouse',Mouse),('key',Key)]
    class Input(c.Structure):_fields_=[('type',w.DWORD),('payload',Payload)]
    user=c.WinDLL('user32',use_last_error=True);user.SendInput.argtypes=[w.UINT,c.POINTER(Input),c.c_int];user.SendInput.restype=w.UINT
    failures=[];mask=data[0];initial=mask
    for bit,(kind,key,flags) in RELEASES.items():
        if not mask&bit:continue
        event=Input()
        if kind==1:event.type=1;event.payload.key.key=key;event.payload.key.flags=flags
        else:event.payload.mouse.flags=flags
        if user.SendInput(1,c.byref(event),c.sizeof(Input))!=1:failures.append(bit)
        else:mask&=~bit
    if mask!=initial:ledger.write_bytes(bytes([mask]))
    evidence=folder/'external-input-cleanup.json'
    attempts=json.loads(evidence.read_text())['attempts'] if evidence.exists() else []
    attempts.append({'initial_obligations':initial,'remaining_obligations':mask,'children':observed,'failures':failures,'time':time.time()})
    evidence.write_text(json.dumps({'attempts':attempts[-16:]},indent=2))
    if failures:raise RuntimeError('external-input-release-failed:'+str(failures))
