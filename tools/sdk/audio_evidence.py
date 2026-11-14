"""Strict audio receipts. Controlled faults never stand in for device events."""
import json
import math
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'Supervisor/Tests'))
from input_continuity_inputs import digest, need, validate_dependencies, dependencies

AUTHOR_PATHS = {'tools/sdk/build_audio_author.py', 'tools/sdk/build_execution.py',
                'tools/sdk/experiments/StageEAudioAuthor.cs',
                'tools/sdk/experiments/StageEAudioExport.cs',
                'tools/sdk/experiments/StageEAudioPlay.cs'}


def exact(data, key, expected):
    actual = data.get(key)
    need(type(actual) is type(expected) and actual == expected, 'audio-field:' + key)


def validate_report(case_id, data, folder, current, upstream=None):
    for key in ('failure', 'error', 'primary_failure'):
        need(key not in data, 'audio-failure-field:' + key)
    validate_observation(case_id, data, folder)
    if case_id == 'audio-lifecycle':
        return
    identity = data.get('samplerIdentity')
    keys = {'host_pid', 'host_creation_filetime', 'pid', 'creation_filetime'}
    need(isinstance(identity, dict) and set(identity) == keys and
         all(type(identity[key]) is int and identity[key] > 0 for key in keys), 'audio-sampler-identity')
    need(identity['host_pid'] != identity['pid'] and identity['host_creation_filetime'] <= identity['creation_filetime'],
         'audio-sampler-identity-order')
    exact(data.get('samplerObservation', {}), 'pid', identity['pid'])
    exact(data, 'negativeStabilityVerdict', False)
    binding = data.get('input_binding')
    need(isinstance(binding, dict), 'audio-input-binding-required')
    for key in ('worker_report_sha256', 'author_report_sha256', 'author_metadata_sha256', 'package_sha256', 'build_id'):
        value = binding.get(key)
        need(type(value) is str and len(value) == 64 and all(c in '0123456789abcdef' for c in value), 'audio-binding-digest:' + key)
    for key, top in {'worker_report_sha256':'worker_report_sha256', 'author_report_sha256':'author_report_sha256',
                     'author_metadata_sha256':'author_metadata_sha256',
                     'package_sha256':'author_package_sha256', 'build_id':'author_build_id'}.items():
        exact(data, top, binding[key])
    for key, role, paths in [('worker_dependencies', 'worker', None), ('author_dependencies', 'author', AUTHOR_PATHS)]:
        recorded = binding.get(key)
        need(isinstance(recorded, dict), 'audio-binding-dependencies:' + key)
        validate_dependencies(recorded, current, role, paths)
        need(recorded == dependencies(current, role, paths), 'audio-binding-extra-dependency:' + key)
    inventory = binding.get('worker_runtime_files')
    need(isinstance(inventory, dict) and bool(inventory), 'audio-binding-worker-inventory')
    for key, value in inventory.items():
        need(type(key) is str and bool(key) and type(value) is str and len(value) == 64 and
             all(c in '0123456789abcdef' for c in value), 'audio-binding-worker-file')
    validate_upstream_binding(data, current, upstream)


def validate_upstream_binding(data, current, upstream):
    """Compare the receipt with artifacts owned by this execution, after it exits."""
    from verify_e import inventory
    need(isinstance(upstream, dict), 'audio-current-upstream-required')
    folders = {}
    for name in ('worker', 'audio-author'):
        item = upstream.get(name)
        need(isinstance(item, dict) and item.get('inventory_scope') == 'directory',
             'audio-current-upstream-required:' + name)
        path = Path(item['path'])
        need(path.name == 'report.json' and digest(path) == item.get('sha256') and
             inventory(path.parent) == item.get('files'), 'audio-upstream-artifact-drift:' + name)
        folders[name] = path.parent
    _, _, expected = validate_inputs(folders['worker'], folders['audio-author'], current)
    need(data['input_binding'] == expected, 'audio-upstream-binding')


