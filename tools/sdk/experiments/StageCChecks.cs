using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpatialWallpaper.Sdk.Editor;
using SpatialWallpaper.Sdk.Security;
using SpatialWallpaper.Sdk.Execution;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
public static class StageCChecks {
 public static GameObject CreateAuthor(){
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var root=new GameObject("AuthorRoot");var child=new GameObject("Peer");child.transform.SetParent(root.transform,false);
  var author=root.AddComponent<StageCAuthor>();var peer=child.AddComponent<StageCPeer>();author.target=root.transform;author.peer=peer;peer.primary=author;author.data=new StageCData();author.items=new List<int>{1,2};
  var prefabSource=new GameObject("SpawnTemplate");new GameObject("SpawnChild").transform.SetParent(prefabSource.transform,false);var spawnAuthor=prefabSource.AddComponent<StageCSpawn>();spawnAuthor.target=prefabSource.transform;var prefabAsset=PrefabUtility.SaveAsPrefabAsset(prefabSource,"Assets/Spawn.prefab");UnityEngine.Object.DestroyImmediate(prefabSource);author.template=new SpatialWallpaper.Sdk.Prefab{source=prefabAsset};
  return root;
 }
 static readonly JArray License=new JArray(new JObject{{"name","StageC original sample"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}});
 public static void CheckBoundary(){
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("Boundary");root.AddComponent<StageCBoundaryAuthor>().target=root.transform;
  var bytes=ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),License);
  using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(Admission.Admit(bytes,"windows-x86_64"),domain)){
   var runtime=new VmRuntime(Admission.Admit(bytes,"windows-x86_64"),domain,host,new ExecutionQuota(10000,32,1000));host.Bind(runtime);bool rejected=false;
   try{runtime.Start();}catch(InvalidOperationException error){rejected=error.Message=="argument-number-range";}
   if(!rejected||runtime.State!=VmState.Faulted||host.Root.transform.GetChild(0).localPosition!=Vector3.zero)throw new Exception("vector-constraint-was-not-enforced-before-native-call");
  }
 }
 public static void CheckNullCall(){
  StageCNullData missing=null;bool ordinaryThrows=false;try{missing.Constant();}catch(NullReferenceException){ordinaryThrows=true;}
  if(!ordinaryThrows)throw new Exception("ordinary-null-call-control-failed");
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("NullCall");root.AddComponent<StageCNullAuthor>();
  var bytes=ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),License);var build=Admission.Admit(bytes,"windows-x86_64");
  using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(build,domain)){
   var runtime=new VmRuntime(build,domain,host,new ExecutionQuota(10000,32,1000));host.Bind(runtime);bool rejected=false;
   try{runtime.Start();}catch(NullReferenceException){rejected=true;}
   if(!rejected||runtime.State!=VmState.Faulted)throw new Exception("null-callvirt-was-not-rejected");
  }
 }
 public static void Run(){
  var root=CreateAuthor();
  var result=CilboxProgramCompiler.Compile(root);
  var bytes=ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),new JArray(new JObject{{"name","StageC original sample"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}}));
  var build=Admission.Admit(bytes,"windows-x86_64");
  File.WriteAllBytes(Path.Combine(Environment.GetEnvironmentVariable("SDK_C_OUTPUT"),"scene.spatial"),bytes);
  using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(build,domain)){
   var runtime=new VmRuntime(build,domain,host,new ExecutionQuota(10000,32,1000));host.Bind(runtime);
   long nativeBefore=domain.Ledger.Used.NativeBytes;runtime.Start();
   long nativePending=domain.Ledger.Used.NativeBytes;
   if(nativePending<=nativeBefore)throw new Exception("native-resource-release-not-retained");
   host.CompletedRender(1);if(domain.Ledger.Used.NativeBytes!=nativePending)throw new Exception("native-resource-refunded-before-two-boundaries");
   host.CompletedRender(2);if(domain.Ledger.Used.NativeBytes!=nativeBefore)throw new Exception("Editor-immediate-resource-release-not-reclaimed");
   for(int i=0;i<60;i++)runtime.Update();
   if((int)runtime.Field(0,result.FieldId(typeof(StageCAuthor),"counter"))!=63||!(bool)runtime.Field(0,result.FieldId(typeof(StageCAuthor),"active"))||(int)runtime.Field(1,result.FieldId(typeof(StageCPeer),"value"))!=140||((OwnedList)runtime.Field(0,result.FieldId(typeof(StageCAuthor),"items"))).Count!=62||Quaternion.Angle(host.Root.transform.GetChild(0).localRotation,Quaternion.Euler(0,60,0))>.01f)throw new Exception("converted-inspector-data-behaviour-mismatch");
   var samples=(OwnedList)runtime.Field(0,result.FieldId(typeof(StageCAuthor),"samples"));if(samples.Count!=1||(float)domain.Get(samples,0)!=1f)throw new Exception("closed-float-list-mismatch");
   int before=host.Root.GetComponentsInChildren<Transform>(true).Length;
   var template=runtime.Field(0,result.FieldId(typeof(StageCAuthor),"template"));
   var clone=(OwnedHandle)host.Call("SpatialWallpaper.Sdk::Objects::Instantiate(Prefab)->Node",null,new object[]{template});
   var nativeClone=host.Root.transform.GetChild(host.Root.transform.childCount-1);
   if(nativeClone.localPosition.y!=0)throw new Exception("Prefab-ran-before-scheduled-Start");
   runtime.Update();
   if(nativeClone.localPosition.y!=5f||Quaternion.Angle(nativeClone.localRotation,Quaternion.Euler(0,1,0))>.01f)throw new Exception("Prefab-Start-Update-mismatch");
   host.Call("SpatialWallpaper.Sdk::Objects::Destroy(Node)->System.Void",null,new object[]{clone});
   bool stale=false;try{domain.Check(clone);}catch(InvalidOperationException){stale=true;}if(!stale)throw new Exception("Prefab-destroy-left-live-handle");
   if(host.Root.GetComponentsInChildren<Transform>(true).Length!=before)throw new Exception("Prefab-destroy-left-native-graph");
   var probe=typeof(UnityOwnedHost).GetField("GraphBuildProbe",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
   bool failed=false;probe.SetValue(null,(Action<int>)(index=>{if(index==1)throw new InvalidOperationException("fixture-native-node-failure");}));
   try{host.Call("SpatialWallpaper.Sdk::Objects::Instantiate(Prefab)->Node",null,new object[]{runtime.Field(0,result.FieldId(typeof(StageCAuthor),"template"))});}catch(InvalidOperationException){failed=true;}finally{probe.SetValue(null,null);}
   if(!failed||host.Root.GetComponentsInChildren<Transform>(true).Length!=before)throw new Exception("prefab-rollback-leaked-owned-nodes");
   runtime.Update();if((int)runtime.Field(0,result.FieldId(typeof(StageCAuthor),"counter"))!=65)throw new Exception("rollback-poisoned-existing-domain");
   runtime.Stop();
  }
  string output=Environment.GetEnvironmentVariable("SDK_C_OUTPUT");
  File.WriteAllText(Path.Combine(output,"program.json"),result.Program.ToString());
  File.WriteAllText(Path.Combine(output,"fields.json"),new JObject{{"counter",result.FieldId(typeof(StageCAuthor),"counter")},{"peer",result.FieldId(typeof(StageCPeer),"value")},{"active",result.FieldId(typeof(StageCAuthor),"active")},{"items",result.FieldId(typeof(StageCAuthor),"items")}}.ToString());
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var negativeType=System.Reflection.Assembly.Load("SdkCallbackNegative").GetType("NegativeAuthor",true);
  var negative=new GameObject("Negative");negative.AddComponent(negativeType);bool callbackRejected=false;
  try{CilboxProgramCompiler.Compile(negative);}catch(InvalidDataException error){callbackRejected=error.Message.Contains("callback");}
  if(!callbackRejected)throw new Exception("unsupported-callback-silently-ignored");
  CheckBoundary();CheckNullCall();
  File.WriteAllText(Path.Combine(output,"compiler-result.json"),new Newtonsoft.Json.Linq.JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_C_RUN")},{"class_id",result.ClassId(typeof(StageCAuthor))},{"counter_after_60",63},{"peer_after_60",140},{"callback_rejected",callbackRejected},{"rollback_checked",true},{"vector_boundary_rejected",true},{"null_call_rejected",true}}.ToString());
 }
}
