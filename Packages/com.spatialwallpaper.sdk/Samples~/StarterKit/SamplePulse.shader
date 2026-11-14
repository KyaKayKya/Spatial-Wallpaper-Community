Shader "SpatialWallpaper/Samples/Pulse" {
 Properties { _BaseColor("Color",Color)=(0.2,0.5,1,1) _Pulse("Pulse",Range(0,1))=0 }
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
  Pass {
   Tags { "LightMode"="UniversalForward" }
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Attributes {float4 positionOS:POSITION;};
   struct Varyings {float4 positionCS:SV_POSITION;};
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;float _Pulse;
   CBUFFER_END
   Varyings Vert(Attributes input){Varyings result;result.positionCS=TransformObjectToHClip(input.positionOS.xyz);return result;}
   half4 Frag(Varyings input):SV_Target{return half4(_BaseColor.rgb*(0.4+0.6*_Pulse),_BaseColor.a);}
   ENDHLSL
  }
 }
}
