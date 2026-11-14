Shader "RainyCafe/StaticSurface"
{
 Properties {
  _BaseMap("Albedo",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1)
  _LightingMap("Static illumination",2D)="black"{} _StaticStrength("Static strength",Float)=1
  _RoughnessMap("Roughness",2D)="white"{} _BumpMap("Normal",2D)="bump"{}
  _OpacityMap("Foliage opacity",2D)="white"{} _UseCutout("Use foliage cutout",Float)=0
  _Metallic("Metallic",Range(0,1))=0 _Cutoff("Cutoff",Range(0,1))=.5 _ExteriorFog("Exterior haze",Float)=0
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Pass {
   Name "ForwardLit" Tags {"LightMode"="UniversalForward"} Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _ADDITIONAL_LIGHTS
   #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   TEXTURE2D(_LightingMap);SAMPLER(sampler_LightingMap);
   TEXTURE2D(_RoughnessMap);SAMPLER(sampler_RoughnessMap);
   TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
   TEXTURE2D(_OpacityMap);SAMPLER(sampler_OpacityMap);
   CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST,_BaseColor;float _StaticStrength,_Metallic,_Cutoff,_ExteriorFog,_UseCutout;
   CBUFFER_END
   float _CafeLightAudit;
   float4 _CafeLightPosition;
   struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;float2 uv:TEXCOORD0;};
   struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;float4 tangentWS:TEXCOORD2;float2 uv:TEXCOORD3;};
   Varyings vert(Attributes v){Varyings o;VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;VertexNormalInputs n=GetVertexNormalInputs(v.normalOS,v.tangentOS);o.normalWS=n.normalWS;o.tangentWS=float4(n.tangentWS,v.tangentOS.w*GetOddNegativeScale());o.uv=v.uv;return o;}
   half4 frag(Varyings v):SV_Target{
    if (_CafeLightAudit > .5) {
     uint count=GetAdditionalLightsCount(); float present=0;
     for(uint j=0;j<count;j++) {
      int index=GetPerObjectLightIndex(j);
      #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
       float3 position=_AdditionalLightsBuffer[index].position.xyz;
      #else
       float3 position=_AdditionalLightsPosition[index].xyz;
      #endif
      present=max(present, distance(position,_CafeLightPosition.xyz)<.0001?1:0);
     }
     return half4(count/8.0,present,0,1);
    }
    clip(lerp(1,SAMPLE_TEXTURE2D(_OpacityMap,sampler_OpacityMap,v.uv).r,_UseCutout)-.5);
    half3 normalTS=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,v.uv));
    half3 n=normalize(v.normalWS);half3 tangent=normalize(v.tangentWS.xyz);half3 bitangent=cross(n,tangent)*v.tangentWS.w;
    InputData input=(InputData)0;input.positionWS=v.positionWS;input.normalWS=normalize(mul(normalTS,half3x3(tangent,bitangent,n)));input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(v.positionWS);input.shadowCoord=TransformWorldToShadowCoord(v.positionWS);input.bakedGI=0;input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(v.positionCS);input.shadowMask=1;
    SurfaceData surface=(SurfaceData)0;surface.albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,v.uv).rgb*_BaseColor.rgb;surface.alpha=1;surface.metallic=_Metallic;surface.smoothness=1-clamp(SAMPLE_TEXTURE2D(_RoughnessMap,sampler_RoughnessMap,v.uv).r,.08,.98);surface.occlusion=1;surface.normalTS=normalTS;surface.emission=SAMPLE_TEXTURE2D(_LightingMap,sampler_LightingMap,v.uv).rgb*_StaticStrength;
    half4 color=UniversalFragmentPBR(input,surface);
    color.rgb=lerp(color.rgb,half3(.009,.017,.028),_ExteriorFog*(1-exp(-max(0,v.positionWS.z-.5)*.65)));
    return color;
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
