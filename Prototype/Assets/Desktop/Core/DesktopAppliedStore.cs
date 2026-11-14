using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Library;
using SpatialWallpaper.Sdk.Capabilities;
using SpatialWallpaper.Sdk.Hosting;

namespace SpatialWallpaper.Desktop {
 public sealed class DesktopAppliedSelection {
  public string ArtworkId{get;}public ArtworkSource Source{get;}public string RevisionId{get;}public string BuildId{get;}
  readonly CalibrationProfile profile;public CalibrationProfile Profile=>profile.Copy();public string DisplayId=>profile.manual.display_id;
  public SoundMix Sound{get;}public bool Paused{get;}
  public DesktopAppliedSelection(string artwork,ArtworkSource source,string revision,string build,CalibrationProfile applied,SoundMix sound,bool paused=false){
   if(string.IsNullOrWhiteSpace(artwork)||artwork.Length>256||!Enum.IsDefined(typeof(ArtworkSource),source)||applied==null||applied.schema_version!=2||applied.manual==null||!applied.Valid()||!applied.manual.Bound||sound==null)throw new ArgumentException("desktop-applied-selection");
   if(source!=ArtworkSource.BuiltinNative){if(!Guid.TryParseExact(artwork,"D",out var work)||work.ToString("D")!=artwork||!Guid.TryParseExact(revision,"D",out var rev)||rev.ToString("D")!=revision||build==null||build.Length!=64||!build.All(Uri.IsHexDigit)||build!=build.ToLowerInvariant())throw new ArgumentException("desktop-applied-exact-revision");}
   else if(revision!=null||build!=null||Guid.TryParse(artwork,out var reserved))throw new ArgumentException("desktop-builtin-content-revision");
   ArtworkId=artwork;Source=source;RevisionId=revision;BuildId=build;profile=applied.Copy();Sound=sound;Paused=paused;
  }
  public JObject Json=>new JObject{{"artwork_id",ArtworkId},{"source",Source.ToString()},{"display_id",DisplayId},{"content_revision",RevisionId},{"build_id",BuildId},{"preset_id",profile.id},{"preset_revision",profile.revision},{"profile",JObject.FromObject(profile)},{"sound",Sound.Json},{"paused",Paused}};
  internal static DesktopAppliedSelection Parse(JObject value){
   DesktopAppliedState.Fields(value,"artwork_id","source","display_id","content_revision","build_id","preset_id","preset_revision","profile","sound","paused");
   if(value["source"]?.Type!=JTokenType.String||!Enum.TryParse((string)value["source"],false,out ArtworkSource source)||!Enum.IsDefined(typeof(ArtworkSource),source)||source.ToString()!=(string)value["source"]||value["artwork_id"]?.Type!=JTokenType.String||value["display_id"]?.Type!=JTokenType.String||value["preset_id"]?.Type!=JTokenType.String||value["preset_revision"]?.Type!=JTokenType.Integer||value["paused"]?.Type!=JTokenType.Boolean||!(value["profile"] is JObject)||!NullableString(value["content_revision"])||!NullableString(value["build_id"]))throw new InvalidDataException("desktop-applied-fields");
   var profile=value["profile"].ToObject<CalibrationProfile>();if(profile==null||profile.id!=(string)value["preset_id"]||profile.revision!=(long)value["preset_revision"]||profile.manual?.display_id!=(string)value["display_id"])throw new InvalidDataException("desktop-applied-profile-identity");
   return new DesktopAppliedSelection((string)value["artwork_id"],source,(string)value["content_revision"],(string)value["build_id"],profile,SoundMix.Parse(value["sound"]),(bool)value["paused"]);
  }
  static bool NullableString(JToken value)=>value!=null&&(value.Type==JTokenType.Null||value.Type==JTokenType.String);
 }
 // The applied table is the authority. A selected UI target may have no applied
 // item; runtime handles and ContentStore mirrors never replace this table.
 public sealed class DesktopAppliedState {
  public string SelectedDisplayId{get;}public IReadOnlyDictionary<string,DesktopAppliedSelection> Targets{get;}
  public DesktopAppliedState(string selected,IEnumerable<DesktopAppliedSelection> targets){
   if(selected!=null&&(string.IsNullOrWhiteSpace(selected)||selected.Length>256))throw new ArgumentException("desktop-selected-target");
   var entries=targets?.ToArray()??throw new ArgumentNullException(nameof(targets));if(entries.Length>RuntimeLimits.MaxInstances||entries.Any(e=>e==null)||entries.Select(e=>e.DisplayId).Distinct(StringComparer.Ordinal).Count()!=entries.Length)throw new ArgumentException("desktop-applied-targets");
   SelectedDisplayId=selected;Targets=new ReadOnlyDictionary<string,DesktopAppliedSelection>(entries.ToDictionary(e=>e.DisplayId,StringComparer.Ordinal));
  }
  public JObject Json=>new JObject{{"format",2},{"selected_display_id",SelectedDisplayId},{"targets",new JArray(Targets.Values.OrderBy(e=>e.DisplayId,StringComparer.Ordinal).Select(e=>e.Json))}};
  public DesktopAppliedState With(DesktopAppliedSelection item)=>new DesktopAppliedState(item.DisplayId,Targets.Values.Where(e=>e.DisplayId!=item.DisplayId).Concat(new[]{item}));
  public DesktopAppliedState Without(string display)=>new DesktopAppliedState(SelectedDisplayId,Targets.Values.Where(e=>e.DisplayId!=display));
  public bool AllPaused=>Targets.Count>0&&Targets.Values.All(s=>s.Paused);
  public DesktopAppliedState WithPause(string display,bool paused){
   if(display!=null&&!Targets.ContainsKey(display))throw new ArgumentException("desktop-target-not-applied");
   return new DesktopAppliedState(SelectedDisplayId,Targets.Values.Select(s=>display!=null&&s.DisplayId!=display?s:new DesktopAppliedSelection(s.ArtworkId,s.Source,s.RevisionId,s.BuildId,s.Profile,s.Sound,paused)));
  }
  public void RequireRouting(string display,bool native,Func<DesktopAppliedSelection,bool> nativePeer){
   if(string.IsNullOrEmpty(display)||nativePeer==null)throw new ArgumentException("desktop-routing-arguments");
   if(Targets.Values.Any(s=>s.DisplayId!=display&&(!native||!nativePeer(s))))throw new InvalidDataException("旧 SDK 或蓝图目前不能占用多个活动目标；已应用的其他目标保持不变。");
  }
  internal static void Fields(JObject value,params string[] fields){if(value==null||!value.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(fields.OrderBy(n=>n,StringComparer.Ordinal)))throw new InvalidDataException("desktop-applied-shape");}
  public static DesktopAppliedState Parse(JObject value){
   try{
    Fields(value,"format","selected_display_id","targets");if(value["format"]?.Type!=JTokenType.Integer||value["format"].ToString()!="2"||value["selected_display_id"]==null||value["selected_display_id"].Type!=JTokenType.Null&&value["selected_display_id"].Type!=JTokenType.String||!(value["targets"] is JArray targets)||targets.Count>RuntimeLimits.MaxInstances)throw new InvalidDataException("desktop-applied-version-or-targets");
    return new DesktopAppliedState((string)value["selected_display_id"],targets.Select(e=>DesktopAppliedSelection.Parse(e as JObject)));
   }catch(Exception error)when(error is ArgumentException||error is OverflowException||error is FormatException||error is JsonException){throw new InvalidDataException("desktop-applied-invalid",error);}
  }
 }
 public sealed class DesktopAppliedStore {
  readonly string path;readonly object writer=new object();readonly Func<string,string,string> resolveBuild;readonly Func<string,SoundMix> readSound;
  DesktopAppliedState snapshot;byte[] original;public DesktopAppliedState Snapshot=>snapshot;
  public bool RecoveredFromBackup{get;private set;}public bool NeedsMigration{get;private set;}
  public DesktopAppliedStore(string file,Func<string,string,string> resolveBuild=null,Func<string,SoundMix> readSound=null){
   path=Path.GetFullPath(file);this.resolveBuild=resolveBuild;this.readSound=readSound;
   if(!File.Exists(path)&&!File.Exists(path+".bak")){snapshot=new DesktopAppliedState(null,Array.Empty<DesktopAppliedSelection>());return;}
   try{Load(path);}catch(Exception error)when(error is IOException||error is InvalidDataException||error is JsonException||error is DecoderFallbackException){if(!File.Exists(path+".bak"))throw;Load(path+".bak");RecoveredFromBackup=true;}
  }
  void Load(string source){
   var info=new FileInfo(source);if((info.Attributes&FileAttributes.ReparsePoint)!=0||info.Length<1||info.Length>65536)throw new InvalidDataException("desktop-applied-file");
   byte[] bytes=File.ReadAllBytes(source);JObject value;
   try{using(var text=new StringReader(new UTF8Encoding(false,true).GetString(bytes)))using(var reader=new JsonTextReader(text){MaxDepth=20,DateParseHandling=DateParseHandling.None}){value=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new InvalidDataException("desktop-applied-json-tail");}}
   catch(JsonException error){throw new InvalidDataException("desktop-applied-json",error);}
   if(value["format"]?.Type==JTokenType.Integer&&value["format"].ToString()=="1"){
    DesktopAppliedState.Fields(value,"format","selection");NeedsMigration=true;
    if(value["selection"]?.Type==JTokenType.Null)snapshot=new DesktopAppliedState(null,Array.Empty<DesktopAppliedSelection>());
    else{
     var selected=value["selection"] as JObject;DesktopAppliedState.Fields(selected,"artwork_id","display_id","preset_id","preset_revision","profile","content_revision");
     if(selected["artwork_id"]?.Type!=JTokenType.String||selected["display_id"]?.Type!=JTokenType.String||selected["preset_id"]?.Type!=JTokenType.String||selected["preset_revision"]?.Type!=JTokenType.Integer||!(selected["profile"] is JObject)||selected["content_revision"]==null||selected["content_revision"].Type!=JTokenType.Null&&selected["content_revision"].Type!=JTokenType.String)throw new InvalidDataException("desktop-legacy-fields");
     string work=(string)selected["artwork_id"],revision=(string)selected["content_revision"],build=null;var profile=selected["profile"]?.ToObject<CalibrationProfile>();
     if(profile==null||profile.schema_version!=2||profile.manual==null||!profile.Valid()||!profile.manual.Bound||profile.id!=(string)selected["preset_id"]||profile.revision!=(long)selected["preset_revision"]||profile.manual.display_id!=(string)selected["display_id"])throw new InvalidDataException("desktop-legacy-profile-identity");
     bool sdk=Guid.TryParseExact(work,"D",out var ignored);if(sdk){
      if(ignored.ToString("D")!=work||!Guid.TryParseExact(revision,"D",out var exact)||exact.ToString("D")!=revision)throw new InvalidDataException("desktop-legacy-revision");
      try{build=resolveBuild?.Invoke(work,revision);}catch(Exception error){throw new ArgumentException("desktop-legacy-revision-unresolved",error);}if(build==null)throw new ArgumentException("desktop-legacy-revision-unresolved");
     }else if(revision!=null)throw new InvalidDataException("desktop-legacy-builtin-revision");
     SoundMix sound;try{sound=readSound?.Invoke(work)??new SoundMix();}catch(Exception error){throw new ArgumentException("desktop-legacy-sound-unresolved",error);}
     var selection=new DesktopAppliedSelection(work,sdk?ArtworkSource.SdkLegacy:ArtworkSource.BuiltinNative,revision,build,profile,sound);snapshot=new DesktopAppliedState(selection.DisplayId,new[]{selection});
    }
   }else{snapshot=DesktopAppliedState.Parse(value);NeedsMigration=false;}
   original=bytes;
  }
  static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  static void Preserve(string name,byte[] bytes){if(File.Exists(name)){if(File.ReadAllBytes(name).SequenceEqual(bytes))return;name+="."+Hash(bytes);}if(File.Exists(name))return;using(var stream=new FileStream(name,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}}
  public void Save(DesktopAppliedState next){lock(writer){
   if(next==null)throw new ArgumentNullException(nameof(next));byte[] bytes=new UTF8Encoding(false,true).GetBytes(next.Json.ToString(Formatting.Indented));if(bytes.Length>65536)throw new InvalidDataException("desktop-applied-size");
   Directory.CreateDirectory(Path.GetDirectoryName(path));if(NeedsMigration&&original!=null)Preserve(path+".format1.bak",original);
   if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("desktop-applied-reparse-write");
   string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{
    using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
    if(File.Exists(path))File.Replace(temp,path,RecoveredFromBackup?path+".corrupt."+Guid.NewGuid().ToString("N")+".json":path+".bak");else File.Move(temp,path);
    snapshot=next;original=bytes;NeedsMigration=false;RecoveredFromBackup=false;
   }finally{if(File.Exists(temp))File.Delete(temp);}
  }}
 }
}
