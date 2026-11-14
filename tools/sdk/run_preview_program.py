"""Run the actual standalone preview image and independently retain child kernels."""
from pathlib import Path
import argparse,subprocess,uuid,json,hashlib,shutil,sys,os
from check_contracts import ROOT,DATA
from provenance import sources
from windows_framework import references,FRAMEWORK
from toolchain_process import run_toolchain
sys.path.insert(0,str(ROOT/'Supervisor/Tests'))
from run_production import packages,require_production_build
parser=argparse.ArgumentParser();parser.add_argument('--worker',type=Path,required=True);parser.add_argument('--runtime',type=Path);parser.add_argument('--author',type=Path);parser.add_argument('--author-kind',choices=['static','llm'],default='static');parser.add_argument('--case',choices=['normal','cancel-start','wrong-runtime','wrong-runtime-file'],default='normal');args=parser.parse_args();worker=args.worker.resolve();wr=json.loads((worker/'report.json').read_text());require_production_build(wr)
out=ROOT/'Validation/Content/sdk-runtime/F/preview-program'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True);report={'status':'failed','author_kind':args.author_kind if args.author else None,'case':args.case,'sources':sources(),'scope':'actual independently launched trusted native preview, retained viewer/Host/LPAC Worker kernels and runtime module paths; not Editor UI, clean machine or release acceptance'}
try:
 ids=packages(out)
 if args.author:
  assert args.case=='normal';author=json.loads((args.author/'author.json').read_text(encoding='utf-8-sig'));shutil.copyfile(args.author/'scene.spatial',out/'healthy.spatial');ids=(author['build'],);report['author_report']=str(args.author/'report.json')
 if args.runtime:
  runtime=args.runtime.resolve()
 else:
  runtime=out/'runtime';subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_preview.py'),'--output',str(runtime)],check=True)
  for name in ['supervisor','validator']:subprocess.run([sys.executable,str(ROOT/f'tools/sdk/build_{name}.py'),'--output',str(runtime/name)],check=True)
  shutil.copytree(worker/'player',runtime/'worker/player');shutil.copyfile(worker/'report.json',runtime/'worker/report.json');build=json.loads((runtime/'build.json').read_text());identity=build['identity'];inventory={p.relative_to(runtime).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in runtime.rglob('*') if p.is_file() and p.name not in ['runtime.json','BuildIdentity.cs','compile.log']};(runtime/'runtime.json').write_text(json.dumps(dict(format=1,kind='spatialwallpaper-runtime',stage='development',files=inventory,**identity),indent=2))
 if args.case in ['wrong-runtime','wrong-runtime-file']:
  original=runtime;wrong=out/'wrong-runtime';wrong.mkdir();manifest=json.loads((runtime/'runtime.json').read_text())
  for name in manifest['files']:
   source=runtime/name;target=wrong/name;target.parent.mkdir(parents=True,exist_ok=True)
   if args.case=='wrong-runtime-file' and name=='worker/player/Worker.exe':shutil.copyfile(source,target);target.write_bytes(target.read_bytes()+b'changed-development-negative')
   else:os.link(source,target)
  if args.case=='wrong-runtime':manifest['sdk']='wrong-sdk-version'
  (wrong/'runtime.json').write_text(json.dumps(manifest,indent=2));runtime=wrong
  report['original_worker_unchanged']=hashlib.sha256((original/'worker/player/Worker.exe').read_bytes()).hexdigest()==manifest['files']['worker/player/Worker.exe'];assert report['original_worker_unchanged']
 report['runtime']=str(runtime);report['runtime_manifest_sha256']=hashlib.sha256((runtime/'runtime.json').read_bytes()).hexdigest()
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
 files=[ROOT/'Supervisor/Tests/PreviewProgramChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PinnedProcessOwner.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PreviewStatusFile.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs']
 subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(FRAMEWORK/'System.Drawing.dll'),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
 report['process']={}
 with (out/'checks.log').open('w') as log:run_toolchain([str(out/'Checks.exe'),str(out),str(runtime),str(out/'healthy.spatial'),ids[0],args.case,str(ROOT/'Validation/Sdk'/('x'+out.name[:10])),str(DATA.parent),args.author_kind if args.author else 'fixture'],ROOT,log,120,report['process'])
 report['result']=json.loads((out/'result.json').read_text());assert report['sources']==sources();report['status']='passed-development-standalone-preview-'+args.case
finally:
 report['sources_unchanged']=report['sources']==sources();report['files']={p.relative_to(out).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in out.rglob('*') if p.is_file() and p.name not in ['endpoint.json','report.json'] and not p.is_relative_to(out/'runtime/worker/player')};(out/'report.json').write_text(json.dumps(report,indent=2));print(out)
