using System;using System.IO;using System.Threading;using System.Threading.Tasks;using SpatialWallpaper.Supervision.Windows;
static class RestrictedProcessChecks{
 static int Main(string[] args){try{
  using(var station=new PrivateStation()){
   var stress=new PrivateStation();var tasks=new Task[8];for(int i=0;i<tasks.Length;i++)tasks[i]=Task.Run(()=>{for(int n=0;n<200000;n++){stress.ChildCreated();Thread.Yield();stress.ChildReleased();}});Task.WaitAll(tasks);if(stress.ChildCount!=0)throw new Exception("station-ownership-counter-leaked");stress.Dispose();
   string root=Path.GetFullPath(args[0]);string exe=Path.Combine(root,"ProcessProbe.exe");
   if(args.Length==3){
    string before=File.GetAccessControl(args[1]).GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access);
    bool rejected=false;try{using(var forbidden=RestrictedProcess.Start(station,exe,new[]{"--exit"},root,128L*1024*1024,null,null)){}}
    catch(InvalidDataException error){rejected=error.Message==args[2];}
    string after=File.GetAccessControl(args[1]).GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access);
    if(!rejected||before!=after)throw new Exception("linked-runtime-modified-external-ACL");
    Console.WriteLine("PASS "+args[2]+" rejected before external ACL mutation");return 0;
   }
   for(int round=0;round<16;round++){
    var previous=RestrictedProcess.Start(station,exe,new[]{"--exit"},root,128L*1024*1024,null,null);if(previous.Wait(5000)!=0)throw new Exception("overlap-first-not-exited");var cleanup=Task.Run(()=>previous.Dispose());
    using(var next=RestrictedProcess.Start(station,exe,new[]{"--exit"},root,128L*1024*1024,null,null)){cleanup.GetAwaiter().GetResult();if(next.Wait(5000)!=0)throw new Exception("overlap-next-not-exited");}
    if(station.ChildCount!=0)throw new Exception("overlap-real-child-count-leaked");
   }
   using(var child=RestrictedProcess.Start(station,exe,new[]{"--exit"},root,128L*1024*1024,null,null)){
    if(child.Wait(5000)!=0||child.ExitCode!=0)throw new Exception("restricted-native-process-did-not-exit");
   }
  }
  Console.WriteLine("PASS C# LPAC/job/private-station native process startup");return 0;
 }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}}
}
