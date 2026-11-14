using System;
using System.Collections.Generic;
using UnityEngine;
namespace SpatialWallpaper.Blueprint {
 public sealed class BlueprintResources:IDisposable {
  readonly Dictionary<BlockRenderKind,Material> materials=new Dictionary<BlockRenderKind,Material>();readonly Dictionary<string,Material> groundMaterials=new Dictionary<string,Material>();readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();Material water;
  public ModelCatalog Catalog {get;}
  public BlueprintResources(ModelCatalog catalog){Catalog=catalog;foreach(BlockRenderKind kind in Enum.GetValues(typeof(BlockRenderKind))){
   var source=Resources.Load<Material>("Blueprint/Materials/"+kind);if(!source)throw new InvalidOperationException("Blueprint material missing: "+kind);var m=new Material(source){name="Blueprint "+kind};materials.Add(kind,m);owned.Add(m);
  }}
  public Material Material(BlockRenderKind kind)=>materials[kind];
  public Material Ground(string key="grass_block_top"){
   if(groundMaterials.TryGetValue(key,out var found))return found;var m=new Material(Material(BlockRenderKind.Opaque)){name="Blueprint repeating "+key};var tile=Resources.Load<Texture2D>("Blueprint/Terrain/"+key);if(tile){m.DisableKeyword("_TILING_ON");m.SetTexture("_BaseMap",tile);m.SetTextureScale("_BaseMap",Vector2.one);m.SetTextureOffset("_BaseMap",Vector2.zero);}else{var r=Catalog.Texture(key);m.EnableKeyword("_TILING_ON");m.SetTextureScale("_BaseMap",new Vector2(r.U1-r.U0,r.V1-r.V0));m.SetTextureOffset("_BaseMap",new Vector2(r.U0,r.V0));}m.SetColor("_BaseColor",key=="grass_block_top"?new Color(.49f,.64f,.29f):Color.white);owned.Add(m);groundMaterials.Add(key,m);return m;
  }
  public Material Water(){if(water)return water;var source=Resources.Load<Material>("Blueprint/Materials/WaterSurface");if(!source)throw new InvalidOperationException("Prepared water material missing");water=new Material(source);owned.Add(water);return water;}
  public void Own(UnityEngine.Object value){if(value)owned.Add(value);}
  public void Dispose(){foreach(var value in owned)if(value)UnityEngine.Object.Destroy(value);owned.Clear();materials.Clear();}
 }
}
