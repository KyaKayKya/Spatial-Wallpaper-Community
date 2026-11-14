using System;using System.IO;using System.Linq;using System.Text;using System.Threading;using System.Collections.Generic;using System.Security.Cryptography;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Supervision.Windows {
 public sealed class AdmissionRejectedException:AdmissionFailureException {public AdmissionRejectedException(string code):base("restricted-Validator-rejected:"+code){}}
 public sealed class ValidationReceipt {
  public string BuildId{get;}public string PackageDigest{get;}readonly string[] capabilities;public IReadOnlyList<string> Capabilities=>Array.AsReadOnly(capabilities);
  ValidationReceipt(string build,string hash,string[] tags){BuildId=build;PackageDigest=hash;capabilities=tags;}
  static bool Digest(string value)=>value!=null&&value.Length==64&&value.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
  internal static ValidationReceipt Parse(byte[] bytes,int length,string expectedHash,uint exitCode){
   JObject report;using(var input=new StringReader(new UTF8Encoding(false,true).GetString(bytes,0,length)))using(var reader=new JsonTextReader(input){MaxDepth=4,DateParseHandling=DateParseHandling.None}){report=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new InvalidDataException("Validator-report-tail");}
   string[] fields={"version","accepted","archive_sha256","rules_sha256","build","capabilities","error"};if(!report.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(fields.OrderBy(n=>n,StringComparer.Ordinal)))throw new InvalidDataException("Validator-report-fields");
   if(report["version"].Type!=JTokenType.Integer||(int)report["version"]!=1||report["accepted"].Type!=JTokenType.Boolean||report["archive_sha256"].Type!=JTokenType.String||(string)report["archive_sha256"]!=expectedHash||report["rules_sha256"].Type!=JTokenType.String||(string)report["rules_sha256"]!=CapabilityNames.RulesDigest||report["error"].Type!=JTokenType.String||((string)report["error"]).Length>96)throw new InvalidDataException("Validator-report-identity");
   var tags=report["capabilities"] as JArray;if(tags==null||tags.Count>32||tags.Any(t=>t.Type!=JTokenType.String||!CapabilityNames.Contains((string)t)))throw new InvalidDataException("Validator-report-capabilities");
   string[] names=tags.Select(t=>(string)t).ToArray();if(!names.SequenceEqual(names.Distinct(StringComparer.Ordinal).OrderBy(n=>n,StringComparer.Ordinal)))throw new InvalidDataException("Validator-report-capability-order");
   if(report["build"].Type!=JTokenType.String)throw new InvalidDataException("Validator-report-build");
   if(!(bool)report["accepted"]){if(exitCode!=10||names.Length!=0||(string)report["build"]!=""||(string)report["error"]=="")throw new InvalidDataException("Validator-report-rejection");throw new AdmissionRejectedException((string)report["error"]);}
   if(exitCode!=0||!Digest((string)report["build"])||(string)report["error"]!="")throw new InvalidDataException("Validator-report-success");
   return new ValidationReceipt((string)report["build"],expectedHash,names);
  }
 }
 // Runs on the existing launch lane; it never parses package content in the trusted Supervisor.
 public sealed class ValidatorLauncher {
  readonly PrivateStation station;readonly WorkerImage image;readonly string attempts;readonly Action<Exception> cleanupFailure;
  public ValidatorLauncher(PrivateStation owner,WorkerImage distribution,string attemptDirectory,Action<Exception> cleanupFailure=null){this.cleanupFailure=cleanupFailure;station=owner??throw new ArgumentNullException(nameof(owner));image=distribution??throw new ArgumentNullException(nameof(distribution));attempts=Path.GetFullPath(attemptDirectory);Directory.CreateDirectory(attempts);if((File.GetAttributes(attempts)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Validator-cache-link");}
  public ValidationReceipt Validate(string immutablePackage){
   Guid attempt=Guid.NewGuid();var workspace=new OwnedAttempt(attempts,attempt,cleanupFailure);RestrictedProcess child=null;WorkerWritePipe pipe=null;Thread reader=null;Exception readError=null;byte[] bytes=null;int used=0;
   try{
    bytes=new byte[8192];
    image.Copy(workspace.Root);string hash;string path=Path.Combine(workspace.Root,"scene.spatial");if((File.GetAttributes(immutablePackage)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Validator-package-link");
    using(var source=new FileStream(immutablePackage,FileMode.Open,FileAccess.Read,FileShare.Read)){
     if(source.Length<1||source.Length>64L*1024*1024)throw new InvalidDataException("Validator-package-size");using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(source)).Replace("-","").ToLowerInvariant();source.Position=0;using(var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))source.CopyTo(target);
    }
    string boundary=WindowsSandboxCanaries.Create(attempts,workspace);var args=new[]{path,"0",Path.Combine(boundary,"aap"),Path.Combine(boundary,"arap")};IntPtr output=IntPtr.Zero;
    child=RestrictedProcess.Start(station,Path.Combine(workspace.Root,image.Executable),args,workspace.Root,512L*1024*1024,sid=>{pipe=new WorkerWritePipe(sid);output=pipe.ChildWriter;args[1]=output.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);return new[]{output};},pid=>pipe.CloseChildCopy(),attempt:attempt,standardOutput:()=>output,cleanupFailure:cleanupFailure);
    reader=new Thread(()=>{try{var extra=new byte[1];while(true){int count=used<bytes.Length?pipe.Read(bytes,used,bytes.Length-used):pipe.Read(extra,0,1);if(count==0)break;if(used==bytes.Length)throw new InvalidDataException("Validator-output-limit");used+=count;}}catch(Exception error){readError=error;}}){IsBackground=true,Name="SpatialWallpaper admission result"};reader.Start();
    if(child.Wait(5000)!=0)throw new TimeoutException("restricted-Validator-deadline");if(!reader.Join(1000))throw new TimeoutException("Validator-output-deadline");if(readError!=null)throw new InvalidDataException("Validator-output-read",readError);return ValidationReceipt.Parse(bytes,used,hash,child.ExitCode);
   }finally{
    Exception failure=null;Action<Action> clean=operation=>{try{operation();}catch(Exception error){cleanupFailure?.Invoke(error);if(failure==null)failure=error;}};
    clean(()=>{if(child!=null){child.Kill(124);if(child.Wait(5000)!=0)throw new TimeoutException("Validator-cleanup-kernel-deadline");}});
    clean(()=>child?.Dispose());clean(()=>pipe?.Dispose());
    clean(()=>{if(reader!=null&&!reader.Join(1000))throw new TimeoutException("Validator-cleanup-reader-deadline");});
    clean(()=>{workspace.Dispose();});if(failure!=null)throw failure;
   }
  }
 }
}
