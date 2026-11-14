"""Modern bounded presentation export and native state probe; not RC acceptance."""
from pathlib import Path
import argparse,json,os,uuid
from build_execution import ROOT,UNITY,create
from provenance import sources
from toolchain_process import run_toolchain

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--ordinary-play',action='store_true');p.add_argument('--no-domain-reload',action='store_true');args=p.parse_args();assert not args.no_domain_reload or args.ordinary_play
 run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/G/modern-presentation'/run;out.mkdir(parents=True);project=ROOT/'Validation/Sdk'/('gp'+run[:10]);print(out,flush=True)
 create(project,[('StageGPresentationChecks.cs',True),('StageGPresentationVisuals.cs',True),('StageGPresentationPlayChecks.cs',True)]);receipt=sources();report={'status':'failed','sources':receipt,'project':str(project),'ordinary_play':args.ordinary_play,'toolchain':{}}
 env=os.environ.copy();env.update(SDK_G_PRESENTATION_OUTPUT=str(out),SDK_G_PRESENTATION_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
 env['SDK_G_PRESENTATION_NO_RELOAD']='1' if args.no_domain_reload else '0'
 try:
  command=[UNITY,'-batchmode','-projectPath',str(project),'-executeMethod','StageGPresentationPlayChecks.Run' if args.ordinary_play else 'StageGPresentationChecks.Run','-logFile',str(out/'unity.log')]
  if not args.ordinary_play:command.insert(2,'-quit')
  with (out/'toolchain.log').open('w') as log:run_toolchain(command,ROOT,log,600,report['toolchain'],env=env)
  result=json.loads((out/'result.json').read_text(encoding='utf-8-sig'));assert result['run_id']==run
  if args.ordinary_play:
   assert all(result[key] is True for key in ['ordinary_play','opt_out_preserved','explicit_stop_restored','restart_preserved'])
   assert result['domain_reload_disabled'] is args.no_domain_reload
  else:
   assert result['state_preserved'] is True
   assert result['live_profile_native_bytes_after_node_destroy']>=65536
   assert len(result['rejected'])==7 and {row['case'] for row in result['rejected']}=={'camera','clip','disabled','child','ambient-conflict','bloom-range','arbitrary-volume'}
   assert result['rendered']['different_rgb_channels']==0 and set(result['rendered']['controls'])=={'bloom','tonemapping','exposure','saturation','vignette','ambient','background'} and all(value>100 for value in result['rendered']['controls'].values())
  assert sources()==receipt,'presentation-source-drift';report.update(status='passed-modern-presentation-ordinary-play-state' if args.ordinary_play else 'passed-modern-presentation-editor-state-render-not-LPAC',result=result)
 finally:(out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)

if __name__=='__main__':main()
