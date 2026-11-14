"""Strict continuity receipt verification. No desktop or process operations."""
import json
NAMES=['Space','Left','Right','Up','Down','Primary','Secondary']
BITS=[1,8,16,32,64,2,128]
def validate(proof,rows,seconds):
    def need(value,message):
        if not value:raise ValueError(message)
    def integer(value,label,minimum=0):
        need(type(value) is int and value>=minimum,'integer-domain:'+label)
    integer(seconds,'duration',1)
    for key in ['requested_seconds','elapsed_ms','cycles','author_edges','source_edges','dropped_edges','owed']:
        integer(proof[key],'proof.'+key)
    integer(proof['epoch'],'proof.epoch',1)
    need(1<=seconds<=1800,'duration-bound')
    need(proof['requested_seconds']==seconds and proof['elapsed_ms']>=seconds*1000,'duration-not-reached')
    need(proof['mode']==('formal-1800s' if seconds==1800 else 'development-short-only'),'mode-mismatch')
    need(0<proof['cycles']<=8192,'cycle-bound')
    edges=proof['cycles']*14
    need(proof['author_edges']==proof['source_edges']==edges and len(rows)==edges,'edge-count')
    need(proof['dropped_edges']==0 and proof['owed']==0,'loss-or-obligations')
    last=0
    for index,row in enumerate(rows):
        for key in ['edge','revision','source_edges','cycle','step','obligations','elapsed_ms']:
            integer(row[key],'row.'+key)
        integer(row['epoch'],'row.epoch',1)
        step=index%14;key=step//2;up=step%2
        need(row['edge']==row['revision']==row['source_edges']==index+1,'edge-sequence')
        need(row['cycle']==index//14 and row['step']==step,'cycle-sequence')
        need(row['ack']==NAMES[key]+('-up' if up else '-down'),'ack-value')
        need(row['epoch']==proof['epoch'] and proof['epoch']>0,'epoch-changed')
        need(row['obligations']==(0 if up else BITS[key]),'edge-obligations')
        need(last<=row['elapsed_ms']<=proof['elapsed_ms'],'event-time-order')
        last=row['elapsed_ms']
    need(last>=seconds*1000,'last-complete-cycle-before-duration')
def validate_mixed(proof,rows,seconds):
    validate(proof,rows,seconds)
    def need(value,message):
        if not value:raise ValueError(message)
    need(proof.get('mixed_motion') is True,'mixed-mode')
    keys=('moves_requested','hook_moves','move_commits')
    for key in keys:need(type(proof.get(key)) is int and proof[key]>=0,'mixed-integer:'+key)
    need(proof['moves_requested']==proof['cycles']*20,'mixed-request-count')
    previous={key:0 for key in keys}
    for row in rows:
        for key in keys:need(type(row.get(key)) is int and row[key]>=previous[key],'mixed-integer:'+key)
        keyboard=row['step']<10
        need(row['moves_requested']-previous['moves_requested']==(2 if keyboard else 0),'mixed-request-edge')
        need(row['hook_moves']-previous['hook_moves']>=(2 if keyboard else 0),'mixed-hook-edge')
        need(row['move_commits']-previous['move_commits']>=(1 if keyboard else 0),'mixed-commit-edge')
        previous={key:row[key] for key in keys}
    need(all(previous[key]==proof[key] for key in keys),'mixed-final-count')

def validate_folder(folder,seconds,mixed=False):
    proof=json.loads((folder/'interaction-continuity.json').read_text())
    rows=[json.loads(line) for line in (folder/'interaction-edges.jsonl').read_text().splitlines()]
    (validate_mixed if mixed else validate)(proof,rows,seconds)
    return proof
