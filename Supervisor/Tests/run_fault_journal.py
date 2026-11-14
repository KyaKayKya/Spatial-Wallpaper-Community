from pathlib import Path
import subprocess,shutil,uuid
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data');OUT=ROOT/'Validation/Content/sdk-runtime/D/journal'/uuid.uuid4().hex
OUT.mkdir(parents=True);shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',OUT/'Newtonsoft.Json.dll')
command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-define:SDK_SUPERVISOR_TESTS','-r:'+str(OUT/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(OUT/'JournalChecks.exe'),str(ROOT/'Supervisor/Tests/FaultJournalChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs'),*map(str,(ROOT/'Supervisor/Core').glob('*.cs'))]
result=subprocess.run(command,capture_output=True,text=True);print(result.stdout+result.stderr)
if result.returncode:raise SystemExit(result.returncode)
result=subprocess.run([str(OUT/'JournalChecks.exe'),str(OUT/'state')],capture_output=True,text=True,timeout=30);print(result.stdout+result.stderr);raise SystemExit(result.returncode)
