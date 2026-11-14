using System;using System.IO;using System.Text;using SpatialWallpaper.Supervision.Windows;
static class ProfileBoundaryChecks{
 static int Main(string[] args){try{
  string root=Path.GetFullPath(args[0]);WorkerWritePipe pipe=null;string[] command={"unused","pending","pending"};
  try{using(var station=new PrivateStation())using(var child=RestrictedProcess.Start(station,Path.Combine(root,"Probe.exe"),command,root,128L*1024*1024,sid=>{
   pipe=new WorkerWritePipe(sid);command[1]=Path.Combine(RestrictedProcess.ProfileDirectory(sid),"sdk-synthetic-canary.txt");command[2]=pipe.ChildWriter.ToInt64().ToString();return new[]{pipe.ChildWriter};
  },null)){
   pipe.CloseChildCopy();if(child.Wait(5000)!=0||child.ExitCode!=0)throw new Exception("profile-probe-exit");
   var bytes=new byte[4096];int used=0,n;while((n=pipe.Read(bytes,used,bytes.Length-used))>0){used+=n;if(used==bytes.Length)throw new Exception("probe-size");}
   File.WriteAllText(args[1],Encoding.UTF8.GetString(bytes,0,used));
  }}finally{if(pipe!=null)pipe.Dispose();}
  return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
