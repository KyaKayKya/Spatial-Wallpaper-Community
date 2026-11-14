// Original MIT trusted transport fixture; contains no author source or assembly.
using System;using System.IO;using System.Collections.Generic;using UnityEngine;using SpatialWallpaper.Sdk.Execution;using SpatialWallpaper.Sdk.Security;using SpatialWallpaper.Sdk.Hosting;
public sealed class StageDWorker:MonoBehaviour {
 OwnedDomain domain;UnityOwnedHost host;VmRuntime runtime;WindowsFrameWriter writer;WorkerFrameCapture capture;int updates,freezeAt=-1;long finalSequence;string mode="finite";bool faultTriggered;
 void Start(){try{Debug.Log("SDK_D_BOOT");
  var args=Environment.GetCommandLineArgs();var values=new Dictionary<string,string>(StringComparer.Ordinal);
  for(int i=0;i+1<args.Length;i++)if(args[i].StartsWith("--sdk-",StringComparison.Ordinal)){if(values.ContainsKey(args[i]))throw new Exception("duplicate-launch-argument");values.Add(args[i],args[++i]);}
  var identity=new WorkerIdentity(Guid.Parse(values["--sdk-attempt"]),Guid.Parse(values["--sdk-instance"]),uint.Parse(values["--sdk-generation"]));
  if(values.ContainsKey("--sdk-fixture-mode"))mode=values["--sdk-fixture-mode"];
  if(mode!="finite"&&mode!="continuous"&&mode!="render-stall"&&mode!="bridge-block"&&mode!="cpu")throw new Exception("unknown-fixture-mode");
  Debug.Log("SDK_D_ARGUMENTS");var build=Admission.Admit(File.ReadAllBytes(values["--sdk-package"]),"windows-x86_64");
  Debug.Log("SDK_D_ADMITTED");ExecutionBindings.Require(build,RenderingBindings.Supports);Debug.Log("SDK_D_BINDINGS");domain=new OwnedDomain(identity.Instance,identity.Generation);Debug.Log("SDK_D_NATIVE_HOST");host=new UnityOwnedHost(build,domain);runtime=new VmRuntime(build,domain,host,new ExecutionQuota(10000,32,1000));host.Bind(runtime);
  Debug.Log("SDK_D_RUNTIME");writer=new WindowsFrameWriter(domain.Ledger,identity,640,360,new IntPtr(long.Parse(values["--sdk-frame-handle"])));
  Debug.Log("SDK_D_WRITER");var view=StageCVisuals.Create(host.Root.transform.GetChild(0));host.ConfigurePresentation(view);capture=view.gameObject.AddComponent<WorkerFrameCapture>();capture.Initialize(view,writer,host,domain,640,360,(Newtonsoft.Json.Linq.JObject)build.Manifest["render_profile"],(observed,hardware)=>Debug.Log("SDK_D_RENDER_PROFILE "+observed.ToString(Newtonsoft.Json.Formatting.None)));
  Debug.Log("SDK_D_CAPTURE");QualitySettings.vSyncCount=0;Application.runInBackground=true;Application.targetFrameRate=60;runtime.Start();Debug.Log("SDK_D_RUNNING");
  if(mode=="render-stall"||mode=="bridge-block"||mode=="cpu"){var heartbeat=new System.Threading.Thread(Pulse){IsBackground=true};heartbeat.Start();}
 }catch(Exception error){Debug.LogException(error);Application.Quit(20);}}
 void Update(){if(runtime==null||writer==null||capture==null)return;try{
  if(writer.Error!=0||capture.Error!=null)throw new Exception("transport-or-capture-failed");
  if(!faultTriggered&&capture.CompletedSequence>=5&&mode!="finite"&&mode!="continuous"){
   faultTriggered=true;Debug.Log("SDK_D_INJECTED_"+mode);
   if(mode=="render-stall")capture.Stop();
   else if(mode=="bridge-block")System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
   else if(mode=="cpu")Spin();
  }
  if(updates<60){runtime.Update();if(++updates==60){freezeAt=Time.frameCount;Debug.Log("SDK_D_UPDATES60 captured="+capture.CompletedSequence);}}
  if(finalSequence==0&&freezeAt>=0&&capture.LastCapturedUnityFrame>=freezeAt)finalSequence=capture.CompletedSequence;
  if(mode=="finite"&&finalSequence>0&&writer.LastSent>=finalSequence)Application.Quit(0);
 }catch(Exception error){Debug.LogException(error);Application.Quit(21);}}
 void OnApplicationQuit(){capture?.Stop();writer?.Dispose();runtime?.Stop();host?.Dispose();}
 static void Pulse(){while(true){Console.WriteLine("SDK_D_FIXTURE_HEARTBEAT");System.Threading.Thread.Sleep(50);}}
 static void Spin(){while(true)System.Threading.Thread.SpinWait(10000);}
}
