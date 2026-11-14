"""Bind actual Client sidecars, retaining failures without creating observations."""
import hashlib
import json
import re


def attach(report, output, host_pid, host_creation_filetime=None):
    errors = []
    observed = {}
    for filename, key, hash_key in [('lifecycle.json', 'lifecycle_observation', 'lifecycle_sha256'),
                                     ('host-exit.json', 'host_exit_observation', 'host_exit_sha256')]:
        try:
            raw = (output/filename).read_bytes()
            report[hash_key] = hashlib.sha256(raw).hexdigest()
            value = json.loads(raw)
            if not isinstance(value, dict):
                raise ValueError('observation-is-not-object')
            report[key] = value
            observed[key] = value
        except (OSError, ValueError) as error:
            errors.append(filename + ':' + repr(error))
    life_record = observed.get('lifecycle_observation', {})
    life = life_record.get('lifecycle')
    if isinstance(life, dict):
        report.setdefault('result', {})['lifecycle'] = life
    else:
        life = {}
    def require(ok, reason):
        if not ok:
            errors.append(reason)
    require(life_record.get('status') == 'observed-terminal', 'Worker terminal observation incomplete')
    for key in ('instance', 'attempt'):
        require(isinstance(life_record.get(key), str) and re.fullmatch('[0-9a-f]{32}', life_record[key]),
                'invalid lifecycle ' + key)
    pid = life_record.get('worker_pid')
    require(type(pid) is int and pid > 0 and type(life.get('worker_pid')) is int and life['worker_pid'] == pid,
            'Worker PID mismatch')
    require(life.get('attempt') == life_record.get('attempt') and bool(life.get('attempt')), 'Worker attempt mismatch')
    creation = life_record.get('worker_creation_filetime')
    require(type(creation) is int and creation > 0, 'Worker creation unknown')
    for source in (life_record, life):
        for key in ('worker_creation_filetime', 'retained_worker_creation_filetime'):
            require(type(source.get(key)) is int and source[key] == creation, 'Worker creation mismatch:' + key)
    for key in ('worker_started', 'worker_kernel_exited', 'cleanup_completed'):
        require(life.get(key) is True, 'Worker unproven:' + key)
    host = observed.get('host_exit_observation', {})
    require(type(host.get('host_pid')) is int and host['host_pid'] == host_pid, 'Host PID mismatch')
    require(type(host_creation_filetime) is int and host_creation_filetime > 0, 'Host original creation unknown')
    for key in ('host_creation_filetime', 'retained_host_creation_filetime'):
        require(type(host.get(key)) is int and host[key] == host_creation_filetime, 'Host creation mismatch:' + key)
    require(host.get('status') == 'observed-kernel-exit', 'Host fallback is not observed exit')
    for key in ('retained_handle', 'exit_acknowledged', 'host_kernel_exited'):
        require(host.get(key) is True, 'Host unproven:' + key)
    if errors:
        raise ValueError('; '.join(errors))
