"""Fresh input lifecycle runs; no historical receipt import or repetition shortcut.

--list is read-only. Execution builds dependencies once, then creates a fresh
Host/Worker for every native cycle. This is one E gate, never an E verdict.
The native fixture must supply result.lifecycle from trusted Worker observation:
attempt, worker_pid, worker_started, worker_kernel_exited plus the production
SupervisedSession.StopEvidence fields. Stop RPC acknowledgement and
Host Exit/owner cleanup durations are not the Worker normal-stop measurement.
Forced exits additionally require same-clock timestamps and separate measured
termination/cleanup durations; see forced_timing_proof. Owner-kill fixtures use
forced_lifecycle with runner-monotonic-ms and client_kernel_exited_at_ms instead.
"""
import argparse
import math
import re
import time
import uuid
from dataclasses import asdict
from pathlib import Path

import verify_e as engine

from input_cycle_plan import FAULT_CASES, schedule, require_fault_catalog


def need(condition, reason):
    if not condition:
        raise ValueError(reason)


def owner_proof(row):
    need(row.get('status') == 'passed', 'case-failed')
    tree = row.get('process_tree', {})
    for key, expected in [('assignment_complete', True), ('handshake_released', True),
                          ('active_after_termination', 0), ('root_kernel_exited', True),
                          ('unexpected_remaining_members', False)]:
        need(type(tree.get(key)) is type(expected) and tree[key] == expected,
             'owner-unproven:' + key)
    need(not row.get('cleanup_error') and not tree.get('cleanup_errors'), 'owner-cleanup-error')


def forced_timing_proof(life, clock='host-monotonic-ms', exited_field='worker_kernel_exited_at_ms'):
    need(isinstance(life, dict), 'missing-forced-lifecycle')
    need(life.get('timing_clock') == clock, 'forced-timing-clock-unproven')
    fields = ('forced_started_at_ms', exited_field, 'cleanup_completed_at_ms',
              'forced_to_kernel_exit_ms', 'kernel_exit_to_cleanup_complete_ms')
    for field in fields:
        value = life.get(field)
        need(type(value) in (int, float) and math.isfinite(value) and value >= 0,
             'forced-timing-unmeasured:' + field)
    began, exited, completed = (life[field] for field in fields[:3])
    need(began <= exited <= completed, 'forced-phase-order')
    need(life.get('cleanup_completed') is True, 'forced-cleanup-completion-unproven')
    need(life['forced_to_kernel_exit_ms'] == exited - began,
         'forced-termination-duration-mismatch')
    need(life['kernel_exit_to_cleanup_complete_ms'] == completed - exited,
         'forced-cleanup-duration-mismatch')
    return {field: life[field] for field in ('timing_clock', 'cleanup_completed', *fields)}


