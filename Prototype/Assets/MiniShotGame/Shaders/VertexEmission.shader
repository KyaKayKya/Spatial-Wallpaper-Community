Shader "MiniShotGame/VertexEmission" {
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
  Pass {
   Cull Off
   ZWrite On
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; };
   struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; };
   Varyings Vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.color=v.color; return o; }
   half4 Frag(Varyings v):SV_Target { return half4(v.color.rgb,1); }
   ENDHLSL
  }
 }
}
