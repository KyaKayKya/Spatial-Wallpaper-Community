using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Security;
using SpatialWallpaper.Content;

namespace SpatialWallpaper.Sdk.Native {
 // A pure program segment, never a renamed schema4 wallpaper ZIP. Resource
 // entries are id/kind metadata, not mesh/texture/audio files or native objects.
 public static class NativeScriptDefinition {
  const string Format="spatial-native-script";
  static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
  public static byte[] Encode(JObject program,JObject content,Func<string,bool> capabilitySupports=null){
   var root=new JObject{{"format",Format},{"version",1},{"sdk",SceneCommandCatalog.TargetSdk},{"vm",RuleCatalog.Current.Snapshot["vm"]},
    {"command_abi",SceneCommandCatalog.Abi},{"catalog_digest",SceneCommandCatalog.Digest},{"rules_sha256",NativeAuthorApi.Rules.Digest},{"program",program.DeepClone()},{"content",content.DeepClone()}};
   root["build_id"]=PackageValidator.Identity(root);byte[] bytes=Utf8.GetBytes(root.ToString(Formatting.None));Admit(bytes,capabilitySupports);return bytes;
  }
  public static AdmittedBuild Admit(byte[] input,Func<string,bool> capabilitySupports=null){
   JsonData.Require(input!=null&&input.Length>0&&input.Length<=SceneCommandCatalog.MaxScriptBytes,"native-script-size");
   var root=JsonData.Parse(Utf8.GetString(input));JsonData.Fields(root,"format","version","sdk","vm","command_abi","catalog_digest","rules_sha256","program","content","build_id");
   JsonData.Require(JsonData.Text(root["format"])==Format&&JsonData.Integer(root["version"],1,1)==1&&JsonData.Text(root["sdk"])==SceneCommandCatalog.TargetSdk&&
    JsonData.Text(root["vm"])==(string)RuleCatalog.Current.Snapshot["vm"]&&JsonData.Integer(root["command_abi"],1,1)==SceneCommandCatalog.Abi&&JsonData.Text(root["catalog_digest"])==SceneCommandCatalog.Digest&&JsonData.Text(root["rules_sha256"])==NativeAuthorApi.Rules.Digest,"native-script-format-identity");
   JsonData.Require(JsonData.Text(root["build_id"])==PackageValidator.Identity(root),"native-script-digest");
   var program=root["program"] as JObject;var content=root["content"] as JObject;JsonData.Require(program!=null&&content!=null,"native-script-program-content");
   var identity=(JObject)root.DeepClone();identity.Remove("program");identity.Remove("content");
   var admitted=Admission.AdmitProgramMetadata(identity,program,content,NativeAuthorApi.Rules);NativeProgramBindings.Require(admitted.Program,capabilitySupports);return admitted;
  }
 }
}
