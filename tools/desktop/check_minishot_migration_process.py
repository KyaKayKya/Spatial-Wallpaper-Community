"""Bounded childless migration helper ownership tests; no GUI or real save access."""
import hashlib,json,pathlib,subprocess,sys,uuid
ROOT=pathlib.Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references

def main():
 out=ROOT/'Validation'/('migration-timeout-'+uuid.uuid4().hex[:8]);out.mkdir(parents=True);print(out,flush=True)
 paths=['Prototype/Assets/ContentRuntime/Runtime/MiniShotMigrationProcess.cs','tools/desktop/MiniShotMigrationProcessChecks.cs'];files=[ROOT/p for p in paths]
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'MiniShotMigrationProcessChecks.exe'),*map(str,files)]
 result=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr)
 if result.returncode:return result.returncode
 result=subprocess.run([str(out/'MiniShotMigrationProcessChecks.exe'),str(out/'data')],capture_output=True,text=True,timeout=10);(out/'checks.log').write_text(result.stdout+result.stderr,encoding='utf8');print(result.stdout+result.stderr)
 (out/'report.json').write_text(json.dumps({'passed':result.returncode==0,'exit':result.returncode,'scope':'real owned childless delayed processes; no actual saves, GUI or input','sources':{p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in paths}},indent=2),encoding='utf8');return result.returncode
if __name__=='__main__':raise SystemExit(main())
