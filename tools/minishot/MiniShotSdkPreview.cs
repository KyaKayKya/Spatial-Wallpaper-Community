using System;using System.Diagnostics;using System.IO;using System.Reflection;using System.Security.Cryptography;using System.Threading.Tasks;using Newtonsoft.Json.Linq;using UnityEditor;using SpatialWallpaper.Sdk.Editor;
public static class MiniShotSdkPreview {
 static Type controller;
 static object Call(string name,params object[] args)=>controller.GetMethod(name,BindingFlags.Public|BindingFlags.Static).Invoke(null,args);
 static T Get<T>(string name)=>(T)controller.GetProperty(name,BindingFlags.Public|BindingFlags.Static).GetValue(null);
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static string Hash(string file){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(file))).Replace("-","").ToLowerInvariant();}
 public static async void Run(){string output=Environment.GetEnvironmentVariable("SDK_MINISHOT_OUTPUT");Exception failure=null;var report=new JObject();try{
  controller=typeof(ScriptSceneExporter).Assembly.GetType("SpatialWallpaper.Sdk.Editor.SandboxPreviewController");Need(controller!=null,"MiniShot-installed-preview-missing");
  var definition=MiniShotSdkBuilder.Create();string project=Path.GetFullPath("."),cache=Path.Combine(project,"minishot-preview");Call("Prepare",definition,cache);
  bool refused=false;try{Call("Export",Path.Combine(output,"before-preview.spatial"));}catch(TargetInvocationException e){refused=e.InnerException is InvalidOperationException;}Need(refused,"MiniShot-unpreviewed-export-accepted");
  await(Task)Call("StartAsync",Environment.GetEnvironmentVariable("SDK_MINISHOT_RUNTIME"),Path.Combine(project,"Attempts"),960,540,.53f,.298125f);
  double deadline=EditorApplication.timeSinceStartup+60;while(Get<string>("State")!="Running"){Need(Get<string>("Error")==null,"MiniShot-preview-error:"+Get<string>("Error"));Need(EditorApplication.timeSinceStartup<deadline,"MiniShot-preview-deadline");await Task.Delay(25);}
  var receipt=Get<JObject>("LastReceipt");Need(receipt!=null,"MiniShot-preview-no-presented-frame");
  var active=controller.GetField("active",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);var viewer=(Process)active.GetType().GetField("viewer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(active);string workspace=(string)active.GetType().GetField("workspace",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(active);
  File.WriteAllText(Path.Combine(output,"ready.json"),new JObject{{"viewer_pid",viewer.Id},{"workspace",workspace},{"receipt",receipt}}.ToString());
  await Task.Delay(3000);Need(Get<string>("State")=="Running"&&Get<string>("Error")==null,"MiniShot-preview-did-not-remain-running");
  string first=Path.Combine(output,"MiniShotGame.spatial"),same=Path.Combine(output,"same-snapshot.spatial");Call("Export",first);definition.displayName="Changed after preview";Call("Export",same);Need(Hash(first)==Hash(same),"MiniShot-export-drifted-from-presented-snapshot");
  report=new JObject{{"status","passed-installed-MiniShot-sandbox-preview-export"},{"receipt",Get<JObject>("LastReceipt")},{"package",first},{"package_sha256",Hash(first)},{"build",Get<string>("BuildId")},{"capabilities",new JArray(Get<string[]>("Capabilities"))},{"export_before_preview_refused",refused},{"same_previewed_bytes_exported",true},{"workspace",workspace},{"scope","installed SDK tarball, production LPAC Worker, native preview presentation and immutable export; not interactive gameplay or RC"}};
 }catch(Exception e){failure=e;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());UnityEngine.Debug.LogException(e);}
 finally{
  try{if(controller!=null)await(Task)Call("StopAsync");}catch(Exception e){File.WriteAllText(Path.Combine(output,"cleanup-error.txt"),e.ToString());if(failure==null)failure=e;}
  bool released=controller!=null&&Get<bool>("ResourcesReleased");string nativeError=controller==null?null:Get<string>("Error");report["resources_released"]=released;report["error"]=nativeError==null?JValue.CreateNull():new JValue(nativeError);
  if(failure==null&&(!released||nativeError!=null))failure=new Exception("MiniShot-preview-cleanup-unproven");
  if(failure==null)File.WriteAllText(Path.Combine(output,"result.json"),report.ToString());else{report["status"]="failed-MiniShot-sandbox-preview-export";File.WriteAllText(Path.Combine(output,"failed-result.json"),report.ToString());if(!File.Exists(Path.Combine(output,"error.txt")))File.WriteAllText(Path.Combine(output,"error.txt"),failure.ToString());}EditorApplication.Exit(failure==null?0:1);
 }}
}
