using System;using System.IO;using System.Reflection;using Newtonsoft.Json;using Newtonsoft.Json.Linq;
static class InstallStatusReadChecks {
 static JObject Read(string path){try{return (JObject)typeof(InstallUpgradeChecks).GetMethod("ReadStatus",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{path});}catch(TargetInvocationException error){throw error.InnerException;}}
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);Console.WriteLine("PASS "+reason);}
 static int Main(string[] args){try{string root=args[0],path=Path.Combine(root,"status.json");Directory.CreateDirectory(root);
  Need(Read(path)==null,"not-yet-published-status-is-pending");
  Need(Read(Path.Combine(root,"absent/status.json"))==null,"not-yet-created-status-directory-is-pending");
  File.WriteAllText(path,"{\"sequence\":1}");using(var held=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
   bool conflict=false;try{using(var other=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){} }catch(IOException error){conflict=(error.HResult&65535)==32;}Need(conflict,"controlled-exclusive-open-produces-real-sharing-violation");
   Need(Read(path)==null,"sharing-conflict-is-pending-within-original-poll");}
  Need((int)Read(path)["sequence"]==1,"released-status-is-readable");
  File.WriteAllText(path,"{");bool malformed=false;try{Read(path);}catch(JsonReaderException){malformed=true;}Need(malformed,"malformed-status-still-fails");
  bool invalid=false;try{Read(null);}catch(ArgumentException){invalid=true;}Need(invalid,"other-errors-still-fail");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
