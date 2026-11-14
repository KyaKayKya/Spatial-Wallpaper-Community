"""Generate same-value Transform/Light native QA program without art changes."""
from pathlib import Path
import argparse
import hashlib
import json

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--empty', action='store_true', help='diagnostic empty command bridge; isolate exchange/snapshot overhead from setters')
args = parser.parse_args()
output = args.output.resolve()
assert not output.exists(), 'fixture output must be new'
rules = json.loads((root / 'tools/sdk/rules/native-scene-v1.json').read_text())
legacy = json.loads((root / 'tools/sdk/rules/v1.json').read_text())
definition = {'format': 'spatial-native-script', 'version': 1, 'sdk': rules['sdk_target'], 'vm': legacy['vm'], 'command_abi': rules['abi'],
              'catalog_digest': hashlib.sha256(json.dumps(rules, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode()).hexdigest(),
              'program': {'classes': [{'id': 'Demo', 'fields': [{'id': 'target', 'type': 'node'}, {'id': 'counter', 'type': 'i32'}, {'id': 'lamp', 'type': 'light'}],
                                       'methods': [{'id': 'Update', 'returns': 'void', 'parameters': [], 'locals': [], 'body': []}]}]},
              'content': {'frame': [.6, .3375, .5], 'resources': [], 'nodes': [], 'prefabs': []}}
for index in range(2):
    transform = {'kind': 'transform', 'config': {'position': [0, 0, 0], 'rotation': [0, 0, 0, 1], 'scale': [1, 1, 1]}, 'fields': {}, 'callbacks': []}
    component = ({'kind': 'script', 'config': {'class': 'Demo'}, 'fields': {'target': 0, 'counter': 0, 'lamp': 1}, 'callbacks': ['Update']} if index == 0 else
                 {'kind': 'light', 'config': {'type': 2, 'color': [1, 1, 1, 1], 'intensity': 1.0, 'range': 10.0, 'spot': 30.0, 'innerSpot': 20.0, 'shadows': 0, 'shadowStrength': 1.0, 'bias': .05, 'normalBias': .4, 'near': .2, 'usePipelineSettings': True}, 'fields': {}, 'callbacks': []})
    definition['content']['nodes'].append({'id': 'n' + str(index), 'parent': -1, 'components': [transform, component]})
body = definition['program']['classes'][0]['methods'][0]['body']


def emit(op, value=None):
    body.append({'op': op, 'value': value})


for field, kind, member, value_type in ([] if args.empty else [('target', 'Transform', 'localPosition', 'UnityEngine.Vector3'), ('lamp', 'Light', 'intensity', 'System.Single')]):
    emit('ldarg', 0)
    emit('ldfield', field)
    emit('ldarg', 0)
    emit('ldfield', field)
    emit('call', 'UnityEngine.CoreModule::UnityEngine.' + kind + '::get_' + member + '()->' + value_type)
    emit('call', 'UnityEngine.CoreModule::UnityEngine.' + kind + '::set_' + member + '(' + value_type + ')->System.Void')
emit('ret')
definition['build_id'] = hashlib.sha256(json.dumps(definition, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest()
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(definition, separators=(',', ':')), encoding='utf-8')
print(output, hashlib.sha256(output.read_bytes()).hexdigest())
