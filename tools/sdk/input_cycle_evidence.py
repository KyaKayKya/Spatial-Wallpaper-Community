"""Revalidate every fresh cycle artifact; summary flags never substitute for evidence."""
from dataclasses import asdict
import json
import math
from pathlib import Path
import re
import sys
import time

import verify_e as engine
from input_cycle_plan import FAULT_CASES, schedule, require_fault_catalog


def need(ok, reason):
    if not ok:
        raise ValueError(reason)


def equal(a, b):
    return json.dumps(a, sort_keys=True, separators=(',', ':'), allow_nan=False) == json.dumps(b, sort_keys=True, separators=(',', ':'), allow_nan=False)


def number(value):
    return type(value) in (int, float) and math.isfinite(value)


def validate_cycle_report(path, receipt, earliest, catalog=None):
    # Lazy import: registry imports only input_cycle_plan, never this runner.
    from input_cycles import owner_proof, native_proof, timeout_proof
    path=Path(path).resolve(); output=path.parent; report=engine.read(path)
    catalog=engine.registry() if catalog is None else catalog
    require_fault_catalog(catalog)
    byid={case.id:case for case in catalog}
    policy_path=engine.ROOT/'tools/sdk/release-policy.json'; policy=engine.read(policy_path)
    plan=schedule(policy)
    need(report.get('schema')==1 and type(report.get('schema')) is int, 'cycle-schema')
    need(report.get('status')=='passed-input-cycle-gate-only' and report.get('scope')=='input-cycle-gate-not-E-verdict','cycle-status')
    need(not any(key in report for key in ('error','final_evidence_error','complete_matrix_validation_error')),'cycle-recorded-failure')
    need(report.get('run_id')==output.name and re.fullmatch('[0-9a-f]{32}',output.name),'cycle-run-identity')
    need(equal(report.get('sources'),receipt),'cycle-source-drift')
    need(equal(report.get('policy'),policy) and report.get('policy_sha256')==engine.digest(policy_path),'cycle-policy')
    began,ended=report.get('started_at_unix'),report.get('finished_at_unix')
    need(number(began) and number(ended) and earliest<=began<=ended<=time.time()+1,'cycle-fresh-window')
    for key in ('source_unchanged','final_evidence_unchanged','configuration_identity_unchanged'):
        need(report.get(key) is True,'cycle-'+key)
    need(equal(report.get('plan'),plan),'cycle-plan')
    expected_catalog=[asdict(byid[name]) for name in dict.fromkeys(step['case'] for step in plan)]
    need(equal(report.get('catalog'),expected_catalog),'cycle-catalog-drift')
    before=output/'environment.json';after=output/'environment-final.json'
    need(engine.digest(before)==report.get('environment_sha256') and engine.digest(after)==report.get('environment_final_sha256'),'cycle-environment-digest')
    identity=engine.read(before)['configuration_identity']
    need(equal(identity,engine.read(after)['configuration_identity']),'cycle-environment-drift')
    dependencies=engine.selection(catalog,['worker','input-author'])
    dependency_rows=report.get('dependencies');rows=report.get('steps')
    need(isinstance(dependency_rows,list) and len(dependency_rows)==len(dependencies),'cycle-build-dependencies')
    need(isinstance(rows,list) and len(rows)==len(plan),'cycle-step-count')
    artifacts={};seen_reports=set();seen_attempts=set();seen_injectors=set();seen_workers=set();previous=began

    def check(case,row,step=None):
        nonlocal previous
        need(isinstance(row,dict) and row.get('id')==case.id,'cycle-case-id')
        owner_proof(row)
        need(not any(key in row for key in ('error','cycle_error','configuration_error','cleanup_error','final_output_error','log_binding_error')),'cycle-failed-row')
        start,finish=row.get('started_at_unix'),row.get('finished_at_unix')
        need(number(start) and number(finish) and previous<=start<=finish<=ended,'cycle-row-time-order')
        previous=finish
        need(equal(row.get('configuration_before'),identity) and equal(row.get('configuration_after'),identity),'cycle-row-configuration-drift')
        expected_inputs={dep:str(Path(artifacts[dep]['path']).parent) for dep in case.dependencies}
        need(equal(row.get('inputs'),expected_inputs),'cycle-upstream-binding')
        args=[arg.format_map(expected_inputs) for arg in case.args]
        need(row.get('command')==[sys.executable,str(engine.ROOT/case.script),*args],'cycle-command')
        need(row.get('script_sha256')==engine.digest(engine.ROOT/case.script),'cycle-script-drift')
        code=row.get('exit_code');need(type(code) is int and ((code!=0)==case.nonzero),'cycle-exit-code')
        log=(output/step['id'] if step else output)/(case.id+'.log')
        need(Path(row.get('log','')).resolve()==log and engine.digest(log)==row.get('log_sha256'),'cycle-log-binding')
        if case.negative:need(case.negative in log.read_text(encoding='utf-8'),'cycle-negative-oracle')
        artifact=row.get('artifact');need(isinstance(artifact,dict),'cycle-missing-artifact')
        nested=Path(artifact['path']).resolve()
        need(nested.parent.parent==(engine.BASE/case.folder).resolve(),'cycle-artifact-folder')
        need(nested not in seen_reports,'cycle-reused-artifact')
        # Re-run the exact registry contract, including worker/author inventories,
        # source receipt, native statuses and required failure oracles.
        validated=engine.validate_report(case,nested,engine.BASE,set(),start,receipt)
        need(equal(validated,artifact),'cycle-artifact-binding')
        seen_reports.add(nested)
        if step:
            need(equal(row.get('step'),step),'cycle-step-order-or-identity')
            data=engine.read(nested)
            if case.id.startswith('owned-timeout-'):
                prepared=Path(artifacts['desktop-base']['path'])
                need(equal(data.get('compiled_report'),engine.read(prepared)) and
                     data.get('client_sha256')==engine.digest(prepared.parent/'Client.exe'),'cycle-timeout-preparation-binding')
                observed=timeout_proof(data,seen_injectors)
            else:
                need(data.get('worker_report_sha256')==artifacts['worker']['sha256'] and
                     data.get('author_report_sha256')==artifacts['input-author']['sha256'],'cycle-worker-author-binding')
                observed=native_proof(data,policy,seen_attempts,case.id=='desktop-cleanup-negative')
                worker=(observed['worker_pid'],observed['worker_creation_filetime'])
                need(worker not in seen_workers,'cycle-reused-worker-process');seen_workers.add(worker)
                if case.id=='desktop-cleanup-negative':
                    need('PASS author-storage-revision-3' in (nested.parent/'client.log').read_text(),'cycle-cleanup-negative-author-effect')
            need(equal(row.get('cycle_proof'),observed),'cycle-proof-summary-mismatch')
        artifacts[case.id]=artifact

    for case,row in zip(dependencies,dependency_rows):check(case,row)
    for step,row in zip(plan,rows):check(byid[step['case']],row,step)
    return {'schema':1,'status':'validated-fresh-input-cycle-matrix','report_sha256':engine.digest(path),
            'policy_sha256':engine.digest(policy_path),'run_id':output.name,'steps':len(plan),
            'normal_cycles':policy['input']['start_stop_cycles'],
            'fault_cycles':{name:policy['input']['fault_cycles_each'] for name in FAULT_CASES},
            'preparations':sum(step['kind'].startswith('prepare-') for step in plan)}
