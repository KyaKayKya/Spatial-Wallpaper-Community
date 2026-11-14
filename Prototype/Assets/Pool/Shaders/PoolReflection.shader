Shader "SpatialWallpaper/PoolReflection" {
 Properties{_ReflectionTex("Reflection",2D)="black"{} _Strength("Strength",Range(0,1))=.95 _Animation("Animation",Float)=1 _WaveNormal("Water surface slopes",2D)="gray"{} _WorldScale("Metres to display",Float)=.049074 _WaterLevel("Water height",Float)=0}
 SubShader{
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+1" "RenderType"="Transparent"}
  Pass{
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_ReflectionTex);SAMPLER(sampler_ReflectionTex);
   TEXTURE2D(_WaveNormal);SAMPLER(sampler_WaveNormal);
   CBUFFER_START(UnityPerMaterial)
   float _Strength,_Animation,_WorldScale,_WaterLevel;
   CBUFFER_END
   struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;};
   V vert(A a){V v;v.world=TransformObjectToWorld(a.p.xyz);v.p=TransformWorldToHClip(v.world);return v;}
   half4 frag(V v):SV_Target{
    float t=_Time.y*_Animation;
    float2 p=v.world.xz/max(_WorldScale,.0001)*.33;
    float2 a=SAMPLE_TEXTURE2D(_WaveNormal,sampler_WaveNormal,p+t*float2(.014,.009)).rg*2-1;
    float2 b=SAMPLE_TEXTURE2D(_WaveNormal,sampler_WaveNormal,p*1.21-t*float2(.009,.013)).rg*2-1;
    float2 ripple=(a+b)*.5;
    float2 uv=GetNormalizedScreenSpaceUV(v.p)+ripple*float2(.014,.024);
    half3 col=SAMPLE_TEXTURE2D(_ReflectionTex,sampler_ReflectionTex,saturate(uv)).rgb;
    float3 normal=normalize(float3(ripple.x*.16,1,ripple.y*.16));
    float fresnel=.08+.92*pow(1-saturate(abs(dot(GetWorldSpaceNormalizeViewDir(v.world),normal))),4);
    return half4(col,_Strength*fresnel);
   }
   ENDHLSL
  }
 }
}
