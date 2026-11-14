using UnityEngine;
namespace SpatialWallpaper.Sdk {
 public enum PresentationProfile { ForwardPlus, Forward }
 // Explicit platform-owned presentation subset consumed by current scenes.
 public sealed class ScenePresentationSettings:MonoBehaviour {
  [Tooltip("Apply the supported presentation settings during ordinary Editor Play. Leave off for legacy scenes with their own presentation.")]
  public bool applyInPlay;
  public bool customAmbientGradient;public Color ambientSky,ambientEquator,ambientGround;
  public bool customAmbient;public Color ambientIrradiance=new Color(.07f,.075f,.085f);
  public bool customBackground;public Color backgroundColor=Color.black;
  public bool customClipDepth;public float clipDepth=1;
  public const string ForwardResource="PlatformForward",ForwardPlusResource="PlatformForwardPlus";
  public bool customCamera;public bool hdr=true,opaqueTexture,depthTexture,cameraPostProcessing;public int msaa=4;public PresentationProfile profile=PresentationProfile.ForwardPlus;
  public bool postProcessing;public Vector3 bloom=new Vector3(1,.12f,.5f);public bool aces=true;public float exposure=.8f,saturation=-18;public Vector2 vignette=new Vector2(.17f,.6f);
 }
}
