// Original MIT SDK verification asset.
Shader "SpatialWallpaper/SdkContractProbe" {
 Properties {
  _Tint ("Tint", Color) = (0.2,0.7,0.9,1)
  _Map ("Map", 2D) = "white" {}
  _Environment ("Environment", Cube) = "" {}
 }
 SubShader { Pass {
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 float4 _Tint;
 float4 vert(float4 position:POSITION):SV_POSITION { return position; }
 float4 frag():SV_Target { return _Tint; }
 ENDHLSL
 } }
}
