"""Give new template source files stable Unity GUIDs without rebuilding other scenes."""
from pathlib import Path
import uuid
root=Path(__file__).resolve().parents[2];assets=root/'Prototype/Assets';start=assets/'Blueprint'
for path in [start,*sorted(start.rglob('*'))]:
    if path.name.endswith('.meta'):continue
    meta=Path(str(path)+'.meta')
    if meta.exists():continue
    # Unity must create native importer settings for textures, shaders and assets.
    if path.is_file() and path.suffix not in ['.cs','.asmdef','.json','.txt']:continue
    guid=uuid.uuid5(uuid.NAMESPACE_URL,'spatialwallpaper:blueprint/'+path.relative_to(start).as_posix()).hex
    header='fileFormatVersion: 2\nguid: '+guid+'\n'
    if path.is_dir():header+='folderAsset: yes\n'
    importer='MonoImporter' if path.suffix=='.cs' else 'AssemblyDefinitionImporter' if path.suffix=='.asmdef' else 'TextScriptImporter' if path.is_file() else 'DefaultImporter'
    body=importer+':\n  externalObjects: {}\n'
    if path.suffix=='.cs':body+='  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n'
    body+='  userData:\n  assetBundleName:\n  assetBundleVariant:\n'
    meta.write_text(header+body,encoding='utf8')
