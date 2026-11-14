using System;using System.IO;using System.Linq;using System.Collections.Generic;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision.Windows;using SpatialWallpaper.Sdk.Hosting;
static class ValidatorChecks {
 static void Require(bool value,string code){if(!value)throw new Exception(code);}
 static void Reject(Action action){bool rejected=false;try{action();}catch(Exception){rejected=true;}Require(rejected,"invalid-Validator-report-accepted");}
 static int Main(string[] args){try{
  var inventory=JObject.Parse(File.ReadAllText(args[1]));var files=new Dictionary<string,string>();foreach(var p in ((JObject)inventory["files"]).Properties())files.Add(p.Name,(string)p.Value);
  using(var station=new PrivateStation()){
   var validator=new ValidatorLauncher(station,new WorkerImage(args[0],"Validator.exe",files),args[2]);
   var duration=System.Diagnostics.Stopwatch.StartNew();var result=validator.Validate(args[3]);long positiveMs=duration.ElapsedMilliseconds;Require(result.BuildId==args[4]&&result.Capabilities.SequenceEqual(new[]{"audio-features","input","llm-proxy","private-storage","view-data"}),"wrong-capability-closure");
   bool denied=false;try{validator.Validate(args[5]);}catch(AdmissionRejectedException){denied=true;}Require(denied,"bad-package-accepted");
   Require(Directory.EnumerateDirectories(args[2]).All(p=>Path.GetFileName(p)=="boundary")&&!Directory.EnumerateFileSystemEntries(Path.Combine(args[2],"boundary")).Any(),"Validator-copy-not-reclaimed");
   var report=new JObject{{"version",1},{"accepted",true},{"archive_sha256",result.PackageDigest},{"rules_sha256",CapabilityNames.RulesDigest},{"build",result.BuildId},{"capabilities",new JArray("input")},{"error",""}};
   Action<JObject,uint> parse=(data,code)=>{byte[] bytes=System.Text.Encoding.UTF8.GetBytes(data.ToString());ValidationReceipt.Parse(bytes,bytes.Length,result.PackageDigest,code);};
   parse(report,0);Reject(()=>parse(report,10));var wrong=(JObject)report.DeepClone();wrong["archive_sha256"]=new string('0',64);Reject(()=>parse(wrong,0));wrong=(JObject)report.DeepClone();wrong["rules_sha256"]=new string('0',64);Reject(()=>parse(wrong,0));wrong=(JObject)report.DeepClone();wrong["capabilities"]=new JArray("arbitrary-network");Reject(()=>parse(wrong,0));wrong=(JObject)report.DeepClone();wrong["capabilities"]=new JArray("input","input");Reject(()=>parse(wrong,0));wrong=(JObject)report.DeepClone();wrong["installation"]=Guid.NewGuid().ToString("N");Reject(()=>parse(wrong,0));
   string faultExe=Path.Combine(args[6],"Validator.exe");string faultHash;using(var sha=System.Security.Cryptography.SHA256.Create())faultHash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(faultExe))).Replace("-","").ToLowerInvariant();var faulty=new ValidatorLauncher(station,new WorkerImage(args[6],"Validator.exe",new Dictionary<string,string>{{"Validator.exe",faultHash}}),args[2]);
   var clock=System.Diagnostics.Stopwatch.StartNew();bool timedOut=false;try{faulty.Validate(args[7]);}catch(TimeoutException){timedOut=true;}long hangMs=clock.ElapsedMilliseconds;Require(timedOut&&clock.ElapsedMilliseconds>=4900&&clock.ElapsedMilliseconds<9000,"Validator-timeout-not-bounded");
   bool oversized=false;try{faulty.Validate(args[8]);}catch(InvalidDataException){oversized=true;}Require(oversized,"oversized-Validator-output-accepted");Require(!Directory.EnumerateFileSystemEntries(Path.Combine(args[2],"boundary")).Any(),"fault-Validator-boundary-not-reclaimed");
   string duplicate=report.ToString(Newtonsoft.Json.Formatting.None).Insert(1,"\"version\":1,");Reject(()=>{byte[] bytes=System.Text.Encoding.UTF8.GetBytes(duplicate);ValidationReceipt.Parse(bytes,bytes.Length,result.PackageDigest,0);});
   Require(station.ChildCount==0,"Validator-child-ownership-leak");File.WriteAllText(args[9],new JObject{{"positive_lpac_ms",positiveMs},{"infinite_process_total_ms",hangMs},{"oversized_output_rejected",oversized},{"unknown_fields_tags_duplicate_and_identity_rejected",true},{"capabilities",new JArray(result.Capabilities)},{"canonical_build",result.BuildId},{"package_sha256",result.PackageDigest},{"no_live_children_after_cases",true}}.ToString());
  }
  Console.WriteLine("PASS real LPAC Validator returns canonical digest and conservative capability closure; bad package denied");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
