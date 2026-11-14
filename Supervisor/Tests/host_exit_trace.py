"""Read fixed Host exit diagnostics. Main-returning never proves kernel exit."""
from __future__ import annotations
from pathlib import Path
import mmap
import os
import struct

MAGIC=0x53574558
CAPACITY=64
SIZE=64+CAPACITY*32
PHASES=('service-dispose','runtime-close','runtime-wait','runtime-result','gpu-close-request','storage-close','storage-wait','llm-dispose','llm-wait','llm-implicit-dispose','audio-dispose','input-dispose','station-dispose','journal-close','journal-wait','endpoint-delete','failure-report','main')
EVENTS=('begin','end','fault','close-requested','skipped','main-returning','forced-process-exit')

def read_host_exit_trace(path: Path, expected_pid: int, expected_creation_filetime: int | None = None):
    if type(expected_pid) is not int or expected_pid<=0:
        raise ValueError('expected Host PID required')
    if expected_creation_filetime is not None and (type(expected_creation_filetime) is not int or expected_creation_filetime<=0):
        raise ValueError('expected creation FILETIME invalid')
    result={'status':'unavailable','kernel_exit_proven':False,'records':[]}
    try:
        with Path(path).open('rb') as stream:
            if os.fstat(stream.fileno()).st_size!=SIZE:
                return {**result,'status':'invalid-size'}
            with mmap.mmap(stream.fileno(),SIZE,access=mmap.ACCESS_READ) as view:
                before=struct.unpack_from('<q',view,8)[0]
                data=view[:]
                after=struct.unpack_from('<q',view,8)[0]
    except (OSError,ValueError):
        return result
    magic,schema=struct.unpack_from('<II',data)
    pid,capacity,count,dropped=struct.unpack_from('<iiii',data,16)
    frequency,start=struct.unpack_from('<qq',data,32)
    failed,owner=struct.unpack_from('<ii',data,48)
    creation=struct.unpack_from('<q',data,56)[0]
    if magic!=MAGIC or schema!=1 or capacity!=CAPACITY or pid!=expected_pid or (expected_creation_filetime is not None and creation!=expected_creation_filetime):
        return {**result,'status':'identity-or-schema-mismatch'}
    if before!=after or before<0 or before%2 or struct.unpack_from('<q',data,8)[0]!=before:
        return {**result,'status':'incomplete-publication'}
    if failed or frequency<=0 or start<=0 or owner<=0 or creation<=0 or not 0<=count<=CAPACITY or dropped<0 or before!=2*(count+dropped):
        return {**result,'status':'invalid-header'}
    rows=[];previous=start
    for index in range(count):
        seq,qpc,phase,event,code,reserved=struct.unpack_from('<qqiiii',data,64+index*32)
        if seq!=index+1 or qpc<previous or not 1<=phase<=len(PHASES) or not 1<=event<=len(EVENTS) or reserved:
            return {**result,'status':'invalid-record'}
        rows.append({'sequence':seq,'qpc':qpc,'elapsed_ms':(qpc-start)*1000/frequency,'phase':PHASES[phase-1],'event':EVENTS[event-1],'code':code})
        previous=qpc
    return {**result,'status':'overflow' if dropped else 'stable','pid':pid,'creation_filetime':creation,'identity_binding':'pid-and-creation' if expected_creation_filetime is not None else 'pid-only','clock':'host-exit-stopwatch','frequency':frequency,'start_qpc':start,'publication':before,'dropped':dropped,'records':rows,'main_returning_observed':any(r['event']=='main-returning' for r in rows)}
