"""Assemble one fresh, standalone Windows portable release from frozen inputs.

This tool never launches a camera, injects input, mutates an existing build, or
claims visual/audio acceptance. Assembly requires Python 3.9+ and packaging.
Ordinary users need neither Python nor Unity installed on their computer.
"""
import argparse
import ast
import csv
import email.parser
import hashlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import zipfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[2]
WORKER_FILES = ('ScriptWorker.exe', 'ScriptWorker.exe.config', 'Newtonsoft.Json.dll')
HOST_FILES = ('Supervisor.exe', 'Supervisor.exe.config', 'Newtonsoft.Json.dll', 'AudioSampler.exe')
VALIDATOR_FILES = ('NativeValidator.exe', 'NativeValidator.exe.config', 'Newtonsoft.Json.dll')
SKIP_PARTS = {'__pycache__', '.git', 'tests', 'test', 'benchmarks', 'ensurepip'}


def require(value, reason):
    if not value:
        raise ValueError(reason)


def digest(path):
    sha = hashlib.sha256()
    with Path(path).open('rb') as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b''):
            sha.update(chunk)
    return sha.hexdigest()


def dump(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def inventory(root, exclude=()):
    return {p.relative_to(root).as_posix(): digest(p) for p in sorted(Path(root).rglob('*'))
            if p.is_file() and p.relative_to(root).as_posix() not in exclude}


def safe_relative(name):
    value = PurePosixPath(name)
    require(value.as_posix() == name and not value.is_absolute() and not {'..', '.'}.intersection(value.parts)
            and '\\' not in name and ':' not in name, 'unsafe inventory path: ' + name)
    return value


def no_link(path):
    path = Path(path)
    require(not path.is_symlink() and not (hasattr(path, 'is_junction') and path.is_junction()), 'linked input refused: ' + str(path))
    if hasattr(path.stat(), 'st_file_attributes'):
        require(not path.stat().st_file_attributes & 0x400, 'reparse input refused: ' + str(path))


def copy_file(source, target, expected=None):
    source, target = Path(source), Path(target)
    no_link(source)
    before = digest(source)
    require(expected is None or before == expected, 'source digest differs: ' + str(source))
    if target.exists():
        require(target.is_file() and digest(target) == before, 'refuse changing existing release file: ' + str(target))
        return before
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)
    require(digest(target) == before and digest(source) == before, 'source changed while assembling: ' + str(source))
    return before


def copy_tree(source, target, excluded=()):
    source, target = Path(source), Path(target)
    no_link(source)
    for path in sorted(source.rglob('*')):
        relative = path.relative_to(source)
        if set(relative.parts).intersection(excluded):
            continue
        no_link(path)
        if path.is_file():
            copy_file(path, target / relative)


class FrozenSource:
    def __init__(self, root, commit):
        self.root = Path(root).resolve()
        require(re.fullmatch(r'[0-9a-f]{40}', commit), 'full source commit SHA required')
        self.commit = commit
        self.protected_input_differences = []
        self.git('cat-file', '-e', commit + '^{commit}')
        self.paths = self.git('ls-tree', '-r', '--name-only', commit).decode('utf-8').splitlines()

    def git(self, *args):
        return subprocess.check_output(['git', '-C', str(self.root), *args])

    def bytes(self, path):
        data = self.git('show', self.commit + ':' + path)
        require(not data.startswith(b'version https://git-lfs.github.com/spec/v1'), 'unmaterialized source asset is not a distributable: ' + path)
        return data

    def json(self, path):
        return json.loads(self.bytes(path))

    def write(self, path, destination):
        destination = Path(destination)
        require(not destination.exists(), 'source destination exists: ' + str(destination))
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(self.bytes(path))

    def source_matches(self, path, expected):
        frozen = self.bytes(path)
        if hashlib.sha256(frozen).hexdigest() == expected:
            return True
        local = self.root / path
        if path.endswith(('.cs', '.shader', '.json', '.py', '.cpp', '.h', '.m')) and local.is_file():
            materialized = local.read_bytes()
            return hashlib.sha256(materialized).hexdigest() == expected and materialized.replace(b'\r\n', b'\n') == frozen.replace(b'\r\n', b'\n')
        return False

    def require_sources(self, receipt):
        # Generated assembly metadata outside the checkout is separately bound by
        # native identity. Every recorded repository source is compared to Git.
        for name, expected in receipt.get('sources', {}).items():
            relative = name.replace('\\', '/')
            if relative in self.paths:
                if relative == 'tools/sdk/experiments/build_unity.py' and not self.source_matches(relative, expected):
                    require(digest(self.root / relative) == expected, 'protected helper changed since component receipt')
                    self.protected_input_differences.append({'path': relative, 'sha256': expected, 'scope': 'pre-existing protected Python experiment helper, not compiled into the component'})
                    continue
                require(self.source_matches(relative, expected), 'component was built from another source: ' + relative)
        for field in ('mutated_sources_sha256', 'diagnostic_mutations'):
            require(not receipt.get(field), 'diagnostic component cannot ship: ' + field)


