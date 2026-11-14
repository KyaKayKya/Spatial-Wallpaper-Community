using System;using System.IO;using System.Collections.Generic;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision.Windows;
static class VmCompatibilityChecks {
 static WorkerImage Image(string path){var files=new Dictionary<string,string>();foreach(var p in ((JObject)JObject.Parse(File.ReadAllText(Path.Combine(path,"build.json")))["files"]).Properties())files.Add(p.Name,(string)p.Value);return new WorkerImage(path,"Validator.exe",files);}
 static int Main(string[] args){try{
  int driver=int.Parse(args[6]);var results=new JArray();using(var station=new PrivateStation()){
   for(int validator=0;validator<2;validator++)for(int package=0;package<2;package++){
    var launch=new ValidatorLauncher(station,Image(args[validator]),args[4]);bool accepted=false;string error="";
    try{launch.Validate(args[2+package]);accepted=true;}catch(AdmissionRejectedException e){error=e.Message;}catch(InvalidDataException e){if(e.Message!="Validator-report-identity")throw;error=e.Message;}
    if(accepted!=(validator==driver&&package==driver))throw new Exception("VM-identity-cross-admission:"+driver+":"+validator+":"+package+":"+error);
    results.Add(new JObject{{"driver",driver==0?"normalized-1":"normalized-2"},{"validator",validator==0?"normalized-1":"normalized-2"},{"package",package==0?"normalized-1":"normalized-2"},{"accepted",accepted},{"error",error}});
   }
   if(station.ChildCount!=0)throw new Exception("compatibility-validator-child-leak");
  }
  File.WriteAllText(args[5],results.ToString());Console.WriteLine("PASS real LPAC matching driver/Validator/package accept; mixed identities reject; no child leak");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
