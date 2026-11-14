"""CPU-only OpenCV DNN benchmark; synthetic input measures cost, NOT accuracy."""
import argparse
import ctypes
import hashlib
import json
import os
import time
import winreg
from pathlib import Path

import cv2
import numpy as np

class Counters(ctypes.Structure):
    _fields_=[('cb',ctypes.c_ulong),('PageFaultCount',ctypes.c_ulong)]+[(x,ctypes.c_size_t) for x in ['PeakWorkingSetSize','WorkingSetSize','QuotaPeakPagedPoolUsage','QuotaPagedPoolUsage','QuotaPeakNonPagedPoolUsage','QuotaNonPagedPoolUsage','PagefileUsage','PeakPagefileUsage']]
def memory():
    c=Counters(); c.cb=ctypes.sizeof(c)
    ctypes.windll.kernel32.GetCurrentProcess.restype=ctypes.c_void_p
    fn=ctypes.windll.psapi.GetProcessMemoryInfo
    fn.argtypes=[ctypes.c_void_p,ctypes.POINTER(Counters),ctypes.c_ulong]
    if not fn(ctypes.windll.kernel32.GetCurrentProcess(),ctypes.byref(c),c.cb): raise ctypes.WinError()
    return {'working_set_bytes':c.WorkingSetSize,'peak_working_set_bytes':c.PeakWorkingSetSize}
def main():
    p=argparse.ArgumentParser(); p.add_argument('--threads',type=int,default=4); p.add_argument('--runs',type=int,default=50); args=p.parse_args()
    root=Path(__file__).resolve().parents[2]/'Validation/Gaze/local'
    model=root/'l2cs_net_1x3x448x448.onnx'
    cv2.setNumThreads(args.threads)
    baseline=memory(); start=time.perf_counter()
    net=cv2.dnn.readNetFromONNX(str(model))
    net.setPreferableBackend(cv2.dnn.DNN_BACKEND_OPENCV); net.setPreferableTarget(cv2.dnn.DNN_TARGET_CPU)
    load_ms=(time.perf_counter()-start)*1000
    x=np.random.default_rng(47).normal(0,1,(1,3,448,448)).astype(np.float32)
    names=net.getUnconnectedOutLayersNames()
    def once():
        t=time.perf_counter(); net.setInput(x); result=net.forward(names); return (time.perf_counter()-t)*1000,result
    cold,outputs=once()
    reference=np.load(root/'export-reference.npz')
    errors={name:float(np.max(np.abs(value-reference[name]))) for name,value in zip(names,outputs)}
    for name,value in zip(names,outputs):
        np.testing.assert_allclose(value,reference[name],rtol=1e-4,atol=1e-4)
    for _ in range(5):once()
    wall=time.perf_counter(); cpu=time.process_time()
    elapsed=[once()[0] for _ in range(args.runs)]
    cpu_used=time.process_time()-cpu; wall_used=time.perf_counter()-wall
    with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,r'HARDWARE\DESCRIPTION\System\CentralProcessor\0') as k: processor=winreg.QueryValueEx(k,'ProcessorNameString')[0]
    report={'model_bytes':model.stat().st_size,'sha256':hashlib.sha256(model.read_bytes()).hexdigest(),'cpu':processor,'logical_cpus':os.cpu_count(),'opencv':cv2.__version__,'target':'CPU','threads':args.threads,'input_shape':list(x.shape),'runs':args.runs,'load_ms':load_ms,'cold_ms':cold,'median_ms':float(np.median(elapsed)),'p95_ms':float(np.percentile(elapsed,95)),'cpu_core_equivalents':cpu_used/wall_used,'baseline_memory':baseline,'loaded_memory':memory(),'outputs':[{'name':n,'shape':list(o.shape),'sample':o.flatten()[:5].tolist(),'finite':bool(np.isfinite(o).all())} for n,o in zip(names,outputs)],'accuracy_measured':False,'scope':'gaze net only; excludes capture, MediaPipe, crop and UI; synthetic normalized input'}
    report['torch_opencv_max_abs_errors']=errors
    (root/f'benchmark-cpu-{args.threads}.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))
if __name__=='__main__':main()
