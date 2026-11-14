using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SpatialWallpaper.Sdk.Native.Unity {
 public sealed class NativePlayQueries:IDisposable {
  sealed class Request {internal GameObject Node;internal string Name,Error;internal JArray Arguments;internal int Frame;internal JToken Result;internal Dictionary<long,GameObject> Nodes;}
  static int nextRequest;
  readonly Transform[] roots;readonly int owner=System.Threading.Thread.CurrentThread.ManagedThreadId;
  readonly Dictionary<int,Request> requests=new Dictionary<int,Request>();bool disposed;
  public NativePlayQueries(IEnumerable<Transform> ownedRoots){roots=ownedRoots?.Distinct().ToArray()??throw new ArgumentNullException(nameof(ownedRoots));if(roots.Length==0||roots.Any(root=>!root||root.GetComponentInChildren<Camera>(true)))throw new ArgumentException("native-Play-query-roots");}
  void Live(){if(disposed)throw new ObjectDisposedException(nameof(NativePlayQueries));if(System.Threading.Thread.CurrentThread.ManagedThreadId!=owner)throw new InvalidOperationException("native-Play-query-thread");}
  int Begin(GameObject node,string name,JArray arguments){
   Live();if(!node||!roots.Any(root=>root&&(node.transform==root||node.transform.IsChildOf(root))))throw new InvalidOperationException("native-Play-query-node-not-owned");SceneCommandRules.CheckQueryArguments(name,arguments);if(requests.Count>=SceneCommandCatalog.MaxCommands||nextRequest==int.MaxValue)throw new InvalidOperationException("native-Play-query-budget");int id=++nextRequest;requests.Add(id,new Request{Node=node,Name=name,Arguments=arguments,Frame=Time.frameCount});return id;
  }
  public void Pump(){
   Live();var waiting=requests.Where(pair=>pair.Value.Result==null&&pair.Value.Error==null&&Time.frameCount>pair.Value.Frame).ToArray();if(waiting.Length==0)return;
   var nodes=roots.Where(root=>root).SelectMany(root=>root.GetComponentsInChildren<Transform>(true)).Distinct().ToArray();var ids=nodes.Select((node,index)=>new{node,id=(long)index+1}).ToDictionary(value=>value.node,value=>value.id);var graph=ids.ToDictionary(pair=>pair.Value,pair=>pair.Key.gameObject);var commands=new List<SceneCommand>();
   foreach(var pair in waiting){if(!pair.Value.Node||!ids.TryGetValue(pair.Value.Node.transform,out var id)){pair.Value.Error="destroyed";continue;}commands.Add(new SceneCommand("Query",id,new JArray(pair.Value.Name,pair.Key,pair.Value.Arguments)));}
   if(commands.Count==0)return;
   try{var identity=new SessionIdentity(Guid.NewGuid(),1,SceneCommandCatalog.Digest);using(var target=new UnitySceneCommandTarget(identity,nodes)){var batch=new CommandBatch(identity,1,1,1,commands);target.TryPrepare(batch,out var prepared);var ack=target.Commit(batch,prepared);foreach(var pair in ack.Results){var request=requests[(int)pair.Key];if((string)pair.Value["status"]=="error")request.Error=(string)pair.Value["error"];else{request.Result=pair.Value["value"];request.Nodes=graph;}}}}
   catch{foreach(var pair in waiting)if(pair.Value.Result==null&&pair.Value.Error==null)pair.Value.Error="unavailable";throw;}
  }
  Request Ready(int id){if(!requests.TryGetValue(id,out var request)||request.Result==null||request.Error!=null)throw new InvalidOperationException("native-query-not-ready");return request;}
  public object Call(string action,object[] arguments){
   Live();switch(action){
    case "BeginBounds":return Begin((GameObject)arguments[0],"bounds",new JArray());
    case "BeginAnimatorState":return Begin((GameObject)arguments[0],"animator_state",new JArray((int)arguments[1]));
    case "BeginRaycast":{var origin=(Vector3)arguments[1];var direction=(Vector3)arguments[2];return Begin((GameObject)arguments[0],"raycast",new JArray(new JArray(origin.x,origin.y,origin.z),new JArray(direction.x,direction.y,direction.z),(float)arguments[3]));}
   }
   int id=(int)arguments[0];
   if(action=="QueryState")return !requests.TryGetValue(id,out var state)?SceneQueries.Unknown:state.Error!=null?SceneQueries.Failed:state.Result==null?SceneQueries.Pending:SceneQueries.Ready;
   if(action=="QueryError")return requests.TryGetValue(id,out var error)?error.Error:null;
   if(action=="QueryRelease"){if(requests.TryGetValue(id,out var release)&&release.Result==null&&release.Error==null)throw new InvalidOperationException("native-query-unacknowledged");requests.Remove(id);return null;}
   var request=Ready(id);string field=arguments.Length>1?(string)arguments[1]:null;
   if(action=="QueryVector"){
    if(request.Name=="bounds"&&(field=="center"||field=="extents")){int offset=field=="center"?0:3;return new Vector3((float)request.Result[offset],(float)request.Result[offset+1],(float)request.Result[offset+2]);}
    if(request.Name=="raycast"&&(field=="point"||field=="normal")&&(bool)request.Result["hit"]){var value=request.Result[field];return new Vector3((float)value[0],(float)value[1],(float)value[2]);}
   }
   if(action=="QueryNode"&&request.Name=="raycast"){if(!(bool)request.Result["hit"])return null;var node=request.Nodes[(long)request.Result["node"]];if(!node)throw new InvalidOperationException("native-query-result-node-destroyed");return node;}
   if(action=="QueryFloat"&&(request.Name=="animator_state"&&field=="normalized_time"||request.Name=="raycast"&&field=="distance"&&(bool)request.Result["hit"]))return (float)request.Result[field];
   if(action=="QueryInt"&&request.Name=="animator_state"&&field=="state_hash")return (int)request.Result[field];
   if(action=="QueryBool"&&(request.Name=="animator_state"&&field=="in_transition"||request.Name=="raycast"&&field=="hit"))return (bool)request.Result[field];
   throw new InvalidOperationException("native-query-field-unavailable");
  }
  public void Dispose(){if(disposed)return;Live();disposed=true;requests.Clear();}
 }
}
