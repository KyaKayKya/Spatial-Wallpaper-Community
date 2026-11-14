using System;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Execution;

namespace SpatialWallpaper.Sdk.Native {
 public static class NativeProgramBindings {
  // Used by native export BEFORE resources are written, and by worker preflight.
  public static void Require(JObject program,Func<string,bool> capabilitySupports=null){
   if(program==null)throw new ArgumentNullException(nameof(program));
   capabilitySupports=capabilitySupports??NativeCapabilityBindings.SupportsMember;
   foreach(JObject cls in (JArray)program["classes"])foreach(JObject method in (JArray)cls["methods"])foreach(JObject instruction in (JArray)method["body"]){
    if((string)instruction["op"]!="call")continue;
    string member=(string)instruction["value"];
    bool supported=CoreLibrary.Supports(member)||NativeAuthorApi.Supports(member)||SceneCommandRules.Member(member)!=null||SceneCommandRules.MirrorMember(member)||
     (!RenderingBindings.Supports(member)||NativeCapabilityBindings.IsView(member))&&capabilitySupports!=null&&capabilitySupports(member);
    if(!supported)throw new InvalidOperationException("native-export-missing-binding:"+member);
   }
  }
 }
}
