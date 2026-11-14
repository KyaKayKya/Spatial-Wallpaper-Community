using System;using System.Collections.Generic;using System.Diagnostics;using System.Globalization;using System.IO;using System.Linq;using System.Runtime.InteropServices;using System.Text;using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Sdk.Hosting {
 // Trusted UI selection only. This class is absent from the author member catalog.
 public static class TrustedAudioSource {
  delegate bool Visitor(IntPtr window,IntPtr context);
  [DllImport("user32.dll")]static extern bool EnumWindows(Visitor visit,IntPtr context);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int length);
  public static JObject Capture(int pid){using(var process=Process.GetProcessById(pid)){if(process.HasExited)throw new InvalidDataException("audio-source-ended");return new JObject{{"process",pid},{"created",PinnedProcessOwner.ReadCreationFileTime(process.Handle).ToString(CultureInfo.InvariantCulture)}};}}
  public static JObject Validate(JToken token){
   if(token==null||token.Type==JTokenType.Null)return null;
   var value=token as JObject;long pid,created;
   if(value==null||!value.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(new[]{"created","process"})||value["process"].Type!=JTokenType.Integer||value["created"].Type!=JTokenType.String||!long.TryParse(value["process"].ToString(),NumberStyles.None,CultureInfo.InvariantCulture,out pid)||pid<1||pid>int.MaxValue||!long.TryParse((string)value["created"],NumberStyles.None,CultureInfo.InvariantCulture,out created)||created<1||created.ToString(CultureInfo.InvariantCulture)!=(string)value["created"])throw new InvalidDataException("audio-source-selection-fields");
   return (JObject)value.DeepClone();
  }
  public static JObject RequireLive(JToken token){var value=Validate(token);if(value==null)return null;int pid=(int)value["process"];long created=long.Parse((string)value["created"],CultureInfo.InvariantCulture);
   try{using(var owner=PinnedProcessOwner.Open(pid,created)){if(owner.HasExited)throw new InvalidDataException("audio-source-ended");}}
   catch(Exception error)when(error is ArgumentException||error is InvalidOperationException||error is System.ComponentModel.Win32Exception){throw new InvalidDataException("audio-source-no-longer-available",error);}
   return value;
  }
  public static JArray Applications(){
   var found=new SortedDictionary<string,JObject>(StringComparer.OrdinalIgnoreCase);var seen=new HashSet<int>();int own=Process.GetCurrentProcess().Id;
   EnumWindows((window,context)=>{if(!IsWindowVisible(window)||found.Count>=128)return true;GetWindowThreadProcessId(window,out var native);if(native<1||native>int.MaxValue||native==own||!seen.Add((int)native))return true;var title=new StringBuilder(257);if(GetWindowText(window,title,title.Capacity)<1)return true;
    try{var source=Capture((int)native);string name=title.ToString();found[name+"\0"+native]=new JObject{{"name",name},{"source",source}};}catch(Exception error)when(error is ArgumentException||error is InvalidOperationException||error is System.ComponentModel.Win32Exception||error is InvalidDataException){}return true;},IntPtr.Zero);
   return new JArray(found.Values);
  }
 }
}
