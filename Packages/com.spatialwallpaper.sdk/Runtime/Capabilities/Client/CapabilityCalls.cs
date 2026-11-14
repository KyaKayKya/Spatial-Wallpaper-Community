using System;using System.Text;using System.IO;using System.Linq;using System.Collections.Generic;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Execution;
namespace SpatialWallpaper.Sdk.Capabilities {
 // Shared asynchronous facade state. The dictionary is bounded by the same eight call credits.
 public sealed class CapabilityCalls {
  sealed class Entry {internal bool Parsed,Error;internal string Version,Reason;internal byte[] Value;internal int State;internal CapabilityOperation Operation;}
  readonly Dictionary<int,Entry> entries=new Dictionary<int,Entry>(9);readonly ResourceLedger ledger;readonly WindowsCapabilityClient client;static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
  readonly AudioFrames audio=new AudioFrames();
  readonly InputFrames input=new InputFrames();
  public readonly SoundMixer Sound=new SoundMixer();readonly byte[] soundPacket=new byte[SoundFormat.PacketBytes];readonly int[] soundPending=new int[4];ulong soundNext;bool soundWasActive;
  void AdvanceSound(ulong now){
   for(int i=0;i<soundPending.Length;i++)if(soundPending[i]!=0&&client.TakeReply(soundPending[i],out var reply))soundPending[i]=0;
   if(!Sound.Active&&!soundWasActive)return;if(client.Closed){Sound.StopMusic();Sound.StopEffects();return;}
   if(soundNext==0||now>soundNext+1000000)soundNext=now;
   for(int n=0;n<4&&soundNext<now+600000;n++){
    int slot=Array.IndexOf(soundPending,0);if(slot<0)break;
    Sound.Render(soundPacket);int id=client.Begin(CapabilityOperation.SoundPcm,soundPacket);soundNext+=200000;if(id==0)break;soundPending[slot]=id;
   }
   soundWasActive=Sound.Active;
  }
  // Editor-only trusted native source. Exported author rules never expose this.
  internal Func<byte[]> PlayInputSource;
  public void AdvanceFrame(){
   client.TakePush(CapabilityOperation.AudioSnapshot,out var snapshot);ulong now=WindowsMachineClock.Qpc100ns();audio.Advance(snapshot?.Data,now,!client.Closed);
   client.TakePush(CapabilityOperation.InputSnapshot,out var inputSnapshot);input.Advance(PlayInputSource!=null?PlayInputSource():inputSnapshot?.Data,WindowsMachineClock.Milliseconds(),!client.Closed);
   AdvanceSound(now);
  }
  public float AudioBand(int index)=>audio.GetBand(index);
  public bool InputButton(string name)=>input.GetButton(name);
  public bool InputDown(string name)=>input.GetButtonDown(name);
  public bool InputUp(string name)=>input.GetButtonUp(name);
  public bool InputFocused()=>input.Focused;
  public float InputX()=>input.X;public float InputY()=>input.Y;public float InputWheel()=>input.Wheel;
  public CapabilityCalls(ResourceLedger accounting,WindowsCapabilityClient transport){ledger=accounting??throw new ArgumentNullException(nameof(accounting));client=transport??throw new ArgumentNullException(nameof(transport));new ManagedReservation(ledger,8192).Bind(this);}
  static void Key(string key){if(string.IsNullOrEmpty(key)||key.Length>256||Utf8.GetByteCount(key)>256)throw new ArgumentException("storage-key");}
  int Begin(CapabilityOperation operation,JObject body){bool control=operation==CapabilityOperation.LlmCancel;if(entries.Values.Count(entry=>(entry.Operation==CapabilityOperation.LlmCancel)==control)>=(control?1:8))return 0;string text=body.ToString(Formatting.None);byte[] bytes=Utf8.GetBytes(text);int id=client.Begin(operation,bytes);if(id>0)entries.Add(id,new Entry{Operation=operation});return id;}
  public int BeginRead(string key){Key(key);var frame=AllocationFrame.Begin(ledger);frame.Charge(checked(key.Length*32L+8192));int id=Begin(CapabilityOperation.StorageRead,new JObject{{"key",key}});GC.KeepAlive(frame);return id;}
  public int BeginPrompt(string prompt){if(prompt==null||prompt.Length>4096)throw new ArgumentException("llm-prompt-size");var frame=AllocationFrame.Begin(ledger);frame.Charge(checked(prompt.Length*32L+8192));int id=Begin(CapabilityOperation.LlmSubmit,new JObject{{"prompt",prompt}});GC.KeepAlive(frame);return id;}
  public int BeginCancel(int request){if(request<1)throw new ArgumentException("llm-request-id");var frame=AllocationFrame.Begin(ledger);frame.Charge(8192);int id=Begin(CapabilityOperation.LlmCancel,new JObject{{"id",request}});GC.KeepAlive(frame);return id;}
  public int BeginWriteText(string key,string value,string version){
   Key(key);if(value!=null&&value.Length>4096)throw new ArgumentException("storage-text-size");if(version==null||version.Length>19)throw new ArgumentException("storage-version");
   var frame=AllocationFrame.Begin(ledger);frame.Charge(checked((key.Length+(value?.Length??0)+version.Length)*32L+8192));byte[] bytes=value==null?null:Utf8.GetBytes(value);
   int id=Begin(CapabilityOperation.StorageCommit,new JObject{{"version",version},{"changes",new JObject{{key,bytes==null?null:Convert.ToBase64String(bytes)}}}});GC.KeepAlive(frame);return id;
  }
  Entry Ensure(int id){
   if(!entries.TryGetValue(id,out var entry)||entry.Parsed)return entry;int state=client.State(id);if(state<3)return entry;
   if(!client.TakeReply(id,out var reply))return entry;var frame=AllocationFrame.Begin(ledger);frame.Charge(checked(reply.Data.Length*16L+8192));
   JObject data;using(var input=new StringReader(Utf8.GetString(reply.Data)))using(var reader=new JsonTextReader(input){MaxDepth=3,DateParseHandling=DateParseHandling.None}){data=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new InvalidDataException("capability-result-tail");}
   if(!data.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(new[]{"error","value","version"})||data["version"].Type!=JTokenType.String||((string)data["version"]).Length>19||data["error"].Type!=JTokenType.String||((string)data["error"]).Length>96||data["value"].Type!=JTokenType.Null&&data["value"].Type!=JTokenType.String)throw new InvalidDataException("capability-result-fields");
   string encoded=data["value"].Type==JTokenType.Null?null:(string)data["value"];if(encoded!=null&&encoded.Length>43692)throw new InvalidDataException("capability-result-value");var memory=new ManagedReservation(ledger,checked((encoded?.Length??0)+1024L));
   byte[] value=encoded==null?null:Convert.FromBase64String(encoded);if(value!=null&&(value.Length>32768||Convert.ToBase64String(value)!=encoded))throw new InvalidDataException("capability-result-base64");
   bool error=((string)data["error"]).Length>0;if(error!=reply.Error)throw new InvalidDataException("capability-result-status");entry.Value=value;entry.Version=(string)data["version"];entry.Reason=(string)data["error"];entry.Error=error;entry.State=entry.Reason=="Closed"?5:error?4:3;entry.Parsed=true;memory.Bind(entry);GC.KeepAlive(frame);return entry;
  }
  string Copy(string value){if(string.IsNullOrEmpty(value))return value;var frame=AllocationFrame.Begin(ledger);frame.Charge(checked(value.Length*2L+64));var memory=new ManagedReservation(ledger,checked(value.Length*2L+64));string result=new string(value.ToCharArray());memory.Bind(result);GC.KeepAlive(frame);return result;}
  public int State(int id){var entry=Ensure(id);return entry==null?0:entry.Parsed?entry.State:client.State(id);}
  public string Version(int id){var entry=Ensure(id);return entry?.Parsed==true?Copy(entry.Version):null;}
  public string Error(int id){var entry=Ensure(id);return entry?.Parsed==true?Copy(entry.Reason):null;}
  public string TakeText(int id){
   var entry=Ensure(id);if(entry?.Parsed!=true)return null;string result=null;
   if(!entry.Error&&entry.Value!=null){var memory=new ManagedReservation(ledger,checked(entry.Value.Length*2L+64));result=Utf8.GetString(entry.Value);if(result.Length>4096)throw new InvalidDataException("storage-text-result-too-large");if(result.Length>0)memory.Bind(result);}
   entries.Remove(id);return result;
  }
  public bool Release(int id){var entry=Ensure(id);return entry?.Parsed==true&&entries.Remove(id);}
 }
}
