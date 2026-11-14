// Original MIT direct OS probe using the production launcher and inherited output only.
using System;using System.IO;using System.Text;using SpatialWallpaper.Supervision.Windows;
static class PermissionBoundaryChecks {
 static int Main(string[] args){try{WorkerWritePipe pipe=null;string root=Path.GetFullPath(args[0]);var command=new[]{Path.Combine(root,"denied-output"),args[1],args[2],args[3],"0"};
  try{using(var station=new PrivateStation())using(var child=RestrictedProcess.Start(station,Path.Combine(root,"Probe.exe"),command,root,128L*1024*1024,sid=>{pipe=new WorkerWritePipe(sid);command[4]=pipe.ChildWriter.ToInt64().ToString();return new[]{pipe.ChildWriter};},null)){
   pipe.CloseChildCopy();if(child.Wait(10000)!=0||child.ExitCode!=0)throw new Exception("permission-probe-exit");var bytes=new byte[4096];int at=0,count;while((count=pipe.Read(bytes,at,bytes.Length-at))>0){at+=count;if(at==bytes.Length)throw new Exception("permission-probe-size");}File.WriteAllText(args[4],Encoding.UTF8.GetString(bytes,0,at));
  }}finally{pipe?.Dispose();}return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
