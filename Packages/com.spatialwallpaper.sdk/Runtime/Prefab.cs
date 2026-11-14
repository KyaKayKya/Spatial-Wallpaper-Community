using System;
using UnityEngine;
namespace SpatialWallpaper.Sdk {
 [Serializable] public sealed class Prefab {public GameObject source;}
 public static class Objects {
  public static GameObject Instantiate(Prefab prefab){if(prefab==null||!prefab.source)throw new ArgumentException("prefab-source");return UnityEngine.Object.Instantiate(prefab.source);}
  public static void Destroy(GameObject node){if(node)UnityEngine.Object.Destroy(node);}
 }
}
