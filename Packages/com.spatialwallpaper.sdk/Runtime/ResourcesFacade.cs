using System;using System.Collections.Generic;using UnityEngine;
namespace SpatialWallpaper.Sdk {
 // Ordinary Unity Play facade. Sandboxed programs bind to the owned shared host instead.
 public static class Resources {
  static readonly HashSet<UnityEngine.Object> created=new HashSet<UnityEngine.Object>();
  static T Keep<T>(T value)where T:UnityEngine.Object{created.Add(value);return value;}
  public static Mesh CreateMesh()=>Keep(new Mesh());
  public static Texture2D CreateTexture(int width,int height){
   Execution.ResourceEstimates.Texture(width,height,4,1,1);
   return Keep(new Texture2D(width,height,TextureFormat.RGBA32,false,true));
  }
  public static Material CreateMaterial(Material original){if(!original)throw new ArgumentNullException(nameof(original));return Keep(new Material(original));}
  public static void BindMesh(GameObject node,Mesh mesh){if(!node)throw new ArgumentNullException(nameof(node));var filter=node.GetComponent<MeshFilter>();if(!filter)throw new InvalidOperationException("missing-owned-mesh-filter");filter.sharedMesh=mesh;}
  static void Remove(UnityEngine.Object value){
   if(!value||!created.Remove(value))throw new InvalidOperationException("release-requires-created-resource");
   UnityEngine.Object.Destroy(value);
  }
  public static void Release(Mesh value)=>Remove(value);
  public static void Release(Texture2D value)=>Remove(value);
  public static void Release(Material value)=>Remove(value);
 }
}
