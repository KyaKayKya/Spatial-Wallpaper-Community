"""Verify source version agreement and reject mismatched release inputs."""
from pathlib import Path
import hashlib,json,shutil,uuid
from release_identity import ROOT,identity
from provenance import sources

out=ROOT/'Validation/Content/sdk-runtime/G/release-identity'/uuid.uuid4().hex
out.mkdir(parents=True);print(out,flush=True)
report={'status':'failed','sources':sources(),'scope':'source version preflight only; no release packaging, installed Unity or RC acceptance claim'}
names=['tools/sdk/rules/v1.json','Packages/com.spatialwallpaper.sdk/package.json','Prototype/ProjectSettings/ProjectVersion.txt','tools/sdk/rules/native-scene-v1.json']
try:
    fixture=out/'inputs'
    for name in names:
        target=fixture/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(ROOT/name,target)
    report['inputs']={name:hashlib.sha256((fixture/name).read_bytes()).hexdigest() for name in names}
    report['identity']=identity()
    rejected=[]
    for case,reason in [('sdk','release-sdk-package-version-mismatch'),('urp','release-urp-dependency-version-mismatch'),('engine','release-unity-version-mismatch')]:
        package=fixture/names[1];engine=fixture/names[2]
        package.write_bytes((ROOT/names[1]).read_bytes());engine.write_bytes((ROOT/names[2]).read_bytes())
        if case=='engine':engine.write_text('m_EditorVersion: 2022.3.0f1\n',encoding='utf-8')
        else:
            value=json.loads(package.read_text(encoding='utf-8'))
            if case=='sdk':value['version']='0.2.0'
            else:value['dependencies']['com.unity.render-pipelines.universal']='14.0.10'
            package.write_text(json.dumps(value),encoding='utf-8')
        try:identity(fixture)
        except ValueError as error:
            assert str(error).startswith(reason),str(error);rejected.append({'case':case,'reason':str(error)})
        else:raise AssertionError('mismatched release input accepted: '+case)
    report['rejected']=rejected;report['status']='passed-development-release-source-identities'
except BaseException as error:
    report['error']=repr(error);raise
finally:
    report['sources_unchanged']=report['sources']==sources()
    (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
