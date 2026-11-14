"""Use real PrivateStore IO and exact product schema4 MiniShot archive; no GUI or input."""
import argparse,hashlib,json,pathlib,shutil,subprocess,sys,uuid
ROOT=pathlib.Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--archive',type=pathlib.Path,required=True);p.add_argument('--supervisor',type=pathlib.Path,required=True);a=p.parse_args()
 out=ROOT/'Validation'/('mig-'+uuid.uuid4().hex[:8]);out.mkdir(parents=True);print(out,flush=True)
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'MiniShotSaveMigrationChecks.exe.config')
 paths=['tools/desktop/MiniShotSaveMigrationChecks.cs','Supervisor/Core/FaultJournal.cs','Supervisor/Broker/BrokerSession.cs','Supervisor/Broker/PrivateStore.cs','Supervisor/Hosting/MiniShotSaveMigrationHost.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/LegacyMiniShotIdentity.cs'];files=[ROOT/x for x in paths]
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'MiniShotSaveMigrationChecks.exe'),*map(str,files)]
 result=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr)
 if result.returncode:return result.returncode
 core=subprocess.run([str(out/'MiniShotSaveMigrationChecks.exe'),str(out/'data'),str(a.archive.resolve())],capture_output=True,text=True,timeout=20);(out/'core.log').write_text(core.stdout+core.stderr,encoding='utf8');print(core.stdout+core.stderr)
 if core.returncode:return core.returncode
 cli=subprocess.run([str(a.supervisor.resolve()),'--migrate-minishot-save',str(out/'data/cli.json')],capture_output=True,text=True,timeout=15);(out/'cli.log').write_text(cli.stdout+cli.stderr,encoding='utf8');print(cli.stdout+cli.stderr)
 payload=json.loads(cli.stdout);assert cli.returncode==0 and payload['status']=='imported' and payload['hit_count']==10
 report={'passed':True,'core_exit':core.returncode,'helper_exit':cli.returncode,'helper_sha256':hashlib.sha256(a.supervisor.read_bytes()).hexdigest(),'archive_sha256':hashlib.sha256(a.archive.read_bytes()).hexdigest(),'source_sha256':{p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in paths},'scope':'real PrivateStore IO plus actual production Supervisor CLI; no manager GUI, rendered application, camera or injected input','migration':payload};(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf8');return 0
if __name__=='__main__':raise SystemExit(main())
