using System;using System.IO;using System.Linq;using System.Collections.Generic;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEngine;using SpatialWallpaper.Sdk;using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;using SpatialWallpaper.Sdk.Execution;
public static class MiniShotVmChecks {
 sealed class Store {internal int Revision;internal readonly Dictionary<string,string> Values=new Dictionary<string,string>();}
 // Controlled capability replies isolate author state transitions. Installed LPAC
 // and native input are exercised separately; this is never their replacement.
 sealed class Replies:IVmHost {
  sealed class Request {internal string Key,Value,Version,Error;internal bool Write,Done;internal int At;}
  readonly UnityOwnedHost native;readonly Store store;readonly Dictionary<int,Request> requests=new Dictionary<int,Request>();int next;
  internal int Frame;internal bool Focus=true,Primary,Space,FailNextWrite;internal float X,Y;
  internal int MusicStarts,Effects;
  internal Replies(UnityOwnedHost host,Store data){native=host;store=data;}
  public bool Supports(string member)=>native.Supports(member)||member.StartsWith("SpatialWallpaper.Sdk::Input::",StringComparison.Ordinal)||member.StartsWith("SpatialWallpaper.Sdk::Storage::",StringComparison.Ordinal);
  public object Resolve(string type,JToken value,int scope)=>native.Resolve(type,value,scope);
  public object Call(string member,object receiver,object[] args){
   switch(member){
    case "SpatialWallpaper.Sdk::Sound::PlayMusic(UnityEngine.AudioClip,System.Single)->System.Void":MusicStarts++;return null;
    case "SpatialWallpaper.Sdk::Sound::PlayEffect(UnityEngine.AudioClip,System.Single)->System.Void":Effects++;return null;
    case "SpatialWallpaper.Sdk::Input::IsFocused()->System.Boolean":return Focus;
    case "SpatialWallpaper.Sdk::Input::GetButtonDown(System.String)->System.Boolean":return (string)args[0]=="Space"?Space:Primary;
    case "SpatialWallpaper.Sdk::Input::GetPointerX()->System.Single":return X;
    case "SpatialWallpaper.Sdk::Input::GetPointerY()->System.Single":return Y;
    case "SpatialWallpaper.Sdk::Storage::BeginRead(System.String)->System.Int32":
     string read=(string)args[0];store.Values.TryGetValue(read,out var text);requests.Add(++next,new Request{Key=read,Value=text,Version=store.Revision.ToString(),At=Frame+2});return next;
    case "SpatialWallpaper.Sdk::Storage::BeginWriteText(System.String,System.String,System.String)->System.Int32":
     requests.Add(++next,new Request{Key=(string)args[0],Value=(string)args[1],Version=(string)args[2],Write=true,At=Frame+3,Error=FailNextWrite?"InjectedStorageFailure":null});FailNextWrite=false;return next;
    case "SpatialWallpaper.Sdk::Storage::State(System.Int32)->System.Int32":
     if(!requests.TryGetValue((int)args[0],out var pending))return 0;if(Frame<pending.At)return 1;
     if(!pending.Done){pending.Done=true;if(pending.Write&&pending.Error==null){if(pending.Version!=store.Revision.ToString())pending.Error="Conflict";else{if(pending.Value==null)store.Values.Remove(pending.Key);else store.Values[pending.Key]=pending.Value;store.Revision++;pending.Version=store.Revision.ToString();}}}return pending.Error==null?3:4;
    case "SpatialWallpaper.Sdk::Storage::Version(System.Int32)->System.String":return requests[(int)args[0]].Version;
    case "SpatialWallpaper.Sdk::Storage::Error(System.Int32)->System.String":return requests[(int)args[0]].Error;
    case "SpatialWallpaper.Sdk::Storage::TakeText(System.Int32)->System.String":var value=requests[(int)args[0]].Value;requests.Remove((int)args[0]);return value;
    case "SpatialWallpaper.Sdk::Storage::Release(System.Int32)->System.Boolean":return requests.Remove((int)args[0]);
    default:return native.Call(member,receiver,args);
   }
  }
 }
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);Debug.Log("PASS "+reason);}
 public static void Run(){string output=Environment.GetEnvironmentVariable("SDK_MINISHOT_OUTPUT");try{
  var definition=MiniShotSdkBuilder.Create();var source=definition.GetComponent<MiniShotSandbox>();var compiled=CilboxProgramCompiler.Compile(definition.gameObject);
  var licenses=new JArray(new JObject{{"name","User supplied MiniShotGame"},{"source","Local user geometry"},{"license","Internal validation only; public redistribution unverified"},{"attribution","Original user geometry and SDK adaptation"}});
  byte[] bytes=ScriptSceneExporter.Export(definition.gameObject,definition.authorFrame,licenses);File.WriteAllBytes(Path.Combine(output,"compiled.spatial"),bytes);var build=Admission.Admit(bytes,"windows-x86_64");var store=new Store();
  var projection=UnityEngine.Object.FindObjectOfType<WindowProjection>();int checks=0;
  for(int session=0;session<2;session++)using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(build,domain)){
   host.ConfigureView(projection);var replies=new Replies(host,store);var vm=new VmRuntime(build,domain,replies,new ExecutionQuota(200000,64,100));host.Bind(vm);vm.Start();
   Func<string,object> field=name=>vm.Field(0,compiled.FieldId(typeof(MiniShotSandbox),name));
   Action tick=()=>{replies.Frame++;vm.Update();};Action settle=()=>{for(int i=0;i<120;i++){tick();if((int)field("phase")==2)return;if((int)field("phase")==4)throw new Exception("unexpected-author-storage-error:"+field("error"));}throw new Exception("author-save-did-not-settle");};
   Action<int> aim=index=>{var p=projection.GetComponent<Camera>().WorldToViewportPoint(source.centers[index]);replies.X=p.x;replies.Y=1-p.y;};
   Action<int> click=index=>{aim(index);replies.Primary=true;tick();replies.Primary=false;};settle();
   if(session==1){Need((bool)domain.Get((OwnedArray)field("hits"),0),"VM-restart-restores-hit-from-storage");checks++;replies.Space=true;tick();replies.Space=false;settle();Need(store.Values.Count==0,"VM-reset-clears-all-persisted-targets");checks++;vm.Stop();continue;}
   Need(store.Values.Count==0,"VM-loading-does-not-write");checks++;Need(replies.MusicStarts==1&&replies.Effects==0,"VM-music-starts-once-without-effect");checks++;
   replies.Focus=false;click(0);replies.Focus=true;Need(!(bool)domain.Get((OwnedArray)field("hits"),0)&&store.Values.Count==0,"VM-unfocused-click-ignored");checks++;
   replies.X=.99f;replies.Y=.01f;replies.Primary=true;tick();replies.Primary=false;Need(store.Values.Count==0&&(int)field("phase")==2,"VM-empty-click-does-not-write");checks++;
   click(0);Need(replies.Effects==1,"VM-hit-effect-only-on-accepted-hit");checks++;Need((bool)domain.Get((OwnedArray)field("hits"),0),"VM-click-marks-nearest-target");checks++;settle();Need(store.Values.Count==1&&store.Values[source.keys[0]]=="hit","VM-hit-durably-saved");checks++;Need(host.Root.GetComponentsInChildren<MeshRenderer>().Count(value=>value.sharedMaterial.shader.name=="MiniShotGameSdk/Target"&&value.sharedMaterial.GetFloat("_Hit")>.5f)==1,"VM-hit-updates-real-native-material");checks++;
   var materials=(OwnedArray)field("targets");var handle=(OwnedHandle)domain.Get(materials,0);Need(handle!=null,"VM-material-reference-retained");checks++;
   click(1);replies.Space=true;tick();replies.Space=false;settle();Need(store.Values.Count==0,"VM-reset-during-save-is-not-lost");checks++;
   replies.FailNextWrite=true;click(0);for(int i=0;i<6;i++)tick();Need((int)field("phase")==4&&(string)field("error")=="InjectedStorageFailure"&&store.Values.Count==0,"VM-save-failure-visible-and-not-recorded-as-success");checks++;
   replies.Space=true;tick();replies.Space=false;settle();Need(!(bool)domain.Get((OwnedArray)field("hits"),0),"VM-explicit-reload-restores-durable-state");checks++;
   click(0);settle();Need(store.Values[source.keys[0]]=="hit","VM-final-hit-saved-for-restart");checks++;vm.Stop();
  }
  File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"status","passed-actual-converted-MiniShot-author"},{"checks",checks},{"capabilities",new JArray(build.CapabilityTags)},{"build_id",build.BuildId},{"scope","real C# conversion, admission and native VM execution with controlled asynchronous capability replies; not LPAC or native input acceptance"}}.ToString());EditorApplication.Exit(0);
 }catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}}
}
