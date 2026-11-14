using System;
using System.IO;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using UnityEngine;
using UnityEditor;

namespace SpatialWallpaper.Desktop.Editor {
 // Real owned renderer/Win32 failure against the production owner methods.
 // No test switch, alternate desktop owner or user data in product code.
 public static class NativeRuntimeFailureChecks {
  static int checks;static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static void Need(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
  static Task Invoke(DesktopRuntime runtime,string name,params object[] args)=>(Task)typeof(DesktopRuntime).GetMethod(name,Private).Invoke(runtime,args);
  public static void Run(){
   string output=Environment.GetEnvironmentVariable("SPATIAL_NATIVE_FAILURE_OUTPUT"),player=Environment.GetEnvironmentVariable("SPATIAL_NATIVE_FAILURE_PLAYER"),profileFile=Environment.GetEnvironmentVariable("SPATIAL_NATIVE_FAILURE_PROFILE");
   var report=new JObject{{"scope","Editor invokes original DesktopRuntime owner methods with real standalone renderer and controlled Win32 self-parent failure; no GUI/quality/performance or whole Launch claim"}};
   NativeDesktopTarget target=null;GameObject root=null;object window=null;FieldInfo originalParent=null;object savedParent=null;
   try{
    var profile=JObject.Parse(File.ReadAllText(profileFile)).ToObject<CalibrationProfile>();var display=DesktopPlatform.Displays()[0];Need(profile.manual.display_id==display.Id,"fixture-display");
    var pose=new NativeDesktopPose(profile,display,true);var selection=new DesktopAppliedSelection("showcase",Library.ArtworkSource.BuiltinNative,null,null,profile,new Sdk.Capabilities.SoundMix());
    var config=new JObject{{"artwork","showcase"},{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"desktop",true},{"pose",pose.Advance(null,QpcClock.Now,0,false)}};
    target=new NativeDesktopTarget(player,Path.Combine(output,"render"),display.Id,config);Need(target.Ready.Wait(35000),"owned-renderer-ready");target.AttachReady();Need(target.Verify(),"actual-child-attached");
    root=new GameObject("Owned native failure check");var runtime=root.AddComponent<DesktopRuntime>();var calibration=root.AddComponent<CalibrationRuntime>();typeof(DesktopRuntime).GetField("runtime",Private).SetValue(runtime,calibration);
    var type=typeof(DesktopRuntime).GetNestedType("NativePlayback",BindingFlags.NonPublic);var playback=Activator.CreateInstance(type,true);type.GetField("Target").SetValue(playback,target);type.GetField("Pose").SetValue(playback,pose);type.GetField("Display").SetValue(playback,display);
    var owners=(IDictionary)typeof(DesktopRuntime).GetField("nativePlaybacks",Private).GetValue(runtime);owners.Add(display.Id,playback);
    window=typeof(NativeDesktopTarget).GetField("surface",Private).GetValue(target);originalParent=window.GetType().GetField("originalParent",Private);savedParent=originalParent.GetValue(window);
    // A valid HWND that would form a parent cycle makes the original Win32
    // SetParent fail, instead of being normalized to the desktop fallback.
    originalParent.SetValue(window,target.Ready.GetAwaiter().GetResult().Window);
    try{Invoke(runtime,"CloseNative",display.Id).GetAwaiter().GetResult();throw new Exception("detach-failure-not-exercised");}
    catch(System.ComponentModel.Win32Exception error){report["detach_error"]=new JObject{{"code",error.NativeErrorCode},{"message",error.Message}};Need(error.Message.Contains("desktop-detach-parent-failed"),"exact-original-detach-failure");}
    Need(owners.Count==1&&ReferenceEquals(owners[display.Id],playback)&&!target.ResourcesReleased,"failed-close-retains-exact-owner");Need((bool)type.GetField("Failed").GetValue(playback),"retiring-owner-marked-failed");
    var until=DateTime.UtcNow.AddSeconds(4);while((bool?)target.State["paused"]!=true&&DateTime.UtcNow<until)Thread.Sleep(10);Need((bool?)target.State["paused"]==true,"retained-failed-owner-actually-paused");
    try{Invoke(runtime,"OpenNative",selection).GetAwaiter().GetResult();throw new Exception("retained-owner-overwritten");}
    catch(InvalidOperationException error){Need(error.Message=="native-target-original-owner-retained","reopen-refuses-retained-original-owner");}
    Need(owners.Count==1&&ReferenceEquals(owners[display.Id],playback),"rejected-reopen-keeps-owner");
    originalParent.SetValue(window,savedParent);Need(target.CloseAsync().Wait(25000)&&target.ResourcesReleased,"retry-releases-actual-original-resources");Invoke(runtime,"CloseNative",display.Id).GetAwaiter().GetResult();Need(owners.Count==0,"owner-removed-only-after-release");
    report["status"]="passed-real-detach-failure-owner-retry";report["checks"]=checks;
   }catch(Exception error){report["status"]="failed";report["error"]=error.ToString();throw;}
   finally{
    if(originalParent!=null)originalParent.SetValue(window,savedParent);
    try{if(target!=null){target.CloseAsync().Wait(25000);Need(target.ResourcesReleased,"final-real-resources-released");report["final_resources_released"]=true;}}catch(Exception error){report["cleanup_error"]=error.ToString();}
    if(root)UnityEngine.Object.DestroyImmediate(root);Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"report.json"),report.ToString());
   }
  }
 }
}
