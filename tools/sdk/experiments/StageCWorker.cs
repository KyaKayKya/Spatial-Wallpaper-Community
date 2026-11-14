// Trusted standalone VM test host. This project contains no author source or assemblies.
using System;using System.IO;using UnityEngine;using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Security;using SpatialWallpaper.Sdk.Execution;
public sealed class StageCWorker:MonoBehaviour {
 VmRuntime runtime;UnityOwnedHost host;OwnedDomain domain;Camera view;int frames;string output,run;JObject fields;
 void Start(){
  try{var args=Environment.GetCommandLineArgs();string package=null,map=null;
   for(int i=0;i+1<args.Length;i++){if(args[i]=="--sdk-package")package=args[i+1];if(args[i]=="--sdk-fields")map=args[i+1];if(args[i]=="--sdk-output")output=args[i+1];if(args[i]=="--sdk-run")run=args[i+1];}
   fields=JObject.Parse(File.ReadAllText(map));var build=Admission.Admit(File.ReadAllBytes(package),"windows-x86_64");domain=new OwnedDomain(Guid.NewGuid(),1);
   host=new UnityOwnedHost(build,domain);runtime=new VmRuntime(build,domain,host,new ExecutionQuota(10000,32,1000));host.Bind(runtime);
   view=StageCVisuals.Create(host.Root.transform.GetChild(0));Application.runInBackground=true;runtime.Start();
  }catch(Exception error){Debug.LogException(error);Application.Quit(20);}
 }
 void Update(){
  if(runtime==null||runtime.State!=VmState.Running)return;
  try{
   runtime.Update();if(++frames!=60)return;
   int counter=(int)runtime.Field(0,(string)fields["counter"]),peer=(int)runtime.Field(1,(string)fields["peer"]);
   bool active=(bool)runtime.Field(0,(string)fields["active"]);int items=((OwnedList)runtime.Field(0,(string)fields["items"])).Count;
   float angle=Quaternion.Angle(Quaternion.identity,host.Root.transform.GetChild(0).localRotation);
   if(counter!=63||peer!=140||!active||items!=62||Math.Abs(angle-60)>.01f)throw new Exception("standalone-behaviour-mismatch");
   StageCVisuals.Capture(view,Path.Combine(output,"frame.png"));
   File.WriteAllText(Path.Combine(output,"worker-result.json"),new JObject{{"run_id",run},{"counter",counter},{"peer",peer},{"active",active},{"items",items},{"angle",angle},{"frames",frames},{"graphics",SystemInfo.graphicsDeviceType.ToString()},{"device",SystemInfo.graphicsDeviceName},{"engine",Application.unityVersion}}.ToString());
   runtime.Stop();host.Dispose();Application.Quit();
  }catch(Exception error){Debug.LogException(error);Application.Quit(21);}
 }
}
