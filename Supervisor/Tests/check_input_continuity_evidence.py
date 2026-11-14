"""Pure evidence mutation negatives; no input or Unity."""
import copy,json,uuid
from pathlib import Path
from input_continuity_evidence import validate,NAMES,BITS
ROOT=Path(__file__).resolve().parents[2];out=ROOT/'Validation/Content/sdk-runtime/E/input-continuity-evidence'/uuid.uuid4().hex;out.mkdir(parents=True)
proof={'mode':'development-short-only','requested_seconds':1,'elapsed_ms':1400,'cycles':1,'author_edges':14,'source_edges':14,'dropped_edges':0,'owed':0,'epoch':7}
rows=[{'edge':i+1,'revision':i+1,'source_edges':i+1,'cycle':0,'step':i,'ack':NAMES[i//2]+('-up' if i%2 else '-down'),'epoch':7,'obligations':0 if i%2 else BITS[i//2],'elapsed_ms':(i+1)*100} for i in range(14)]
validate(proof,rows,1);results=[]
mutations=[
 ('lost-edge',lambda p,r:r.pop(3)),
 ('duplicate-edge',lambda p,r:r.__setitem__(3,copy.deepcopy(r[2]))),
 ('wrong-ack',lambda p,r:r[0].update(ack='Left-down')),
 ('epoch-reset',lambda p,r:r[8].update(epoch=8)),
 ('unreleased-key',lambda p,r:r[-1].update(obligations=128)),
 ('short-as-formal',lambda p,r:p.update(mode='formal-1800s')),
 ('source-edge-missing',lambda p,r:r[2].update(source_edges=2)),
 ('reused-revision',lambda p,r:r[5].update(revision=5)),
 ('truncated-run',lambda p,r:p.update(elapsed_ms=999)),
 ('false-zero-loss',lambda p,r:p.update(dropped_edges=1)),
 ('wrong-direction-obligation',lambda p,r:r[2].update(obligations=16)),
 ('bool-revision',lambda p,r:r[0].update(revision=True)),
 ('bool-cycles',lambda p,r:p.update(cycles=True)),
 ('float-edge',lambda p,r:r[0].update(edge=1.0)),
 ('negative-time',lambda p,r:r[0].update(elapsed_ms=-1)),
 ('float-time',lambda p,r:r[0].update(elapsed_ms=100.0)),
 ('zero-epoch',lambda p,r:p.update(epoch=0)),
 ('bool-epoch',lambda p,r:p.update(epoch=True)),
 ('negative-summary-time',lambda p,r:p.update(elapsed_ms=-1400)),
]
for name,mutate in mutations:
 p,r=copy.deepcopy(proof),copy.deepcopy(rows);mutate(p,r)
 try:validate(p,r,1)
 except ValueError as e:results.append({'case':name,'rejected':str(e)})
 else:raise AssertionError('accepted-invalid-evidence:'+name)
(out/'report.json').write_text(json.dumps({'status':'passed-controlled-evidence-negatives','results':results},indent=2));print(out);print('PASS',len(results),'precise evidence rejection cases')
