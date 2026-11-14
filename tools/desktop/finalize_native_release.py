"""Finalize this owned SDK 0.4 portable release by verified SHA-different overlays.

--prepare-only updates the directory and checksums, keeping its initial ZIP in
Archive. Run the two bounded release smoke tools against that prepared directory.
--seal embeds their exact passed reports and creates a new ZIP without changing
runtime bytes. Neither stage launches the product or changes user data.
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
import zipfile
from pathlib import Path
try:
    from . import assemble_native_release as assembly
except ImportError:
    import assemble_native_release as assembly

ROOT = Path(__file__).resolve().parents[2]
INITIAL_MANIFEST = 'b8b4f266159c5686f17082fea277a01b9b623a0975af66f8004bf8192c126365'
INITIAL_ZIP = '808e843279628f062ece908bb2d9bdf7850f780e073a7893a51aaf8053c1d63d'
VERSION = '0.4.0-preview.1'
PENDING = 'portable-preview-release-automated-smoke-pending'
SEALED = 'portable-preview-release-automated-smoke-passed-manual-experience-pending'
PLAYER_NAMES = {'SpatialWallpaper.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'MonoBleedingEdge', 'SpatialWallpaper_Data', 'D3D12', 'WinPixEventRuntime.dll', 'baselib.dll'}
PLAYER_EXCLUDED = {'sdk-runtime', 'native-runtime', 'native-content', 'Tracker', 'Python', '__pycache__'}
require, digest = assembly.require, assembly.digest


def unique(pairs):
    value = {}
    for key, item in pairs:
        require(key not in value, 'duplicate JSON key: ' + key)
        value[key] = item
    return value


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'), object_pairs_hook=unique,
                      parse_constant=lambda v: (_ for _ in ()).throw(ValueError('nonfinite JSON value')))


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + '\n').encode('utf-8')


def safe(path):
    path = Path(path).absolute()
    for item in (path, *path.parents):
        if item.exists():
            assembly.no_link(item)
    return path


def relative(name):
    value = assembly.safe_relative(name)
    require(name and all(p not in {'', '.', '..'} for p in name.split('/')) and all(ord(c) >= 32 and c not in '<>"|?*' for c in name), 'unsafe release path')
    return value


def read_sums(path):
    result = {}
    for line in Path(path).read_text(encoding='utf-8-sig').splitlines():
        sha, name = line.split('  ', 1)
        relative(name)
        require(name not in result and re.fullmatch(r'[0-9a-f]{64}', sha), 'invalid/duplicate checksum entry')
        result[name] = sha
    return result


def load_owned(release):
    release = safe(release).resolve()
    require(release.is_dir(), 'owned release directory missing')
    path = release / 'release-manifest.json'
    manifest = read(path)
    require(manifest.get('format') == 'spatial-wallpaper-portable-release' and manifest.get('version') == VERSION, 'wrong owned release identity')
    require(re.fullmatch(r'[0-9a-f]{40}', manifest.get('source_commit', '')), 'invalid release source SHA')
    initial = digest(path) == INITIAL_MANIFEST
    if not initial:
        require(manifest.get('initial_manifest_sha256') == INITIAL_MANIFEST, 'release is not this task initial lineage')
        backup = release / 'Provenance/InitialReleaseManifest.json'
        require(backup.is_file() and digest(backup) == INITIAL_MANIFEST, 'initial owned manifest anchor missing')
    files = manifest.get('files')
    require(isinstance(files, dict) and files, 'owned release inventory missing')
    for name, expected in files.items():
        relative(name)
        require(re.fullmatch(r'[0-9a-f]{64}', expected), 'invalid owned file SHA')
    require(read_sums(release / 'SHA256SUMS.txt') == {**files, 'release-manifest.json': digest(path)}, 'owned checksum list differs from manifest')
    return release, manifest, initial


class Overlay:
    """Only initial-owned exact bytes may be replaced; unknown files are retained."""
    def __init__(self, release, old):
        self.release, self.old = Path(release), dict(old)
        self.files, self.plan, self.changed, self.skipped = dict(old), {}, [], []

    def file(self, name, path, expected=None):
        relative(name)
        path = safe(path)
        require(path.is_file(), 'overlay source missing: ' + name)
        actual = digest(path)
        require(expected is None or expected == actual, 'overlay source digest differs: ' + name)
        self.plan[name] = (path, actual)
        self.files[name] = actual

    def data(self, name, data, stage):
        relative(name)
        path = Path(stage) / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        self.file(name, path)

    def preflight(self):
        # One verification pass of known old inputs. Never adopt or overwrite an
        # unrelated file merely because it happens to have a desired basename.
        for name, expected in self.old.items():
            path = safe(self.release / name)
            require(path.is_file() and digest(path) == expected, 'owned release drift: ' + name)
        for name, (_, expected) in self.plan.items():
            target = safe(self.release / name)
            require(not target.exists() or name in self.old, 'unknown existing file would be overwritten: ' + name)
            require(not target.exists() or target.is_file(), 'overlay target is not a file')
            (self.skipped if self.old.get(name) == expected else self.changed).append(name)

    def apply(self):
        for name in self.changed:
            source, expected = self.plan[name]
            target = safe(self.release / name)
            if target.exists():
                require(digest(target) == self.old[name], 'target changed after preflight: ' + name)
            require(digest(source) == expected, 'source changed after preflight: ' + name)
            target.parent.mkdir(parents=True, exist_ok=True)
            temporary = target.with_name(target.name + '.finalize.tmp')
            require(not temporary.exists(), 'unfinished overlay file requires review: ' + name)
            try:
                shutil.copyfile(source, temporary)
                require(digest(temporary) == expected, 'overlay copy digest differs: ' + name)
                os.replace(temporary, target)
            finally:
                if temporary.exists():
                    temporary.unlink()


def receipt(source, path, source_field='sources', allow_scenes=False, exact_commit=False):
    value = read(path)
    if 'source_commit' in value:
        require(value['source_commit'] == source.commit, 'component source commit differs: ' + str(path))
    elif exact_commit:
        raise ValueError('component source commit missing: ' + str(path))
    inputs = value.get(source_field)
    require(isinstance(inputs, dict) and inputs, 'component source receipt missing: ' + str(path))
    retained = []
    for raw, expected in inputs.items():
        name = raw.replace('\\', '/')
        if name in source.paths:
            if source.source_matches(name, expected):
                continue
            special = allow_scenes and name.endswith('.unity') or name == 'tools/sdk/experiments/build_unity.py'
            require(special and (source.root / name).is_file() and digest(source.root / name) == expected, 'component source blob differs: ' + name)
            retained.append({'path': name, 'sha256': expected, 'scope': 'preserved pre-existing protected scene' if name.endswith('.unity') else 'protected noncompiled helper'})
        elif Path(raw).is_absolute():
            require(Path(raw).name in {'AssemblyMetadata.cs', 'BuildIdentity.cs'} and Path(raw).is_file() and digest(Path(raw)) == expected, 'unknown generated component source: ' + raw)
        else:
            relative(name)
            require((source.root / name).is_file() and digest(source.root / name) == expected, 'retained local original source differs: ' + name)
    require(not value.get('mutated_sources_sha256') and not value.get('diagnostic_mutations'), 'diagnostic component cannot ship')
    return value, {'matched_source_commit': source.commit, 'method': 'exact frozen Git blobs, CRLF-equivalent materialization, and explicitly retained original asset inputs', 'retained_inputs': retained, 'receipt_has_source_commit': 'source_commit' in value}


def add_component(overlay, source, directory, names, destination, label, stage):
    value, match = receipt(source, directory / 'build.json')
    inventory = value.get('runtime_files', value.get('files', {}))
    for name in names:
        require(name in inventory, 'component file inventory missing: ' + name)
        overlay.file(destination + '/' + name, directory / name, inventory[name])
    overlay.data('Provenance/' + label + '.json', encoded({'receipt': value, 'source_match': match}), stage)
    return value


def archive_initial(release):
    portable = Path(str(release) + '.zip')
    archived = release.parent / 'Archive' / (release.name + '.initial-808e.zip')
    safe(archived)
    moved = False
    if portable.exists():
        require(not archived.exists() and digest(portable) == INITIAL_ZIP, 'existing ZIP is not the known initial archive or destination exists')
        checksum = Path(str(portable) + '.sha256')
        if checksum.exists():
            require(checksum.read_text(encoding='utf-8-sig').split()[0] == INITIAL_ZIP and not Path(str(archived) + '.sha256').exists(), 'initial ZIP checksum sidecar differs or archive sidecar exists')
        archived.parent.mkdir(parents=True, exist_ok=True)
        portable.rename(archived)
        moved = True
        if checksum.exists():
            checksum.rename(Path(str(archived) + '.sha256'))
    require(archived.is_file() and (moved or digest(archived) == INITIAL_ZIP), 'known initial ZIP archive missing or changed')
    return archived


def finish_manifest(release, manifest):
    path = release / 'release-manifest.json'
    temporary = release / 'release-manifest.json.finalize.tmp'
    require(not temporary.exists(), 'unfinished manifest requires review')
    temporary.write_bytes(encoded(manifest))
    os.replace(temporary, path)
    sums = {**manifest['files'], 'release-manifest.json': digest(path)}
    data = ''.join(sha + '  ' + name + '\n' for name, sha in sorted(sums.items())).encode('utf-8')
    temporary = release / 'SHA256SUMS.txt.finalize.tmp'
    require(not temporary.exists(), 'unfinished checksums require review')
    temporary.write_bytes(data)
    os.replace(temporary, release / 'SHA256SUMS.txt')


def prepared_components(args, source, release, old, overlay, stage):
    rules = source.json('tools/sdk/rules/native-scene-v1.json')
    native = {'sdk': rules['sdk_target'], 'schema': rules['schema_target'], 'scene_command_abi': rules['abi'], 'catalog_digest': hashlib.sha256(json.dumps(rules, ensure_ascii=True, sort_keys=True, separators=(',', ':')).encode()).hexdigest()}
    require(native == old['native'] and native['sdk'] == VERSION, 'native identity/catalog changed across finalization')
    player, player_match = receipt(source, args.player / 'build.json', allow_scenes=True, exact_commit=True)
    require(player.get('exit_code') == 0 and player.get('source_and_original_scene_unchanged') is True, 'new Player build failed or mutated assets')
    selected = {}
    for name, expected in player['files'].items():
        path = relative(name)
        if path.parts[0] in PLAYER_NAMES and not set(path.parts[1:]).intersection(PLAYER_EXCLUDED):
            overlay.file(name, args.player / name, expected)
            selected[name] = expected
    require('SpatialWallpaper.exe' in selected and 'SpatialWallpaper_Data/Managed/Assembly-CSharp.dll' in selected, 'new Player executable/managed runtime missing')
    missing = [name for name in old['files'] if relative(name).parts[0] in PLAYER_NAMES and not set(relative(name).parts[1:]).intersection(PLAYER_EXCLUDED) and name not in selected]
    require(not missing, 'new Player removed shipped files; explicit review required: ' + str(missing[:3]))
    overlay.data('Provenance/Player.json', encoded(player), stage)
    overlay.data('Provenance/Player.SourceMatch.json', encoded(player_match), stage)
    worker = add_component(overlay, source, args.native_worker, assembly.WORKER_FILES, 'native-runtime/worker', 'ScriptWorker', stage)
    require(worker['identity']['sdk'] == VERSION and worker['identity']['scene_catalog_digest'] == native['catalog_digest'] and str(worker['identity']['scene_command_abi']) == '1', 'new ScriptWorker native identity differs')
    add_component(overlay, source, args.native_host, assembly.HOST_FILES, 'native-runtime/supervisor', 'Supervisor', stage)
    for name in assembly.HOST_FILES:
        overlay.file('sdk-runtime/supervisor/' + name, args.native_host / name)
    validator = add_component(overlay, source, args.native_validator, assembly.VALIDATOR_FILES, 'Authoring/NativeValidator', 'NativeValidator', stage)
    require(validator.get('source_commit') == source.commit and all(validator['identity'].get(k) == v for k, v in native.items()), 'new native validator source/identity differs')
    index = read(args.platform_index)
    require(index.get('format') == 'spatial-native-platform-index' and index.get('version') == 1, 'invalid platform index')
    index_digest = digest(args.platform_index)
    if index_digest == old['files']['native-runtime/native-platform-library.json']:
        index_match = {'matched_source_commit': source.commit, 'sha256': index_digest, 'method': 'exact retained initial trusted platform-index bytes; no index or library re-export claim'}
    else:
        index_receipt, index_match = receipt(source, args.platform_index.parent / 'build.json', source_field='source_sha256', allow_scenes=True, exact_commit=True)
        require(index_receipt.get('exit_code') == 0 and index_receipt.get('source_and_protected_unchanged') is True, 'new platform-index export failed or drifted')
        records = {k.replace('\\', '/'): v for k, v in index_receipt.get('files_sha256', {}).items()}
        require(records.get(args.platform_index.name) == index_digest, 'platform index differs from export receipt')
        index_match = {'receipt': index_receipt, 'source_match': index_match}
    overlay.data('Provenance/PlatformIndex.json', encoded(index_match), stage)
    overlay.file('native-runtime/native-platform-library.json', args.platform_index)
    overlay.file('Authoring/NativeValidator/native-platform-library.json', args.platform_index)
    def subfiles(prefix):
        return {name[len(prefix):]: sha for name, sha in overlay.files.items() if name.startswith(prefix)}
    legacy = read(release / 'sdk-runtime/runtime.json')
    legacy['files'] = {k: v for k, v in subfiles('sdk-runtime/').items() if k != 'runtime.json'}
    overlay.data('sdk-runtime/runtime.json', encoded(legacy), stage)
    native_manifest = {**native, 'files': {k: v for k, v in subfiles('native-runtime/').items() if k != 'native-runtime.json'}, 'worker_files': {n: overlay.files['native-runtime/worker/' + n] for n in assembly.WORKER_FILES}}
    overlay.data('native-runtime/native-runtime.json', encoded(native_manifest), stage)
    validation_files = {k: v for k, v in subfiles('Authoring/NativeValidator/').items() if k != 'build.json'}
    overlay.data('Authoring/NativeValidator/build.json', encoded({'source_commit': source.commit, 'files': validation_files}), stage)
    export, export_match = receipt(source, args.packages / 'export-receipt.json', source_field='inputs', allow_scenes=True, exact_commit=True)
    require(export.get('exit_code') == 0 and export.get('inputs_unchanged') is True, 'native artwork export failed or drifted')
    overlay.data('Provenance/NativeArtworks.json', encoded({'receipt': export, 'source_match': export_match}), stage)
    packages = assembly.native_packages(source, args.packages, args.platform_index, args.native_validator / 'NativeValidator.exe', stage, native)
    for path in sorted((stage / 'native-content').rglob('*')):
        if path.is_file():
            overlay.file(path.relative_to(stage).as_posix(), path)
    for path in sorted((stage / 'SpatialWallpaper_Data/StreamingAssets/native-content').rglob('*')):
        if path.is_file():
            overlay.file(path.relative_to(stage).as_posix(), path)
    creator_receipt = args.creator_example.parent.parent / 'export-receipt.json'
    if not creator_receipt.exists():
        creator_receipt = args.creator_example.parent.parent / 'receipt.json'
    creator, creator_match = receipt(source, creator_receipt, exact_commit=True)
    require(creator.get('passed') is True and creator.get('unity_exit') == 0 and creator.get('validator_exit') == 0, 'creator export/admission failed')
    creator_name = args.creator_example.relative_to(creator_receipt.parent).as_posix()
    creator_files = {k.replace('\\', '/'): v for k, v in creator['files'].items()}
    require(creator_files.get(creator_name) == digest(args.creator_example), 'creator archive differs from export receipt')
    result = subprocess.run([str(args.native_validator / 'NativeValidator.exe'), '--package', str(args.creator_example), '--platform-index', str(args.platform_index)], capture_output=True, timeout=20, check=True,
                            env={k: v for k, v in os.environ.items() if k.upper() in {'SYSTEMROOT','WINDIR','PATH','TEMP','TMP','COMSPEC','PATHEXT','USERPROFILE','LOCALAPPDATA','PROCESSOR_ARCHITECTURE','NUMBER_OF_PROCESSORS'}})
    admitted = json.loads(result.stdout.decode('utf-8-sig'), object_pairs_hook=unique)
    require(admitted.get('valid') is True and admitted.get('policy') == 'native-schema5-external-data-v1' and admitted.get('license_share') is True and admitted.get('sha256') == digest(args.creator_example) and admitted.get('sdk') == VERSION and admitted.get('schema') == 5 and admitted.get('command_abi') == 1, 'creator example admission/version/share mismatch')
    overlay.file('Authoring/Examples/OriginalExternal/original-cc0-triangle.spatial', args.creator_example, admitted['sha256'])
    overlay.data('Authoring/Examples/OriginalExternal/admission.json', encoded(admitted), stage)
    overlay.data('Provenance/CreatorExample.json', encoded({'receipt': creator, 'source_match': creator_match}), stage)
    # Launcher, Python and Tracker are retained only when their frozen source
    # has not changed. No large runtime cache or models are copied again.
    old_source = assembly.FrozenSource(source.root, old['source_commit'])
    paths = [p for p in source.paths if p.startswith('tracker/src/') or p in {'tracker/pyproject.toml','tracker/uv.lock','tools/desktop/Launcher.cs','tools/desktop/build_launcher.py'}]
    for name in paths:
        require(name in old_source.paths and source.bytes(name) == old_source.bytes(name), 'retained Launcher/Tracker source changed; rebuild explicitly: ' + name)
    return native, packages


def prepare(args):
    release, old, initial = load_owned(args.release)
    require(initial or old['status'] == PENDING and old['source_commit'] == args.source_commit, 'only initial or same-freeze pending release may be prepared')
    source = assembly.FrozenSource(args.source_root, args.source_commit)
    for name in ('tools/desktop/finalize_native_release.py', 'tools/desktop/assemble_native_release.py'):
        require(source.source_matches(name, digest(Path(__file__) if name.endswith('/finalize_native_release.py') else Path(assembly.__file__))), 'executed assembly tool differs from frozen source')
    with tempfile.TemporaryDirectory(prefix='spatial-finalize-') as temporary:
        stage = Path(temporary)
        overlay = Overlay(release, old['files'])
        native, packages = prepared_components(args, source, release, old, overlay, stage)
        authoring = assembly.package_source(source, stage)
        for name in source.paths:
            if name.startswith('market/') and name.endswith(('.json','.md','.txt')) and not (stage / 'Authoring' / name).exists():
                source.write(name, stage / 'Authoring' / name)
            filename = Path(name).name.lower()
            if filename.startswith(('license','notice','copying','thirdpartynotice','third-party-notice')) and not name.endswith('.meta') and name.startswith(('Packages/','Prototype/Assets/','tracker/')):
                target = stage / 'Licenses/Source' / name
                source.write(name, target)
        cache = source.root / 'Prototype/Library/PackageCache/com.unity.nuget.newtonsoft-json@3.2.1'
        license_path = args.newtonsoft_license or cache / 'LICENSE.md'
        notices_path = args.newtonsoft_notices or cache / 'Third Party Notices.md'
        overlay.file('Licenses/Newtonsoft.Json/LICENSE.md', license_path)
        overlay.file('Licenses/Newtonsoft.Json/Third Party Notices.md', notices_path)
        for prefix in ('Authoring', 'Licenses'):
            for path in sorted((stage / prefix).rglob('*')):
                if path.is_file():
                    overlay.file(path.relative_to(stage).as_posix(), path)
        dependencies = read(release / 'DEPENDENCIES.json')
        dependencies['newtonsoft_notices'] = [{'path': n, 'sha256': overlay.files[n], 'status': 'preserved-upstream-text-not-a-rights-audit'} for n in ('Licenses/Newtonsoft.Json/LICENSE.md','Licenses/Newtonsoft.Json/Third Party Notices.md')]
        overlay.data('DEPENDENCIES.json', encoded(dependencies), stage)
        if initial:
            overlay.data('Provenance/InitialReleaseManifest.json', (release / 'release-manifest.json').read_bytes(), stage)
        finalization = {'source_commit': source.commit, 'old_source_commit': old['source_commit'], 'old_manifest_sha256': digest(release / 'release-manifest.json'), 'initial_manifest_sha256': INITIAL_MANIFEST, 'initial_zip_sha256': INITIAL_ZIP, 'finalization_tool_sha256': digest(Path(__file__)), 'assembly_source_sha256': hashlib.sha256(source.bytes('tools/desktop/assemble_native_release.py')).hexdigest(), 'retained_launch_tracker_python': True, 'scope': 'SHA-different owned release overlay; no visual/sound/input/display acceptance'}
        overlay.data('Provenance/Finalization.json', encoded(finalization), stage)
        overlay.preflight()
        archive = archive_initial(release)
        overlay.apply()
        manifest = {**old, 'source_commit': source.commit, 'assembly_tool_sha256': digest(Path(__file__)), 'initial_manifest_sha256': INITIAL_MANIFEST, 'native': native, 'authoring': authoring, 'packages': packages, 'status': PENDING, 'files': dict(sorted(overlay.files.items()))}
        manifest['finalization'] = {'initial_zip': str(archive), 'initial_zip_sha256': INITIAL_ZIP, 'changed_files': len(overlay.changed), 'unchanged_overlay_files': len(overlay.skipped), 'user_data': 'unknown files preserved in place, never added to the release inventory or ZIP'}
        finish_manifest(release, manifest)
        print(json.dumps({'prepared': True, 'release': str(release), 'source_commit': source.commit, 'manifest_sha256': digest(release / 'release-manifest.json'), 'changed_files': len(overlay.changed), 'skipped_files': len(overlay.skipped), 'zip_created': False}))


def check_report(path, release, manifest, manifest_sha, external=False):
    safe(path)
    raw, report = Path(path).read_bytes(), read(path)
    require(report.get('passed') is True and Path(report.get('release', '')).resolve() == release and report.get('source_commit') == manifest['source_commit'] and report.get('release_manifest_sha256') == manifest_sha and report.get('tested_critical_inputs_unchanged') is True, 'smoke report not bound to current prepared release')
    job = report.get('job', {})
    require(job.get('assigned_before_release') is True and job.get('forced_termination') is False and job.get('exit_code') == 0 and job.get('root_exit_code') == 0 and job.get('active_after_cleanup') == 0, 'smoke owned Job did not cleanly finish')
    result = report.get('native', {})
    require(result.get('passed') is True and len(result.get('cases', [])) == (1 if external else 5) and all(c.get('cleanly_closed') is True for c in result['cases']), 'smoke did not complete required native cases')
    expected = 'passed-final-external-native-import-render-retirement' if external else 'passed-five-final-native-packages-minimal-render-smoke'
    require(report.get('status') == expected and (not external or report.get('gpu_retirement', {}).get('passed') is True), 'wrong smoke status or external retirement missing')
    return raw, report


def zip_release(release, manifest):
    portable = Path(str(release) + '.zip')
    temporary = Path(str(portable) + '.finalize.tmp')
    require(not portable.exists() and not temporary.exists() and not Path(str(portable) + '.sha256').exists(), 'final ZIP or temporary/checksum already exists; never overwrite')
    expected = {**manifest['files'], 'release-manifest.json': digest(release / 'release-manifest.json'), 'SHA256SUMS.txt': digest(release / 'SHA256SUMS.txt')}
    try:
        with zipfile.ZipFile(temporary, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as archive:
            for name, sha in sorted(expected.items()):
                path = safe(release / name)
                require(path.is_file(), 'seal input missing: ' + name)
                info = zipfile.ZipInfo.from_file(path, release.name + '/' + name)
                info.compress_type = zipfile.ZIP_DEFLATED
                info._compresslevel = 6
                actual = hashlib.sha256()
                with path.open('rb') as source, archive.open(info, 'w', force_zip64=True) as output:
                    for block in iter(lambda: source.read(1024 * 1024), b''):
                        actual.update(block)
                        output.write(block)
                require(actual.hexdigest() == sha, 'seal input changed: ' + name)
        temporary.rename(portable)
    finally:
        if temporary.exists():
            temporary.unlink()
    sha = digest(portable)
    with Path(str(portable) + '.sha256').open('x', encoding='utf-8') as output:
        output.write(sha + '  ' + portable.name + '\n')
    return portable, sha


def seal(args):
    release, manifest, initial = load_owned(args.release)
    require(not initial and manifest['status'] == PENDING and manifest['source_commit'] == args.source_commit, 'seal requires the same prepared frozen release')
    portable = Path(str(release) + '.zip')
    require(not portable.exists() and not Path(str(portable) + '.finalize.tmp').exists() and not Path(str(portable) + '.sha256').exists(), 'seal ZIP destination is not fresh')
    manifest_sha = digest(release / 'release-manifest.json')
    native_raw, native = check_report(args.smoke_report, release, manifest, manifest_sha)
    external_raw, external = check_report(args.external_smoke_report, release, manifest, manifest_sha, True)
    # Recheck precisely the executables/components used by both actual smokes.
    critical = [n for n in manifest['files'] if n.endswith(('.exe','.dll','.config','.spatial')) or n.startswith('native-runtime/')]
    for name in critical:
        require(digest(safe(release / name)) == manifest['files'][name], 'tested executable/component changed after smoke: ' + name)
    with tempfile.TemporaryDirectory(prefix='spatial-seal-') as temporary:
        overlay = Overlay(release, manifest['files'])
        overlay.data('Provenance/Smoke.Native.json', native_raw, temporary)
        overlay.data('Provenance/Smoke.External.json', external_raw, temporary)
        for name in overlay.plan:
            require(not (release / name).exists(), 'embedded smoke evidence already exists')
        overlay.changed = list(overlay.plan)
        overlay.apply()
        manifest['files'] = dict(sorted(overlay.files.items()))
    manifest['automated_smoke'] = {'prepared_manifest_sha256': manifest_sha, 'native_report_sha256': hashlib.sha256(native_raw).hexdigest(), 'external_report_sha256': hashlib.sha256(external_raw).hexdigest(), 'scope': native.get('scope'), 'not_verified': sorted(set(native.get('not_verified', []) + external.get('not_verified', [])))}
    manifest['status'] = SEALED
    finish_manifest(release, manifest)
    portable, sha = zip_release(release, manifest)
    print(json.dumps({'sealed': True, 'launch': str(release / 'Launch.exe'), 'portable': str(portable), 'sha256': sha, 'manifest_sha256': digest(release / 'release-manifest.json'), 'manual_experience_pending': True}))


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--release', type=Path, required=True)
    p.add_argument('--source-commit', required=True)
    p.add_argument('--source-root', type=Path, default=ROOT)
    mode = p.add_mutually_exclusive_group(required=True)
    mode.add_argument('--prepare-only', action='store_true')
    mode.add_argument('--seal', action='store_true')
    for name in ('player','native-worker','native-host','native-validator','platform-index','packages','creator-example','newtonsoft-license','newtonsoft-notices','smoke-report','external-smoke-report'):
        p.add_argument('--' + name, type=Path)
    args = p.parse_args()
    require(re.fullmatch(r'[0-9a-f]{40}', args.source_commit), 'full frozen source SHA required')
    needed = ('smoke_report','external_smoke_report') if args.seal else ('player','native_worker','native_host','native_validator','platform_index','packages','creator_example')
    for name in needed:
        require(getattr(args, name) is not None, 'required for this stage: --' + name.replace('_','-'))
    for name in ('release', *needed, 'source_root'):
        setattr(args, name, safe(getattr(args, name)).resolve())
    (seal if args.seal else prepare)(args)


if __name__ == '__main__':
    main()
