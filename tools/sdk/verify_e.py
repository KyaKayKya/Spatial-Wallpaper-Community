"""Execute the E case catalog. Missing release gates always fail closed.

--list is read-only. --case executes a dependency closure, still never passes E.
No historical artifact directories or diagnostic Workers are accepted as inputs.
"""
from dataclasses import asdict, dataclass, field, replace
from pathlib import Path
import argparse
import hashlib
import json
import platform
import sys
import time
import uuid
import zipfile

from provenance import ROOT, sources
from case_process import CaseProcess
from sdk_environment import collect as collect_environment
from sdk_environment import configuration_identity as collect_configuration
from configuration_coverage import coverage as configuration_coverage, COMPLETE as CONFIGURATION_COMPLETE

BASE = ROOT / 'Validation/Content/sdk-runtime'
UNIMPLEMENTED = {
    'supported-configuration-catalog': 'Candidate catalog requires exact observed identities and every current-run E case for every requested configuration; unresolved runtime profiles remain blocked.',
    'interaction-1800': 'The fresh continuity build, preflight, short/omission cases, eight key cleanups and acknowledged 1800-second case must all pass this execution.',
    'start-stop-100': 'This execution requires the complete fresh input-cycles matrix and revalidated normal lifecycle evidence.',
    'fault-cycles-20-each': 'This execution requires every frozen fault repeated with distinct processes and revalidated evidence.',
    'historical-failure-closure': 'Input timeout, author revision loss and Host exit failures require explicit linked closure evidence.',
}


@dataclass(frozen=True)
class Case:
    id: str
    script: str
    args: tuple = ()
    dependencies: tuple = ()
    folder: str = ''
    statuses: tuple = ()
    required: dict = field(default_factory=dict)
    resource: str = 'background'
    timeout: int = 240
    negative: str = ''
    nonzero: bool = False
    sources_required: bool = True
    role: str = ''
    result_cases: tuple = ()
    result_key: str = 'results'
    contains: dict = field(default_factory=dict)
    nonzero_fields: tuple = ()
    log_contains: dict = field(default_factory=dict)
    report_file: str = ''
    source_field: str = 'sources'
    output_files: tuple = ()


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def value_at(data, key):
    for part in key.split('.'):
        if not isinstance(data, dict) or part not in data:
            raise ValueError('required-field:' + key)
        data = data[part]
    return data


def inventory(path):
    return {p.relative_to(path).as_posix(): digest(p) for p in path.rglob('*') if p.is_file()}