def component(source, directory, filenames, target):
    directory = Path(directory)
    receipt = read(directory / 'build.json')
    source.require_sources(receipt)
    hashes = receipt.get('runtime_files', receipt.get('files', {}))
    for name in filenames:
        require(name in hashes, 'missing component inventory: ' + name)
        copy_file(directory / name, target / name, hashes[name])
    return receipt


def package_source(source, out):
    sdk = 'Packages/com.spatialwallpaper.sdk/'
    package = source.json(sdk + 'package.json')
    archive = out / 'Authoring' / ('com.spatialwallpaper.sdk-' + package['version'] + '.tgz')
    archive.parent.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive, 'w:gz') as target:
        for path in source.paths:
            if not path.startswith(sdk):
                continue
            data = source.bytes(path)
            item = tarfile.TarInfo('package/' + path[len(sdk):])
            item.size, item.mode, item.mtime = len(data), 0o644, 0
            target.addfile(item, io.BytesIO(data))
    for prefix in ('tools/wallpapers/', 'tools/market/', 'market/schemas/', '.github/PULL_REQUEST_TEMPLATE/'):
        for path in source.paths:
            if path.startswith(prefix):
                source.write(path, out / 'Authoring' / path)
    for path in source.paths:
        if path.startswith('docs/guides/') and any(term in path for term in ('市场', 'SDK', '外来原生', '公开源码')):
            source.write(path, out / 'Authoring' / path)
    source.write('market/requirements.txt', out / 'Authoring/market/requirements.txt')
    for name in ('Editor/OriginalNativeExample.cs', 'Editor/OriginalNativeExample.cs.meta', 'Shaders/OriginalColor.shader', 'Shaders/OriginalColor.shader.meta'):
        source.write('Prototype/Assets/NativeArtworks/' + name, out / 'Authoring/Examples/OriginalExternal' / name)
    source.write('Packages/com.spatialwallpaper.sdk/Samples~/ExternalNative/CC0-EXAMPLE-LICENSE.txt', out / 'Authoring/Examples/OriginalExternal/CC0-EXAMPLE-LICENSE.txt')
    for path in source.paths:
        if path.startswith(sdk + 'Samples~/NativeScripts/') and path.endswith('.cs'):
            source.write(path, out / 'Authoring/Examples' / Path(path).name)
    (out / 'Authoring/ExportNative.cmd').write_text('@echo off\r\n"%~dp0../Python/python.exe" "%~dp0tools/wallpapers/package_native.py" %*\r\n', encoding='utf-8')
    return {'sdkPackage': archive.relative_to(out).as_posix(), 'packageVersion': package['version']}