def native_proof(data, policy, seen_attempts, cleanup_negative=False, expected_revision=3):
    result = data.get('result', {})
    life = result.get('lifecycle', {})
    attempt = life.get('attempt', '')
    need(isinstance(attempt, str) and re.fullmatch('[0-9a-f]{32}', attempt), 'missing-worker-attempt')
    need(attempt not in seen_attempts, 'reused-worker-attempt')
    need(type(life.get('worker_pid')) is int and life['worker_pid'] > 0, 'worker-pid')
    need(type(life.get('worker_creation_filetime')) is int and life['worker_creation_filetime'] > 0,
         'worker-creation-unknown')
    need(type(life.get('retained_worker_creation_filetime')) is int and
         life['retained_worker_creation_filetime'] == life['worker_creation_filetime'], 'worker-creation-mismatch')
    need(life.get('worker_started') is True and life.get('worker_kernel_exited') is True,
         'worker-start-or-kernel-exit-unproven')
    need(isinstance(life.get('stop_reason'), str) and bool(life['stop_reason']), 'missing-stop-reason')
    need(type(life.get('observed_exit_code')) is int and life['observed_exit_code'] >= 0,
         'missing-kernel-exit-code')
    if cleanup_negative:
        need(result.get('cleanup_fault_detected') is True, 'missing-cleanup-fault')
    if life.get('forced_stop') is True:
        need(cleanup_negative, 'normal-cycle-used-forced-stop')
        need(life.get('graceful_stop') is False and life.get('forced_stop') is True,
             'cleanup-negative-must-not-count-as-normal-stop')
        need(life.get('forced_alive_observed') is True, 'forced-alive-observation-unproven')
        forced_timing_proof(life)
        for field in ('stop_requested_at_ms', 'stop_published_at_ms'):
            need(field in life, 'missing-stop-observation:' + field)
            value = life[field]
            need(value is None or type(value) in (int, float) and math.isfinite(value) and value >= 0,
                 'invalid-stop-observation:' + field)
        requested, published = life['stop_requested_at_ms'], life['stop_published_at_ms']
        if requested is not None:
            need(requested <= life['forced_started_at_ms'], 'forced-before-stop-request')
        if published is not None:
            need(requested is not None and requested <= published <= life['worker_kernel_exited_at_ms'],
                 'forced-stop-publication-order')
    else:
        if not cleanup_negative:
            need(type(expected_revision) is int and expected_revision>0 and
                 type(result.get('author_durable_revision')) is int and result['author_durable_revision'] == expected_revision,
                 'author-effect-unproven')
            need(result.get('stop_exit') is True, 'normal-stop-unproven')
        need(life.get('graceful_stop') is True and life.get('forced_stop') is False,
             'forced-exit-is-not-normal-stop')
        need(life.get('timing_clock') == 'host-monotonic-ms' and life.get('cleanup_completed') is True,
             'normal-stop-clock-or-cleanup-unproven')
        for field in ('stop_requested_at_ms', 'stop_published_at_ms', 'worker_kernel_exited_at_ms',
                      'cleanup_completed_at_ms', 'kernel_exit_to_cleanup_complete_ms'):
            value = life.get(field)
            need(type(value) in (int, float) and math.isfinite(value) and value >= 0,
                 'normal-stop-time-unmeasured:' + field)
        requested, published, exited, cleaned = (life[field] for field in (
            'stop_requested_at_ms', 'stop_published_at_ms', 'worker_kernel_exited_at_ms', 'cleanup_completed_at_ms'))
        need(requested <= published <= exited <= cleaned, 'normal-stop-phase-order')
        need(life.get('forced_started_at_ms', 'missing') is None and
             life.get('forced_to_kernel_exit_ms', 'missing') is None, 'normal-stop-has-force-or-missing-observation')
        need(life['observed_exit_code'] == 0, 'normal-stop-nonzero-exit')
        duration = life.get('normal_stop_to_kernel_exit_ms')
        need(type(duration) in (int, float) and math.isfinite(duration) and
             0 <= duration <= policy['runtime']['normal_stop_grace_ms'], 'normal-stop-grace-exceeded-or-unmeasured')
        need(duration == exited-requested and life['kernel_exit_to_cleanup_complete_ms'] == cleaned-exited,
             'normal-stop-duration-mismatch')
    seen_attempts.add(attempt)
    return life


def timeout_proof(data, seen_injectors):
    for key in ('outer_job_assigned', 'client_kernel_exit_observed', 'without_outer_cleanup_held',
                'held_before_kill', 'native_released', 'outer_job_active_zero'):
        need(data.get(key) is True, 'owned-timeout-unproven:' + key)
    need(data.get('ledger_after_cleanup') == 0 and not data.get('cleanup_errors'), 'unreleased-input')
    identities = data.get('published_injector_identities', [])
    need(isinstance(identities, list) and bool(identities), 'missing-injector-identities')
    for identity in identities:
        need(isinstance(identity, list) and len(identity) == 2 and
             all(type(n) is int and n > 0 for n in identity), 'invalid-injector-identity')
        need(tuple(identity) not in seen_injectors, 'reused-injector-process')
    need(len(set(map(tuple, identities))) == len(identities), 'duplicate-injector-identity')
    timing = forced_timing_proof(data.get('forced_lifecycle'), 'runner-monotonic-ms',
                                'client_kernel_exited_at_ms')
    seen_injectors.update(map(tuple, identities))
    return {'forced_cleanup': True, 'normal_stop_claimed': False,
            'injector_identities': identities, 'native_released': True,
            'forced_lifecycle': timing}


def run_steps(plan, run, save):
    rows = []
    for step in plan:
        try:
            row = run(step)
        except BaseException as error:
            row = {'status': 'failed', 'error': repr(error)}
        rows.append(dict(row, step=step))
        save(rows)
        if row.get('status') != 'passed':
            break
    return rows


