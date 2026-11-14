using System;using System.IO;using System.Linq;using System.Text;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Content {
 // Work defaults remain in the original file. This bounded preference table
 // remembers explicit work/display mixes even while that target is offline or
 // a different work is applied. Display IDs are JSON keys, never file paths.
 public sealed class ContentTargetSoundStore {
  readonly string path;JObject state=new JObject();
  public JObject Snapshot=>(JObject)state.DeepClone();
  public ContentTargetSoundStore(string path){this.path=Path.GetFullPath(path);Safe();if(File.Exists(this.path)){if(new FileInfo(this.path).Length>1048576)throw new InvalidDataException("target-sound-size");using(var input=new StringReader(new UTF8Encoding(false,true).GetString(File.ReadAllBytes(this.path))))using(var reader=new JsonTextReader(input){MaxDepth=8,DateParseHandling=DateParseHandling.None}){state=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new InvalidDataException("target-sound-tail");}Validate(state);}}
  public static void Display(string value){if(string.IsNullOrWhiteSpace(value)||value.Length>256||value.Any(char.IsControl))throw new InvalidDataException("target-sound-display");}
  static void Work(string value){if(!Guid.TryParseExact(value,"D",out var id)||id.ToString("D")!=value)throw new InvalidDataException("target-sound-work");}
  static void Validate(JObject value){if(value==null||value.Count>4096)throw new InvalidDataException("target-sound-shape");int count=0;foreach(var work in value.Properties()){Work(work.Name);var targets=work.Value as JObject;if(targets==null||targets.Count>64)throw new InvalidDataException("target-sound-target-count");foreach(var target in targets.Properties()){Display(target.Name);SoundMix.Parse(target.Value);if(++count>4096)throw new InvalidDataException("target-sound-count");}}}
  void Safe(){for(string parent=path;parent!=null;parent=Path.GetDirectoryName(parent))if(File.Exists(parent)||Directory.Exists(parent))if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("target-sound-link");}
  public SoundMix Find(string work,string display){Work(work);Display(display);var value=state[work]?[display];return value==null?null:SoundMix.Parse(value);}
  public JObject Page(System.Collections.Generic.IEnumerable<string> works){var result=new JObject();foreach(string work in works)if(state[work]!=null)result[work]=state[work].DeepClone();return result;}
  public void Set(string work,string display,SoundMix mix){Work(work);Display(display);if(mix==null)throw new ArgumentNullException(nameof(mix));var next=Snapshot;if(next[work]==null)next[work]=new JObject();next[work][display]=mix.Json;Restore(next);}
  public void SetGlobal(string work,SoundMix mix){Work(work);var next=Snapshot;if(next[work] is JObject targets){foreach(var target in targets.Properties().ToArray())targets[target.Name]=mix.Json;Restore(next);}}
  public void Remove(string work){Work(work);if(state[work]==null)return;var next=Snapshot;next.Remove(work);Restore(next);}
  public void Restore(JObject value){Validate(value);byte[] bytes=new UTF8Encoding(false,true).GetBytes(value.ToString(Formatting.None));if(bytes.Length>1048576)throw new InvalidDataException("target-sound-size");Safe();Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(bytes,0,bytes.Length);output.Flush(true);}if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);state=(JObject)value.DeepClone();}finally{if(File.Exists(temporary))File.Delete(temporary);}}
 }
}
