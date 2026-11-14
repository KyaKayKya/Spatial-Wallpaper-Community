using System;using System.IO;using System.Text;using System.Diagnostics;using System.Reflection;using System.Security.Cryptography;using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Capabilities;using SpatialWallpaper.Supervision.Interactive;using SpatialWallpaper.Supervision.Broker;
static partial class NativeDesktopInputHostChecks {
  static bool continuityDropRelease,continuityMixedMotion;static int continuitySeconds;static long continuityEdges,continuityMoves;static byte continuityOwed;
  static System.Drawing.Point continuityOrigin;static System.Drawing.Rectangle continuityDisplay;
 static readonly ushort[] continuityKeys={32,37,39,38,40,1,2};
 static readonly byte[] continuityBits={1,8,16,32,64,2,128};
 static readonly string[] continuityNames={"Space","Left","Right","Up","Down","Primary","Secondary"};
 sealed class ContinuityTarget:IDesktopInputTarget {
  readonly IDesktopInputTarget inner;readonly object gate=new object();uint held;long transitions;
  public ContinuityTarget(IDesktopInputTarget inner){this.inner=inner;}
  public ulong CaptureEpoch=>inner.CaptureEpoch;
  public long Transitions{get{lock(gate)return transitions;}}
  public uint Held{get{lock(gate)return held;}}
  public void Observe(InputObservation v){lock(gate){inner.Observe(v);if(v.Held!=held){transitions++;held=v.Held;}}}
  public void Revoke()=>inner.Revoke();public void AcquireFocus(InputObservation v)=>inner.AcquireFocus(v);
  public InputFocusConfirmation GetFocusConfirmation()=>inner.GetFocusConfirmation();
 }
 static ContinuityTarget continuityTarget;
 static IDesktopInputTarget TrackContinuity(IDesktopInputTarget target,string[] args){
   continuityMixedMotion=Array.IndexOf(args,"--interaction-mixed-motion")>=0;continuityDropRelease=Array.IndexOf(args,"--interaction-drop-release")>=0;foreach(string arg in args)if(arg.StartsWith("--interaction-seconds=",StringComparison.Ordinal)){continuitySeconds=int.Parse(arg.Substring(22));return continuityTarget=new ContinuityTarget(target);}return target;
 }
 static Event ContinuityEvent(int index,bool up){
  if(index<5)return new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=continuityKeys[index],Flags=up?2u:0u}}};
  return new Event{Data=new Payload{Mouse=new Mouse{Flags=index==5?(up?4u:2u):(up?16u:8u)}}};
 }
  static Event ContinuityMove(int offset){return new Event{Data=new Payload{Mouse=new Mouse{X=(int)(((long)(continuityOrigin.X+offset-continuityDisplay.Left)*65536+32768)/continuityDisplay.Width),Y=(int)(((long)(continuityOrigin.Y-continuityDisplay.Top)*65536+32768)/continuityDisplay.Height),Flags=0xa001}}};}
 static void ContinuitySend(int index,bool up){
  byte bit=continuityBits[index];if(!up){Need((GetAsyncKeyState(continuityKeys[index])&0x8000)==0,"continuity-no-preheld-key");continuityOwed|=bit;PersistObligations();}
   var events=continuityMixedMotion&&index<5?new[]{ContinuityMove(1),ContinuityEvent(index,up),ContinuityMove(0)}:new[]{ContinuityEvent(index,up)};
   long sent,returned;Need(ProcessInject(events,out sent,out returned)==events.Length,"continuity-owned-event-sent");
   if(events.Length==3){continuityMoves+=2;Need(GetCursorPos(out var actual)&&actual.X==continuityOrigin.X&&actual.Y==continuityOrigin.Y,"continuity-mixed-cursor-returned");}
  if(up){continuityOwed=(byte)(continuityOwed&~bit);PersistObligations();}
 }
 static void ReleaseContinuity(System.Collections.Generic.List<Exception> failures){
  for(int i=0;i<continuityBits.Length;i++)if((continuityOwed&continuityBits[i])!=0)try{
   long a,b;Need(ProcessInject(new[]{ContinuityEvent(i,true)},out a,out b)==1,"continuity-paired-release");continuityOwed=(byte)(continuityOwed&~continuityBits[i]);PersistObligations();
  }catch(Exception e){failures.Add(e);}
 }
 // Read only an atomic snapshot, without opening a second PrivateStore owner.
 static Tuple<long,string> ContinuityAck(string path){
  byte[] bytes;try{using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){if(f.Length<52||f.Length>1024)throw new InvalidDataException("continuity-snapshot-bound");bytes=new byte[f.Length];int at=0;while(at<bytes.Length){int n=f.Read(bytes,at,bytes.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}}}catch(FileNotFoundException){return Tuple.Create(0L,(string)null);}catch(DirectoryNotFoundException){return Tuple.Create(0L,(string)null);}
  using(var hash=SHA256.Create()){var sum=hash.ComputeHash(bytes,0,bytes.Length-32);for(int i=0;i<32;i++)if(sum[i]!=bytes[bytes.Length-32+i])throw new InvalidDataException("continuity-checksum");}
  using(var reader=new BinaryReader(new MemoryStream(bytes,0,bytes.Length-32),Encoding.UTF8)){
   if(reader.ReadUInt64()!=0x31564b5350575353UL)throw new InvalidDataException("continuity-magic");long revision=reader.ReadInt64();
   if(reader.ReadInt32()!=1)throw new InvalidDataException("continuity-key-count");int k=reader.ReadInt32(),v=reader.ReadInt32();
   if(k!=14||v<1||v>32||Encoding.UTF8.GetString(reader.ReadBytes(k))!="continuity-ack")throw new InvalidDataException("continuity-key");
   string value=Encoding.UTF8.GetString(reader.ReadBytes(v));if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("continuity-tail");return Tuple.Create(revision,value);
  }
 }
 static void CheckInteractionContinuity(WindowsDesktopInput source,DesktopInputChannel channel,string output,string snapshot){
  Need(backgroundInjection&&continuityTarget!=null&&continuitySeconds>0&&continuitySeconds<=1800,"continuity-owned-source-required");
  foreach(var key in continuityKeys)Need((GetAsyncKeyState(key)&0x8000)==0,"continuity-all-keys-initially-up");
   var fields=BindingFlags.Instance|BindingFlags.NonPublic;Func<string,long> read=name=>Convert.ToInt64(typeof(WindowsDesktopInput).GetField(name,fields).GetValue(source));long initialMoves=read("mouseMoveReceived"),initialCommits=read("mouseMoveCommits");
   if(continuityMixedMotion){Need(GetCursorPos(out var origin),"continuity-mixed-origin");continuityOrigin=new System.Drawing.Point(origin.X,origin.Y);continuityDisplay=System.Windows.Forms.Screen.PrimaryScreen.Bounds;Need(continuityDisplay.Contains(origin.X,origin.Y)&&continuityDisplay.Contains(origin.X+1,origin.Y),"continuity-mixed-display");Need(probe.Query(origin.X,origin.Y)?.Classification==1&&probe.Query(origin.X+1,origin.Y)?.Classification==1,"continuity-mixed-blank-points");}
  ulong epoch=channel.CaptureEpoch;long initial=continuityTarget.Transitions;var timer=Stopwatch.StartNew();int cycles=0;
  Action check=()=>{if(!source.Focused||GetForegroundWindow()!=window.Handle||source.Failure!=null||channel.Failure!=null||channel.CaptureEpoch!=epoch||!channel.GetFocusConfirmation().Focused)throw new Exception("continuity-focused-same-epoch");};
  // Every UI pump inside injection/waits checks continuity, not just cycle endpoints.
  replyGateTick=check;
  using(var journal=new FileStream(Path.Combine(output,"interaction-edges.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read))
  using(var log=new StreamWriter(journal,new UTF8Encoding(false))){
   try{
    while(timer.ElapsedMilliseconds<continuitySeconds*1000L){
     Need(cycles<8192,"continuity-cycle-bound");
     for(int step=0;step<14;step++){
       check();int index=step/2;bool up=step%2!=0;long expected=continuityEdges+1;string wanted=continuityNames[index]+(up?"-up":"-down");long beforeMoves=read("mouseMoveReceived"),beforeCommits=read("mouseMoveCommits");
      if(continuityDropRelease&&expected==2){
       Need(continuityEdges==1&&continuityTarget.Held==4&&continuityOwed==1&&(GetAsyncKeyState(32)&0x8000)!=0,"continuity-negative-held-before-omission");
       File.WriteAllText(Path.Combine(output,"interaction-omission.json"),new JObject{{"omitted_edge",2},{"revision_before",1},{"owed",1},{"source_edges",continuityTarget.Transitions-initial},{"epoch",epoch}}.ToString());
      }else ContinuitySend(index,up);var deadline=Stopwatch.StartNew();Tuple<long,string> ack;
      do{Pump(1);check();ack=ContinuityAck(snapshot);if(ack.Item1>expected)throw new Exception("continuity-extra-durable-edge");if(ack.Item1==expected)break;if(deadline.ElapsedMilliseconds>=5000)throw new Exception("continuity-missing-author-edge");}while(true);
      Need(ack.Item2==wanted,"continuity-author-ack-value");Need(continuityTarget.Transitions-initial==expected,"continuity-source-edge-count");
       if(continuityMixedMotion&&index<5){Need(read("mouseMoveReceived")-beforeMoves>=2,"continuity-mixed-hook-received");Need(read("mouseMoveCommits")-beforeCommits>=1,"continuity-mixed-move-committed");}
      uint wantedHeld=up?0u:index<5?1u<<(index+2):1u<<(index-5);Need(continuityTarget.Held==wantedHeld,"continuity-source-held");
      continuityEdges=expected;
       log.WriteLine(new JObject{{"edge",expected},{"cycle",cycles},{"step",step},{"ack",ack.Item2},{"revision",ack.Item1},{"epoch",epoch},{"source_edges",continuityTarget.Transitions-initial},{"elapsed_ms",timer.ElapsedMilliseconds},{"obligations",continuityOwed},{"moves_requested",continuityMoves},{"hook_moves",read("mouseMoveReceived")-initialMoves},{"move_commits",read("mouseMoveCommits")-initialCommits}}.ToString(Newtonsoft.Json.Formatting.None));log.Flush();journal.Flush(true);
     }
     cycles++;Need(continuityOwed==0&&continuityTarget.Held==0,"continuity-cycle-released");foreach(var key in continuityKeys)Need((GetAsyncKeyState(key)&0x8000)==0,"continuity-cycle-native-up");
    }
    check();Need(cycles>0&&continuityEdges==cycles*14L,"continuity-complete-cycles");
     File.WriteAllText(Path.Combine(output,"interaction-continuity.json"),new JObject{{"mode",continuitySeconds==1800?"formal-1800s":"development-short-only"},{"requested_seconds",continuitySeconds},{"elapsed_ms",timer.ElapsedMilliseconds},{"cycles",cycles},{"author_edges",continuityEdges},{"source_edges",continuityTarget.Transitions-initial},{"dropped_edges",0},{"epoch",epoch},{"owed",continuityOwed},{"mixed_motion",continuityMixedMotion},{"moves_requested",continuityMoves},{"hook_moves",read("mouseMoveReceived")-initialMoves},{"move_commits",read("mouseMoveCommits")-initialCommits}}.ToString());
   }finally{replyGateTick=null;}
  }
 }
}
