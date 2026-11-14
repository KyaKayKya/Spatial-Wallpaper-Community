using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
namespace SpatialWallpaper.Sdk.Editor {
 // Runtime-created materials are absent from Unity's serialized dependency
 // scan. Compile their actual local keyword sets over retained platform variants.
 // No native Material or ShaderVariantCollection object enters the package.
 public sealed class ExportShaderVariants:IPreprocessShaders {
  internal static HashSet<string> InstancedSources;
  internal static Dictionary<string,string[][]> LocalConsumers;
  public int callbackOrder=>int.MaxValue;
  public void OnProcessShader(Shader shader,ShaderSnippetData snippet,IList<ShaderCompilerData> variants){
   if(InstancedSources==null)return;
   string path=AssetDatabase.GetAssetPath(shader);var instancing=new ShaderKeyword("INSTANCING_ON");
   if(!InstancedSources.Contains(path))for(int i=variants.Count-1;i>=0;i--)if(variants[i].shaderKeywordSet.IsEnabled(instancing))variants.RemoveAt(i);
   if(LocalConsumers==null||!LocalConsumers.TryGetValue(path,out var consumers))return;
   var local=shader.keywordSpace.keywords.Where(k=>!k.isOverridable).Select(k=>new ShaderKeyword(shader,k.name)).ToArray();
   if(local.Length==0)return;
   int count=variants.Count;
   for(int i=0;i<count;i++){
    var variant=variants[i];foreach(var keyword in local)variant.shaderKeywordSet.Disable(keyword);
    foreach(string keyword in consumers[0])variant.shaderKeywordSet.Enable(new ShaderKeyword(shader,keyword));
    variants[i]=variant;
   }
  }
  internal static Dictionary<string,string[][]> Consumers(IEnumerable<Material> materials){
   var result=materials.GroupBy(m=>AssetDatabase.GetAssetPath(m.shader)).ToDictionary(g=>g.Key,g=>g.Select(m=>m.shaderKeywords.Where(k=>{var keyword=m.shader.keywordSpace.FindKeyword(k);return keyword.isValid&&!keyword.isOverridable;}).OrderBy(k=>k,System.StringComparer.Ordinal).ToArray()).GroupBy(keys=>string.Join(" ",keys)).Select(keys=>keys.First()).ToArray());
   // ShaderKeywordSet carries native state; struct copies cannot produce
   // independent combinations. Reject unsupported author input before building.
   if(result.Values.Any(sets=>sets.Length!=1))throw new InvalidDataException("multiple-local-keyword-combinations-unsupported");return result;
  }
 }
}
