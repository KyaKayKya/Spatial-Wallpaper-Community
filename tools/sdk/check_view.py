"""Check read-only authored view data in an owned reusable Unity project."""
from pathlib import Path
import json,os,uuid
from build_execution import ROOT,UNITY,create
from toolchain_process import run_toolchain

out=ROOT/'Validation/Content/sdk-runtime/G/view'/uuid.uuid4().hex
out.mkdir(parents=True);print(out,flush=True)
project=ROOT/'Validation/Sdk/v-minishot'
create(project,[('SdkViewChecks.cs',True)])
env=os.environ.copy();env['SDK_VIEW_OUTPUT']=str(out);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
report={'status':'failed','project':str(project),'process':{}}
try:
 with (out/'toolchain.log').open('w') as log:run_toolchain([UNITY,'-batchmode','-projectPath',str(project),'-executeMethod','SdkViewChecks.Run','-logFile',str(out/'unity.log')],ROOT,log,600,report['process'],env=env)
 report['result']=json.loads((out/'result.json').read_text(encoding='utf-8-sig'));report['status']='passed-selected-view-binding-not-RC'
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
