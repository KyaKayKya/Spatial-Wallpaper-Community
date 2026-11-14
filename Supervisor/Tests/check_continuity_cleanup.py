"""Exercise the real outer cleanup with fake native calls; never inject input."""
import json, tempfile, uuid
from pathlib import Path
import owned_input_cleanup as cleanup

class NativeCall:
    def __init__(self, call=lambda *args: 1): self.call=call
    def __call__(self,*args): return self.call(*args)
class Native:
    def __init__(self,fail=None):
        self.events=[];self.fail=fail
        for name in ['OpenProcess','WaitForSingleObject','GetProcessTimes','CloseHandle']:
            setattr(self,name,NativeCall())
        self.SendInput=NativeCall(self.send)
    def send(self,count,pointer,size):
        event=pointer._obj
        value=(event.type,event.payload.key.key,event.payload.key.flags) if event.type else (0,0,event.payload.mouse.flags)
        self.events.append(value)
        return 0 if value==self.fail else 1

expected={1:(1,32,2),2:(0,0,4),4:(1,65,2),8:(1,37,2),16:(1,39,2),32:(1,38,2),64:(1,40,2),128:(0,0,16)}
original=cleanup.c.WinDLL;cases=[]
try:
    for mask in [*expected,255]:
        native=Native();cleanup.c.WinDLL=lambda *a,**kw:native
        with tempfile.TemporaryDirectory() as path:
            folder=Path(path);(folder/'input-obligations.bin').write_bytes(bytes([mask]))
            cleanup.cleanup(folder)
            assert native.events==[event for bit,event in expected.items() if mask&bit]
            assert (folder/'input-obligations.bin').read_bytes()==b'\0'
            cases.append('mask-'+str(mask))
    native=Native(expected[16]);cleanup.c.WinDLL=lambda *a,**kw:native
    with tempfile.TemporaryDirectory() as path:
        folder=Path(path);(folder/'input-obligations.bin').write_bytes(b'\xff')
        try:cleanup.cleanup(folder)
        except RuntimeError as error:assert str(error)=='external-input-release-failed:[16]'
        else:raise AssertionError('release failure was hidden')
        assert native.events==list(expected.values()),'one failure skipped later releases'
        assert (folder/'input-obligations.bin').read_bytes()==bytes([16])
        native.fail=None;native.events=[];cleanup.cleanup(folder)
        assert native.events==[expected[16]]
        assert len(json.loads((folder/'external-input-cleanup.json').read_text())['attempts'])==2
        cases.append('partial-failure-retry-preserves-first-evidence')
finally:cleanup.c.WinDLL=original
out=Path(__file__).resolve().parents[2]/'Validation/Content/sdk-runtime/E/input-continuity-cleanup'/uuid.uuid4().hex
out.mkdir(parents=True);(out/'report.json').write_text(json.dumps({'scope':'native API mocked; no real input or process exit proof','cases':cases},indent=2))
print(out);print('PASS',len(cases),'cleanup mapping and retry cases')
