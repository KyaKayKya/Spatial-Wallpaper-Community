"""Analyze exact Worker FrameTimingManager rows; never substitute CPU utilization."""
from pathlib import Path
import json,math

POLICY=json.loads((Path(__file__).with_name('release-policy.json')).read_text())['performance']
PREFIX='SDK_FRAME_TIMINGS '

class TimingSeries:
    def __init__(self,identity):
        if set(identity)!={'attempt','instance','generation','build','width','height','target_fps'} or identity['target_fps'] not in POLICY['target_fps']:
            raise ValueError('frame-timing-expected-identity')
        self.identity=dict(identity);self.frequency=None;self.rows={};self.last=0

    def read(self,text):
        # A bounded log snapshot can start or end inside a line. Only complete
        # dedicated records are accepted; ordinary Unity diagnostics stay raw.
        for line in text.splitlines():
            if not line.startswith(PREFIX) or not line.rstrip().endswith('}'):continue
            if len(line)>16384:raise ValueError('frame-timing-record-size')
            value=json.loads(line[len(PREFIX):])
            if set(value)!=set(self.identity)|{'format','cpu_timer_frequency','rows'} or type(value['format']) is not int or value['format']!=1 or any(type(value[k]) is not type(v) or value[k]!=v for k,v in self.identity.items()):
                raise ValueError('frame-timing-record-identity-or-fields')
            frequency=value['cpu_timer_frequency']
            if type(frequency) is not int or frequency<1 or self.frequency is not None and frequency!=self.frequency:
                raise ValueError('frame-timing-clock-frequency')
            self.frequency=frequency
            if not isinstance(value['rows'],list) or not 1<=len(value['rows'])<=32:raise ValueError('frame-timing-batch-shape')
            for row in value['rows']:
                if not isinstance(row,list) or len(row)!=7 or any(type(v) is not int or v<1 for v in row[:2]) or any(type(v) not in (int,float) or not math.isfinite(v) or v<0 for v in row[2:]):
                    raise ValueError('frame-timing-row-shape')
                sequence=row[0]
                if sequence in self.rows:
                    if row!=self.rows[sequence]:raise ValueError('frame-timing-conflicting-sample')
                    continue
                if self.rows:
                    last=self.last
                    if sequence!=last+1:raise ValueError('frame-timing-sequence-gap-or-regression')
                    if row[1]<=self.rows[last][1]:raise ValueError('frame-timing-frame-clock-regression')
                if len(self.rows)>=36000:raise ValueError('frame-timing-run-exceeds-bounded-sample-count')
                self.rows[sequence]=row
                self.last=sequence

    def summary(self):
        rows=list(self.rows.values())
        missing_main=sum(row[3]<=0 for row in rows)
        missing_render=sum(row[4]<=0 for row in rows)
        missing_gpu=sum(row[6]<=0 for row in rows)
        result={'status':'unmeasured','samples':len(rows),
                'missing_main_thread_samples':missing_main,
                'missing_render_thread_samples':missing_render,
                'missing_gpu_samples':missing_gpu,
                'cpu_scope':'Separate Unity thread durations; their maximum is a diagnostic, not the frame critical path.',
                'api':'Unity 2022.3 FrameTimingManager','unit':'milliseconds',
                'target_fps':self.identity['target_fps'],'grade':None,
                'limitations':['cpu-critical-path-unproven','actual-rendered-frame-association-unproven',
                               'reported-timestamp-frequency-unverified']}
        if missing_gpu:result['limitations'].append('gpu-samples-unavailable')
        if not rows:return result
        def stats(values):
            values=sorted(values)
            return {**{'p'+str(p):values[math.ceil(len(values)*p/100)-1] for p in POLICY['percentiles']},'max':values[-1]}
        # Retain zero values and their coverage counts. Never report a positive
        # main-thread duration as proof that render-thread timing was measured.
        result.update(main_thread_ms=stats([row[3] for row in rows]),
                      render_thread_ms=stats([row[4] for row in rows]),
                      thread_max_ms=stats([max(row[3],row[4]) for row in rows]),
                      gpu_frame_ms=stats([row[6] for row in rows]),
                      present_wait_ms=stats([row[5] for row in rows]),
                      full_cpu_frame_ms=stats([row[2] for row in rows]),
                      raw_timestamp_span=rows[-1][1]-rows[0][1],
                      reported_timer_frequency=self.frequency)
        return result
