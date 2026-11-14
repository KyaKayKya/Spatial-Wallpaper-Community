using System;using System.IO;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;
namespace SpatialWallpaper.Sdk.Editor {
 // Only the pinned pipeline's built-in light configuration crosses this boundary.
 internal static class SceneLightEncoding {
  static void Need(bool value,string reason){if(!value)throw new InvalidDataException("export:"+reason);}
  internal static JObject Encode(Light light){
   Need(light.enabled&&(light.type==LightType.Point||light.type==LightType.Spot||light.type==LightType.Directional),"light-type-or-disabled-not-supported");
   Need(!light.cookie&&!light.flare&&light.lightmapBakeType==LightmapBakeType.Realtime,"light-cookie-flare-or-baking-not-supported");
   Need(light.cullingMask==-1&&light.renderingLayerMask==1&&!light.useColorTemperature&&light.renderMode==LightRenderMode.Auto,"light-render-state-not-supported");
   Need(light.innerSpotAngle<=light.spotAngle,"light-inner-spot-exceeds-spot");
   var data=light.GetComponent<UniversalAdditionalLightData>();
   var defaults=new GameObject("SDK fixed light defaults"){hideFlags=HideFlags.HideAndDontSave};defaults.SetActive(false);
   try{
    var baseLight=defaults.AddComponent<Light>();var baseData=defaults.AddComponent<UniversalAdditionalLightData>();
    Need(light.bounceIntensity==baseLight.bounceIntensity&&light.shadowResolution==baseLight.shadowResolution&&light.shadowCustomResolution==baseLight.shadowCustomResolution&&light.lightShadowCasterMode==baseLight.lightShadowCasterMode&&light.useBoundingSphereOverride==baseLight.useBoundingSphereOverride&&light.useViewFrustumForShadowCasterCull==baseLight.useViewFrustumForShadowCasterCull,"light-unrepresented-state-not-supported");
    if(data){
     Need(data.GetType()==typeof(UniversalAdditionalLightData)&&data.enabled,"light-additional-data-type-or-disabled");
     Need(data.additionalLightsShadowResolutionTier==baseData.additionalLightsShadowResolutionTier&&data.renderingLayers==baseData.renderingLayers&&data.customShadowLayers==baseData.customShadowLayers&&data.shadowRenderingLayers==baseData.shadowRenderingLayers&&data.lightCookieSize==baseData.lightCookieSize&&data.lightCookieOffset==baseData.lightCookieOffset&&data.softShadowQuality==baseData.softShadowQuality,"light-additional-settings-not-supported");
    }
    var color=light.color;
    return new JObject{{"type",(int)light.type},{"color",new JArray(color.r,color.g,color.b,color.a)},{"intensity",light.intensity},{"range",light.range},{"spot",light.spotAngle},{"innerSpot",light.innerSpotAngle},{"shadows",(int)light.shadows},{"shadowStrength",light.shadowStrength},{"bias",light.shadowBias},{"normalBias",light.shadowNormalBias},{"near",light.shadowNearPlane},{"usePipelineSettings",!data||data.usePipelineSettings}};
   }finally{UnityEngine.Object.DestroyImmediate(defaults);}
  }
 }
}
