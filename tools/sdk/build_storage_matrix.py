"""Build three distinct converted author packages for real storage combination checks."""
import os, subprocess, uuid, json, hashlib
from build_execution import ROOT, UNITY, create
from provenance import sources
out=ROOT/'Validation/Content/sdk-runtime/E/storage-matrix-author'/uuid.uuid4().hex
out.mkdir(parents=True)
project=ROOT/'Validation/Sdk'/('m'+out.name[:10])
create(project,[('StageEStorageMatrixAuthor.cs',False),('StageEStorageMatrixExport.cs',True)])
receipt=sources();env=os.environ.copy();env.update(SDK_E_OUTPUT=str(out),SDK_E_RUN=out.name)
env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
subprocess.run([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageEStorageMatrixExport.Run','-logFile',str(out/'conversion.log')],env=env,timeout=900,check=True,creationflags=subprocess.CREATE_NO_WINDOW)
authors={str(role):{'metadata':json.loads((out/str(role)/'author.json').read_text(encoding='utf-8-sig')),'package_sha256':hashlib.sha256((out/str(role)/'scene.spatial').read_bytes()).hexdigest()} for role in range(1,4)}
assert len({v['metadata']['build'] for v in authors.values()})==3
assert sources()==receipt
(out/'report.json').write_text(json.dumps({'sources':receipt,'authors':authors,'project':str(project)},indent=2))
print(out,flush=True)
