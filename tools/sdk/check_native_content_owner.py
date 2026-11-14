from pathlib import Path
import sys,uuid,json,hashlib,os,subprocess,shutil
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/sdk'));from check_contracts import DATA
from trusted_client_sources import client_sources
from windows_framework import references
mode=sys.argv[2] if len(sys.argv)>2 else 'start-failure';negative='--negative' in sys.argv;run=uuid.uuid4().hex;out=root/'Validation/Content/sdk-runtime/F/native-owner'/run;out.mkdir(parents=True);runtime=Path(sys.argv[1]).resolve();bad=out/'bad-runtime';bad.mkdir();manifest=json.loads((runtime/'runtime.json').read_text())
for name in manifest['files']:
 source=runtime/name;target=bad/name;target.parent.mkdir(parents=True,exist_ok=True)
 if name=='SpatialWallpaper.Preview.exe':target.write_bytes(b'invalid PE controlled process-start failure');manifest['files'][name]=hashlib.sha256(target.read_bytes()).hexdigest()
 else:os.link(source,target)
(bad/'runtime.json').write_text(json.dumps(manifest))
original=root/'Prototype/Assets/ContentRuntime/Runtime/NativeContentSurface.cs';source=original.read_text();assert source.count('IntPtr parent=OwnWindow();')==1;source=source.replace('IntPtr parent=OwnWindow();','IntPtr parent=IntPtr.Zero;').replace('ClipChildren(parent);','/* controlled parent-window bypass */')
if mode in ['verify-cancel','release-pending']:
 needle='await Task.Run(()=>TrustedRuntimeDistribution.Verify(root,identity,Path.Combine(root,"SpatialWallpaper.Preview.exe"),startupToken));';assert source.count(needle)==1;source=source.replace(needle,'await Task.Run(async()=>{await Task.Delay(Timeout.Infinite,startupToken);return new JObject();});')
 if negative and mode=='verify-cancel':assert source.count('startupCancellation?.Cancel();')==1;source=source.replace('startupCancellation?.Cancel();','/* controlled missing startup cancellation */')
 if negative and mode=='release-pending':
  needle='public async Task<JObject> CallReadyAsync(string command,JObject payload){';assert source.count(needle)==1;source=source.replace(needle,needle+'await StartAsync().ConfigureAwait(false);')
elif mode=='stop-inflight' and negative:
 needle='if(!stopping.IsCompleted||stopRequested&&process!=null)throw new InvalidOperationException("content-native-stop-pending");';assert source.count(needle)==1;source=source.replace(needle,'')
candidate=out/'NativeContentSurface.cs'
if mode=='status-gap':
 source=source.replace('PreviewStatusFile.Read(', 'StatusReadProbe.Read(')
source+='''
static class StatusReadProbe {
 internal static bool OnceMissing;
 internal static Newtonsoft.Json.Linq.JObject Read(string path,int pid,long birth)=>Read(path,pid,birth,out int unavailable);
 internal static Newtonsoft.Json.Linq.JObject Read(string path,int pid,long birth,out int unavailable){if(OnceMissing){OnceMissing=false;unavailable=32;return null;}return SpatialWallpaper.Sdk.Hosting.PreviewStatusFile.Read(path,pid,birth,out unavailable);}
}
'''
candidate.write_text(source)
if mode=='status-gap':
 receipt=json.loads(Path(sys.argv[sys.argv.index('--status-receipt')+1]).read_text(encoding='utf-8-sig'))['receipt'] if '--status-receipt' in sys.argv else json.loads((runtime.parent/'report.json').read_text())['result']['receipt'];(out/'status-receipt.json').write_text(json.dumps(receipt))
