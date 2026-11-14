using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEditor;using SpatialWallpaper.Content;using SpatialWallpaper.Sdk.Security;
namespace SpatialWallpaper.Sdk.Editor {
 internal sealed class SceneResourceRegistry {
  internal readonly Dictionary<string,byte[]> Files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
  internal readonly JArray Definitions=new JArray();
  readonly Dictionary<UnityEngine.Object,string> ids=new Dictionary<UnityEngine.Object,string>();
  readonly Dictionary<Shader,ShaderBundleGate.ShaderDescription> shaderDescriptions=new Dictionary<Shader,ShaderBundleGate.ShaderDescription>();
  static void Need(bool yes,string reason){if(!yes)throw new InvalidDataException("export:"+reason);}
  string Add(UnityEngine.Object source,string kind,byte[] bytes,string extension){
   Need(Definitions.Count<RuleCatalog.Current.Limit("resources"),"resource-limit");string id="r"+Definitions.Count,path="resources/"+PackageValidator.Digest(bytes)+extension;if(!Files.ContainsKey(path))Files.Add(path,bytes);Definitions.Add(new JObject{{"id",id},{"kind",kind},{"file",path}});ids.Add(source,id);return id;
  }
  internal string Reference(UnityEngine.Object source){
   if(!source)return null;if(ids.TryGetValue(source,out var id))return id;
   if(source is Mesh mesh){
    Need(mesh.blendShapeCount==0&&mesh.bindposes.Length==0&&mesh.boneWeights.Length==0,"mesh-skinning-not-supported");
    Need(mesh.GetVertexAttributes().All(a=>a.attribute==UnityEngine.Rendering.VertexAttribute.Position||a.attribute==UnityEngine.Rendering.VertexAttribute.Normal||a.attribute==UnityEngine.Rendering.VertexAttribute.Tangent||a.attribute==UnityEngine.Rendering.VertexAttribute.Color||a.attribute==UnityEngine.Rendering.VertexAttribute.TexCoord0||a.attribute==UnityEngine.Rendering.VertexAttribute.TexCoord1),"mesh-vertex-channel-not-supported");
    return Add(source,"mesh",SceneResourceEncoding.Mesh(mesh),".smesh");
   }
   if(source is Texture texture){Need(texture.wrapModeU==texture.wrapModeV&&texture.wrapModeV==texture.wrapModeW,"texture-mixed-wrap-not-supported");return Add(source,texture is Cubemap?"cubemap":"texture",SceneResourceEncoding.Texture(texture),".stex");}
   if(source is Material material)return Material(material);
   if(source is AudioClip clip)return Add(source,"sound",SceneResourceEncoding.Sound(clip),".saudio");
   throw new InvalidDataException("export:resource-type:"+source.GetType().FullName);
  }
  string Material(Material material){
   var shader=material.shader;Need(shader,"material-shader-not-supported");
   Need(material.shaderKeywords.Length==0,"material-keywords-not-supported");
   for(int pass=0;pass<material.passCount;pass++)Need(material.GetShaderPassEnabled(material.GetPassName(pass)),"material-disabled-pass-not-supported");
   var defaults=new Material(shader);try{Need(material.renderQueue==defaults.renderQueue&&!material.enableInstancing&&material.doubleSidedGI==defaults.doubleSidedGI&&material.globalIlluminationFlags==defaults.globalIlluminationFlags,"material-render-state-not-supported");}finally{UnityEngine.Object.DestroyImmediate(defaults);}
   string bundle="";ShaderBundleGate.ShaderDescription description;
   if(shader.name=="Universal Render Pipeline/Lit"||shader.name=="Universal Render Pipeline/Unlit")description=RuleCatalog.Current.BuiltinShader(shader.name);
   else bundle=Shader(shader,material,out description);
   // GetColor performs the shader's color-space roundtrip. Preserve the values
   // Unity serializes so loading does not repeat that lossy conversion.
   var savedColors=new Dictionary<string,Color>(StringComparer.Ordinal);using(var serialized=new SerializedObject(material)){serialized.Update();var colors=serialized.FindProperty("m_SavedProperties.m_Colors");
    if(colors!=null)for(int i=0;i<colors.arraySize;i++){var pair=colors.GetArrayElementAtIndex(i);savedColors[pair.FindPropertyRelative("first").stringValue]=pair.FindPropertyRelative("second").colorValue;}
   }
   var properties=new JObject();
   for(int i=0;i<ShaderUtil.GetPropertyCount(shader);i++){
    string name=ShaderUtil.GetPropertyName(shader,i);Need(description.Properties.ContainsKey(name),"material-property-drift");var type=ShaderUtil.GetPropertyType(shader,i);
    switch(type){
     case ShaderUtil.ShaderPropertyType.Color:var color=savedColors.TryGetValue(name,out var saved)?saved:material.GetColor(name);properties[name]=new JObject{{"type","color"},{"value",new JArray(color.r,color.g,color.b,color.a)}};break;
     case ShaderUtil.ShaderPropertyType.Float:case ShaderUtil.ShaderPropertyType.Range:properties[name]=new JObject{{"type","f32"},{"value",material.GetFloat(name)}};break;
     case ShaderUtil.ShaderPropertyType.TexEnv:
      Need(material.GetTextureScale(name)==Vector2.one&&material.GetTextureOffset(name)==Vector2.zero,"material-texture-transform-not-supported");var texture=material.GetTexture(name);if(texture)properties[name]=new JObject{{"type","texture"},{"value",Reference(texture)}};break;
     default:throw new InvalidDataException("export:material-property-type-not-supported:"+name);
    }
   }
   return Add(material,"material",Encoding.UTF8.GetBytes(new JObject{{"shader",shader.name},{"bundle",bundle},{"properties",properties}}.ToString(Formatting.None)),".json");
  }
  string Shader(Shader shader,Material consumer,out ShaderBundleGate.ShaderDescription description){
   if(ids.TryGetValue(shader,out var existing)){description=shaderDescriptions[shader];return existing;}
   string source=AssetDatabase.GetAssetPath(shader);Need(!string.IsNullOrEmpty(source)&&(source.StartsWith("Assets/",StringComparison.Ordinal)||source.StartsWith("Packages/",StringComparison.Ordinal)),"material-shader-not-supported");
   string parent=Path.GetFullPath("Library/SpatialWallpaper/ShaderExport"),scratch=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
   try{var bytes=SceneShaderEncoding.Build(scratch,new[]{source},new[]{consumer},new HashSet<string>(StringComparer.Ordinal),PresentationProfile.ForwardPlus);description=ShaderBundleGate.Inspect(bytes).Single(item=>item.Name==shader.name);shaderDescriptions.Add(shader,description);return Add(shader,"shader",bytes,".shaderbundle");}
   finally{string owned=Path.GetFullPath(scratch);if(!owned.StartsWith(parent+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("shader-scratch-outside-owned-directory");if(Directory.Exists(owned))Directory.Delete(owned,true);}
  }
  internal JObject Renderer(MeshFilter filter,MeshRenderer renderer){
   Need(filter&&renderer&&filter.sharedMesh&&renderer.enabled,"mesh-renderer-pair-required");Need(!renderer.forceRenderingOff,"renderer-force-off-not-supported");Need(renderer.sharedMaterials.Length==1&&renderer.sharedMaterial&&filter.sharedMesh.subMeshCount==1,"one-material-submesh-required");
   Need(!renderer.HasPropertyBlock()&&renderer.lightmapIndex<0&&renderer.realtimeLightmapIndex<0,"renderer-overrides-not-supported");
   Need(renderer.sortingLayerID==0&&renderer.sortingOrder==0&&renderer.additionalVertexStreams==null&&renderer.lightProbeProxyVolumeOverride==null&&renderer.probeAnchor==null,"renderer-state-not-supported");
   return new JObject{{"kind","mesh-renderer"},{"config",new JObject{{"mesh",Reference(filter.sharedMesh)},{"material",Reference(renderer.sharedMaterial)}}},{"fields",new JObject()},{"callbacks",new JArray()}};
  }
 }
}