def validate_observation(case_id, data, folder):
    """Behavior fields alone; callers must not label this a complete new receipt."""
    if case_id == 'audio-lifecycle':
        exact(data, 'childrenExited', True)
        exact(data, 'samplersExited', True)
        rows = data.get('cases')
        need(isinstance(rows, list) and len(rows) == 2 and all(isinstance(row, dict) for row in rows), 'audio-lifecycle-cases')
        need({row.get('mode') for row in rows} == {'dispose', 'crash'}, 'audio-lifecycle-modes')
        for row in rows:
            exact(row, 'samplerExited', True)
            exact(row, 'sourceStillAlive', True)
            need(type(row.get('samplerPid')) is int and row['samplerPid'] > 0, 'audio-lifecycle-pid')
            elapsed = row.get('ownerStopToSamplerExitObservedMs')
            need(type(elapsed) in (float, int) and math.isfinite(elapsed) and elapsed >= 0, 'audio-lifecycle-time')
        return
    mode = case_id.removeprefix('audio-host-')
    need(mode in ('direct', 'tree', 'pause'), 'audio-host-mode')
    boundary = 'sampler-pause' if mode == 'pause' else 'source-exit'
    for key, expected in dict(childrenExited=True, samplerExited=True, cleanupErrors=[],
                              treeSource=mode == 'tree', boundary=boundary, negativeNoPause=False).items():
        exact(data, key, expected)
    result = data.get('result')
    need(isinstance(result, dict), 'audio-host-result')
    need(json.loads((folder / 'result.json').read_text(encoding='utf-8-sig')) == result, 'audio-host-result-drift')
    for key, expected in dict(configured_independent_host=True, boundary=boundary,
                              author_heard_before_boundary=True, revision=3,
                              stable_zero_author_updates=120, author_observed_nonzero_then_zero=True,
                              stop_exit=True).items():
        exact(result, key, expected)
    if mode == 'tree':
        exact(data, 'sourceChildAliveAfterAuthorCleared', True)
        exact(data, 'sourceChildExited', True)
    if mode == 'pause':
        exact(data, 'samplerFallbackUsed', False)
        exact(data, 'sourceAndHostAliveAfterSamplerExitAndAuthorClear', True)
        observation = data.get('samplerObservation')
        need(isinstance(observation, dict), 'audio-pause-observation')
        for key, expected in dict(authorRevisionImmediatelyBeforeBoundary=1, pauseInjected=True,
                                  exitedBeforeFallback=True, sourceAliveAtExitCheck=True,
                                  hostAliveAtExitCheck=True).items():
            exact(observation, key, expected)


def validate_inputs(worker, author, current, stable=True, negative=False):
    # Reuse the catalog's canonical package identity and continuity's strict
    # filtered-set comparison, including deleted dependencies.
    from verify_e import require_package_build
    wr = json.loads((worker / 'report.json').read_text(encoding='utf-8-sig'))
    ar = json.loads((author / 'report.json').read_text(encoding='utf-8-sig'))
    exact(wr, 'status', 'production-Worker-built-not-acceptance')
    need(wr.get('release_candidate') is not False, 'audio-production-worker-required')
    exact(ar, 'status', 'partial-audio-CSharp-Play-only')
    exact(ar, 'stableZeroPackage', stable)
    exact(ar, 'negativeStabilityVerdictPackage', negative)
    validate_dependencies(wr.get('sources', {}), current, 'worker')
    validate_dependencies(ar.get('sources', {}), current, 'author', AUTHOR_PATHS)
    metadata = json.loads((author / 'author.json').read_text(encoding='utf-8-sig'))
    need(metadata == ar.get('author'), 'audio-author-metadata-drift')
    package = author / 'scene.spatial'
    need(digest(package) == ar.get('package_sha256'), 'audio-author-package-drift')
    require_package_build(package, metadata)
    inventory = {path.relative_to(worker / 'player').as_posix(): digest(path)
                 for path in (worker / 'player').rglob('*') if path.is_file()}
    need(bool(inventory) and inventory == wr.get('runtime_files'), 'audio-worker-inventory-drift')
    return wr, ar, dict(worker_report_sha256=digest(worker / 'report.json'),
                        author_report_sha256=digest(author / 'report.json'),
                        author_metadata_sha256=digest(author / 'author.json'),
                        package_sha256=digest(package), build_id=metadata['build'],
                        worker_dependencies=dependencies(current, 'worker'),
                        author_dependencies=dependencies(current, 'author', AUTHOR_PATHS),
                        worker_runtime_files=inventory)
