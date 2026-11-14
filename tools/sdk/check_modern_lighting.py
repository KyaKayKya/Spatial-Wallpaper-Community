"""Check modern light properties, rejection and arbitrary-LookAt Editor render roundtrip; not LPAC acceptance."""
from pathlib import Path
import argparse,json,os,uuid
from build_execution import ROOT,UNITY,create
from provenance import sources
from toolchain_process import run_toolchain

def validate_result(value,run):
 rows=value['cases'];expected={(kind,flag) for kind in ('Point','Spot','Directional') for flag in (True,False)}
 if value['run_id']!=run or len(rows)!=6 or {(row['type'],row['pipeline_settings']) for row in rows}!=expected or not all(row['properties_preserved'] is True and type(row['pipeline_settings']) is bool for row in rows):raise ValueError('lighting-result-incomplete')

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--reuse-project',type=Path);args=p.parse_args()
 run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/G/modern-lighting'/run;out.mkdir(parents=True);print(out,flush=True)
 project=args.reuse_project.resolve() if args.reuse_project else ROOT/'Validation/Sdk'/('gl'+run[:10])
 marker=project/'modern-lighting-owner.json';owner={'owner':'sdk-modern-lighting','project':str(project)}
 if args.reuse_project:
  if project.parent!=(ROOT/'Validation/Sdk').resolve() or not marker.is_file() or json.loads(marker.read_text())!=owner:raise ValueError('not-owned-modern-lighting-project')
 else:project.mkdir();marker.write_text(json.dumps(owner))
 create(project,[('StageGModernLightingChecks.cs',True),('StageGModernLightingVisuals.cs',True)]);receipt=sources();env=os.environ.copy();env.update(SDK_G_LIGHT_OUTPUT=str(out),SDK_G_LIGHT_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
 report={'status':'failed','sources':receipt,'project':str(project),'clean_project':not bool(args.reuse_project),'scope':__doc__,'toolchain':{}}
 try:
  with (out/'toolchain.log').open('w',encoding='utf-8') as log:run_toolchain([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageGModernLightingChecks.Run','-logFile',str(out/'unity.log')],ROOT,log,600,report['toolchain'],env=env)
  value=json.loads((out/'result.json').read_text(encoding='utf-8-sig'))
  validate_result(value,run)
  if {row['case'] for row in value['rejected']}!={'disabled','area','cookie','baked','culling-mask','bounce','additional-shadow-tier'} or len(value['rejected'])!=7:raise ValueError('lighting-negative-result-incomplete')
  if {row['type'] for row in value['rendered']}!={'Point','Spot','Directional'} or len(value['rendered'])!=3 or any(row['different_rgb_channels']!=0 or row['light_control_different_channels']<=100 or row['shadow_control_different_channels']<=100 for row in value['rendered']):raise ValueError('lighting-render-result-incomplete')
  if sources()!=receipt:raise ValueError('lighting-source-drift')
  report.update(status='passed-modern-light-editor-render-roundtrip-not-lpac',result=value);return 0
 except BaseException as error:report['primary_failure']=repr(error);raise
 finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out,flush=True)

if __name__=='__main__':raise SystemExit(main())
