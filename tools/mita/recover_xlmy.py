"""Extract the matching character from a private AssetRipper 2.0.0 export."""
import argparse,hashlib,json,re,shutil
from pathlib import Path
import yaml
from prepare import COMMON
from recover_xlmy_camera import extract as camera
from recover_xlmy_effects import extract as effects
from recover_xlmy_audio import extract as audio

ARCHIVE_SHA256='9d0d1037019c0d0c3b62490927cc527051f9ad8f4d8f22db9a4fcc681f81fa8a'
SOURCE='https://www.bilibili.com/video/BV1XZcNeaEsd/'

def recover(export,archive):
    if hashlib.sha256(archive.read_bytes()).hexdigest()!=ARCHIVE_SHA256:raise ValueError('Unexpected original archive digest')
    assets=export/'Assets'
    scenes=list((assets/'Scenes').glob('*.unity'))
    if len(scenes)!=1:raise ValueError('Expected the original single-scene export')
    scene=scenes[0].read_text(encoding='utf8')
    chunks=list(re.finditer(r'--- !u!(\d+) &(-?\d+)\n(.*?)(?=\n--- !u!|\Z)',scene,re.S))
    docs={int(m[2]):yaml.safe_load(m[3]) for m in chunks}
    actor_ids=[i for i,d in docs.items() if d.get('GameObject',{}).get('m_Name')=='米塔(主要脚本在这里)']
    if len(actor_ids)!=1:raise ValueError('Original actor root not found')
    transforms={i:d['Transform'] for i,d in docs.items() if 'Transform' in d}
    root_id=next(i for i,t in transforms.items() if t['m_GameObject']['fileID']==actor_ids[0])
    keep_transforms=set()
    def descend(ident):
        keep_transforms.add(ident)
        for ref in transforms[ident]['m_Children']:descend(ref['fileID'])
    descend(root_id)
    keep_go={transforms[i]['m_GameObject']['fileID'] for i in keep_transforms}
    keep=keep_transforms|keep_go
    for ident,d in docs.items():
        if any(kind in d and d[kind]['m_GameObject']['fileID'] in keep_go for kind in ['Animator','SkinnedMeshRenderer','MeshFilter','MeshRenderer']):keep.add(ident)
    classes={int(m[2]):int(m[1]) for m in chunks}
    mapping={i:classes[i]*100000+i for i in keep}
    pieces=[]
    for m in chunks:
        ident=int(m[2])
        if ident not in keep:continue
        text=m[3]
        if m[1]=='1':text=re.sub(r'^  - component: \{fileID: (-?\d+)\}\n',lambda v:v[0] if int(v[1]) in keep else '',text,flags=re.M)
        if ident==root_id:text=re.sub(r'm_Father: \{fileID: -?\d+\}','m_Father: {fileID: 0}',text)
        if m[1]=='95':
            text=re.sub(r'm_Avatar: \{[^\n]+\}','m_Avatar: {fileID: 0}',text)
            text=re.sub(r'm_Controller: \{[^\n]+\}','m_Controller: {fileID: 0}',text)
        text=re.sub(r'fileID: (-?\d+)',lambda v:'fileID: '+str(mapping.get(int(v[1]),int(v[1]))),text)
        pieces.append('--- !u!'+m[1]+' &'+str(mapping[ident])+'\n'+text)
    target=COMMON/'.local-assets/mita-xlmy/minimal';target.mkdir(exist_ok=True)
    prefab=target/'XlmyMita.prefab'
    prefab.write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'+'\n'.join(pieces),encoding='utf8')
    prefab.with_suffix('.prefab.meta').write_text('fileFormatVersion: 2\nguid: 5dc8b0f8389c4f038b5efa22e9371ae4\nPrefabImporter:\n  externalObjects: {}\n')
    required=set(re.findall(r'guid: ([0-9a-f]{32})',prefab.read_text(encoding='utf8')))
    for folder in ['Mesh','AnimationClip']:
        for path in (assets/folder).glob('*'):
            if path.name.endswith('.meta'):continue
            meta=path.with_name(path.name+'.meta')
            if folder=='AnimationClip' and not path.name.startswith('MitaPerson'):continue
            if folder=='Mesh' and not any(g in meta.read_text() for g in required):continue
            output=target/folder;output.mkdir(exist_ok=True)
            shutil.copy2(path,output/path.name);shutil.copy2(meta,output/meta.name)
    if len(list((target/'Mesh').glob('*.asset')))!=8 or len(list((target/'AnimationClip').glob('*.anim')))!=7:raise ValueError('Unexpected character payload')
    guids={}
    for meta in assets.rglob('*.meta'):
        match=re.search(r'^guid: (\w+)',meta.read_text(encoding='utf8'),re.M)
        if match:guids[match[1]]=meta.with_suffix('')
    textures={};output=target/'Texture2D';output.mkdir(exist_ok=True)
    for ident in keep:
        renderer=docs[ident].get('SkinnedMeshRenderer')
        if not renderer:continue
        name=docs[renderer['m_GameObject']['fileID']]['GameObject']['m_Name']
        material=guids[renderer['m_Materials'][0]['guid']]
        data=yaml.safe_load('\n'.join(material.read_text(encoding='utf8').splitlines()[3:]))['Material']['m_SavedProperties']['m_TexEnvs']
        if isinstance(data,list):data={k:v for entry in data for k,v in entry.items()}
        texture=guids[data['_MainTex']['m_Texture']['guid']]
        shutil.copy2(texture,output/texture.name);shutil.copy2(texture.with_name(texture.name+'.meta'),output/(texture.name+'.meta'))
        textures[name]='Assets/Xlmy/Texture2D/'+texture.name
    (target/'texture-map.json').write_text(json.dumps(textures,ensure_ascii=False,indent=2),encoding='utf8')
    camera(assets,target/'Camera')
    effects(assets,target,docs,guids,chunks,root_id)
    audio(archive,target)
    files={str(p.relative_to(target)):{'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in target.rglob('*') if p.is_file() and p.name!='manifest.json'}
    (target/'manifest.json').write_text(json.dumps({'source_video':SOURCE,'archive_sha256':ARCHIVE_SHA256,'files':files},indent=2),encoding='utf8')
    print('Recovered 8 meshes, 7 clips, matching atlases and original camera:',target)

if __name__=='__main__':
    parser=argparse.ArgumentParser();cache=COMMON/'.local-assets/mita-xlmy'
    parser.add_argument('--export',type=Path,default=cache/'recovered-project/ExportedProject')
    parser.add_argument('--archive',type=Path,default=cache/'xlmy-original.zip')
    args=parser.parse_args();recover(args.export.resolve(),args.archive.resolve())
