// Trusted ordinary-Play observation; no VM is involved.
using System;using System.IO;using UnityEngine;using Newtonsoft.Json.Linq;
[DefaultExecutionOrder(10000)]
public sealed class StageCPlayObserver:MonoBehaviour {
 public StageCAuthor author;public Camera view;int frames;
 void LateUpdate(){
  if(++frames!=60)return;
  try{
   if(author.counter!=63||author.peer.value!=140||!author.active||author.items.Count!=62||author.samples.Count!=1||author.samples[0]!=1f)throw new Exception("ordinary-Play-behaviour-mismatch");
   string output=Environment.GetEnvironmentVariable("SDK_C_OUTPUT");StageCVisuals.Capture(view,Path.Combine(output,"play.png"));
   File.WriteAllText(Path.Combine(output,"play-result.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_C_RUN")},{"counter",author.counter},{"peer",author.peer.value},{"active",author.active},{"items",author.items.Count},{"angle",Quaternion.Angle(Quaternion.identity,author.transform.localRotation)},{"frames",frames},{"engine",Application.unityVersion}}.ToString());
  }catch(Exception error){File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("SDK_C_OUTPUT"),"play-error.txt"),error.ToString());}
 }
}
