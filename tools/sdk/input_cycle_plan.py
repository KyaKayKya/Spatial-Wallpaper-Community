"""Pure frozen cycle catalog; imports no runner or registry."""
FAULT_CASES = (
    'desktop-manual-recovery', 'desktop-manual-active-recovery',
    'desktop-automatic', 'desktop-automatic-mouse', 'desktop-automatic-key',
    'desktop-automatic-cleanup', 'desktop-automatic-timeout-active',
    'desktop-automatic-timeout-pending', 'desktop-automatic-heartbeat-exit',
    'desktop-window-ready', 'desktop-window-mouse', 'desktop-window-close',
    'desktop-window-connecting', 'desktop-heartbeat-expiry',
    'desktop-heartbeat-lease-expiry', 'desktop-evidence-focus',
    'desktop-evidence-publish', 'desktop-expired-activation',
    'desktop-expiry-after-validation', 'desktop-cleanup-negative',
    'desktop-pending-move', 'desktop-pending-wheel', 'desktop-inflight-move',
    'desktop-inflight-wheel', 'desktop-queued-revoke',
    'owned-timeout-base', 'owned-timeout-unmapped', 'owned-timeout-pid-publication',
    'owned-timeout-creation', 'owned-timeout-creation-negative',
)


def require_fault_catalog(catalog):
    discovered = set()
    for case in catalog:
        args = case.args
        if case.id.startswith('owned-timeout-'):
            discovered.add(case.id)
        elif case.script == 'Supervisor/Tests/run_native_input_host.py':
            if any(arg.startswith('--negative-') for arg in args):
                if '--negative-cleanup-fault' in args:
                    discovered.add(case.id)
                continue
            recovery = '--recover-helper' in args or '--automatic-recovery' in args
            ordinary = any(flag in args for flag in ('--continuous-motion', '--continuous-unmapped',
                                                     '--soak-seconds', '--interaction-seconds'))
            pending = '--pending-interruption' in args
            inflight = '--inflight-reply' in args and args[args.index('--inflight-reply') + 1] in ('move', 'wheel')
            queued = '--queued-notification' in args and args[args.index('--queued-notification') + 1] == 'revoke'
            if recovery and not ordinary or pending or inflight or queued:
                discovered.add(case.id)
    need(discovered == set(FAULT_CASES), 'fault-catalog-closure-mismatch:' +
         repr({'missing': sorted(set(FAULT_CASES)-discovered), 'unmapped': sorted(discovered-set(FAULT_CASES))}))


def schedule(policy):
    plan = []
    def append(case, cycle, kind):
        plan.append({'id': f'{case}-{kind}-{cycle:03}', 'case': case,
                     'cycle': cycle, 'kind': kind})
    for cycle in range(1, policy['input']['start_stop_cycles'] + 1):
        append('desktop-base', cycle, 'normal')
    for case in FAULT_CASES:
        for cycle in range(1, policy['input']['fault_cycles_each'] + 1):
            if case.startswith('owned-timeout-'):
                append('desktop-base', cycle, 'prepare-' + case)
            append(case, cycle, 'fault')
    return plan


def need(condition, reason):
    if not condition:
        raise ValueError(reason)
