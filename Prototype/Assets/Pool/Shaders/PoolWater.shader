Shader "SpatialWallpaper/PoolWater" {
 Properties {
  _Animation("Animation",Float)=1
  _WaterLevel("Water height",Float)=0
  _WorldScale("Metres to display",Float)=.049074
  _AbsorptionScale("Absorption scale",Float)=1
  _Caustics("Caustics",2D)="black"{}
 }
 SubShader {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Pass {
   ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   TEXTURE2D(_Caustics);SAMPLER(sampler_Caustics);
   CBUFFER_START(UnityPerMaterial)
   float _Animation,_WaterLevel,_WorldScale,_AbsorptionScale;
   CBUFFER_END
   struct A{float4 p:POSITION;};
   struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;float eye:TEXCOORD1;};
   V vert(A a){V v;v.world=TransformObjectToWorld(a.p.xyz);v.p=TransformWorldToHClip(v.world);v.eye=-TransformWorldToView(v.world).z;return v;}
   half4 frag(V v):SV_Target{
    float2 uv=GetNormalizedScreenSpaceUV(v.p);
    float2 p=v.world.xz/max(_WorldScale,.0001);
    float t=_Time.y*_Animation;
    float2 wave=float2(sin(p.x*5.3+p.y*3.1+t*.9)+.4*sin(p.y*12-t*1.1),cos(p.y*6.1-p.x*2.2+t*.7)+.35*sin(p.x*14+t*.8));
    float rawDepth=SampleSceneDepth(uv);
    float depth=LinearEyeDepth(rawDepth,_ZBufferParams);
    float3 hit=ComputeWorldSpacePosition(uv,rawDepth,UNITY_MATRIX_I_VP);
    float distance=max(0,(depth-v.eye)/max(_WorldScale,.0001));
    float2 bent=uv+wave*.0025*saturate(distance*2);
    float bentDepth=LinearEyeDepth(SampleSceneDepth(bent),_ZBufferParams);
    // Never pull foreground walls or sky across the contact boundary.
    if(bentDepth<v.eye||abs(bentDepth-depth)>.08)bent=uv;
    float3 behind=SampleSceneColor(bent);
    float vertical=max(0,(_WaterLevel-hit.y)/max(_WorldScale,.0001));
    float3 trans=exp(-float3(1.25,.38,.30)*min(distance+vertical*1.8,15)*_AbsorptionScale);
    float2 cuv=hit.xz/max(_WorldScale,.0001)*.26;
    float ca=SAMPLE_TEXTURE2D(_Caustics,sampler_Caustics,cuv+t*float2(.018,.013)).b;
    float cb=SAMPLE_TEXTURE2D(_Caustics,sampler_Caustics,cuv*1.13-t*float2(.011,.017)).b;
    behind+=(ca+cb+4*ca*cb)*saturate(dot(behind,float3(.2126,.7152,.0722)))*float3(.08,.26,.22);
    float3 water=behind*trans+float3(.003,.035,.029)*(1-trans);
    return half4(water,1);
   }
   ENDHLSL
  }
 }
}
