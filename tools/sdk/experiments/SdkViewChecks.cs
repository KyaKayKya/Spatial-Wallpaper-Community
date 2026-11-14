using System;using System.IO;using System.Reflection;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEngine;using SpatialWallpaper.Sdk;using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Execution;using SpatialWallpaper.Sdk.Security;
public static class SdkViewChecks {
 const string Eye="SpatialWallpaper.Sdk::View::GetEye()->UnityEngine.Vector3",Size="SpatialWallpaper.Sdk::View::GetSize()->UnityEngine.Vector3";
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);Debug.Log("PASS "+reason);}
 static void Vector(object value,Vector3 wanted,string reason){var v=(VectorValue)value;Need(Mathf.Abs(v.X-wanted.x)<.000001f&&Mathf.Abs(v.Y-wanted.y)<.000001f&&Mathf.Abs(v.Z-wanted.z)<.000001f,reason);}
 public static void Run(){string output=Environment.GetEnvironmentVariable("SDK_VIEW_OUTPUT");try{
  var api=typeof(SceneDefinition).Assembly.GetType("SpatialWallpaper.Sdk.View");Need(api!=null,"read-only-view-api-missing");
  Need(api.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly).Length==2,"view-exposes-only-two-value-queries");
  var bind=api.GetMethod("BindPlay",BindingFlags.NonPublic|BindingFlags.Static);Need(bind!=null,"ordinary-play-view-binding-missing");
  bool unbound=false;try{api.GetMethod("GetEye").Invoke(null,null);}catch(TargetInvocationException e){unbound=e.InnerException is InvalidOperationException;}Need(unbound,"ordinary-view-without-session-refused");
  var content=new GameObject("View test content");var licenses=new JArray(new JObject{{"name","View fixture"},{"source","Spatial Wallpaper tools/sdk"},{"license","MIT"},{"attribution","Spatial Wallpaper"}});
  var build=Admission.Admit(ScriptSceneExporter.Export(content,new Vector3(.53f,.298125f,2),licenses),"windows-x86_64");
  var camera=new GameObject("Trusted test view").AddComponent<Camera>();var projection=camera.gameObject.AddComponent<WindowProjection>();projection.viewportManagedExternally=true;
  using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(build,domain)){
   bool missing=false;try{host.Call(Eye,null,new object[0]);}catch(InvalidOperationException e){missing=e.Message=="view-unavailable";}Need(missing,"VM-unbound-view-refused");
   var configure=typeof(UnityOwnedHost).GetMethod("ConfigureView");Need(configure!=null,"trusted-view-host-binding-missing");configure.Invoke(host,new object[]{projection});
   Vector(host.Call(Eye,null,new object[0]),new Vector3(0,0,-.6f),"VM-default-eye");Vector(host.Call(Size,null,new object[0]),new Vector3(.53f,.298125f,0),"VM-authored-size");
   projection.displayWidth=.8f;projection.displayHeight=.4f;Need(projection.SetScaledEye(new Vector3(.15f,-.04f,-.8f),.5f),"scaled-eye-applied");
   Vector(host.Call(Eye,null,new object[0]),projection.EyeScreen,"VM-updated-author-eye");Vector(host.Call(Size,null,new object[0]),new Vector3(.8f,.4f,0),"VM-updated-effective-window");
   bind.Invoke(null,new object[]{projection});Need((Vector3)api.GetMethod("GetEye").Invoke(null,null)==projection.EyeScreen,"ordinary-play-same-author-eye");Need((Vector3)api.GetMethod("GetSize").Invoke(null,null)==new Vector3(.8f,.4f,0),"ordinary-play-same-window");bind.Invoke(null,new object[]{null});
   host.Dispose();bool stopped=false;try{host.Call(Eye,null,new object[0]);}catch(InvalidOperationException){stopped=true;}Need(stopped,"stopped-host-view-refused");
  }
  UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(content);
  File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"status","passed-selected-view-binding"},{"engine",Application.unityVersion},{"scope","actual Unity native host and ordinary API value binding; not automatic Play lifecycle or LPAC"}}.ToString());EditorApplication.Exit(0);
 }catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}}
}
