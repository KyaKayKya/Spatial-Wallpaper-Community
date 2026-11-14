using UnityEngine;
namespace SpatialWallpaper.Sdk {
 [DisallowMultipleComponent]
 public sealed class SceneDefinition:MonoBehaviour {
  public string templateId="0ba09280-7fda-4f38-ac65-97cd4f94bbe2";
  public string displayName="Spatial sample";
  public Vector3 authorFrame=new Vector3(.53f,.298125f,2);
  public Transform contentRoot;
  public bool editable=true,reshare;
  [TextArea] public string licenseSource="Procedural geometry authored in this project";
  public string licenseName="Local validation only";
  [TextArea] public string licenseAttribution="";
 }
}
