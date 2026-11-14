using System;using System.Diagnostics;using System.IO;using Newtonsoft.Json.Linq;
// Fixture-only bounded raw observations. Never decides which candidate is blank.
sealed class DesktopCandidateEvidence {
 const int Capacity=512;readonly JArray rows=new JArray();long total;int dropped;public bool Found;
 static JToken Value(Func<object> read){try{var value=read();return value==null?JValue.CreateNull():JToken.FromObject(value);}catch(Exception error){return new JObject{{"unavailable",error.GetType().Name}};}}
 internal void Probe(int x,int y,Func<JObject> query,Func<JObject> metadata,Func<long> machine,Func<long> foreground){
  var row=new JObject{{"ordinal",++total},{"x",x},{"y",y},{"machine_time_before",Value(()=>machine())},{"foreground_before",Value(()=>foreground())}};
  if(rows.Count<Capacity)rows.Add(row);else dropped++;
  row["query_began_qpc"]=Stopwatch.GetTimestamp();
  try{row["hit"]=query()??(JToken)JValue.CreateNull();}catch(Exception error){row["query_error"]=error.GetType().Name+":"+error.Message;throw;}
  finally{row["query_returned_qpc"]=Stopwatch.GetTimestamp();row["machine_time_after"]=Value(()=>machine());row["foreground_after"]=Value(()=>foreground());try{row["probe"]=metadata();}catch(Exception error){row["metadata_error"]=error.GetType().Name+":"+error.Message;}}
 }
 internal JObject Snapshot()=>new JObject{{"schema",1},{"scope","fixture candidate queries; raw classifications, not blanket desktop eligibility"},{"frequency",Stopwatch.Frequency},{"capacity",Capacity},{"total",total},{"dropped",dropped},{"found",Found},{"candidates",rows.DeepClone()}};
 internal void Save(string path,Exception primary,Action<string,string> write=null){
  try{var value=Snapshot();value["primary_failure"]=primary==null?null:primary.GetType().Name+":"+primary.Message;(write??File.WriteAllText)(path,value.ToString());}
  catch(Exception secondary){if(primary==null)throw;try{primary.Data["candidate-evidence-save-error"]=secondary.ToString();}catch{}try{Console.Error.WriteLine("candidate-evidence-save-error:"+secondary.GetType().Name);}catch{}}
 }
}
