"""Collect explicit final runs and fresh core checks; never select an old GREEN."""
from pathlib import Path
import argparse,hashlib,json,os,platform,shutil,subprocess,sys
from verify_receipt import validate
root=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser();parser.add_argument('--runtime',required=True);parser.add_argument('--manager',required=True);parser.add_argument('--library',required=True);parser.add_argument('--lifecycle',required=True);parser.add_argument('--reopen',required=True);parser.add_argument('--pointer',required=True);parser.add_argument('--visibility',required=True);parser.add_argument('--cold');args=parser.parse_args()
receipt=validate();out=root/'Validation/Desktop/verified';out.mkdir(parents=True,exist_ok=True)
summary={'passed':False,'platform':platform.platform(),'runtime_run':args.runtime,'manager_run':args.manager,'library_run':args.library,'checks':[]}
source=root/'Validation/Desktop'/args.lifecycle;assert json.loads((source/'result.json').read_text())['passed']
shutil.copy2(source/'result.json',out/'lifecycle.json');summary['lifecycle_run']=args.lifecycle
shutil.copy2(root/'Validation/Desktop/splash-settings.json',out/'splash-settings.json')
for group,name in [('reopen',args.reopen),('pointer',args.pointer),('visibility',args.visibility)]:
    source=root/'Validation/Desktop'/name;result=json.loads((source/'result.json').read_text());assert result['passed'],name
    expected=hashlib.sha256(json.dumps(receipt,sort_keys=True).encode()).hexdigest()
    assert result.get('build_fingerprint')==expected,'Unbound or stale acceptance: '+name
    shutil.copy2(source/'result.json',out/(group+'.json'));summary[group+'_run']=name
    if group=='visibility':
        for artwork in ['limpool','showcase','pool','train','parking','minishotgame','rainycafe']:shutil.copy2(source/('native-'+artwork+'.png'),out/('native-'+artwork+'.png'))
if args.cold:
    cold=json.loads((root/'Validation/Desktop'/args.cold).read_text());assert len(set(cold['runs']))>=20
    expected=hashlib.sha256(json.dumps(receipt,sort_keys=True).encode()).hexdigest()
    for name in cold['runs']:
        result=json.loads((root/'Validation/Desktop'/name/'result.json').read_text());assert result['passed'] and result['cold_preview'] and result['build_fingerprint']==expected,name
    summary['cold_preview_runs']=cold['runs'];shutil.copy2(root/'Validation/Desktop'/args.cold,out/'cold-preview.json')
(out/'summary.json').write_text(json.dumps(summary,indent=2))
for group,name in [('runtime',args.runtime),('manager',args.manager)]:
    source=root/'Validation/Desktop'/name;result=json.loads((source/'result.json').read_text());assert result['passed'],name
    shutil.copy2(source/'result.json',out/(group+'.json'))
    if group=='manager':
        for file in ['manager-applied.png','manager-preview.png','manager-calibration.png','manager-settings-default.png','manager-vertical-fit.png','manager-vertical-compact.png','manager-vertical-fullscreen.png','vertical-measurements.jsonl','manager-horizontal-fit.png','manager-horizontal-compact.png','horizontal-measurements.jsonl','result.txt']:shutil.copy2(source/file,out/file)
library=root/'Validation/Library'/args.library/'result.txt';assert library.read_text().startswith('PASS');shutil.copy2(library,out/'library-result.txt')
shutil.copy2(library.parent/'limpool-fullscreen.png',out/'fullscreen-limpool.png')
for script in ['tools/desktop/check.py','tools/library/check.py','tools/integration/run_calibration_checks.py','tools/integration/run_core_checks.py']:
    run=subprocess.run([sys.executable,script],cwd=root,capture_output=True,text=True,encoding='utf-8-sig',errors='replace');text='\n'.join(line.rstrip() for line in (run.stdout+run.stderr).replace('\ufeff','').splitlines()).strip()+'\n'
    name=Path(script).parent.name+'-'+Path(script).stem+'.txt';(out/name).write_text(text,encoding='utf-8');summary['checks'].append({'command':script,'exit_code':run.returncode});assert run.returncode==0,text[-2000:]
main=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip()).parent
env=dict(os.environ,PYTHONPATH=str(root/'tracker/src'))
run=subprocess.run([str(main/'tracker/.venv/Scripts/python.exe'),'-m','pytest',str(root/'tracker/tests'),'-q'],cwd=root,env=env,capture_output=True,text=True,encoding='utf-8-sig',errors='replace')
(out/'tracker-tests.txt').write_text(run.stdout+run.stderr,encoding='utf-8');assert run.returncode==0,(run.stdout+run.stderr)[-3000:]
shutil.copy2(root/'Validation/Desktop/build-receipt.json',out/'build-receipt.json')
summary['tracker_pytest']=(run.stdout+run.stderr).strip();summary['build_receipt_sha256']=hashlib.sha256((out/'build-receipt.json').read_bytes()).hexdigest();summary['passed']=True
(out/'summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8');print(json.dumps(summary,ensure_ascii=False,indent=2))