def production_python(source, python_home, site, out):
    from packaging.requirements import Requirement
    from packaging.markers import default_environment
    python_home, site = Path(python_home), Path(site)
    require((python_home / 'python311.dll').is_file(), 'bundled Tracker requires CPython 3.11 x64')
    # CPython home and its licenses are external inputs with full output hashes;
    # no pyvenv.cfg or development absolute path enters the distribution.
    for name in ('python.exe', 'python3.dll', 'python311.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'LICENSE.txt'):
        copy_file(python_home / name, out / 'Python' / name)
    copy_tree(python_home / 'Lib', out / 'Python/Lib', excluded=SKIP_PARTS | {'site-packages', 'idlelib', 'turtledemo'})
    copy_tree(python_home / 'DLLs', out / 'Python/DLLs', excluded=SKIP_PARTS)
    metadata = {}
    normalize = lambda name: re.sub(r'[-_.]+', '-', name).lower()
    for folder in sorted(site.glob('*.dist-info')):
        value = email.parser.Parser().parsestr((folder / 'METADATA').read_text(encoding='utf-8'))
        metadata[normalize(value['Name'])] = (folder, value)
    project = source.bytes('tracker/pyproject.toml').decode('utf-8')
    match = re.search(r'^dependencies\s*=\s*(\[[\s\S]*?\n\])', project, re.MULTILINE)
    require(match, 'tracker production dependency declarations missing')
    pending = ast.literal_eval(match.group(1))
    environment = default_environment()
    environment.update(sys_platform='win32', platform_system='Windows', platform_machine='AMD64',
                       python_version='3.11', python_full_version='3.11.14', extra='')
    selected = {}
    while pending:
        requirement = Requirement(pending.pop())
        if requirement.marker and not requirement.marker.evaluate(environment):
            continue
        key = normalize(requirement.name)
        if key in selected:
            continue
        require(key in metadata, 'production Python dependency not installed: ' + key)
        folder, value = metadata[key]
        require(not requirement.specifier or requirement.specifier.contains(value['Version']), 'installed Python dependency violates Tracker requirement: ' + key)
        selected[key] = (folder, value)
        pending.extend(value.get_all('Requires-Dist', []))
    receipts = []
    for key, (folder, value) in sorted(selected.items()):
        require(key not in {'pytest', 'pytest-asyncio', 'ruff', 'spatial-wallpaper-tracker'}, 'development/editable package entered runtime closure: ' + key)
        copied = 0
        with (folder / 'RECORD').open(encoding='utf-8', newline='') as records:
            for row in csv.reader(records):
                if not row:
                    continue
                name = row[0].replace('\\', '/')
                parts = PurePosixPath(name)
                if parts.is_absolute() or '..' in parts.parts or set(parts.parts).intersection(SKIP_PARTS) or parts.suffix in {'.pyc', '.pyo'}:
                    continue
                if parts.suffix == '.pth':
                    # Editable and virtualenv bootstrap hooks are not runtime dependencies.
                    require(not (site / name).read_text(encoding='utf-8').strip(), 'unexpected production .pth hook: ' + name)
                    continue
                path = site.joinpath(*parts.parts)
                require(path.is_file(), 'installed production RECORD missing file: ' + name)
                target = out / 'Python/Lib/site-packages' / Path(*parts.parts)
                if target.exists():
                    require(digest(path) == digest(target), 'Python distributions disagree about shared file: ' + name)
                else:
                    copy_file(path, target)
                    copied += 1
        receipts.append({'name': value['Name'], 'version': value['Version'],
                         'declaredLicense': value.get('License-Expression') or value.get('License'),
                         'licenseClassifiers': [c for c in value.get_all('Classifier', []) if c.startswith('License ::')],
                         'noticeDirectory': 'Python/Lib/site-packages/' + folder.name,
                         'copiedFiles': copied, 'licenseStatus': 'upstream-declared-not-a-rights-audit'})
    (out / 'Python/python311._pth').write_text('.\nDLLs\nLib\nLib/site-packages\n../Tracker/src\nimport site\n', encoding='utf-8')
    for path in source.paths:
        if path.startswith('tracker/src/') and path.endswith('.py') or path in {'tracker/README.md', 'tracker/pyproject.toml', 'tracker/uv.lock'}:
            source.write(path, out / 'Tracker' / path[len('tracker/'):])
    return receipts


def tracker_models(source, candidates, out):
    tree = ast.parse(source.bytes('tracker/src/spatial_tracker/assets.py'))
    assets = None
    for node in tree.body:
        if isinstance(node, ast.Assign) and any(isinstance(t, ast.Name) and t.id == 'ASSETS' for t in node.targets):
            assets = ast.literal_eval(node.value)
    require(assets, 'frozen Tracker model identities missing')
    result = []
    for name, (url, expected) in assets.items():
        available = [Path(root) / name for root in candidates if root and (Path(root) / name).is_file()]
        require(available, 'model unavailable locally; use --models: ' + name)
        chosen = next((p for p in available if digest(p) == expected), None)
        require(chosen is not None, 'all local copies differ from frozen Tracker model digest: ' + name)
        copy_file(chosen, out / 'Tracker/.cache/models' / name, expected)
        result.append({'name': name, 'upstream': url, 'sha256': expected, 'licenseStatus': 'preserved-model-source-license-review-required'})
    return result


def native_packages(source, packages, platform_index, validator, out, native):
    files = sorted(Path(packages).glob('*.spatial'))
    require(len(files) == 5, 'exactly five native packages required')
    require((Path(packages) / 'native-artworks.json').is_file(), 'native-artworks export receipt required')
    export = read(Path(packages) / 'native-artworks.json')
    catalog = []
    for path in files:
        result = subprocess.run([str(validator), '--package', str(path.resolve()), '--platform-index', str(platform_index.resolve())], capture_output=True, timeout=20, check=True)
        report = json.loads(result.stdout.decode('utf-8-sig'))
        require(report['valid'] and report['sdk'] == native['sdk'] and report['schema'] == native['schema'] and report['command_abi'] == native['scene_command_abi'], 'native package version mismatch: ' + path.name)
        require(report['sha256'] == digest(path), 'native validator package digest mismatch')
        require(not any(e['work_id'] == report['work_id'] for e in catalog), 'duplicate stable native work GUID')
        with zipfile.ZipFile(path) as package:
            manifest = json.loads(package.read('manifest.json'))
        known = [p for p in export['packages'] if Path(p['file']).name == path.name]
        require(len(known) == 1 and known[0]['sha256'] == report['sha256'] and known[0]['build_id'] == report['build_id'], 'package differs from native export receipt: ' + path.name)
        entry = {'file': path.name, 'original_id': 'native-' + manifest['scene']['id'], 'cover_id': manifest['scene']['id'], 'name': manifest['name'], 'work_id': report['work_id'], 'revision': report['revision'], 'build_id': report['build_id'], 'sha256': report['sha256'], 'license_share': report['license_share']}
        catalog.append(entry)
        copy_file(path, out / 'native-content' / path.name, report['sha256'])
        copy_file(path, out / 'SpatialWallpaper_Data/StreamingAssets/native-content' / path.name, report['sha256'])
    dump(out / 'native-content/catalog.json', catalog)
    dump(out / 'SpatialWallpaper_Data/StreamingAssets/native-content/catalog.json', catalog)
    require({p['cover_id'] for p in catalog} == {'showcase', 'parking', 'train', 'rainycafe', 'minishotgame'}, 'wrong five native scenes')
    export = dict(export, packages=[dict(item, file=Path(item['file']).name) for item in export['packages']])
    dump(out / 'native-content/export-receipt.json', export)
    return catalog


def assemble(args):
    source = FrozenSource(args.source_root, args.source_commit)
    out = args.output.resolve()
    portable = out.with_suffix(out.suffix + '.zip')
    require(not portable.exists() and not Path(str(portable) + '.sha256').exists(), 'release ZIP/checksum already exists; no overwrite')
    if out.exists():
        require(args.resume and not (out / 'release-manifest.json').exists() and read(out / 'Provenance/Player.json')['source_commit'] == source.commit, 'only an unfinished owned assembly from this frozen commit can resume')
    for directory in (args.player, args.legacy_runtime, args.native_worker, args.native_host, args.native_validator, args.packages):
        require(not out.is_relative_to(directory.resolve()), 'output cannot be nested in a source build: ' + str(directory))
    native_rules = source.json('tools/sdk/rules/native-scene-v1.json')
    native = {'sdk': native_rules['sdk_target'], 'schema': native_rules['schema_target'], 'scene_command_abi': native_rules['abi'],
              'catalog_digest': hashlib.sha256(json.dumps(native_rules, sort_keys=True, ensure_ascii=True, separators=(',', ':')).encode()).hexdigest()}
    require(native['sdk'] == '0.4.0-preview.1' and native['schema'] == 5 and native['scene_command_abi'] == 1, 'release identity is not frozen native SDK 0.4/schema5/ABI1')
    # Validate all original inventories before any large final copy.
    legacy = read(args.legacy_runtime / 'runtime.json')
    for name, expected in legacy['files'].items():
        path = args.legacy_runtime.joinpath(*safe_relative(name).parts)
        require(path.is_file() and digest(path) == expected, 'legacy runtime drift: ' + name)
    worker_receipt = read(args.native_worker / 'build.json')
    require(worker_receipt['identity']['sdk'] == native['sdk'] and worker_receipt['identity']['scene_catalog_digest'] == native['catalog_digest'] and str(worker_receipt['identity']['scene_command_abi']) == str(native['scene_command_abi']), 'pure ScriptWorker identity differs from native catalog')
    index = read(args.platform_index)
    require(index['format'] == 'spatial-native-platform-index' and index['version'] == 1, 'trusted platform index format')
    common = Path(source.git('rev-parse', '--path-format=absolute', '--git-common-dir').decode().strip()).parent
    venv = common / 'tracker/.venv'
    home = args.tracker_python_home
    if home is None:
        values = dict(line.split(' = ', 1) for line in (venv / 'pyvenv.cfg').read_text(encoding='utf-8').splitlines() if ' = ' in line)
        home = Path(values['home'])
    site = args.tracker_site or venv / 'Lib/site-packages'
    player_receipt = read(args.player / 'build.json')
    require(player_receipt.get('source_commit') == source.commit and player_receipt.get('exit_code') == 0 and player_receipt.get('source_and_original_scene_unchanged') is True, 'player was not successfully built at the frozen source commit')
    asset_overrides = []
    for name, expected in player_receipt.get('sources', {}).items():
        if name not in source.paths:
            continue
        frozen_digest = hashlib.sha256(source.bytes(name)).hexdigest()
        if not source.source_matches(name, expected):
            require(name.endswith('.unity'), 'player code or shader differs from frozen source: ' + name)
            asset_overrides.append({'path': name, 'frozenGitSha256': frozen_digest, 'retainedLocalSha256': expected, 'reason': 'pre-existing protected local scene; never overwritten or staged'})
    out.mkdir(parents=True, exist_ok=args.resume)
    dump(out / 'Provenance/Player.json', player_receipt)
    dump(out / 'Provenance/RetainedLocalAssets.json', asset_overrides)
    player_names = {'SpatialWallpaper.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'MonoBleedingEdge', 'SpatialWallpaper_Data', 'D3D12', 'WinPixEventRuntime.dll', 'baselib.dll'}
    for path in args.player.iterdir():
        if path.name not in player_names:
            continue
        if path.is_dir():
            copy_tree(path, out / path.name, excluded={'sdk-runtime', 'native-runtime', 'native-content', 'Tracker', 'Python', '__pycache__'})
        else:
            copy_file(path, out / path.name)
    require((out / 'SpatialWallpaper.exe').is_file() and (out / 'SpatialWallpaper_Data').is_dir(), 'Unity player layout missing')
    # Keep exact old content and compatibility lanes; replace only the host once.
    for name, expected in legacy['files'].items():
        if name in {'supervisor/' + n for n in HOST_FILES} or name in {'BuildIdentity.cs', 'compile.log', 'DesktopHitTester.obj'}:
            continue
        copy_file(args.legacy_runtime / Path(*safe_relative(name).parts), out / 'sdk-runtime' / Path(*safe_relative(name).parts), expected)
    host_receipt = component(source, args.native_host, HOST_FILES, out / 'native-runtime/supervisor')
    for name in HOST_FILES:
        copy_file(args.native_host / name, out / 'sdk-runtime/supervisor' / name)
    legacy['files'] = inventory(out / 'sdk-runtime')
    dump(out / 'sdk-runtime/runtime.json', legacy)
    component(source, args.native_worker, WORKER_FILES, out / 'native-runtime/worker')
    component(source, args.native_validator, VALIDATOR_FILES, out / 'Authoring/NativeValidator')
    copy_file(args.platform_index, out / 'native-runtime/native-platform-library.json')
    copy_file(args.platform_index, out / 'Authoring/NativeValidator/native-platform-library.json')
    dump(out / 'Authoring/NativeValidator/build.json', {'source_commit': source.commit, 'files': inventory(out / 'Authoring/NativeValidator')})
    native_manifest = {**native, 'files': inventory(out / 'native-runtime'),
                       'worker_files': {name: digest(out / 'native-runtime/worker' / name) for name in WORKER_FILES}}
    dump(out / 'native-runtime/native-runtime.json', native_manifest)
    python_dependencies = production_python(source, home, site, out)
    models = tracker_models(source, [args.models, source.root / 'Prototype/Builds/Native-Baseline/Tracker/.cache/models', common / 'Prototype/Builds/Native-Baseline/Tracker/.cache/models', common / 'tracker/.cache/models'], out)
    packages = native_packages(source, args.packages, out / 'native-runtime/native-platform-library.json', out / 'Authoring/NativeValidator/NativeValidator.exe', out, native)
    sdk = package_source(source, out)
    example_validation = subprocess.run([str(out / 'Authoring/NativeValidator/NativeValidator.exe'), '--package', str(args.creator_example.resolve()), '--platform-index', str(out / 'native-runtime/native-platform-library.json')], capture_output=True, text=True, check=True)
    example_report = json.loads(example_validation.stdout)
    require(example_report.get('policy') == 'native-schema5-external-data-v1' and example_report.get('license_share') is True, 'creator example lacks admitted original redistribution permission')
    copy_file(args.creator_example, out / 'Authoring/Examples/OriginalExternal/original-cc0-triangle.spatial', example_report['sha256'])
    dump(out / 'Authoring/Examples/OriginalExternal/admission.json', example_report)
    # The existing builder is the only launcher implementation. Its current
    # source must match the explicitly frozen commit, including relative paths.
    for name in ('tools/desktop/Launcher.cs', 'tools/desktop/build_launcher.py'):
        require((source.root / name).read_bytes() == source.bytes(name), 'launcher source differs from frozen source: ' + name)
    subprocess.run([sys.executable, str(source.root / 'tools/desktop/build_launcher.py'), '--output', str(out / 'Launch.exe'), '--tracking-python', 'Python/python.exe'], cwd=str(source.root), check=True)
    config = (out / 'Launch.exe.config').read_text(encoding='utf-8')
    require('Python/python.exe' in config and str(home) not in config, 'launcher has a development Python dependency')
    notices = []
    for name in source.paths:
        filename = PurePosixPath(name).name.lower()
        if filename.startswith(('license', 'notice', 'copying', 'thirdpartynotice', 'third-party-notice')) and not name.endswith('.meta') and name.startswith(('Packages/', 'Prototype/Assets/', 'tracker/')):
            source.write(name, out / 'Licenses/Source' / name)
            notices.append({'path': name, 'licenseStatus': 'preserved-upstream-text-not-a-rights-audit'})
    dump(out / 'DEPENDENCIES.json', {'runtime': ['Windows x64', '.NET Framework 4.8', 'bundled Unity Player runtime', 'bundled CPython 3.11 with production Tracker dependencies'],
          'authoring': ['Unity ' + source.json('tools/sdk/rules/v1.json')['engine'] + ' for Unity scene/script authoring', 'bundled Python for standard-library export tool; market publisher additionally uses Authoring/market/requirements.txt'],
          'python': python_dependencies, 'models': models, 'notices': notices,
          'assetLicenses': 'Original built-in asset permissions are not certified by this assembler. Local-validation packages remain local-validation.'})
    instructions = 'Spatial Wallpaper ' + native['sdk'] + '\n\n解压整个目录后双击 Launch.exe。不要只复制 Launch.exe。普通运行不需要另装 Unity/Python；Windows x64 与 .NET Framework 4.8 为系统依赖。\n原Library内导入/显式应用、更新、卸载和社区市场；关闭管理器后原托盘继续控制桌面，完全退出恢复系统壁纸。\n新五包在 native-content，旧 sdk-content 在播放器 StreamingAssets 中保留。Authoring 包含SDK、匹配验证器、导出工具、示例及市场投稿/评论/撤回文档。\n安装/升级：解压至新的空目录，从新目录启动。用户数据位于原管理器数据根，勿删除。卸载：先从托盘完全退出，再删除本便携目录；用户库、作品存档需由用户另行决定是否删除。\n手动体验：五件画面与音效；咖啡馆多层循环；MiniShot视角/命中/存档；用户静音；更新不自动切换；应用失败恢复确切修订；关闭/重开/完全退出/重启；多显示目标与离线校准。未执行项目不能算通过。\n本工具只组包及静态摘要检查，不代表上述真实体验已通过。版权/资源准入/脚本隔离/驱动与GPU风险分别处理。\n'
    (out / '安装与使用.txt').write_text(instructions, encoding='utf-8')
    release = {'format': 'spatial-wallpaper-portable-release', 'version': native['sdk'], 'source_commit': source.commit, 'assembly_tool_sha256': digest(Path(__file__)), 'protected_noncompiled_input_differences': source.protected_input_differences,
               'native': native, 'legacy_sdk': legacy.get('sdk'), 'authoring': sdk,
               'packages': packages, 'host_reused_in_both_lanes': True,
               'status': 'assembled-portable-release-pending-required-smoke-and-manual-experience',
               'files': inventory(out, exclude={'release-manifest.json', 'SHA256SUMS.txt'})}
    dump(out / 'release-manifest.json', release)
    sums = dict(release['files'])
    sums['release-manifest.json'] = digest(out / 'release-manifest.json')
    (out / 'SHA256SUMS.txt').write_text(''.join(sha + '  ' + name + '\n' for name, sha in sorted(sums.items())), encoding='utf-8')
    with zipfile.ZipFile(portable, 'x', compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as archive:
        for path in sorted(out.rglob('*')):
            if path.is_file():
                archive.write(path, out.name + '/' + path.relative_to(out).as_posix())
    zip_sha256 = digest(portable)
    Path(str(portable) + '.sha256').write_text(zip_sha256 + '  ' + portable.name + '\n', encoding='utf-8')
    print(json.dumps({'launch': str(out / 'Launch.exe'), 'portable': str(portable), 'manifest': str(out / 'release-manifest.json'), 'sha256': zip_sha256}, ensure_ascii=False))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('player', 'legacy-runtime', 'native-worker', 'native-host', 'native-validator', 'platform-index', 'packages', 'creator-example', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--source-commit', required=True)
    parser.add_argument('--resume', action='store_true', help='Verify and retain exact files of an unfinished owned assembly; never overwrite existing program files')
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--tracker-python-home', type=Path)
    parser.add_argument('--tracker-site', type=Path)
    parser.add_argument('--models', type=Path)
    assemble(parser.parse_args())


if __name__ == '__main__':
    main()