checks=out/'Checks.cs';checks.write_text('''using System;using System.IO;using System.Threading;using System.Diagnostics;using System.Reflection;using System.Threading.Tasks;using Newtonsoft.Json.Linq;using SpatialWallpaper.Content;using SpatialWallpaper.Sdk.Hosting;
static class DisplayViewport {public static IntPtr RegisteredRenderWindow=>IntPtr.Zero;}
static class Checks {
 static int Main(string[] a){try{Run(a).GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 static async Task Run(string[] a){var owner=new NativeContentSurface(a[0],Path.Combine(a[1],"state"),Path.Combine(a[1],"attempts"),640,360,.6f,.3375f);
 if(a[2]=="status-gap"){
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;var kind=typeof(NativeContentSurface);string path=Path.Combine(a[1],"status.json");
  using(var self=PinnedProcessOwner.PinCurrent())using(var observer=Process.GetCurrentProcess())using(var connection=new ContentSurfaceConnection("SpatialWallpaper.ContentSurface."+Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N"),self.Id,self.CreationFileTime)){
   kind.GetField("process",flags).SetValue(owner,observer);kind.GetField("connection",flags).SetValue(owner,connection);kind.GetField("creation",flags).SetValue(owner,self.CreationFileTime);kind.GetField("workspace",flags).SetValue(owner,a[1]);
   var value=new JObject{{"format",1},{"sequence",1},{"state","Running"},{"viewer_pid",self.Id},{"viewer_creation_filetime",self.CreationFileTime},{"host_pid",self.Id},{"host_creation_filetime",self.CreationFileTime},{"runtime_stage","development"},{"receipt",JObject.Parse(File.ReadAllText(Path.Combine(a[1],"status-receipt.json")))},{"error",null},{"window_resources_released",false},{"host_resources_released",false},{"forced_host_termination",false},{"host_diagnostic_tail",""}};
   File.WriteAllText(path,value.ToString());if(owner.ObserveFailure()!=null||owner.ObservedReceipt==null)throw new Exception("status-gap-fixture-never-observed-valid-status");
   using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None)){
    var time=Stopwatch.StartNew();string missing=owner.ObserveFailure();if(missing!=null||owner.ObservedReceipt!=null||time.ElapsedMilliseconds>=500)throw new Exception("transient-status-gap-retired-owner-or-kept-proof:"+missing);
    Thread.Sleep(1100);if(owner.ObserveFailure()!="content-native-status-missing")throw new Exception("status-gap-renewed-missing-heartbeat");
   }
   value["sequence"]=2;File.WriteAllText(path,value.ToString());if(owner.ObserveFailure()!=null||owner.ObservedReceipt==null)throw new Exception("status-gap-did-not-recover-on-fresh-progress");
   Thread.Sleep(1100);value["sequence"]=3;File.WriteAllText(path,value.ToString());StatusReadProbe.OnceMissing=true;
   if(owner.ObserveFailure()!=null||owner.ObservedReceipt==null)throw new Exception("observer-poll-gap-single-sharing-conflict-falsely-retires-fresh-owner");
   var recovered=owner.StatusDiagnostic;if((int)recovered["first_unavailable"]!=32||(int)recovered["unavailable"]!=0||(int)recovered["read_attempts"]!=2)throw new Exception("status-reread-diagnostic-proof-missing");File.WriteAllText(Path.Combine(a[1],"status-reread-proof.json"),recovered.ToString());
   Thread.Sleep(1100);StatusReadProbe.OnceMissing=true;if(owner.ObserveFailure()!="content-native-heartbeat-stale"||owner.ObservedReceipt!=null)throw new Exception("one-reread-renewed-stale-heartbeat");
   value["sequence"]=1;File.WriteAllText(path,value.ToString());if(owner.ObserveFailure()!="content-native-status-regressed"||owner.ObservedReceipt!=null)throw new Exception("status-gap-accepted-regressed-progress-or-kept-proof");
   kind.GetField("process",flags).SetValue(owner,null);kind.GetField("connection",flags).SetValue(owner,null);owner.Dispose();
  }
 }
 else if(a[2]=="release-pending"){var pending=owner.StartAsync();var release=owner.CallReadyAsync("ReleaseStatic",new JObject());if(await Task.WhenAny(release,Task.Delay(500))!=release)throw new Exception("synchronous-static-release-waited-for-startup");bool refused=false;try{await release;}catch(InvalidOperationException){refused=true;}await owner.StopAsync();try{await pending;}catch(OperationCanceledException){}if(!refused||!owner.ResourcesReleased)throw new Exception("pending-static-release-was-not-rejected-before-startup");}
 else if(a[2]=="verify-cancel"){var pending=owner.StartAsync();await Task.Delay(25);var ending=owner.StopAsync();if(await Task.WhenAny(ending,Task.Delay(1500))!=ending)throw new Exception("startup-stop-did-not-cancel-pending-verification");await ending;bool cancelled=false;try{await pending;}catch(OperationCanceledException){cancelled=true;}if(!cancelled||!owner.ResourcesReleased)throw new Exception("startup-stop-late-resource-ownership");}
 else if(a[2]=="stop-inflight"){var flags=BindingFlags.Instance|BindingFlags.NonPublic;using(var own=PinnedProcessOwner.PinCurrent())using(var observer=Process.GetCurrentProcess())using(var connection=new ContentSurfaceConnection("SpatialWallpaper.ContentSurface."+Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N"),own.Id,own.CreationFileTime)){typeof(NativeContentSurface).GetField("process",flags).SetValue(owner,observer);typeof(NativeContentSurface).GetField("connection",flags).SetValue(owner,connection);typeof(NativeContentSurface).GetField("stopping",flags).SetValue(owner,new TaskCompletionSource<bool>().Task);bool refused=false;try{await owner.StartAsync();}catch(InvalidOperationException){refused=true;}if(!refused)throw new Exception("new-start-accepted-while-stop-still-owned");}}
 else{Exception start=null,stop=null;try{await owner.StartAsync();}catch(Exception e){start=e;}try{await owner.StopAsync();}catch(Exception e){stop=e;}Console.WriteLine("start="+start+" stop="+stop+" released="+owner.ResourcesReleased);if(!(start is System.ComponentModel.Win32Exception)||stop!=null||!owner.ResourcesReleased)throw new Exception("failed-process-start-retained-unassociated-observer");}
 File.WriteAllText(Path.Combine(a[1],"result.json"),new JObject{{"case",a[2]},{"scope","controlled invalid PE, pending verification, stop-state or bounded status-gap injection; actual reader/retained self observer, no UI/actual Host rendering claim; OwnWindow/clip bypass"}}.ToString());}
}''')
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(root/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
files=client_sources(root)+[candidate,checks,root/'Packages/com.spatialwallpaper.sdk/Runtime/Security/Rules.cs',root/'Packages/com.spatialwallpaper.sdk/Runtime/Security/Rules.Generated.cs',root/'Prototype/Assets/Tracking/Core/RuntimeDirectory.cs'];report={'status':'failed','case':mode,'negative':negative,'original_source_sha256':hashlib.sha256(original.read_bytes()).hexdigest(),'controlled_source_sha256':hashlib.sha256(candidate.read_bytes()).hexdigest(),'scope':'controlled native owner lifecycle; no actual Unity parent'}
try:
 subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:System.Drawing.dll','-r:System.Windows.Forms.dll','-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
 result=subprocess.run([str(out/'Checks.exe'),str(bad),str(out),mode],capture_output=True,text=True,timeout=20);(out/'checks.log').write_text(result.stdout+result.stderr);report['exit_code']=result.returncode;result.check_returncode();report['status']='passed-controlled-native-owner-lifecycle'
finally:(out/'report.json').write_text(json.dumps(report,indent=2));print(out)