def require_package_build(path, metadata):
    with zipfile.ZipFile(path) as archive:
        manifest = json.loads(archive.read('manifest.json'))
    build = manifest.pop('build_id')
    canonical = json.dumps(manifest,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
    if build != hashlib.sha256(canonical).hexdigest() or build != metadata.get('build'):
        raise ValueError('package-build')


def validate_report(case, path, base, before, began, receipt, upstream=None, catalog=None):
    path, base = path.resolve(), base.resolve()
    if not path.is_relative_to(base):
        raise ValueError('foreign-artifact')
    if path in before:
        raise ValueError('old-artifact')
    if path.stat().st_mtime < began:
        raise ValueError('stale-artifact')
    data = read(path)
    if case.statuses and data.get('status') not in case.statuses:
        raise ValueError('unexpected-status:' + str(data.get('status')))
    if (case.sources_required or case.source_field in data) and data.get(case.source_field) != receipt:
        raise ValueError('source-drift')
    for key, expected in case.required.items():
        actual = value_at(data, key)
        if type(actual) is not type(expected) or actual != expected:
            raise ValueError('required-field:' + key)
    for key, text in case.contains.items():
        if text not in str(value_at(data, key)):
            raise ValueError('required-field-text:' + key)
    for key in case.nonzero_fields:
        value = value_at(data, key)
        if type(value) is not int or value == 0:
            raise ValueError('required-nonzero:' + key)
    for name, expected in case.log_contains.items():
        log = (path.parent/name).resolve()
        if not log.is_relative_to(path.parent) or expected not in log.read_text(encoding='utf-8-sig',errors='replace'):
            raise ValueError('required-log-oracle:' + name)
    if case.result_cases:
        results = value_at(data, case.result_key)
        names = [r.get('case') for r in results] if isinstance(results,list) and all(isinstance(r,dict) for r in results) else []
        if len(names) != len(case.result_cases) or set(names) != set(case.result_cases):
            raise ValueError('result-case-set')
    for key in ('external_input_cleanup_error', 'host_cleanup_error'):
        if key in data:
            raise ValueError('cleanup-failure:' + key)
    if data.get('cleanupErrors'):
        raise ValueError('cleanup-failure')
    if case.role == 'audio-observation':
        from audio_evidence import validate_report as validate_audio_report
        validate_audio_report(case.id, data, path.parent, receipt, upstream)
    if case.role == 'audio-races':
        from audio_race_evidence import validate as validate_audio_races
        validate_audio_races(case.id, data, path.parent, receipt)
    if case.role == 'worker':
        if data.get('release_candidate') is False or not data.get('runtime_files') or inventory(path.parent/'player') != data.get('runtime_files'):
            raise ValueError('worker-inventory')
    if case.role == 'author':
        if digest(path.parent/'scene.spatial') != data.get('package_sha256') or read(path.parent/'author.json') != data.get('author'):
            raise ValueError('author-inventory')
        require_package_build(path.parent/'scene.spatial',data['author'])
    if case.role == 'matrix-authors':
        entries = data.get('authors', {})
        if set(entries) != {'1', '2', '3'}:
            raise ValueError('author-role-set')
        builds = []
        for key, entry in entries.items():
            if read(path.parent/key/'author.json') != entry['metadata'] or digest(path.parent/key/'scene.spatial') != entry['package_sha256']:
                raise ValueError('matrix-author-inventory')
            if entry['metadata'].get('role') != int(key):
                raise ValueError('matrix-author-role')
            require_package_build(path.parent/key/'scene.spatial',entry['metadata'])
            builds.append(entry['metadata']['build'])
        if len(set(builds)) != 3:
            raise ValueError('matrix-build-set')
    if case.role == 'continuity':
        sys.path.insert(0,str(ROOT/'Supervisor/Tests'))
        from input_continuity_evidence import validate_folder
        proof=validate_folder(path.parent,case.required['interaction_seconds'])
        if data.get('interaction_proof')!=proof or data.get('result',{}).get('author_durable_revision')!=proof['author_edges']:
            raise ValueError('continuity-proof-binding')
    # Legacy fixed reports share a directory with other stage reports that the
    # orchestrator updates. Bind only this report, not unrelated mutable siblings.
    artifact={'path': str(path), 'sha256': digest(path), 'status': data.get('status'),
            'inventory_scope': 'report' if case.report_file else 'directory',
            'files': {path.name:digest(path)} if case.report_file else inventory(path.parent)}
    if case.role == 'input-cycles':
        from input_cycle_evidence import validate_cycle_report
        artifact['cycle_matrix']=validate_cycle_report(path,receipt,began,catalog)
    return artifact


def require_case_set(cases, rows):
    names = [row['id'] for row in rows]
    if len(names) != len(set(names)) or set(names) != {c.id for c in cases}:
        raise ValueError('case-set-incomplete-or-duplicate')


def verdict(cases, rows, support_catalog=None, policy=None, receipt=None, run_started_at=None):
    require_case_set(cases, rows)
    complete_catalog={case.id for case in cases}=={case.id for case in registry()}
    from e_cases import INTERACTION_REQUIRED
    gates=dict(UNIMPLEMENTED)
    states={row['id']:row['status'] for row in rows}
    if all(states.get(name)=='passed' for name in INTERACTION_REQUIRED):
        gates.pop('interaction-1800')
    coverage=configuration_coverage(support_catalog,policy or {},[case.id for case in cases],rows)
    if coverage['status']==CONFIGURATION_COMPLETE:
        gates.pop('supported-configuration-catalog')
    cycle_validation=None
    cycle_case=next((case for case in cases if case.id=='input-cycles' and case.role=='input-cycles'),None)
    cycle_row=next((row for row in rows if row['id']=='input-cycles'),None)
    if cycle_case and cycle_row and cycle_row.get('status')=='passed' and receipt is not None and run_started_at is not None:
        try:
            from input_cycles import owner_proof
            owner_proof(cycle_row)
            if cycle_row.get('started_at_unix',0)<run_started_at:
                raise ValueError('cycle-row-before-current-execution')
            artifact=cycle_row['artifact']
            actual=validate_report(cycle_case,Path(artifact['path']),BASE,set(),cycle_row['started_at_unix'],receipt,catalog=cases if complete_catalog else None)
            if actual!=artifact:
                raise ValueError('cycle-gate-artifact-drift')
            cycle_validation=actual['cycle_matrix']
            gates.pop('start-stop-100');gates.pop('fault-cycles-20-each')
        except (OSError,ValueError,KeyError,TypeError) as error:
            cycle_validation={'status':'failed','error':repr(error)}
    historical={'status':'blocked','reason':'missing-current-source-receipt'}
    if receipt is not None:
        try:
            from historical_failure_closure import evaluate_current
            historical=evaluate_current(ROOT,receipt)
            if historical['status']=='passed-historical-closure':
                gates.pop('historical-failure-closure')
        except (OSError,ValueError,KeyError,TypeError,ImportError) as error:
            historical={'status':'blocked','error':repr(error)}
    failed=[r['id'] for r in rows if r['status'] != 'passed']
    if not complete_catalog:gates['complete-E-catalog']='A selected subset cannot certify E.'
    passed=not gates and not failed
    return {'status': 'passed-stage-E-capabilities-only' if passed else 'failed', 'unimplemented_gates': gates,
            'configuration_coverage': coverage,
            'input_cycle_validation': cycle_validation,
             'historical_failure_closure': historical,
            'failed_cases': failed,
            'reason': 'Complete current-run E catalog and all release gates validated; F/G remain separate.' if passed else 'E release gates are incomplete or cases failed; successful subcases are not E acceptance.'}


def publish(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix('.tmp')
    temp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    temp.replace(path)


def selection(cases, requested):
    by_id = {c.id: c for c in cases}
    wanted = set()
    def include(name):
        if name not in by_id:
            raise ValueError('unknown-case:' + name)
        if name in wanted:
            return
        wanted.add(name)
        for dependency in by_id[name].dependencies:
            include(dependency)
    for name in requested:
        include(name)
    return [c for c in cases if c.id in wanted] if requested else cases


def execute(case, artifacts, receipt, output, catalog=None):
    inputs = {}
    for dependency in case.dependencies:
        if dependency in artifacts:
            item = artifacts[dependency]
            actual = {Path(item['path']).name:digest(item['path'])} if item.get('inventory_scope')=='report' else inventory(Path(item['path']).parent)
            if actual != item['files']:
                raise ValueError('upstream-artifact-drift:' + dependency)
            inputs[dependency] = str(Path(item['path']).parent)
    args = [arg.format_map(inputs) for arg in case.args]
    command = [sys.executable, str(ROOT/case.script), *args]
    folder = BASE/case.folder if case.folder else None
    fixed = BASE/case.report_file if case.report_file else None
    previous_digest = digest(fixed) if fixed and fixed.exists() else None
    before = {p.resolve() for p in folder.glob('*/report.json')} if folder else set()
    before_directories = set(folder.glob('*')) if folder else set()
    began = time.time()
    row = {'id': case.id, 'command': command, 'started_at_unix': began,
           'status': 'failed', 'negative_oracle': case.negative, 'inputs': inputs,
           'script_sha256': digest(ROOT/case.script), 'resource': case.resource}
    row['configuration_before']=collect_configuration()
    log = output/(case.id + '.log')
    try:
        if sources() != receipt:
            raise ValueError('source-drift-before-run')
        with log.open('w', encoding='utf-8') as stream:
            owner = CaseProcess()
            try:
                owner.start(command[1], args, ROOT, stream)
                code = owner.wait(timeout=case.timeout)
            finally:
                try:
                    owner.close()
                except BaseException as error:
                    row.update(status='failed', cleanup_error=repr(error))
                row['process_tree'] = owner.evidence.copy()
            if row.get('cleanup_error'):
                raise RuntimeError('case-process-cleanup-failed')
            if row['process_tree'].get('unexpected_remaining_members'):
                raise RuntimeError('case-descendants-survived-runner')
        row['exit_code'] = code
        text = log.read_text(encoding='utf-8')
        if (code != 0) != case.nonzero:
            raise ValueError('unexpected-exit:' + str(code))
        if case.negative and case.negative not in text:
            raise ValueError('missing-negative-oracle:' + case.negative)
    except BaseException as error:
        row['error'] = repr(error)
        if isinstance(error, KeyboardInterrupt):
            row['interrupted'] = True
    finally:
        # The fixture normally releases its obligations. Repeat its owner-scoped
        # fallback even after parent timeout, before proceeding to another case.
        if case.folder in ('E/native-desktop-input-host', 'E/owned-input-timeout'):
            try:
                tree = row.get('process_tree', {})
                if tree.get('active_after_termination') != 0 or tree.get('root_kernel_exited') is not True:
                    row['outer_input_cleanup'] = 'release-skipped-owner-live-or-unproven'
                    raise RuntimeError('release-skipped-owner-live-or-unproven')
                sys.path.insert(0, str(ROOT/'Supervisor/Tests'))
                from owned_input_cleanup import cleanup
                for directory in set(folder.glob('*')) - before_directories:
                    if directory.is_dir():
                        cleanup(directory)
                row['outer_input_cleanup'] = True
            except BaseException as error:
                prior = row.get('cleanup_error')
                row.update(status='failed', cleanup_error=(prior + '; ' if prior else '') + repr(error))
    # Cleanup may append to external-input-cleanup.json. Validate and freeze
    # only after all owned mutations finish, never publish a pre-cleanup digest.
    try:
        if 'error' not in row and 'cleanup_error' not in row:
            if fixed:
                if digest(fixed) == previous_digest:
                    raise ValueError('old-artifact:fixed-report-not-replaced')
                row['artifact'] = validate_report(case, fixed, BASE, set(), began, receipt, upstream=artifacts,catalog=catalog)
            elif folder:
                candidates = {p.resolve() for p in folder.glob('*/report.json')} - before
                if len(candidates) != 1:
                    raise ValueError('artifact-count:' + str(len(candidates)))
                row['artifact'] = validate_report(case, candidates.pop(), BASE, before, began, receipt, upstream=artifacts,catalog=catalog)
            row['output_files'] = {}
            for name in case.output_files:
                path = BASE/name
                if path.stat().st_mtime < began:
                    raise ValueError('stale-output-file:' + name)
                row['output_files'][name] = digest(path)
            if sources() != receipt:
                raise ValueError('source-drift-after-run')
            row['status'] = 'passed'
    except BaseException as error:
        row.update(status='failed', error=repr(error))
        if isinstance(error, KeyboardInterrupt):
            row['interrupted'] = True
    finally:
        # Retain final cleanup receipts even when exit/report validation failed.
        try:
            row['final_outputs'] = []
            tree = row.get('process_tree', {})
            row['final_outputs_owner_exited'] = tree.get('active_after_termination') == 0 and tree.get('root_kernel_exited') is True
            if folder:
                for directory in sorted(set(folder.glob('*')) - before_directories):
                    if directory.is_dir():
                        row['final_outputs'].append({'path':str(directory), 'files':inventory(directory)})
            if fixed and fixed.exists():
                row['final_outputs'].append({'path':str(fixed), 'sha256':digest(fixed)})
        except BaseException as error:
            row.update(status='failed', final_output_error=repr(error))
            if isinstance(error, KeyboardInterrupt):
                row['interrupted'] = True
        row['configuration_after']=collect_configuration()
        if row['configuration_before']!=row['configuration_after']:
            row.update(status='failed',configuration_error='configuration changed across case execution')
        row['finished_at_unix'] = time.time()
        try:
            if log.exists():
                row.update(log=str(log), log_sha256=digest(log))
        except BaseException as error:
            # Keep the existing ownership/cleanup evidence even when the last
            # receipt cannot be read. Never replace this row with a generic error.
            row.update(status='failed', log=str(log), log_binding_error=repr(error))
            if isinstance(error, KeyboardInterrupt):
                row['interrupted'] = True
    return row


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--list', action='store_true')
    parser.add_argument('--case', action='append', default=[])
    parser.add_argument('--worker',type=Path,help='Revalidate this exact production Worker binary instead of rebuilding it.')
    parser.add_argument('--render-probe',type=Path,help='Real fixed-Worker profile observation; no support claim is inferred.')
    parser.add_argument('--fail-fast',action='store_true',help='Stop at the first failing case; the incomplete execution remains failed.')
    options = parser.parse_args()
    if options.render_probe and not options.worker:parser.error('--render-probe requires the fixed --worker it observed')
    cases = with_fixed_worker(registry(),options.worker)
    if options.render_probe:
        from sdk_environment import bind_render_probe
        bind_render_probe(options.render_probe,options.worker)
        cases=[replace(case,args=case.args+('--render-probe',str(options.render_probe.resolve()))) if case.id=='input-cycles' else case for case in cases]
    selected = selection(cases, options.case)
    if options.list:
        print(json.dumps({'cases': [asdict(c) for c in selected], 'unimplemented_gates': UNIMPLEMENTED}, indent=2))
        return 0
    output = BASE/'E/acceptance'/uuid.uuid4().hex
    output.mkdir(parents=True)
    receipt = sources()
    report = {'schema': 1, 'run_id': output.name, 'status': 'running', 'sources': receipt,
              'platform': platform.platform(), 'started_at_unix': time.time(),
              'policy': read(ROOT/'tools/sdk/release-policy.json'),
              'policy_sha256': digest(ROOT/'tools/sdk/release-policy.json'),
              'catalog': [asdict(c) for c in cases], 'steps': []}
    support_catalog=None
    try:
        support_path=ROOT/'tools/sdk/supported-configurations.json'
        support_catalog=read(support_path)
        report['support_catalog']={'path':str(support_path),'sha256':digest(support_path),'declaration':support_catalog,
                                   'support_claim':False}
    except (OSError,ValueError) as error:
        report['support_catalog']={'status':'unavailable','error':repr(error),'support_claim':False}
    environment=collect_environment()
    publish(output/'environment.json',environment)
    report['environment']={'path':str(output/'environment.json'),'sha256':digest(output/'environment.json'),
                           'configuration_identity':environment['configuration_identity'],
                           'configuration_complete':environment['configuration_complete']}
    target = BASE/'reports/stage-E.json'
    def save():
        publish(output/'report.json', report)
        publish(target, report)
    artifacts, states = {}, {}
    save()
    try:
        for case in cases:
            if case not in selected:
                row = {'id': case.id, 'status': 'not-run', 'reason': 'outside requested dependency closure'}
            elif any(states.get(d) != 'passed' for d in case.dependencies):
                row = {'id': case.id, 'status': 'blocked', 'reason': 'upstream case failed'}
            else:
                print('E case', case.id, flush=True)
                try:
                    row = execute(case, artifacts, receipt, output,catalog=cases)
                except BaseException as error:
                    # An exception escaping execute has no trustworthy cleanup
                    # proof. Stop before another case can acquire the desktop.
                    row = {'id': case.id, 'status': 'failed', 'error': repr(error),
                           'cleanup_error': 'execute-boundary-owner-cleanup-unproven'}
                    if isinstance(error, KeyboardInterrupt):
                        row['interrupted'] = True
            states[case.id] = row['status']
            if row['status'] == 'passed' and 'artifact' in row:
                artifacts[case.id] = row['artifact']
            report['steps'].append(row)
            save()
            if row.get('cleanup_error'):
                raise RuntimeError('cleanup-failed-stop-scheduling')
            if row.get('interrupted'):
                raise KeyboardInterrupt('case interrupted; owned tree drained')
            if options.fail_fast and row['status']=='failed':
                raise RuntimeError('first-failure-stopped-scheduling:'+case.id)
        report.update(verdict(cases, report['steps'],support_catalog,report['policy'],receipt,report['started_at_unix']))
    except BaseException as error:
        report.update(status='failed', error=repr(error), unimplemented_gates=UNIMPLEMENTED)
        recorded = {row['id'] for row in report['steps']}
        report['steps'].extend({'id':case.id,'status':'blocked','reason':'scheduler aborted'} for case in cases if case.id not in recorded)
    finally:
        report['configuration_coverage']=configuration_coverage(support_catalog,report['policy'],[case.id for case in cases],report['steps'])
        report['finished_at_unix'] = time.time()
        report['source_unchanged'] = sources() == receipt
        if not report['source_unchanged']:
            report.update(status='failed', error='source drift')
        final_environment=collect_environment()
        publish(output/'environment-final.json',final_environment)
        report['environment_final']={'path':str(output/'environment-final.json'),'sha256':digest(output/'environment-final.json')}
        report['configuration_identity_unchanged']=final_environment['configuration_identity']==environment['configuration_identity']
        if not report['configuration_identity_unchanged']:
            report.update(status='failed',configuration_error='observed OS/display/DPI identity changed during execution')
        save()
        print(output, flush=True)
    return 0 if report['status']=='passed-stage-E-capabilities-only' else 1


def registry():
    from e_cases import cases
    return cases(Case)


def with_fixed_worker(cases,worker):
    if worker is None:return cases
    worker=Path(worker).resolve()
    return [replace(case,script='tools/sdk/reuse_worker.py',args=('--worker',str(worker))) if case.id=='worker' else
            replace(case,args=case.args+('--worker',str(worker))) if case.id=='input-cycles' else case for case in cases]


if __name__ == '__main__':
    raise SystemExit(main())
