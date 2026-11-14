"""Read-only preflight for dedicated continuity artifacts and standalone modes."""
import hashlib
import json
import zipfile

AUTHOR_PATHS = {
    'tools/sdk/build_input_author.py', 'tools/sdk/build_execution.py',
    'tools/sdk/experiments/StageEInputAuthor.cs',
    'tools/sdk/experiments/StageEInputRuntimeAuthor.cs',
    'tools/sdk/experiments/StageEInputContinuityAuthor.cs',
    'tools/sdk/experiments/StageEInputExport.cs',
}
WORKER_PATHS = {
    'tools/sdk/build_worker.py', 'tools/sdk/build_execution.py',
    'tools/sdk/experiments/SdkWorkerBuild.cs',
    'tools/sdk/experiments/StageCPipeline.cs',
}


def need(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def dependencies(receipt, role, author_paths=None):
    paths = (AUTHOR_PATHS if author_paths is None else author_paths) if role == 'author' else WORKER_PATHS
    prefix = 'Packages/com.spatialwallpaper.sdk/' if role == 'author' else 'Packages/com.spatialwallpaper.sdk/Runtime/'
    return {key: value for key, value in receipt.items() if key in paths or key.startswith(prefix)}


def validate_dependencies(recorded, current, role, author_paths=None):
    paths = (AUTHOR_PATHS if author_paths is None else author_paths) if role == 'author' else WORKER_PATHS
    need(paths <= current.keys(), role + '-current-dependency-set-incomplete')
    expected = dependencies(current, role, author_paths)
    need(len(expected) > len(paths), role + '-current-sdk-dependencies-missing')
    need(dependencies(recorded, role, author_paths) == expected, role + '-source-dependency-drift')


def validate_modes(args):
    values = vars(args)
    seconds = values.get('interaction_seconds', 0)
    drop = values.get('interaction_drop_release', False)
    mixed = values.get('interaction_mixed_motion', False)
    if not seconds and not drop and not mixed:
        return
    need(type(seconds) is int and 1 <= seconds <= 1800, 'continuity-duration-bound')
    need(values.get('desktop') is True and values.get('background_injection') is True, 'continuity-desktop-background-required')
    allowed = {'worker', 'author', 'desktop', 'background_injection', 'interaction_seconds', 'interaction_drop_release', 'interaction_mixed_motion'}
    forbidden = sorted(key for key, value in values.items() if key not in allowed and value)
    need(not forbidden, 'continuity-standalone-mode:' + ','.join(forbidden))
    need(not drop or seconds < 1800, 'continuity-negative-is-short-only')


def validate_inputs(worker, author, current):
    wr = json.loads((worker/'report.json').read_text())
    ar = json.loads((author/'report.json').read_text())
    need(ar.get('status') == 'partial-input-CSharp-export-only', 'continuity-author-build-status')
    need(wr.get('status') == 'production-Worker-built-not-acceptance' and wr.get('release_candidate') is not False, 'continuity-production-worker-required')
    validate_dependencies(ar.get('sources', {}), current, 'author')
    validate_dependencies(wr.get('sources', {}), current, 'worker')
    metadata = json.loads((author/'author.json').read_text(encoding='utf-8-sig'))
    need(metadata == ar.get('author'), 'continuity-author-metadata-drift')
    need(metadata.get('fixture') == 'input-continuity-14-v1', 'continuity-dedicated-author-required')
    need(metadata.get('capabilities') == ['input', 'private-storage'], 'continuity-author-capabilities')
    package = author/'scene.spatial'
    need(digest(package) == ar.get('package_sha256'), 'continuity-author-package-drift')
    with zipfile.ZipFile(package) as archive:
        manifest = json.loads(archive.read('manifest.json'))
    build = manifest.pop('build_id')
    canonical = json.dumps(manifest, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()
    need(build == hashlib.sha256(canonical).hexdigest() and metadata.get('build') == build, 'continuity-author-build-id-drift')
    inventory = {path.relative_to(worker/'player').as_posix(): digest(path)
                 for path in (worker/'player').rglob('*') if path.is_file()}
    need(bool(inventory) and inventory == wr.get('runtime_files'), 'continuity-worker-inventory-drift')
    return {'author_dependencies': dependencies(current, 'author'),
            'worker_dependencies': dependencies(current, 'worker'),
            'author_report_sha256': digest(author/'report.json'),
            'author_metadata_sha256': digest(author/'author.json'),
            'package_sha256': digest(package), 'build_id': build,
            'worker_report_sha256': digest(worker/'report.json'), 'worker_runtime_files': inventory}
