Shader "SpatialWallpaper/LimpoolUnderwater" {
 SubShader { Tags {"RenderPipeline"="UniversalPipeline"}
 Pass { ZWrite Off ZTest Always Cull Off
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 float4x4 _WorldToPoolScreen;
 float _PoolWaterHeight,_PoolMetreScale;
 float4 Frag(Varyings i):SV_Target {
  float2 uv=i.texcoord;
  float3 color=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv).rgb;
  float depth=SampleSceneDepth(uv);
  float3 hit=ComputeWorldSpacePosition(uv,max(depth,.000001),UNITY_MATRIX_I_VP);
  float3 eye=mul(_WorldToPoolScreen,float4(_WorldSpaceCameraPos,1)).xyz;
  float3 ray=mul(_WorldToPoolScreen,float4(hit,1)).xyz-eye;
  // Intersect the opaque-depth ray with z>=0 and y<=water height. The water
  // surface is analytical because the opaque depth excludes transparent water.
  float start=max(0,-eye.z/max(ray.z,.000001));
  float end=1;
  if(abs(ray.y)<.0000001){if(eye.y>_PoolWaterHeight)return float4(color,1);}
  else {
   float crossing=(_PoolWaterHeight-eye.y)/ray.y;
   if(ray.y>0)end=min(end,crossing);
   else start=max(start,crossing);
  }
  float metres=min(18,length(hit-_WorldSpaceCameraPos)*max(0,end-start)/_PoolMetreScale);
  float3 transmission=exp(-metres*float3(1.4,.65,.48));
  return float4(color*transmission+float3(.012,.065,.105)*(1-transmission),1);
 }
 ENDHLSL
 } }
}
