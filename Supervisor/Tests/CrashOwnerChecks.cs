using System;using System.IO;using System.Threading;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Windows;
static class CrashOwnerChecks {
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[1]),record=Path.Combine(root,"ready.json");
  if(args[0]=="--verify"){
   var entries=JArray.Parse(File.ReadAllText(record));using(var journal=new FaultJournal(Path.Combine(root,"journal"))){
    if(journal.ActiveCount!=0)throw new Exception("unclean-recovery-retained-active");
    foreach(JObject entry in entries){
     Guid install=Guid.Parse((string)entry["installation"]);string build=(string)entry["build"];
     if(!journal.IsQuarantined(install,build))throw new Exception("crash-recovery-did-not-quarantine-all");
     bool denied=false;try{journal.Begin(install,build,Guid.NewGuid(),2,StartIntent.Resume);}catch(InvalidOperationException){denied=true;}
     if(!denied)throw new Exception("crashed-instance-auto-resumed");
    }
   }
   Console.WriteLine("PASS abnormal owner exit kills both Jobs; recovery quarantines all active builds");return 0;
  }
  var station=new PrivateStation();var log=new FaultJournal(Path.Combine(root,"journal"));var ready=new JArray();
  for(int i=0;i<2;i++){
   string runtime=Path.Combine(root,"worker"+i);Directory.CreateDirectory(runtime);File.Copy(Path.Combine(root,"ProcessProbe.exe"),Path.Combine(runtime,"ProcessProbe.exe"));File.WriteAllText(Path.Combine(runtime,".sdk-worker-owned"),"SDK crash fixture");
   var receipt=log.Begin(Guid.NewGuid(),new string(i==0?'c':'d',64),Guid.NewGuid(),1,StartIntent.Manual);
   FrameSignalPipe pipe=null;var command=new[]{"--blocked","0"};
   var child=RestrictedProcess.Start(station,Path.Combine(runtime,"ProcessProbe.exe"),command,runtime,128L*1024*1024,sid=>{pipe=new FrameSignalPipe(sid);command[1]=pipe.ChildWriter.ToInt64().ToString();return new[]{pipe.ChildWriter};},pid=>{log.RecordProcess(receipt,pid);pipe.CloseChildCopy();},receipt.Attempt);
   bool frame;long sequence;if(!pipe.Receive(out frame,out sequence)||!frame||sequence!=1)throw new Exception("crash-fixture-first-frame-missing");log.MarkRunning(receipt);
   ready.Add(new JObject{{"pid",child.ProcessId},{"profile",child.ProfileName},{"installation",receipt.Installation.ToString("N")},{"build",receipt.BuildId}});
   // Native handles remain owned by this process until the driver forcibly terminates it.
   GC.KeepAlive(child);GC.KeepAlive(pipe);
  }
  File.WriteAllText(record,ready.ToString());Thread.Sleep(Timeout.Infinite);return 99;
 }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}}
}
