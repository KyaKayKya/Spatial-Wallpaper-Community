using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Security;

namespace SpatialWallpaper.Sdk.Native {
 public static class NativeAuthorApi {
  public static RuleCatalog Rules{get;}=RuleCatalog.CreateNative(JObject.Parse(SceneCommandCatalog.Json),SceneCommandCatalog.Digest);
  static readonly Dictionary<string,string> actions=((JArray)JObject.Parse(SceneCommandCatalog.Json)["author_members"]).Cast<JObject>().ToDictionary(value=>(string)value["id"],value=>(string)value["action"],StringComparer.Ordinal);
  public static bool Supports(string member)=>actions.ContainsKey(member);
  public static string Action(string member)=>actions.TryGetValue(member,out var action)?action:throw new InvalidOperationException("native-author-member-not-installed:"+member);
 }
}