def bind_final_evidence(rows):
    for row in rows:
        artifact = row.get('artifact')
        if artifact:
            path = Path(artifact['path'])
            actual = ({path.name: engine.digest(path)} if artifact.get('inventory_scope') == 'report'
                      else engine.inventory(path.parent))
            need(actual == artifact['files'], 'prior-cycle-artifact-drift:' + str(path))
        if row.get('log'):
            need(engine.digest(row['log']) == row.get('log_sha256'), 'prior-cycle-log-drift')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--list', action='store_true')
    parser.add_argument('--worker',type=Path,help='Reuse a revalidated production binary; every cycle still starts a new process.')
    parser.add_argument('--render-probe',type=Path)
    args = parser.parse_args()
    if args.render_probe and not args.worker:parser.error('--render-probe requires the fixed --worker it observed')
    if args.render_probe:
        from sdk_environment import bind_render_probe
        bind_render_probe(args.render_probe,args.worker)
    policy_path = engine.ROOT/'tools/sdk/release-policy.json'
    policy = engine.read(policy_path)
    plan = schedule(policy)
    catalog = engine.with_fixed_worker(engine.registry(),args.worker)
    require_fault_catalog(catalog)
    by_id = {case.id: case for case in catalog}
    for step in plan:
        need(step['case'] in by_id, 'missing-case:' + step['case'])
    if args.list:
        import json
        print(json.dumps({'policy': policy, 'fault_cases': FAULT_CASES, 'plan': plan,
                          'scope': 'input-cycle-gate-not-E-verdict'}, indent=2))
        return 0
    output = engine.BASE/'E/input-cycles'/uuid.uuid4().hex
    output.mkdir(parents=True)
    receipt = engine.sources()
    environment = engine.collect_environment()
    engine.publish(output/'environment.json', environment)
    report = {'schema': 1, 'run_id': output.name, 'status': 'running', 'sources': receipt,
              'policy': policy, 'policy_sha256': engine.digest(policy_path),
              'started_at_unix': time.time(), 'plan': plan, 'steps': [], 'dependencies': [],
              'catalog': [asdict(by_id[name]) for name in dict.fromkeys(s['case'] for s in plan)],
              'environment_sha256': engine.digest(output/'environment.json'),
              'scope': 'input-cycle-gate-not-E-verdict'}
    artifacts, seen_attempts, seen_injectors, seen_reports, seen_workers = {}, set(), set(), set(), set()
    def save(rows=None):
        if rows is not None:
            report['steps'] = rows
        engine.publish(output/'report.json', report)
    def execute(step):
        case = by_id[step['case']]
        step_output = output/step['id']
        step_output.mkdir()
        row = engine.execute(case, artifacts, receipt, step_output,catalog=catalog)
        try:
            owner_proof(row)
            artifact = row['artifact']
            path = Path(artifact['path'])
            need(str(path.resolve()) not in seen_reports, 'reused-case-report')
            need(engine.inventory(path.parent) == artifact['files'], 'case-artifact-drift')
            data = engine.read(path)
            if step['case'].startswith('owned-timeout-'):
                row['cycle_proof'] = timeout_proof(data, seen_injectors)
            else:
                cleanup_negative = step['case'] == 'desktop-cleanup-negative'
                row['cycle_proof'] = native_proof(data, policy, seen_attempts, cleanup_negative)
                worker=(row['cycle_proof']['worker_pid'],row['cycle_proof']['worker_creation_filetime'])
                need(worker not in seen_workers,'reused-worker-process');seen_workers.add(worker)
                if cleanup_negative:
                    need('PASS author-storage-revision-3' in (path.parent/'client.log').read_text(),
                         'cleanup-negative-author-effect-unproven')
            seen_reports.add(str(path.resolve()))
            artifacts[case.id] = artifact
        except Exception as error:
            row.update(status='failed', cycle_error=repr(error))
        return row
    save()
    try:
        # Build once or revalidate the explicitly fixed binary once. No process,
        # attempt, author effect or lifecycle observation is reused.
        for case in engine.selection(catalog, ['worker', 'input-author']):
            row = engine.execute(case, artifacts, receipt, output,catalog=catalog)
            report['dependencies'].append(row)
            save()
            owner_proof(row)
            artifacts[case.id] = row['artifact']
        rows = run_steps(plan, execute, save)
        need(len(rows) == len(plan) and all(row['status'] == 'passed' for row in rows),
             'cycle-matrix-incomplete-first-failure-stopped')
        report['status'] = 'passed-input-cycle-gate-only'
    except BaseException as error:
        report.update(status='failed', error=repr(error))
    finally:
        report['finished_at_unix'] = time.time()
        try:
            bind_final_evidence(report['dependencies'] + report['steps'])
            report['final_evidence_unchanged'] = True
        except Exception as error:
            report.update(status='failed', final_evidence_unchanged=False,
                          final_evidence_error=repr(error))
        report['source_unchanged'] = engine.sources() == receipt
        final = engine.collect_environment()
        engine.publish(output/'environment-final.json', final)
        report['environment_final_sha256'] = engine.digest(output/'environment-final.json')
        report['configuration_identity_unchanged'] = final['configuration_identity'] == environment['configuration_identity']
        if not report['source_unchanged'] or not report['configuration_identity_unchanged']:
            report.update(status='failed', error='source-or-configuration-drift')
        save()
        if report['status']=='passed-input-cycle-gate-only':
            try:
                from input_cycle_evidence import validate_cycle_report
                validate_cycle_report(output/'report.json',receipt,report['started_at_unix'],catalog)
            except Exception as error:
                report.update(status='failed',complete_matrix_validation_error=repr(error));save()
        print(output, flush=True)
    return 0 if report['status'] == 'passed-input-cycle-gate-only' else 1


if __name__ == '__main__':
    raise SystemExit(main())
