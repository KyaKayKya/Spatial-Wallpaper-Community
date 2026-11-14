using System;using System.IO;using System.Linq;using System.Text;using Newtonsoft.Json;using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Sdk.Hosting {
 // Reading a snapshot does not prove liveness. The caller must retain the exact
 // viewer process and require heartbeat progress before showing it as running.
 public static class PreviewStatusFile {
  public static JObject Read(string path,int viewerPid,long viewerCreation){
   return Read(path,viewerPid,viewerCreation,out var unavailable);
  }
  public static JObject Read(string path,int viewerPid,long viewerCreation,out int unavailable){
   try{return ReadCore(path,viewerPid,viewerCreation,out unavailable);}catch(Exception error)when(error is JsonException||error is OverflowException||error is InvalidCastException||error is DecoderFallbackException){throw new InvalidDataException("preview-status-contract",error);}
  }
  static JObject ReadCore(string path,int viewerPid,long viewerCreation,out int unavailable){
   unavailable=0;
   if(viewerPid<=0||viewerCreation<=0)throw new ArgumentException("preview-status-owner");JObject value;
   try{using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){if(file.Length<1||file.Length>32768||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("preview-status-file");using(var text=new StreamReader(file,new UTF8Encoding(false,true),false,1024))using(var reader=new JsonTextReader(text){MaxDepth=12,DateParseHandling=DateParseHandling.None}){value=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new InvalidDataException("preview-status-tail");}}}
   catch(FileNotFoundException){unavailable=2;return null;}catch(DirectoryNotFoundException){unavailable=3;return null;}catch(IOException error)when((error.HResult&65535)==32||(error.HResult&65535)==33){unavailable=error.HResult&65535;return null;}
   var fields=new[]{"format","state","sequence","viewer_pid","viewer_creation_filetime","host_pid","host_creation_filetime","runtime_stage","receipt","error","window_resources_released","host_resources_released","forced_host_termination","host_diagnostic_tail"};if(!value.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(fields.OrderBy(n=>n,StringComparer.Ordinal)))throw new InvalidDataException("preview-status-fields");
   foreach(string name in new[]{"format","sequence","viewer_pid","viewer_creation_filetime","host_pid","host_creation_filetime"})if(value[name].Type!=JTokenType.Integer)throw new InvalidDataException("preview-status-number");if((int)value["format"]!=1||(long)value["sequence"]<=0||(int)value["viewer_pid"]!=viewerPid||(long)value["viewer_creation_filetime"]!=viewerCreation||(int)value["host_pid"]<0||(long)value["host_creation_filetime"]<0||((int)value["host_pid"]==0)!=((long)value["host_creation_filetime"]==0))throw new InvalidDataException("preview-status-owner-or-version");
   foreach(string name in new[]{"window_resources_released","host_resources_released","forced_host_termination"})if(value[name].Type!=JTokenType.Boolean)throw new InvalidDataException("preview-status-flag");
   if(value["state"].Type!=JTokenType.String||!new[]{"CheckingRuntime","StartingHost","Starting","Running","Stopped","Stopping","Closing","Faulted","Failed"}.Contains((string)value["state"])||value["runtime_stage"].Type!=JTokenType.Null&&(value["runtime_stage"].Type!=JTokenType.String||!new[]{"development","rc"}.Contains((string)value["runtime_stage"]))||value["error"].Type!=JTokenType.Null&&(value["error"].Type!=JTokenType.String||((string)value["error"]).Length>2048)||value["host_diagnostic_tail"].Type!=JTokenType.String||((string)value["host_diagnostic_tail"]).Length>8192||value["receipt"].Type!=JTokenType.Null&&!(value["receipt"] is JObject))throw new InvalidDataException("preview-status-shape");
   var receipt=value["receipt"] as JObject;if(receipt!=null)Receipt(receipt);if(((string)value["state"]=="Running")&&(receipt==null||(int)value["host_pid"]==0||(bool)value["window_resources_released"]||(bool)value["host_resources_released"]||(bool)value["forced_host_termination"]))throw new InvalidDataException("preview-status-running-proof");return value;
  }
  static void Receipt(JObject value){
   var fields=new[]{"attempt","instance","generation","build","sequence","projection_revision","target","target_revision","width","height","scope","work_id","revision_id"};if(!value.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(fields.OrderBy(n=>n,StringComparer.Ordinal)))throw new InvalidDataException("preview-status-receipt-fields");
   foreach(string name in new[]{"attempt","instance","target","work_id","revision_id"})if(value[name].Type!=JTokenType.String||!Guid.TryParseExact((string)value[name],name=="work_id"||name=="revision_id"?"D":"N",out var identity)||identity==Guid.Empty)throw new InvalidDataException("preview-status-receipt-identity");
   foreach(string name in new[]{"generation","sequence","projection_revision","target_revision","width","height"})if(value[name].Type!=JTokenType.Integer||(long)value[name]<=0)throw new InvalidDataException("preview-status-receipt-number");if((long)value["generation"]>uint.MaxValue||(long)value["width"]>RuntimeLimits.MaxRenderSide||(long)value["height"]>RuntimeLimits.MaxRenderSide||checked((long)value["width"]*(long)value["height"])>RuntimeLimits.MaxRenderPixels)throw new InvalidDataException("preview-status-receipt-size");
   foreach(string name in new[]{"build"})if(value[name].Type!=JTokenType.String||((string)value[name]).Length!=64||((string)value[name]).Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))throw new InvalidDataException("preview-status-receipt-digest");if(value["scope"].Type!=JTokenType.String||(string)value["scope"]!="owned-target-frame-submission")throw new InvalidDataException("preview-status-receipt-scope");
  }
 }
}
