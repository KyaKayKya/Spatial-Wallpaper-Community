using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpatialWallpaper.Blueprint {
 public sealed class ModelCatalog {
  public string Version {get;private set;}
  public string Fingerprint {get;private set;}
  readonly Dictionary<string,TextureRegion> textures=new Dictionary<string,TextureRegion>(StringComparer.Ordinal);
  readonly Dictionary<string,BlockModel> models=new Dictionary<string,BlockModel>(StringComparer.Ordinal);
  public bool HasTexture(string name)=>textures.ContainsKey(name);
  public TextureRegion Texture(string name)=>textures.TryGetValue(name,out var region)?region:textures["magenta_concrete"];
  public BlockModel Resolve(BlockState state){if(models.TryGetValue(state.Key,out var model))return model;model=new BlockModelResolver(this).Resolve(state);models.Add(state.Key,model);return model;}
  public static ModelCatalog Load(string json){
   if(json==null||json.Length>4*1024*1024)throw new BlueprintImportException("model-catalog-budget");
   JObject root;using(var reader=new JsonTextReader(new StringReader(json)){MaxDepth=16})root=JObject.Load(reader);
   var catalog=new ModelCatalog{Version=(string)root["version"]??throw new BlueprintImportException("model-catalog-version")};
   using(var sha=SHA256.Create())catalog.Fingerprint=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-","").ToLowerInvariant();
   var entries=root["textures"] as JObject??throw new BlueprintImportException("model-catalog-textures");if(entries.Count<1||entries.Count>8192)throw new BlueprintImportException("model-catalog-budget");
   foreach(var entry in entries.Properties()){
    var r=new TextureRegion{U0=(float)entry.Value["u0"],V0=(float)entry.Value["v0"],U1=(float)entry.Value["u1"],V1=(float)entry.Value["v1"]};
    foreach(float value in new[]{r.U0,r.V0,r.U1,r.V1})if(float.IsNaN(value)||float.IsInfinity(value)||value<0||value>1)throw new BlueprintImportException("model-catalog-uv");
    if(r.U1<=r.U0||r.V1<=r.V0)throw new BlueprintImportException("model-catalog-uv");catalog.textures.Add(entry.Name,r);
   }
   if(!catalog.HasTexture("magenta_concrete"))throw new BlueprintImportException("model-catalog-placeholder");return catalog;
  }
 }
}
