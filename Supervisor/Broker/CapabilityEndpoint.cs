using System;using System.IO;using System.Text;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using System.Globalization;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Supervision.Broker {
 public sealed class BrokerReply {
  public CapabilityOperation Operation{get;}public int RequestId{get;}public bool Error{get;}readonly byte[] payload;public byte[] Payload=>(byte[])payload.Clone();
  internal BrokerReply(CapabilityOperation op,int id,StorageResult result){Operation=op;RequestId=id;Error=result.Failure!=StorageFailure.None;byte[] value=result.Value;payload=new UTF8Encoding(false,true).GetBytes(new JObject{{"version",result.Version.ToString(CultureInfo.InvariantCulture)},{"value",value==null?null:Convert.ToBase64String(value)},{"error",Error?result.Failure.ToString():""}}.ToString(Formatting.None));if(payload.Length>CapabilityWire.MaxPayloadBytes)throw new InvalidDataException("capability-reply-size");}
  internal BrokerReply(CapabilityOperation op,int id,LlmResult result){Operation=op;RequestId=id;Error=result.Failure!=LlmFailure.None;payload=new UTF8Encoding(false,true).GetBytes(new JObject{{"version","0"},{"value",result.Text==null?null:Convert.ToBase64String(new UTF8Encoding(false,true).GetBytes(result.Text))},{"error",Error?result.Failure.ToString():""}}.ToString(Formatting.None));if(payload.Length>CapabilityWire.MaxPayloadBytes)throw new InvalidDataException("capability-reply-size");}
 }
 // Pure session router. A later OS adapter supplies these exact bytes; this class does not accept an installation selector.
 public sealed class CapabilityEndpoint {
  public const int MaxPending=8;readonly object gate=new object();readonly BrokerSession session;readonly WorkerIdentity identity;readonly StorageBroker storage;readonly LlmBroker llm;
  readonly HashSet<int> pending=new HashSet<int>(),controls=new HashSet<int>();readonly Queue<BrokerReply> replies=new Queue<BrokerReply>(MaxPending+1);long sequence;int lastRequest,closed,fault;
  readonly SoundOutput sound;
  public bool Faulted=>Volatile.Read(ref fault)!=0;public bool Closed=>Volatile.Read(ref closed)!=0;
#if SDK_SUPERVISOR_TESTS
  public static Action<CapabilityEndpoint> AfterLanguageFinish;
  public static Action<CapabilityEndpoint> AfterReceiveReserved;
  public static Action<CapabilityEndpoint> AfterStorageFinish;
  public static Action<CapabilityEndpoint> BeforeFault;
#endif
  public CapabilityEndpoint(BrokerSession owner,StorageBroker persistence,LlmBroker language=null,SoundOutput sound=null){this.sound=sound;session=owner??throw new ArgumentNullException(nameof(owner));storage=persistence??throw new ArgumentNullException(nameof(persistence));llm=language;identity=new WorkerIdentity(owner.Attempt,owner.Instance,owner.Generation);}
  static void Need(bool value,string code){if(!value)throw new InvalidDataException(code);}
  static JObject Body(byte[] bytes,int length){
   string text=new UTF8Encoding(false,true).GetString(bytes,0,length);
   // Bound work before constructing a JObject: small exact schemas do not need arbitrary token trees.
   using(var input=new StringReader(text))using(var reader=new JsonTextReader(input){MaxDepth=5,DateParseHandling=DateParseHandling.None}){int tokens=0;while(reader.Read())Need(++tokens<=64,"capability-token-count");}
   using(var input=new StringReader(text))using(var reader=new JsonTextReader(input){MaxDepth=5,DateParseHandling=DateParseHandling.None}){var body=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});Need(!reader.Read(),"capability-json-tail");return body;}
  }
  static void Fields(JObject body,params string[] names)=>Need(body.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(names.OrderBy(n=>n,StringComparer.Ordinal)),"capability-fields");
  static string Text(JToken value,int maximum){Need(value!=null&&value.Type==JTokenType.String&&((string)value).Length<=maximum,"capability-string");return (string)value;}
  void Finish(CapabilityHeader header,StorageResult result){lock(gate){if(Closed)return;Need(pending.Contains(header.RequestId)&&replies.Count<MaxPending+1,"capability-reply-ownership");replies.Enqueue(new BrokerReply(header.Operation,header.RequestId,result));}}
  void Finish(CapabilityHeader header,LlmResult result){lock(gate){if(Closed)return;Need(pending.Contains(header.RequestId)&&replies.Count<MaxPending+1,"capability-reply-ownership");replies.Enqueue(new BrokerReply(header.Operation,header.RequestId,result));}}
  public bool Receive(byte[] header,byte[] payload,int length){
   try{
    CapabilityHeader message;lock(gate){
     if(Closed)return false;message=CapabilityWire.Validate(header,identity,false,sequence);Need(payload!=null&&length==message.PayloadBytes&&length<=payload.Length,"capability-body-length");bool control=message.Operation==CapabilityOperation.LlmCancel;Need(message.RequestId>lastRequest&&(control?controls.Count<1:pending.Count-controls.Count<MaxPending),"capability-request-replay-or-capacity");sequence=message.Sequence;lastRequest=message.RequestId;pending.Add(message.RequestId);if(control)controls.Add(message.RequestId);
    }
    // Actor jobs copy their bounded arguments before this receive buffer is reused.
#if SDK_SUPERVISOR_TESTS
    AfterReceiveReserved?.Invoke(this);
#endif
    if(message.Operation==CapabilityOperation.SoundPcm){
     session.Demand("sound-playback");Need(length==SpatialWallpaper.Sdk.SoundFormat.PacketBytes,"sound-packet-size");
     bool accepted=sound!=null&&sound.Submit(payload,length);Finish(message,new StorageResult(accepted?StorageFailure.None:sound==null||sound.Error!=null?StorageFailure.Unavailable:StorageFailure.Busy));return true;
    }
    session.Demand(message.Operation==CapabilityOperation.StorageRead||message.Operation==CapabilityOperation.StorageCommit?"private-storage":"llm-proxy");
    JObject body=Body(payload,length);Task<StorageResult> work;
    if(message.Operation==CapabilityOperation.StorageRead){Fields(body,"key");work=storage.Read(session,Text(body["key"],PrivateStore.MaxKeyBytes));}
    else if(message.Operation==CapabilityOperation.StorageCommit){
     Fields(body,"version","changes");string revision=Text(body["version"],19);Need(long.TryParse(revision,NumberStyles.None,CultureInfo.InvariantCulture,out var version)&&version>=0&&version.ToString(CultureInfo.InvariantCulture)==revision,"capability-storage-version");
     var changes=body["changes"] as JObject;Need(changes!=null&&changes.Count<=StorageBroker.MaxChanges,"capability-storage-changes");var data=new Dictionary<string,byte[]>(StringComparer.Ordinal);long total=0;
     foreach(var change in changes.Properties()){
      Need(change.Name.Length>0&&change.Name.Length<=PrivateStore.MaxKeyBytes,"capability-storage-key");byte[] value=null;
      if(change.Value.Type!=JTokenType.Null){string encoded=Text(change.Value,((StorageBroker.MaxValueBytes+2)/3)*4);value=Convert.FromBase64String(encoded);Need(value.Length<=StorageBroker.MaxValueBytes&&Convert.ToBase64String(value)==encoded,"capability-storage-base64");}
      total+=Encoding.UTF8.GetByteCount(change.Name)+(value?.Length??0);Need(total<=StorageBroker.MaxValueBytes,"capability-storage-payload");data.Add(change.Name,value);
     }
     work=storage.Commit(session,version,data);
    }else{
     Task<LlmResult> language;
     if(message.Operation==CapabilityOperation.LlmSubmit){Fields(body,"prompt");string prompt=Text(body["prompt"],LlmBroker.MaxPromptChars);language=llm==null?Task.FromResult(new LlmResult(LlmFailure.Unavailable)):llm.Submit(session,message.RequestId,prompt);}
     else{Fields(body,"id");Need(body["id"]?.Type==JTokenType.Integer&&(long)body["id"]>0&&(long)body["id"]<=int.MaxValue,"capability-cancel-id");language=Task.FromResult(new LlmResult(llm==null?LlmFailure.Unavailable:llm.Cancel(session,(int)body["id"])?LlmFailure.None:LlmFailure.Invalid));}
     language.ContinueWith(done=>{try{Finish(message,done.Status==TaskStatus.RanToCompletion?done.Result:new LlmResult(LlmFailure.Transport));}catch{Fault();}
#if SDK_SUPERVISOR_TESTS
      finally{AfterLanguageFinish?.Invoke(this);}
#endif
     },CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);return true;
    }
    work.ContinueWith(done=>{try{Finish(message,done.Status==TaskStatus.RanToCompletion?done.Result:new StorageResult(StorageFailure.Io));}catch{Fault();}
#if SDK_SUPERVISOR_TESTS
     finally{AfterStorageFinish?.Invoke(this);}
#endif
    },CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);return true;
   }catch(Exception error)when(error is InvalidDataException||error is UnauthorizedAccessException||error is IOException||error is ArgumentException||error is InvalidOperationException||error is JsonException||error is FormatException||error is OverflowException){Fault();return false;}
  }
  public bool TryTake(out BrokerReply reply){lock(gate){if(Closed||replies.Count==0){reply=null;return false;}reply=replies.Dequeue();pending.Remove(reply.RequestId);controls.Remove(reply.RequestId);return true;}}
  void Fault(){
#if SDK_SUPERVISOR_TESTS
   BeforeFault?.Invoke(this);
#endif
   lock(gate){if(Closed)return;Interlocked.Exchange(ref fault,1);Interlocked.Exchange(ref closed,1);}Close();
  }
  public void Close(){lock(gate){Interlocked.Exchange(ref closed,1);pending.Clear();controls.Clear();replies.Clear();}sound?.Close();session.Close();llm?.CancelSession(session);}
 }
}
