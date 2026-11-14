extern alias supervisor;
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Native;
using SpatialWallpaper.Sdk.Capabilities;
using Windows=supervisor::SpatialWallpaper.Supervision.Windows;
using Host=supervisor::SpatialWallpaper.Sdk.Hosting;
using Caps=supervisor::SpatialWallpaper.Sdk.Capabilities;

static class NativeInputAuthorityChecks {
 static readonly List<string> checks=new List<string>();
 static void Yes(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
 static void No(Action action,string label){try{action();}catch(InvalidDataException){checks.Add(label);return;}catch(InvalidOperationException){checks.Add(label);return;}throw new Exception("accepted:"+label);}
 sealed class Target:INativeCommandTarget {
  public int Commits;public Action Preparing;long revision=1,sequence;
  public SceneSnapshot Snapshot=>new SceneSnapshot(revision,++sequence,new[]{new SceneObject(1,"node")});
  public bool TryPrepare(CommandBatch batch,out object prepared){Preparing?.Invoke();prepared=batch;return true;}
  public BatchAck Commit(CommandBatch batch,object prepared){Commits++;revision++;return new BatchAck(batch.Identity,batch.Tick,batch.Sequence,true,Snapshot);}
  public void CancelPreparation(){}
 }
 static long Now()=>Caps.WindowsMachineClock.Milliseconds();
 static void Live(Caps.InputTarget target,long sequence=1){long now=Now();target.Observe(new Caps.InputObservation(sequence,now,true,true,true,true,true,false,true,1,.5f,.5f,0,target.CaptureEpoch));}
 static int Child(string[] args){
  using(var renderer=Host.PinnedProcessOwner.Open(int.Parse(args[1]),long.Parse(args[2])))
  using(var broker=new Caps.InputBroker())using(var registry=new Windows.NativeInputRegistry(broker)){
   var identity=new Host.WorkerIdentity(Guid.ParseExact(args[3],"N"),Guid.ParseExact(args[4],"N"),1);
   registry.RegisterOrdered(identity,true,renderer.Id,renderer.CreationFileTime);string name=registry.OpenAuthority(identity,renderer.Id,renderer.CreationFileTime);Live(registry.Find(identity,true,renderer.Id,renderer.CreationFileTime));
   Console.WriteLine(name);Console.Out.Flush();Console.ReadLine();
   // Exit without managed cleanup so the retained read-only view still holds
   // a positive epoch. Only the original Host kernel exit can revoke this case.
   Environment.Exit(0);return 0;
  }
 }
 static void HostExit(){
  using(var self=Host.PinnedProcessOwner.PinCurrent()){
   var id=new Host.WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);
   var start=new ProcessStartInfo(typeof(NativeInputAuthorityChecks).Assembly.Location,"--authority-host "+self.Id+" "+self.CreationFileTime+" "+id.Attempt.ToString("N")+" "+id.Instance.ToString("N")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true};
   using(var child=Process.Start(start))try{
    using(var owner=Host.PinnedProcessOwner.Open(child.Id,Host.PinnedProcessOwner.ReadCreationFileTime(child.Handle))){
     string name=child.StandardOutput.ReadLine();using(var read=new Host.NativeInputAuthorityReader(name,id,owner,self)){
      Yes(read.Read().Epoch!=0,"actual child Host publishes authority");child.StandardInput.WriteLine("exit");child.StandardInput.Flush();Yes(child.WaitForExit(5000)&&child.ExitCode==0,"original authority Host exited");Yes(read.Read().Epoch==0,"Host exit revokes retained positive mapped record");
     }
    }
   }finally{if(!child.HasExited){child.StandardInput.Close();child.WaitForExit(5000);}}
  }
 }
 static int Run(string[] args){
  if(args[0]=="--authority-host")return Child(args);
  bool cached=args.Length>1&&args[1]=="--cached-authority";string output=args[0];Directory.CreateDirectory(output);
  using(var self=Host.PinnedProcessOwner.PinCurrent())using(var broker=new Caps.InputBroker())using(var registry=new Windows.NativeInputRegistry(broker)){
   var id=new Host.WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);registry.RegisterOrdered(id,true,self.Id,self.CreationFileTime);
   string name=registry.OpenAuthority(id,self.Id,self.CreationFileTime);var input=registry.Find(id,true,self.Id,self.CreationFileTime);
   using(var read=new Host.NativeInputAuthorityReader(name,id,self,self)){
    Yes(read.Read().Epoch==0,"registered but unfocused record grants no authority");
    No(()=>{using(var wrong=new Host.NativeInputAuthorityReader(name,new Host.WorkerIdentity(Guid.NewGuid(),id.Instance,1),self,self)){}},"attempt mismatch rejected");
    No(()=>{using(var wrong=new Host.NativeInputAuthorityReader(name,new Host.WorkerIdentity(id.Attempt,id.Instance,2),self,self)){}},"generation mismatch rejected");
    No(()=>registry.OpenAuthority(new Host.WorkerIdentity(id.Attempt,id.Instance,2),self.Id,self.CreationFileTime),"manager cannot open old generation");
    Live(input);byte[] frozen=input.Snapshot();ulong epoch=BitConverter.ToUInt64(frozen,8);Yes(read.Read().Epoch==epoch&&epoch!=0,"real Broker grants exact current epoch");
    var identity=new SessionIdentity(id.Instance,1,SceneCommandCatalog.Digest);var target=new Target();
    Func<InputAuthority> source=()=>{var value=read.Read();return new InputAuthority(value.Epoch,value.Time);};
    using(var pump=new ScriptBridgePump(identity,target,cached?null:source,Now)){
     pump.PublishInputAuthority(frozen,Now());pump.MarkTickIssued(new TickInput(1,target.Snapshot,inputs:frozen));pump.TryEnqueue(new CommandBatch(identity,1,1,1,new[]{new SceneCommand("SetPosition",1,new JArray(3,0,0))}));
     input.Revoke();Yes(read.Read().Epoch==0,"Broker.Revoke completed and authoritative record revoked");
     // Deliberately never refresh the frozen background snapshot after revoke.
     pump.Pump();Yes(target.Commits==0&&pump.Stopped,"completed remote revoke defeats frozen background cache before commit");
    }
    Live(input,2);var live=input.Snapshot();var preparing=new Target();using(var pump=new ScriptBridgePump(identity,preparing,source,Now)){
     pump.PublishInputAuthority(live,Now());pump.MarkTickIssued(new TickInput(1,preparing.Snapshot,inputs:live));pump.TryEnqueue(new CommandBatch(identity,1,1,1,new[]{new SceneCommand("SetPosition",1,new JArray(3,0,0))}));preparing.Preparing=()=>input.Revoke();pump.Pump();Yes(preparing.Commits==0&&pump.Stopped,"Broker revoke inside preparation rejects at final commit check");
    }
    Live(input,3);live=input.Snapshot();var allowed=new Target();using(var pump=new ScriptBridgePump(identity,allowed,source,Now)){
     pump.PublishInputAuthority(live,Now());pump.MarkTickIssued(new TickInput(1,allowed.Snapshot,inputs:live));pump.TryEnqueue(new CommandBatch(identity,1,1,1,new[]{new SceneCommand("SetPosition",1,new JArray(3,0,0))}));pump.Pump();Yes(allowed.Commits==1&&!pump.Stopped,"current shared authority allows exactly one native commit");
    }
    using(var other=broker.Register(new Host.WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),false)){Live(other);Yes(read.Read().Epoch==0,"exclusive focus transfer revokes original mapped authority");}
    Live(input,4);long old=(long)read.Read().Time;Thread.Sleep(300);Yes((long)read.Read().Time==old,"mapped reads cannot refresh original evidence time");var stale=input.Snapshot();Yes(read.Read().Epoch==0,"stale Broker read publishes revocation; source="+old+",now="+Now()+",snapshot-focus="+BitConverter.ToUInt32(stale,4)+",snapshot-epoch="+BitConverter.ToUInt64(stale,8)+",mapped-epoch="+read.Read().Epoch);
    Live(input,5);input.Observe(new Caps.InputObservation(6,Now(),false,true,true,true,true,false,false,0,.5f,.5f,0,input.CaptureEpoch));Yes(read.Read().Epoch==0,"invalid target evidence revokes mapped authority");
    Live(input,7);registry.Remove(id);Yes(read.Read().Epoch==0,"unregister publishes revoke before disposing writer");
   }
  }
  HostExit();File.WriteAllText(Path.Combine(output,"report.json"),new JObject{{"scope","actual Broker/read-only mapped authority/native command target; synthetic trusted observations, no OS focus or displayed-frame claim"},{"checks",new JArray(checks)},{"passed",checks.Count}}.ToString());Console.WriteLine("native input authority checks passed: "+checks.Count);return 0;
 }
 static int Main(string[] args){try{return Run(args);}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
