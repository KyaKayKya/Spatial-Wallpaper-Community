"""Exact mutation identity and first failure, never substring-only acceptance."""
import json
import re
from build_render_negative import MODES

def require_negative(log,mode,attempt,result):
    location,condition,mutation,error,frames=MODES[mode]
    lines=log.splitlines();prefix='SDK_RENDER_NEGATIVE '
    hits=[(index,line[len(prefix):]) for index,line in enumerate(lines) if line.startswith(prefix)]
    if len(hits)!=1:raise ValueError('render-negative-marker-count')
    index,text=hits[0];marker=json.loads(text)
    expected=dict(mode=mode,attempt=attempt,stage=location,completed_sequence=frames,next_sequence=frames+1,mutation_count=1)
    for key,value in expected.items():
        if type(marker.get(key)) is not type(value) or marker[key]!=value:raise ValueError('render-negative-marker-'+key)
    if type(marker.get('unity_frame')) is not int or marker['unity_frame']<0:raise ValueError('render-negative-unity-frame')
    if mode=='renderer-after-frames':
        for key in ('renderer_changed','pipeline_same','data_same'):
            if marker.get(key) is not True:raise ValueError('render-negative-object-'+key)
    if mode=='data-during-warmup':
        if marker.get('data_changed') is not True or type(marker.get('selected_index')) is not int or marker['selected_index']!=0:
            raise ValueError('render-negative-selected-data')
    failures=[(i,line) for i,line in enumerate(lines) if re.match(r'^\w+Exception:',line)]
    if mode=='stop-before-late-readback':
        late_prefix='SDK_RENDER_NEGATIVE_LATE_READBACK '
        late=[(i,json.loads(line[len(late_prefix):])) for i,line in enumerate(lines) if line.startswith(late_prefix)]
        if len(late)!=1 or late[0][0]<=index or failures:raise ValueError('render-negative-late-readback')
        value=late[0][1]
        for key,expected in dict(reason='capture-stopped-late-readback',attempt=attempt,request_sequence=6,completed_sequence=5,stopped=True,has_error=False).items():
            if type(value.get(key)) is not type(expected) or value[key]!=expected:raise ValueError('render-negative-late-readback-'+key)
        frequency=result.get('clock_frequency');origin=result.get('clock_origin_qpc');forced=result.get('stop_evidence',{}).get('forced_started_at_ms')
        # Both producers use native QPC normalized to 100 ns, not Mono's
        # process-relative Stopwatch. Reject the entire forced-start millisecond
        # conservatively: a rounded timestamp cannot order events within it.
        if (type(frequency) is not int or frequency!=10000000 or type(origin) is not int or origin<0 or
            type(forced) is not int or forced<0 or
            any(type(row.get('frequency')) is not int or row['frequency']!=frequency or
                type(row.get('qpc')) is not int for row in (marker,value)) or
            not origin<=marker['qpc']<=value['qpc'] or
            (value['qpc']-origin)*1000>=forced*frequency):raise ValueError('render-negative-late-readback-order')
    elif not failures or failures[0][0]<=index or failures[0][1]!='InvalidDataException: '+error:
        raise ValueError('render-negative-first-failure')
    receipts=[json.loads(line[len('SDK_RENDER_PROFILE '):]) for line in lines if line.startswith('SDK_RENDER_PROFILE ')]
    if len(receipts)!=(1 if frames else 0) or any(row.get('attempt')!=attempt for row in receipts):raise ValueError('render-negative-profile-receipt')
    if result.get('received_frames')!=frames or result.get('last_sequence')!=frames or result.get('attempt')!=attempt:
        raise ValueError('render-negative-received-sequences')
    return marker
