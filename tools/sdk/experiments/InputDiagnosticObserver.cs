using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Capabilities;
// Trusted experimental observer. Never included by build_worker.py or in an SDK sample.
[DefaultExecutionOrder(10000)]
public sealed class InputDiagnosticObserver:MonoBehaviour {
 const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 WorkerRuntime worker;int frame,emitted;string previous;float next;bool stopped;
 static object Get(object value,string name)=>value?.GetType().GetField(name,Fields)?.GetValue(value);
 static JToken Scalar(object value)=>value==null?JValue.CreateNull():value is bool||value is int||value is uint||value is long||value is ulong||value is float||value is double?JToken.FromObject(value):new JValue("non-scalar");
 void LateUpdate(){if(stopped||emitted>=64)return;try{
  if(worker==null)worker=FindObjectOfType<WorkerRuntime>();var runtime=Get(worker,"runtime");if(runtime==null)return;frame++;
  var state=new JObject();var roots=Get(runtime,"roots") as IList;
  if(roots!=null&&roots.Count>0){var fields=Get(roots[0],"Fields") as IDictionary;var values=new JObject();if(fields!=null){int n=0;foreach(DictionaryEntry field in fields){if(n++>=16)break;values[(string)field.Key]=Scalar(field.Value);}}state["author"]=values;}
  object calls=Get(worker,"calls"),input=Get(calls,"input");var snapshot=new JObject();foreach(string name in new[]{"epoch","blockedThrough","lastTime","held","pressed","released"})snapshot[name]=Scalar(Get(input,name));
  // Read the same values exposed to author code, without advancing frames or consuming replies.
  var frames=input as InputFrames;if(frames!=null){snapshot["Focused"]=frames.Focused;snapshot["X"]=frames.X;snapshot["Y"]=frames.Y;snapshot["Wheel"]=frames.Wheel;}state["input"]=snapshot;
  var client=Get(calls,"client") as WindowsCapabilityClient;state["worker_failed"]=Scalar(Get(worker,"failed"));if(client!=null){state["transport_closed"]=client.Closed;state["transport_error"]=client.Error;}
  var entries=Get(calls,"entries") as IDictionary;var requests=new JObject();if(entries!=null)foreach(DictionaryEntry entry in entries){var row=new JObject();foreach(string name in new[]{"Parsed","Error","State"})row[name]=Scalar(Get(entry.Value,name));if(client!=null)row["transport_state"]=client.State((int)entry.Key);row["reason"]=Get(entry.Value,"Reason") as string;requests[entry.Key.ToString()]=row;}state["requests"]=requests;
  string signature=state.ToString(Newtonsoft.Json.Formatting.None);
  // lastTime changes every frame: exclude it from the change trigger, but retain in evidence.
  snapshot.Remove("lastTime");string change=state.ToString(Newtonsoft.Json.Formatting.None);
  if(change!=previous||Time.realtimeSinceStartup>=next){previous=change;next=Time.realtimeSinceStartup+1;emitted++;Debug.Log("SDK_INPUT_DIAGNOSTIC frame="+frame+" utc="+DateTime.UtcNow.ToString("O")+" "+signature);}
 }catch(Exception error){stopped=true;Debug.Log("SDK_INPUT_DIAGNOSTIC_ERROR "+error.GetType().Name+":"+error.Message);}}
}
