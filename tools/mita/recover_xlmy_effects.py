"""Recover native particle payloads without the source UI/audio frameworks."""
import hashlib,json,re,shutil
import yaml

def extract(assets,target,docs,guids,chunks,actor_root):
    output=target/'Effects';output.mkdir(exist_ok=True)
    control=next(d['MonoBehaviour'] for d in docs.values() if 'clickParticles' in d.get('MonoBehaviour',{}))
    ref=control['clickParticles'][0]['fileID'];go=docs[ref]['ParticleSystem']['m_GameObject']['fileID']
    ids={go}|{r['component']['fileID'] for r in docs[go]['GameObject']['m_Component']}
    selected=[m for m in chunks if int(m[2]) in ids and int(m[1]) in {1,4,198,199}]
    mapping={int(m[2]):int(m[1])*100000+int(m[2]) for m in selected}
    pieces=[]
    for m in selected:
        text=m[3]
        if m[1]=='4':text=re.sub(r'm_Father: \{fileID: \d+\}','m_Father: {fileID: 0}',text)
        text=re.sub(r'fileID: (-?\d+)',lambda v:'fileID: '+str(mapping.get(int(v[1]),int(v[1]))),text)
        pieces.append('--- !u!'+m[1]+' &'+str(mapping[int(m[2])])+'\n'+text)
    click=output/'Click.prefab';click.write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'+'\n'.join(pieces),encoding='utf8')
    click.with_suffix('.prefab.meta').write_text('fileFormatVersion: 2\nguid: '+hashlib.sha256(b'xlmy-click-particles').hexdigest()[:32]+'\nPrefabImporter:\n  externalObjects: {}\n')
    config={}
    def material(guid):
        data=yaml.safe_load('\n'.join(guids[guid].read_text(encoding='utf8').splitlines()[3:]))['Material']['m_SavedProperties']
        textures=data['m_TexEnvs']
        if isinstance(textures,list):textures={k:v for item in textures for k,v in item.items()}
        colors=data['m_Colors']
        if isinstance(colors,list):colors={k:v for item in colors for k,v in item.items()}
        texture=guids[textures['_MainTex']['m_Texture']['guid']]
        shutil.copy2(texture,output/texture.name);shutil.copy2(texture.with_name(texture.name+'.meta'),output/(texture.name+'.meta'))
        return {'texture':'Assets/Xlmy/Effects/'+texture.name,'color':colors.get('_Color',{'r':1,'g':1,'b':1,'a':1})}
    renderer=next(docs[i]['ParticleSystemRenderer'] for i in ids if 'ParticleSystemRenderer' in docs[i])
    config['click']=material(renderer['m_Materials'][0]['guid'])
    start=next(d['MonoBehaviour'] for d in docs.values() if 'winkParticles' in d.get('MonoBehaviour',{}))
    wink=guids[start['winkParticles']['guid']];shutil.copy2(wink,output/'Wink.prefab');shutil.copy2(wink.with_name(wink.name+'.meta'),output/'Wink.prefab.meta')
    wink_text=wink.read_text(encoding='utf8')
    refs=re.findall(r'm_Materials:\n  - \{fileID: \d+, guid: (\w+)',wink_text)
    if len(refs)!=1:raise ValueError('Unexpected wink particle material count')
    config['wink']=material(refs[0])
    current=start['winkRoot']['fileID'];path=[]
    while current!=actor_root:
        transform=docs[current]['Transform'];path.append(docs[transform['m_GameObject']['fileID']]['GameObject']['m_Name']);current=transform['m_Father']['fileID']
    config['wink_path']='/'.join(reversed(path))
    (output/'effects.json').write_text(json.dumps(config,ensure_ascii=False,indent=2),encoding='utf8')
    print('Recovered original Sparks, wink particles and matching textures')
