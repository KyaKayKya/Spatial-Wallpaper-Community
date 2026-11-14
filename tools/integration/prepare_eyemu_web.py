"""Prepare pinned original EyeMU assets for a local, camera-off-by-default spike."""
import hashlib
import json
from pathlib import Path
import shutil
import urllib.request

ROOT=Path(__file__).resolve().parents[2]
LOCAL=ROOT/'Validation/Gaze/local'
OUT=LOCAL/'eyemu-web'
SHA='7184a11584604337c882e4e038dc4321f3016c46'
BASE=f'https://raw.githubusercontent.com/FIGLAB/EyeMU/{SHA}/flask2/static/'

def fetch(url,path):
    path.parent.mkdir(parents=True,exist_ok=True)
    if not path.exists():
        with urllib.request.urlopen(url,timeout=60) as response:
            data=response.read()
        path.write_bytes(data)
    return {'url':url,'file':str(path.relative_to(OUT)), 'bytes':path.stat().st_size,
            'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}

def main():
    entries=[]
    for remote,target in [('tf.min.js','tf.min.js'),('mlweb.js','mlweb.js'),
                          ('collectioncolor.js','collectioncolor-source.js'),
                          ('models/tfjsmodel4/my-model.json','model/my-model.json')]:
        entries.append(fetch(BASE+remote,OUT/target))
    model=json.loads((OUT/'model/my-model.json').read_text())
    for group in model['weightsManifest']:
        for name in group['paths']:
            entries.append(fetch(BASE+'models/tfjsmodel4/'+name,OUT/'model'/name))
    entries.append(fetch('https://cdn.jsdelivr.net/npm/@tensorflow-models/facemesh@0.0.5/dist/facemesh.js',OUT/'facemesh.js'))
    # Extract only pure author geometry functions; never execute the source's camera bootstrap.
    source=(OUT/'collectioncolor-source.js').read_text()
    functions=[]
    for name in ['getFaceYaw','getFacePitch','getFaceRoll','getFaceSize','getEyeCorners','eyeBoundsFromCorners']:
        start=source.index('function '+name+'(')
        end=source.index('\n}',start)+2
        functions.append(source[start:end])
    (OUT/'original-geometry.js').write_text('var videoDivisor=4,videoWidth=0,videoHeight=0;\n'+'\n'.join(functions),encoding='utf-8')
    for name in ['uniteye-core.js','uniteye-cv.js']:
        shutil.copy2(LOCAL/'uniteye-source/webgl'/name,OUT/name)
    (OUT/'models').mkdir(exist_ok=True)
    shutil.copy2(LOCAL/'uniteye-source/webgl/models/eyemu.onnx',OUT/'models/eyemu.onnx')
    for name in ['index.html','experiment.js','diagnostics.mjs']:
        shutil.copy2(ROOT/'tools/integration/eyemu_web'/name,OUT/name)
    manifest={'original_commit':SHA,'uniteye_commit':'0cbbc9bfbb0d0b0b95b57deec3095e5d5ac81996',
              'assets':entries,'camera_started':False,
              'limits':['Legacy FaceMesh model files and UnitEye browser runtimes load from upstream when requested.']}
    (OUT/'assets.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    print(json.dumps(manifest,indent=2))

if __name__=='__main__':main()
