"""Check source identities before assembling a development SDK distribution."""
from pathlib import Path
import hashlib,json

ROOT=Path(__file__).resolve().parents[2]

def identity(root=ROOT):
    rules_path=root/'tools/sdk/rules/v1.json'
    rules=json.loads(rules_path.read_text(encoding='utf-8'))
    package=json.loads((root/'Packages/com.spatialwallpaper.sdk/package.json').read_text(encoding='utf-8'))
    native=json.loads((root/'tools/sdk/rules/native-scene-v1.json').read_text(encoding='utf-8'))
    if package['version'] not in (rules['sdk'],native['sdk_target']):
        raise ValueError('release-sdk-package-version-mismatch: '+package['version']+' != '+rules['sdk'])
    if package['dependencies'].get('com.unity.render-pipelines.universal')!=rules['pipeline']:
        raise ValueError('release-urp-dependency-version-mismatch')
    version=(root/'Prototype/ProjectSettings/ProjectVersion.txt').read_text(encoding='utf-8')
    if ('m_EditorVersion: '+rules['engine']) not in version.splitlines():
        raise ValueError('release-unity-version-mismatch')
    canonical=json.dumps(rules,ensure_ascii=True,sort_keys=True,separators=(',',':')).encode()
    return {**{key:rules[key] for key in ['sdk','engine','pipeline','vm','backend']},
            'rules_sha256':hashlib.sha256(canonical).hexdigest(),
            'policy_id':rules['runtime_policy']['id'],
            'package_sha256':hashlib.sha256((root/'Packages/com.spatialwallpaper.sdk/package.json').read_bytes()).hexdigest()}

def native_identity(root=ROOT):
    legacy=identity(root);native=json.loads((root/'tools/sdk/rules/native-scene-v1.json').read_text(encoding='utf-8'))
    package=json.loads((root/'Packages/com.spatialwallpaper.sdk/package.json').read_text(encoding='utf-8'))
    if package['version']!=native['sdk_target']:raise ValueError('native-release-sdk-package-version-mismatch')
    return {'sdk':native['sdk_target'],'schema':5,'scene_command_abi':native['abi'],'catalog_digest':hashlib.sha256(json.dumps(native,ensure_ascii=True,sort_keys=True,separators=(',',':')).encode()).hexdigest(),'engine':legacy['engine'],'pipeline':legacy['pipeline'],'vm':legacy['vm'],'legacy_compatibility_sdk':legacy['sdk']}

def build_identity(output):
    value=identity()
    value.pop('package_sha256')
    path=Path(output)/'BuildIdentity.cs'
    path.write_text('using System.Reflection;\n'+''.join(
        '[assembly:AssemblyMetadata('+json.dumps(key)+','+json.dumps(item)+')]\n'
        for key,item in value.items()),encoding='utf-8')
    return value,path

if __name__=='__main__':
    print(json.dumps(identity(),indent=2))
