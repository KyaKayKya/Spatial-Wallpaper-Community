using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SpatialWallpaper.Sdk.Native.Unity {
 public sealed class NativeAssetBinding {
  public readonly string Name,Kind,SourceDigest;
  public readonly UnityEngine.Object Asset;
  public NativeAssetBinding(string name,string kind,UnityEngine.Object asset,string sourceDigest){
   if(string.IsNullOrWhiteSpace(name)||name.Length>256||!asset||sourceDigest==null||sourceDigest.Length!=64||!sourceDigest.All(Uri.IsHexDigit))throw new InvalidDataException("native-asset-binding");
   bool supported=kind=="material"&&asset is Material||kind=="sound"&&asset is AudioClip||kind=="mesh"&&asset is Mesh||kind=="texture"&&asset is Texture2D||kind=="cubemap"&&asset is Cubemap||kind=="prefab"&&asset is GameObject;
   if(!supported)throw new InvalidDataException("native-asset-kind");Name=name;Kind=kind;Asset=asset;SourceDigest=sourceDigest.ToLowerInvariant();
  }
 }
 public sealed class NativeAssetCatalog {
  readonly NativeAssetBinding[] bindings;
  Dictionary<long,NativeAssetBinding> resources=new Dictionary<long,NativeAssetBinding>();
  Dictionary<string,long> aliases=new Dictionary<string,long>(StringComparer.Ordinal);
  IReadOnlyList<SceneObject> definitions;bool bound;
  public bool IsBound=>bound;
  public NativeAssetCatalog(IEnumerable<NativeAssetBinding> values){
   bindings=values?.Take(SceneCommandCatalog.MaxObjects+1).ToArray()??throw new ArgumentNullException(nameof(values));
   if(bindings.Length>SceneCommandCatalog.MaxObjects||bindings.Any(value=>value==null)||bindings.Select(value=>value.Name).Distinct(StringComparer.Ordinal).Count()!=bindings.Length)throw new InvalidDataException("native-asset-catalog-size-or-alias");
  }
  public IReadOnlyList<SceneObject> Definitions=>definitions??throw new InvalidOperationException("native-asset-catalog-not-bound");
  public JObject Bindings=>new JObject(aliases.Select(pair=>new JProperty(pair.Key,pair.Value)));
  public void Bind(long firstId){
   if(bound||firstId<1||firstId+bindings.Length>SceneCommandCatalog.MaxObjects+1)throw new InvalidOperationException("native-asset-catalog-binding");
   var values=new List<SceneObject>();var nativeIds=new Dictionary<UnityEngine.Object,long>();var stagedResources=new Dictionary<long,NativeAssetBinding>();var stagedAliases=new Dictionary<string,long>(StringComparer.Ordinal);
   foreach(var binding in bindings){
    if(nativeIds.TryGetValue(binding.Asset,out var existing)){if(stagedResources[existing].Kind!=binding.Kind||stagedResources[existing].SourceDigest!=binding.SourceDigest)throw new InvalidDataException("native-asset-alias-identity");stagedAliases.Add(binding.Name,existing);continue;}
    long id=firstId+values.Count;var state=new JObject{{"source_sha256",binding.SourceDigest},{"name",binding.Asset.name}};
    if(binding.Asset is Mesh mesh){state["vertices"]=mesh.vertexCount;state["submeshes"]=mesh.subMeshCount;state["blend_shapes"]=mesh.blendShapeCount;state["vertex_streams"]=mesh.vertexBufferCount;state["vertex_layout"]=new JArray(mesh.GetVertexAttributes().Select(attribute=>new JObject{{"attribute",(int)attribute.attribute},{"format",(int)attribute.format},{"dimension",attribute.dimension},{"stream",attribute.stream}}));}
    else if(binding.Asset is Texture texture){state["width"]=texture.width;state["height"]=texture.height;state["dimension"]=(int)texture.dimension;}
    else if(binding.Asset is AudioClip clip){state["samples"]=clip.samples;state["channels"]=clip.channels;state["frequency"]=clip.frequency;}
    else if(binding.Asset is Material material){if(!material.shader||!material.shader.isSupported)throw new InvalidDataException("native-material-shader-unavailable");state["shader"]=material.shader.name;state["keywords"]=new JArray(material.shaderKeywords);state["render_queue"]=material.renderQueue;}
    var template=binding.Asset is GameObject prefab?PrefabDefinition(prefab):null;
    values.Add(new SceneObject(id,binding.Kind,state:state,template:template));stagedResources.Add(id,binding);nativeIds.Add(binding.Asset,id);stagedAliases.Add(binding.Name,id);
   }
   definitions=Array.AsReadOnly(values.ToArray());resources=stagedResources;aliases=stagedAliases;bound=true;
  }
  static IEnumerable<SceneObject> PrefabDefinition(GameObject prefab){
   var nodes=prefab.GetComponentsInChildren<Transform>(true);
   if(nodes.Length==0||nodes.Length>SceneCommandCatalog.MaxCommands||prefab.GetComponentInChildren<Camera>(true))throw new InvalidDataException("native-prefab-layout");
   var ids=new Dictionary<Transform,long>();var values=new List<SceneObject>();
   foreach(var node in nodes){long id=values.Count+1;long parent=node==prefab.transform?0:ids[node.parent];ids.Add(node,id);var position=node.localPosition;var rotation=node.localRotation;var scale=node.localScale;var light=node.GetComponent<Light>();
    var state=new JObject{{"position",new JArray(position.x,position.y,position.z)},{"rotation",new JArray(rotation.x,rotation.y,rotation.z,rotation.w)},{"scale",new JArray(scale.x,scale.y,scale.z)},{"active",node.gameObject.activeSelf}};if(light)state["intensity"]=light.intensity;values.Add(new SceneObject(id,light?"light":"node",parent,state));
   }
   return values;
  }
  public T Require<T>(long id,string kind)where T:UnityEngine.Object{
   if(!bound||!resources.TryGetValue(id,out var value)||value.Kind!=kind||!(value.Asset is T asset)||!asset)throw new InvalidOperationException("native-resource-not-owned:"+kind);return asset;
  }
 }
}
