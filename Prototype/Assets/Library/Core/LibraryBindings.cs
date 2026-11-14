using System;
using System.Collections.Generic;
using System.Linq;
namespace SpatialWallpaper.Library {
 public sealed class LibraryBindings {
  readonly Func<string,string> read;readonly Action<string,string> write;
  public LibraryBindings(Func<string,string> read,Action<string,string> write){this.read=read;this.write=write;}
  static string Key(string artwork,string display)=>"Preset."+Uri.EscapeDataString(artwork)+"/"+Uri.EscapeDataString(display);
  public string GetPreset(string artwork,string display)=>read(Key(artwork,display));
  public void SetPreset(string artwork,string display,string preset)=>write(Key(artwork,display),preset);
  public string GetDefaultPreset(string display)=>read("DefaultPreset."+Uri.EscapeDataString(display));
  public void SetDefaultPreset(string display,string preset)=>write("DefaultPreset."+Uri.EscapeDataString(display),preset);
  public string ResolvePreset(string artwork,string display,IEnumerable<string> compatible,string applied){
   var ids=compatible.ToArray();foreach(var id in new[]{GetPreset(artwork,display),GetDefaultPreset(display),applied})if(id!=null&&ids.Contains(id))return id;
   return ids.Length==1?ids[0]:null;
  }
  public void SetTarget(string artwork,string display)=>write("Target."+artwork,display);
  public void Migrate(string original,string artwork,IEnumerable<string> displays){
   string target=ResolveTarget(original,null);if(target!=null&&ResolveTarget(artwork,null)==null)SetTarget(artwork,target);
   foreach(string display in displays){string preset=GetPreset(original,display);if(preset!=null&&GetPreset(artwork,display)==null)SetPreset(artwork,display,preset);}
  }
  public string ResolveTarget(string artwork,string fallback){string value=read("Target."+artwork);return string.IsNullOrEmpty(value)?fallback:value;}
 }
}
