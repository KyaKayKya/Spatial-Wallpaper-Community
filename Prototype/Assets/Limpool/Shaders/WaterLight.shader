Shader "SpatialWallpaper/LimpoolWaterLight" {
 Properties { _Caustics("Caustics",2D)="white"{} _Strength("Strength",Float)=0.2 _Underwater("Underwater",Float)=1 _Wall("Wall",Float)=1 }
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10"} Pass {
 Blend One One ZWrite Off ZTest LEqual Offset -1,-1 Cull Back
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
 #pragma multi_compile _ _SHADOWS_SOFT
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_Caustics); SAMPLER(sampler_Caustics);
 float _Strength,_Underwater,_Wall;
 struct A {float4 p:POSITION;float3 n:NORMAL;float4 mask:COLOR;};
 struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0;float3 n:TEXCOORD1;float mask:TEXCOORD2;};
 V vert(A a){V o;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);o.mask=a.mask.r;return o;}
 half4 frag(V i):SV_Target{
  float3 p=TransformWorldToObject(i.w);
  float t=_Time.y;
  float below=1-smoothstep(1.07,1.13,p.y);
  float2 uv=lerp(float2(p.x+p.z*.7,p.y*1.6)*.65,p.xz*.42,below);
  float a=SAMPLE_TEXTURE2D(_Caustics,sampler_Caustics,uv+float2(.008,.005)*t).r;
  float b=SAMPLE_TEXTURE2D(_Caustics,sampler_Caustics,uv*1.31+float2(-.006,.004)*t).r;
  float pattern=min(a,b)*1.6;
  Light sun=GetMainLight(TransformWorldToShadowCoord(i.w));
  float underwater=below*sun.shadowAttenuation*saturate(dot(i.n,sun.direction)+.3);
  // Restricted approximation of upward water bounce: inward side walls and rear pier,
  // within 1.8 authored metres above water, fading away from the lit pool region.
  float inward=saturate(dot(i.n,normalize(float3(-p.x,0,4.5-p.z))));
  float wall=(1-below)*exp(-max(0,p.y-1.1)*1.5)*inward;
  wall*=smoothstep(1,3,p.z)*(1-smoothstep(8.1,8.6,p.z));
  wall*=exp(-pow((p.z-5.4)*.32,2))*i.mask;
  return half4(float3(.12,.65,.75)*_Strength*(underwater*pattern*_Underwater+wall*pattern*.45*_Wall),0);
 }
 ENDHLSL
 } }
}
