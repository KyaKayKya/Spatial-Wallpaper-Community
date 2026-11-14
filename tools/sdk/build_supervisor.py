"""Compile the independent Windows Supervisor from shared production sources."""
from pathlib import Path
import argparse,subprocess,shutil,json,hashlib
import sys
from check_contracts import ROOT,DATA
from provenance import sources
from windows_framework import references,identity
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--play-broker',action='store_true');parser.add_argument('--source-override',type=Path);args=parser.parse_args();out=args.output.resolve();name='PlayBroker' if args.play_broker else 'Supervisor'
 if out.exists():raise ValueError('Supervisor output must be a new directory')
 out.mkdir(parents=True);receipt=sources();shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/(name+'.exe.config'))
 files=[ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs',ROOT/('Supervisor/PlayBroker.cs' if args.play_broker else 'Supervisor/Host.cs'),ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs']
 files += list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities').glob('*.cs'))
 files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Data/SoundData.cs')
 if args.play_broker:files.append(ROOT/'Supervisor/Preview/DeterministicLlmServer.cs')
 files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeSession.cs')
 files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PinnedProcessOwner.cs')
 files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/NativeInputAuthorityReader.cs')
 files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PreviewInputChannel.cs')
 files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/NativeScriptControl.cs')
 files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/SupervisorManagerConnection.cs')
 files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimePackageInspection.cs')
 files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Native/SceneCommandCatalog.Generated.cs')
 files += list((ROOT/'Supervisor/Session').glob('*.cs'))
 for folder in ['Supervisor/Core','Supervisor/Windows','Supervisor/Hosting','Supervisor/Broker']:files += list((ROOT/folder).glob('*.cs'))
 from release_identity import build_identity
 version,assembly=build_identity(out);files.append(assembly)
 mutations={}
 if args.source_override:
  override=args.source_override.resolve();assert override.is_relative_to(ROOT/'Validation/Content/sdk-runtime') and override.name in ['ValidatorLauncher.cs','WindowsWorkerLauncher.cs','TrustedAudioProcess.cs','Host.cs'],'only owned cleanup fault fixture sources may be overridden'
  original=next(f for f in files if f.name==override.name);files=[override if f==original else f for f in files];mutations[original.relative_to(ROOT).as_posix()]=hashlib.sha256(override.read_bytes()).hexdigest()
 subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-target:winexe','-r:'+str(out/'Newtonsoft.Json.dll'),*references(),'-out:'+str(out/(name+'.exe')),*map(str,files)],check=True)
 sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
 from native_build import compile_probe
 compile_probe(ROOT/'Supervisor/Interactive/ProcessAudioSampler.cpp',out/'AudioSampler.exe')
 assert sources()==receipt,'Supervisor source changed during build'
 (out/'build.json').write_text(json.dumps({'identity':version,'sources':receipt,'mutated_sources_sha256':mutations,**({} if args.play_broker else {'host_cleanup_protocol':1}),'framework_references':identity(),'files':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir() if p.is_file()}},indent=2));print(out)
if __name__=='__main__':main()
