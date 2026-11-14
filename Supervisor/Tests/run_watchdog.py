from pathlib import Path
import subprocess,uuid
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data');OUT=ROOT/'Validation/Content/sdk-runtime/D/watchdog'/uuid.uuid4().hex
OUT.mkdir(parents=True)
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(DATA/'Managed/Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(OUT/'Checks.exe'),str(ROOT/'Supervisor/Tests/WatchdogChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs'),*map(str,(ROOT/'Supervisor/Core').glob('*.cs'))]
r=subprocess.run(cmd,capture_output=True,text=True);print(r.stdout+r.stderr)
if r.returncode:raise SystemExit(r.returncode)
raise SystemExit(subprocess.call([str(OUT/'Checks.exe')],timeout=30))
