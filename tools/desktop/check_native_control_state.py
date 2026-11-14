"""Check native status capture and serialization using the existing Unity project cache."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[2]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--expect-budget-red', action='store_true')
    parser.add_argument('--script-node-cost', action='store_true')
    parser.add_argument('--extended-commands', action='store_true')
    args = parser.parse_args()
    stage = ROOT / 'Validation/Content/sdk-runtime/native-render/task3'
    output = stage / 'control-state' / uuid.uuid4().hex[:12]
    output.mkdir(parents=True)
    protected = json.loads((stage / 'build-receipt-snapshot-cache.json').read_text(encoding='utf-8'))['protected_sha256_unchanged']
    paths = list(protected) + ['Prototype/Assets/Desktop/NativeRenderInstance.cs', 'Prototype/Assets/Desktop/Editor/NativeSceneCommandChecks.cs', 'Packages/com.spatialwallpaper.sdk/Runtime/Native/Unity/UnitySceneCommandTarget.cs', 'Packages/com.spatialwallpaper.sdk/Runtime/Native/Unity/NativeAssetCatalog.cs', 'Packages/com.spatialwallpaper.sdk/Runtime/Native/Unity/NativeSoundBus.cs', 'Packages/com.spatialwallpaper.sdk/Runtime/Native/Unity/NativePrefabPool.cs', 'Prototype/Assets/Desktop/Core/NativeDesktopPose.cs', 'Prototype/Assets/Desktop/DesktopNativeAdapter.cs']
    paths = sorted(set(paths + [path.relative_to(ROOT).as_posix() for path in (ROOT / 'Packages/com.spatialwallpaper.sdk').rglob('*.cs')]))
    before = {path: digest(ROOT / path) for path in paths}
    method = 'RunExtendedCommands' if args.extended_commands else 'RunScriptStatusCost' if args.script_node_cost else 'RunControlStateCost'
    command = ['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe', '-batchmode', '-quit', '-projectPath', str(ROOT / 'Prototype'), '-executeMethod', 'SpatialWallpaper.Desktop.Editor.NativeSceneCommandChecks.' + method, '-logFile', str(output / 'unity.log')]
    environment = dict(os.environ, SPATIAL_SCENE_COMMAND_CHECK_OUTPUT=str(output))
    environment.setdefault('ALLUSERSPROFILE', environment.get('PROGRAMDATA', 'C:/ProgramData'))
    result = subprocess.run(command, cwd=ROOT, env=environment)
    report = {'command': command, 'exit_code': result.returncode, 'source_sha256': before,
              'inputs_unchanged': before == {path: digest(ROOT / path) for path in paths},
              'method': method,
              'scope': 'Unity Editor native command capabilities; not Player visual acceptance or complete task4' if args.extended_commands else 'Unity Editor state capture/serialization allocation event checks; not Player performance or RC'}
    receipt = output / ('extended-native-commands.json' if args.extended_commands else 'script-status-cost.json' if args.script_node_cost else 'control-state-cost.json')
    report['result'] = json.loads(receipt.read_text(encoding='utf-8')) if receipt.is_file() else None
    if args.expect_budget_red:
        failure = 'script-status-must-not-reclone-unchanged-native-node-metadata' if args.script_node_cost else 'control-state-capture-must-not-clone-under-render-lock'
        report['expected_budget_red'] = result.returncode != 0 and report['result'] is not None and not report['result']['passed'] and failure in (output / 'unity.log').read_text(encoding='utf-8-sig')
    (output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'output': str(output), **{key: value for key, value in report.items() if key not in ['source_sha256', 'command']}}, indent=2))
    return 0 if report['inputs_unchanged'] and (report.get('expected_budget_red') if args.expect_budget_red else result.returncode == 0 and report['result'] is not None and report['result']['passed']) else 1


if __name__ == '__main__':
    raise SystemExit(main())
