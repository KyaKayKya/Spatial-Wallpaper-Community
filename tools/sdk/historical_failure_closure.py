"""Audit reviewed historical records; fresh D--G acceptance remains separate.

The reviewer judges causality and the scope of a fix. This checker checks that
the record and its original evidence are complete and unchanged. Old evidence
does not need to be rewritten into the current E runner's output format.
"""
import argparse,hashlib,json,re,subprocess,uuid
from pathlib import Path,PurePosixPath
from provenance import ROOT,sources

CATALOG='tools/sdk/historical-failures.json'
CATEGORIES={'input-timeout','author-revision-loss','host-exit-timeout'}

def digest(data):return hashlib.sha256(data).hexdigest()
def need(value,reason):
    if not value:raise ValueError(reason)

def text(value):return isinstance(value,str) and bool(value.strip())

def reference(root,value):
    need(isinstance(value,dict) and text(value.get('scope')),'evidence-reference-or-scope')
    name=value.get('path');need(text(name),'evidence-path')
    relative=PurePosixPath(name)
    need(not relative.is_absolute() and '\\' not in name and ':' not in name and '..' not in relative.parts and relative.as_posix()==name,'evidence-path-escape')
    path=(root/relative).resolve();need(path.is_relative_to(root.resolve()) and path.is_file(),'evidence-missing-or-escape')
    need(digest(path.read_bytes())==value.get('sha256'),'evidence-hash-drift:'+name)
    return {'path':name,'sha256':value['sha256'],'scope':value['scope']}

def commit_exists(root,commit):
    result=subprocess.run(['git','cat-file','-e',commit+'^{commit}'],cwd=root,capture_output=True)
    return result.returncode==0

def reviewed_closure(root,incident):
    closure=incident.get('closure');need(isinstance(closure,dict),'missing-reviewed-closure')
    need(text(closure.get('root_cause')) and text(incident.get('scope')),'missing-root-cause-or-limited-scope')
    commit=closure.get('fix_commit');need(isinstance(commit,str) and re.fullmatch('[0-9a-f]{40}',commit) and commit_exists(root,commit),'missing-or-unknown-fix-commit')
    failures=closure.get('failure_evidence');need(isinstance(failures,list) and failures,'missing-original-failure-evidence')
    failures=[reference(root,value) for value in failures]
    runs=incident.get('historical_runs');need(isinstance(runs,list) and runs and all(text(run) for run in runs),'missing-original-runs')
    need(all(any(run in PurePosixPath(ref['path']).parts for ref in failures) for run in runs),'missing-original-run-reference')
    red,green,real=[reference(root,closure.get(key)) for key in ['red','green','real']]
    need(red['sha256']!=green['sha256'],'same-evidence-cannot-prove-failure-and-success')
    review=closure.get('review');need(isinstance(review,dict) and review.get('decision')=='accepted','missing-accepted-independent-review')
    need(text(review.get('implementer')) and text(review.get('reviewer')) and review['implementer']!=review['reviewer'],'missing-independent-reviewer')
    reviewed=reference(root,review.get('reference'))
    supporting=[reference(root,value) for value in closure.get('supporting_evidence',[])]
    return {'root_cause':closure['root_cause'],'fix_commit':commit,'failure_evidence':failures,'red':red,'green':green,'real':real,'review':reviewed,'supporting_evidence':supporting,'remaining':closure.get('remaining','')}

def evaluate_current(root,receipt):
    root=Path(root).resolve();result={'status':'blocked','scope':'historical reviewed closure only; never fresh E/F/G acceptance','incidents':[],'open_incidents':[],'errors':[]}
    try:
        path=root/CATALOG;raw=path.read_bytes()
        need(isinstance(receipt,dict) and receipt.get(CATALOG)==digest(raw),'catalog-source-receipt-missing-or-drift')
        catalog=json.loads(raw);need(catalog.get('schema')==1 and set(catalog.get('required_categories',[]))==CATEGORIES,'catalog-schema-or-required-categories')
        required=catalog.get('required_incidents');incidents=catalog.get('incidents')
        need(isinstance(required,list) and required and len(required)==len(set(required)),'required-incident-set')
        need(isinstance(incidents,list) and len(incidents)==len(required) and {entry['id'] for entry in incidents}==set(required),'incident-set-incomplete-or-duplicate')
        need(CATEGORIES<=set(entry.get('category') for entry in incidents),'missing-incident-category')
        result['catalog_sha256']=digest(raw)
        for incident in incidents:
            row={'id':incident['id'],'category':incident['category'],'scope':incident.get('scope'),'status':'open'}
            try:
                need(incident.get('status') in ['open','closed'],'invalid-incident-status')
                if incident['status']=='closed':row.update(status='closed',closure=reviewed_closure(root,incident))
                else:row['open_reason']=incident.get('open_reason') or 'Root cause or reviewed closure remains unresolved.'
            except (OSError,ValueError,KeyError,TypeError) as error:row['error']=str(error)
            if row['status']!='closed':result['open_incidents'].append(row['id'])
            result['incidents'].append(row)
        if not result['open_incidents']:result['status']='passed-historical-closure'
    except (OSError,ValueError,KeyError,TypeError) as error:result['errors'].append(str(error))
    return result

def main():
    argparse.ArgumentParser(description=__doc__).parse_args()
    out=ROOT/'Validation/Content/sdk-runtime/E/historical-failure-closure'/uuid.uuid4().hex;out.mkdir(parents=True)
    receipt=sources();report=evaluate_current(ROOT,receipt);report['sources']=receipt
    (out/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(out,flush=True)
    return 0 if report['status']=='passed-historical-closure' else 1

if __name__=='__main__':raise SystemExit(main())
