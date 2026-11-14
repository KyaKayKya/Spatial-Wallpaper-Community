using System;using System.IO;using System.Text;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision.Windows;
static class StorageBoundaryChecks {
 static int Main(string[] args){WorkerWritePipe output=null;try{
  using(var readable=new FileStream(args[1],FileMode.Open,FileAccess.Read,FileShare.Read)){if(readable.Length<1)throw new Exception("full-trust-storage-control");}
  JObject observed;using(var station=new PrivateStation()){
   var values=new[]{@"\\.\pipe\SpatialWallpaper.NoSuch."+Guid.NewGuid().ToString("N"),args[1],"0"};
   using(var child=RestrictedProcess.Start(station,Path.Combine(args[0],"Probe.exe"),values,args[0],128L*1024*1024,sid=>{output=new WorkerWritePipe(sid);values[2]=output.ChildWriter.ToInt64().ToString();return new[]{output.ChildWriter};},null)){
    output.CloseChildCopy();if(child.Wait(5000)!=0||child.ExitCode!=0)throw new Exception("storage-native-probe-exit");
    var bytes=new byte[4096];int used=0,count;while((count=output.Read(bytes,used,bytes.Length-used))>0){used+=count;if(used==bytes.Length)throw new Exception("storage-probe-size");}
    observed=JObject.Parse(Encoding.UTF8.GetString(bytes,0,used));if((int?)observed["endpoint_file_open_error"]!=5)throw new Exception("LPAC-read-private-store:"+observed);
   }
  }
  File.WriteAllText(args[2],observed.ToString());Console.WriteLine("PASS full-trust storage readable; direct LPAC native open denied");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{output?.Dispose();}}
}
