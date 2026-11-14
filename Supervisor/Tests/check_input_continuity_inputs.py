"""Copy actual artifacts and reject altered receipts without launching a process."""
import argparse
import copy
import json
from pathlib import Path
import shutil
import sys
import uuid
from input_continuity_inputs import digest, validate_inputs, validate_modes

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'))
from provenance import sources


def source_receipt(root):
    return sources(root)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--worker',required=True,type=Path)
    parser.add_argument('--author',required=True,type=Path)
    parser.add_argument('--source-root',type=Path,default=ROOT)
    args=parser.parse_args()
    current=source_receipt(args.source_root.resolve())
    out=ROOT/'Validation/Content/sdk-runtime/E/input-continuity-inputs'/uuid.uuid4().hex
    out.mkdir(parents=True);print(out,flush=True)
    report={'status':'failed','sources':source_receipt(ROOT),'validation_source_root':str(args.source_root.resolve()),'validation_sources':current,'cases':[]}
    try:
        report['original_inputs']=validate_inputs(args.worker,args.author,current)
        runner=(args.source_root/'Supervisor/Tests/run_native_input_host.py').read_text()
        parser_line=next(line for line in runner.splitlines() if line.startswith('parser=argparse.ArgumentParser();'))
        namespace={'argparse':argparse,'Path':Path}
        exec(parser_line.split(';args=parser.parse_args()')[0],namespace)
        old_guards='assert not args.interaction_drop_release'+runner.split('assert not args.interaction_drop_release',1)[1].split('policy=json.loads',1)[0]
        for mode in ['--compile-only','--trace-self-test','--injector-self-test','--diagnostic-worker']:
            mixed=namespace['parser'].parse_args(['--worker','worker','--author','author','--desktop','--background-injection','--interaction-seconds','1800',mode])
            exec(old_guards,{'args':mixed})
            try:validate_modes(mixed)
            except ValueError as error:reason=str(error)
            else:raise AssertionError('mixed mode accepted:'+mode)
            report['cases'].append({'case':'mixed'+mode,'passed':True,'old_predicate_accepted':True,'rejection':reason})
        worker=out/'worker';author=out/'author'
        shutil.copytree(args.worker,worker);shutil.copytree(args.author,author)
        validate_inputs(worker,author,current)
        report['cases'].append({'case':'actual-copies-positive','passed':True})
        original_author=json.loads((author/'report.json').read_text())
        original_worker=json.loads((worker/'report.json').read_text())
        original_metadata=(author/'author.json').read_bytes()
        mutations=[
            ('exporter-source','author','tools/sdk/experiments/StageEInputExport.cs','change'),
            ('compiler-source','author','Packages/com.spatialwallpaper.sdk/Editor/CilboxProgramCompiler.cs','change'),
            ('author-build-source','author','tools/sdk/build_input_author.py','change'),
            ('project-builder-source','author','tools/sdk/build_execution.py','change'),
            ('missing-author-dependency','author','tools/sdk/experiments/StageEInputRuntimeAuthor.cs','remove'),
            ('removed-author-dependency','author','Packages/com.spatialwallpaper.sdk/Editor/Removed.cs','add'),
            ('worker-build-source','worker','tools/sdk/build_worker.py','change'),
            ('missing-worker-dependency','worker','tools/sdk/experiments/StageCPipeline.cs','remove'),
            ('removed-worker-dependency','worker','Packages/com.spatialwallpaper.sdk/Runtime/Removed.cs','add'),
            ('metadata-file','metadata',None,None),
            ('canonical-build','build',None,None),
            ('author-status','author-status',None,None),
            ('worker-status','worker-status',None,None),
            ('diagnostic-worker','diagnostic',None,None),
        ]
        for name,role,path,operation in mutations:
            ar=copy.deepcopy(original_author);wr=copy.deepcopy(original_worker)
            (author/'author.json').write_bytes(original_metadata)
            if role in ['author','worker']:
                target=ar if role=='author' else wr
                if operation=='remove':target['sources'].pop(path)
                else:target['sources'][path]='0'*64
            elif role=='metadata':
                metadata=json.loads(original_metadata);metadata['fixture']='input-runtime'
                (author/'author.json').write_text(json.dumps(metadata))
            elif role=='build':
                ar['author']['build']='0'*64
                (author/'author.json').write_text(json.dumps(ar['author']))
            elif role=='author-status':ar['status']='failed'
            elif role=='worker-status':wr['status']='failed'
            elif role=='diagnostic':wr['release_candidate']=False
            (author/'report.json').write_text(json.dumps(ar));(worker/'report.json').write_text(json.dumps(wr))
            # The exact prior artifact predicate: one author source plus self-
            # reported metadata/package and Worker inventory. This is a test
            # oracle only; never used for production acceptance.
            old_accepts=(digest(author/'scene.spatial')==ar['package_sha256']
                         and ar['author'].get('fixture')=='input-continuity-14-v1'
                         and ar['sources'].get('tools/sdk/experiments/StageEInputContinuityAuthor.cs')==current.get('tools/sdk/experiments/StageEInputContinuityAuthor.cs')
                         and {p.relative_to(worker/'player').as_posix():digest(p) for p in (worker/'player').rglob('*') if p.is_file()}==wr['runtime_files']
                         and wr.get('release_candidate') is not False)
            try:validate_inputs(worker,author,current)
            except ValueError as error:reason=str(error)
            else:raise AssertionError('mutation accepted:'+name)
            if role!='diagnostic' and not old_accepts:raise AssertionError('old predicate did not reproduce:'+name)
            report['cases'].append({'case':name,'passed':True,'old_predicate_accepted':old_accepts,'rejection':reason})
        (author/'report.json').write_text(json.dumps(original_author));(worker/'report.json').write_text(json.dumps(original_worker));(author/'author.json').write_bytes(original_metadata)
        validate_inputs(worker,author,current)
        if source_receipt(args.source_root.resolve())!=current:raise AssertionError('validation source drift')
        report['status']='passed';return 0
    except Exception as error:
        report['error']=repr(error);print(error,file=sys.stderr);return 1
    finally:
        (out/'report.json').write_text(json.dumps(report,indent=2))


if __name__=='__main__':raise SystemExit(main())
