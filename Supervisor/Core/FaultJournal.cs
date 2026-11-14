using System;using System.IO;using System.Linq;using System.Text;using System.ComponentModel;using System.Runtime.InteropServices;using Newtonsoft.Json;using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Supervision {
 public enum StartIntent {Manual,Resume}
 public enum FaultReason {ScriptFault,Budget,ProcessExit,FrameTimeout,StartTimeout,StopTimeout,Admission,DeviceLost,Protocol,Monitoring,UncleanSupervisorExit}
 public sealed class AttemptReceipt {
  internal readonly Guid Epoch;public Guid Attempt{get;}public Guid Installation{get;}public string BuildId{get;}public Guid Instance{get;}public uint Generation{get;}
  internal AttemptReceipt(Guid epoch,Guid attempt,Guid installation,string build,Guid instance,uint generation){Epoch=epoch;Attempt=attempt;Installation=installation;BuildId=build;Instance=instance;Generation=generation;}
 }
 public sealed class RollbackTicket {
  internal readonly Guid Epoch,Installation,Instance;internal readonly string FailedKey,FailedAttempt,Build;internal readonly uint Generation;internal bool Used;
  internal RollbackTicket(Guid epoch,Guid installation,string failedKey,string failedAttempt,string build,Guid instance,uint generation){Epoch=epoch;Installation=installation;FailedKey=failedKey;FailedAttempt=failedAttempt;Build=build;Instance=instance;Generation=generation;}
 }
 // Trusted metadata only. Call this on the Supervisor's bounded persistence actor, never a UI/monitor loop.
 public sealed class FaultJournal:IDisposable {
  const int MaxActive=64,MaxFaults=8192,MaxHistory=256,MaxBytes=4*1024*1024;
  readonly object gate=new object();readonly Guid epoch=Guid.NewGuid();readonly string directory,path;readonly FileStream ownership;
  JObject state;bool unavailable,disposed;
#if SDK_SUPERVISOR_TESTS
  public static Action BeforeReplace;
#endif
  public FaultJournal(string root){
   directory=Path.GetFullPath(root);Directory.CreateDirectory(directory);path=Path.Combine(directory,"state.json");
   ownership=new FileStream(Path.Combine(directory,"state.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
   try{
    foreach(string stale in Directory.EnumerateFiles(directory,"write-*.tmp")){
     string name=Path.GetFileName(stale);
     if(name.Length!=42||!Guid.TryParseExact(name.Substring(6,32),"N",out var ignored)||!string.Equals(Path.GetDirectoryName(Path.GetFullPath(stale)),directory,StringComparison.OrdinalIgnoreCase)||(File.GetAttributes(stale)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("journal-temp-path");
     File.Delete(stale);
    }
    state=File.Exists(path)?Read(path):new JObject{{"version",2},{"revision",0L},{"active",new JObject()},{"faults",new JObject()},{"history",new JArray()}};
    if(((JObject)state["active"]).Count>0){
     var next=Copy();
     foreach(var property in ((JObject)next["active"]).Properties().ToArray())RecordFault(next,property.Name,(JObject)property.Value,FaultReason.UncleanSupervisorExit);
     ((JObject)next["active"]).RemoveAll();Commit(next);
    }
   }catch{ownership.Dispose();throw;}
  }
  static string N(Guid value){if(value==Guid.Empty)throw new ArgumentException("trusted-identity-required");return value.ToString("N");}
  static string Hash(string value){if(value==null||value.Length!=64||value.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f'||c>='A'&&c<='F')))throw new ArgumentException("build-digest");return value.ToLowerInvariant();}
  static string Key(Guid installation,string build)=>N(installation)+"_"+Hash(build);
  void Live(){if(disposed||unavailable)throw new InvalidOperationException("fault-journal-unavailable");}
  internal bool Available{get{lock(gate)return !disposed&&!unavailable;}}
  JObject Copy()=>(JObject)state.DeepClone();
  public int ActiveCount{get{lock(gate){Live();return ((JObject)state["active"]).Count;}}}
  public bool IsQuarantined(Guid installation,string build){lock(gate){Live();return Blocked(state,Key(installation,build));}}
  static bool Blocked(JObject data,string key)=>data["faults"][key]!=null&&(bool)data["faults"][key]["blocked"];
  static void History(JObject data,string attempt,JObject active,string eventName){
   var history=(JArray)data["history"];history.Add(new JObject{{"attempt",attempt},{"install",(string)active["install"]},{"build",(string)active["build"]},{"instance",(string)active["instance"]},{"generation",(long)active["generation"]},{"diagnostic",""},{"event",eventName},{"at",DateTime.UtcNow.Ticks}});
   while(history.Count>MaxHistory)history.RemoveAt(0);
  }
  public bool RecordDiagnostic(AttemptReceipt receipt,string diagnostic){
   if(diagnostic==null||diagnostic.Length>4096)throw new ArgumentException("diagnostic-size");
   lock(gate){Live();if(receipt==null||receipt.Epoch!=epoch)throw new InvalidOperationException("foreign-attempt");var next=Copy();var record=((JArray)next["history"]).OfType<JObject>().LastOrDefault(r=>(string)r["attempt"]==N(receipt.Attempt)&&(string)r["event"]!="Starting"&&(string)r["event"]!="Running");if(record==null||(string)record["diagnostic"]!="")return false;record["diagnostic"]=diagnostic;Commit(next);return true;}
  }
  public JObject Diagnostic(Guid attempt){lock(gate){Live();var record=((JArray)state["history"]).OfType<JObject>().LastOrDefault(r=>(string)r["attempt"]==N(attempt)&&(string)r["event"]!="Starting"&&(string)r["event"]!="Running");return record==null?null:(JObject)record.DeepClone();}}
  public AttemptReceipt Begin(Guid installation,string build,Guid instance,uint generation,StartIntent intent){
   if(!Enum.IsDefined(typeof(StartIntent),intent))throw new ArgumentException("start-intent");
   lock(gate){Live();return BeginCore(installation,Hash(build),instance,generation,intent.ToString());}
  }
  AttemptReceipt BeginCore(Guid installation,string build,Guid instance,uint generation,string intent){
   string key=Key(installation,build),instanceId=N(instance);if(generation==0)throw new ArgumentException("instance-generation");
   var active=(JObject)state["active"];var faults=(JObject)state["faults"];
   if(active.Count>=MaxActive||active.Properties().Any(p=>(string)p.Value["instance"]==instanceId))throw new InvalidOperationException("active-instance-boundary");
   if(intent!="Manual"&&Blocked(state,key))throw new InvalidOperationException("build-quarantined");
   int pending=active.Properties().Select(p=>(string)p.Value["install"]+"_"+(string)p.Value["build"]).Distinct(StringComparer.Ordinal).Count(k=>faults[k]==null);
   if(faults[key]==null&&!active.Properties().Any(p=>(string)p.Value["install"]+"_"+(string)p.Value["build"]==key)&&faults.Count+pending>=MaxFaults)throw new InvalidOperationException("fault-index-full");
   var next=Copy();Guid attempt=Guid.NewGuid();
   string retry=intent=="Manual"&&Blocked(state,key)?(string)state["faults"][key]["last_attempt"]:"";
   var record=new JObject{{"install",N(installation)},{"build",build},{"instance",instanceId},{"generation",(long)generation},{"state","Starting"},{"intent",intent},{"retry_of",retry},{"process",0},{"created",DateTime.UtcNow.Ticks}};
   ((JObject)next["active"]).Add(N(attempt),record);History(next,N(attempt),record,"Starting");Commit(next);
   return new AttemptReceipt(epoch,attempt,installation,build,instance,generation);
  }
  JObject Active(AttemptReceipt receipt){
   Live();if(receipt==null||receipt.Epoch!=epoch)throw new InvalidOperationException("foreign-attempt");
   return state["active"][N(receipt.Attempt)] as JObject;
  }
  public void RecordProcess(AttemptReceipt receipt,int processId){
   lock(gate){
    var active=Active(receipt);if(active==null||(string)active["state"]!="Starting"||(int)active["process"]!=0||processId<=0||((JObject)state["active"]).Properties().Any(p=>(int)p.Value["process"]==processId))throw new InvalidOperationException("process-binding-state");
    var next=Copy();next["active"][N(receipt.Attempt)]["process"]=processId;Commit(next);
   }
  }
  public bool MarkRunning(AttemptReceipt receipt){
   lock(gate){
    var active=Active(receipt);if(active==null||(string)active["state"]!="Starting"||(int)active["process"]<=0)return false;
    var next=Copy();var record=(JObject)next["active"][N(receipt.Attempt)];record["state"]="Running";
    string key=Key(receipt.Installation,receipt.BuildId),retry=(string)record["retry_of"];
    if(retry.Length>0&&next["faults"][key]!=null&&(string)next["faults"][key]["last_attempt"]==retry){next["faults"][key]["blocked"]=false;next["faults"][key]["recovered_by"]=N(receipt.Attempt);}
    History(next,N(receipt.Attempt),record,"Running");Commit(next);return true;
   }
  }
  public bool MarkStopping(AttemptReceipt receipt){
   lock(gate){var active=Active(receipt);if(active==null||(string)active["state"]=="Stopping")return false;var next=Copy();next["active"][N(receipt.Attempt)]["state"]="Stopping";Commit(next);return true;}
  }
  public void Complete(AttemptReceipt receipt){
   lock(gate){
    var active=Active(receipt);if(active==null)return;if((string)active["state"]!="Stopping")throw new InvalidOperationException("stop-not-requested");
    var next=Copy();History(next,N(receipt.Attempt),(JObject)next["active"][N(receipt.Attempt)],"Stopped");((JObject)next["active"]).Remove(N(receipt.Attempt));Commit(next);
   }
  }
  static void RecordFault(JObject data,string attempt,JObject active,FaultReason reason){
   var faults=(JObject)data["faults"];string key=(string)active["install"]+"_"+(string)active["build"];long now=DateTime.UtcNow.Ticks;
   var fault=faults[key] as JObject;
   if(fault==null){if(faults.Count>=MaxFaults)throw new InvalidOperationException("fault-index-full");fault=new JObject{{"first_reason",reason.ToString()},{"first_at",now}};faults.Add(key,fault);}
   fault["last_reason"]=reason.ToString();fault["last_at"]=now;fault["last_attempt"]=attempt;fault["last_instance"]=(string)active["instance"];fault["last_generation"]=(long)active["generation"];fault["blocked"]=true;fault["recovered_by"]="";
   fault["rollback_used"]=(string)active["intent"]=="Rollback"||reason==FaultReason.UncleanSupervisorExit;
   History(data,attempt,active,reason.ToString());
  }
  public bool Fail(AttemptReceipt receipt,FaultReason reason){
   if(!Enum.IsDefined(typeof(FaultReason),reason))throw new ArgumentException("fault-reason");
   lock(gate){var active=Active(receipt);if(active==null)return false;var next=Copy();RecordFault(next,N(receipt.Attempt),(JObject)next["active"][N(receipt.Attempt)],reason);((JObject)next["active"]).Remove(N(receipt.Attempt));Commit(next);return true;}
  }
  public RollbackTicket ReserveRollback(Guid installation,string failedBuild,string healthyBuild){
   lock(gate){
    Live();string key=Key(installation,failedBuild),healthy=Hash(healthyBuild);var fault=state["faults"][key] as JObject;
    if(fault==null||!(bool)fault["blocked"]||(bool)fault["rollback_used"]||Blocked(state,Key(installation,healthy)))return null;
    var next=Copy();next["faults"][key]["rollback_used"]=true;Commit(next);
    return new RollbackTicket(epoch,installation,key,(string)fault["last_attempt"],healthy,Guid.ParseExact((string)fault["last_instance"],"N"),(uint)(long)fault["last_generation"]);
   }
  }
  public AttemptReceipt BeginRollback(RollbackTicket ticket,Guid instance,uint generation){
   lock(gate){
    Live();if(ticket==null||ticket.Epoch!=epoch||ticket.Used||ticket.Instance!=instance||generation<=ticket.Generation)throw new InvalidOperationException("rollback-ticket");
    var fault=state["faults"][ticket.FailedKey];
    if(fault==null||!(bool)fault["rollback_used"]||(string)fault["last_attempt"]!=ticket.FailedAttempt||Blocked(state,Key(ticket.Installation,ticket.Build)))throw new InvalidOperationException("stale-rollback-ticket");
    var receipt=BeginCore(ticket.Installation,ticket.Build,instance,generation,"Rollback");ticket.Used=true;return receipt;
   }
  }
  void Commit(JObject next){
   next["revision"]=checked((long)state["revision"]+1);string text=next.ToString(Formatting.None);byte[] bytes=new UTF8Encoding(false,true).GetBytes(text);
   if(bytes.Length>MaxBytes)throw new InvalidOperationException("fault-journal-size");
   string temp=Path.Combine(directory,"write-"+Guid.NewGuid().ToString("N")+".tmp");
   try{
    using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
#if SDK_SUPERVISOR_TESTS
    BeforeReplace?.Invoke();
#endif
    if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("fault-journal-platform-unverified");
    if(!MoveFileEx(temp,path,0x1|0x8))throw new Win32Exception(Marshal.GetLastWin32Error());
    state=next;
   }catch{unavailable=true;throw;}
   finally{if(File.Exists(temp))File.Delete(temp);}
  }
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool MoveFileEx(string existing,string target,uint flags);
  static void Fields(JObject value,params string[] names){if(value==null||!value.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(names.OrderBy(n=>n,StringComparer.Ordinal)))throw new InvalidDataException("journal-fields");}
  static string Text(JToken value){if(value==null||value.Type!=JTokenType.String)throw new InvalidDataException("journal-string");return (string)value;}
  static void Identity(JToken value,bool empty=false){string text=Text(value);if(empty&&text.Length==0)return;if(!Guid.TryParseExact(text,"N",out var id)||id==Guid.Empty||id.ToString("N")!=text)throw new InvalidDataException("journal-identity");}
  static void Integer(JToken value,long min,long max){if(value==null||value.Type!=JTokenType.Integer||(long)value<min||(long)value>max)throw new InvalidDataException("journal-number");}
  static void OneOf(JToken value,params string[] allowed){if(!allowed.Contains(Text(value),StringComparer.Ordinal))throw new InvalidDataException("journal-enum");}
  static JObject Read(string path){
   var info=new FileInfo(path);if(info.Length<=0||info.Length>MaxBytes)throw new InvalidDataException("journal-size");
   JObject data;using(var stream=new StreamReader(path,new UTF8Encoding(false,true)))using(var reader=new JsonTextReader(stream){MaxDepth=12,DateParseHandling=DateParseHandling.None}){
    data=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new InvalidDataException("journal-tail");
   }
   Fields(data,"version","revision","active","faults","history");Integer(data["version"],2,2);Integer(data["revision"],0,long.MaxValue);
   var active=data["active"] as JObject;var faults=data["faults"] as JObject;var history=data["history"] as JArray;
   if(active==null||faults==null||history==null||active.Count>MaxActive||faults.Count>MaxFaults||history.Count>MaxHistory)throw new InvalidDataException("journal-count");
   foreach(var property in active.Properties()){
    Identity(new JValue(property.Name));var record=property.Value as JObject;Fields(record,"install","build","instance","generation","state","intent","retry_of","process","created");
    Identity(record["install"]);Identity(record["instance"]);Identity(record["retry_of"],true);if(Hash(Text(record["build"]))!=Text(record["build"]))throw new InvalidDataException("journal-build");
    Integer(record["generation"],1,uint.MaxValue);Integer(record["process"],0,int.MaxValue);Integer(record["created"],1,DateTime.MaxValue.Ticks);OneOf(record["state"],"Starting","Running","Stopping");OneOf(record["intent"],"Manual","Resume","Rollback");
   }
   foreach(var property in faults.Properties()){
    if(property.Name.Length!=97||property.Name[32]!='_')throw new InvalidDataException("journal-key");Identity(new JValue(property.Name.Substring(0,32)));if(Hash(property.Name.Substring(33))!=property.Name.Substring(33))throw new InvalidDataException("journal-key");
    var fault=property.Value as JObject;Fields(fault,"first_reason","first_at","last_reason","last_at","last_attempt","last_instance","last_generation","blocked","recovered_by","rollback_used");
    OneOf(fault["first_reason"],Enum.GetNames(typeof(FaultReason)));OneOf(fault["last_reason"],Enum.GetNames(typeof(FaultReason)));Integer(fault["first_at"],1,DateTime.MaxValue.Ticks);Integer(fault["last_at"],1,DateTime.MaxValue.Ticks);
    Identity(fault["last_attempt"]);Identity(fault["last_instance"]);Identity(fault["recovered_by"],true);Integer(fault["last_generation"],1,uint.MaxValue);
    if(fault["blocked"].Type!=JTokenType.Boolean||fault["rollback_used"].Type!=JTokenType.Boolean)throw new InvalidDataException("journal-bool");
   }
   foreach(var value in history){
    var record=value as JObject;Fields(record,"attempt","install","build","instance","generation","diagnostic","event","at");Identity(record["instance"]);Integer(record["generation"],1,uint.MaxValue);if(Text(record["diagnostic"]).Length>4096)throw new InvalidDataException("diagnostic-size");Identity(record["attempt"]);Identity(record["install"]);if(Hash(Text(record["build"]))!=Text(record["build"]))throw new InvalidDataException("journal-build");
    OneOf(record["event"],Enum.GetNames(typeof(FaultReason)).Concat(new[]{"Starting","Running","Stopped"}).ToArray());Integer(record["at"],1,DateTime.MaxValue.Ticks);
   }
   return data;
  }
  public void Dispose(){lock(gate){if(disposed)return;disposed=true;ownership.Dispose();}}
 }
}
