Shader "SpatialWallpaper/BlueprintLit" {
 Properties {
  [MainTexture]_BaseMap("Pixel atlas",2D)="white"{} [MainColor]_BaseColor("Tint",Color)=(1,1,1,1)
  [HDR]_EmissionColor("Emission",Color)=(0,0,0,1) _Smoothness("Smoothness",Range(0,1))=.08
  _Cutoff("Cutout",Range(0,1))=.5 _Cull("Cull",Float)=2 _SrcBlend("Source blend",Float)=1 _DstBlend("Destination blend",Float)=0 _ZWrite("Depth write",Float)=1
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Pass {
   Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
   Blend [_SrcBlend] [_DstBlend] ZWrite [_ZWrite] Cull [_Cull]
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma target 3.5
   #pragma shader_feature_local_fragment _ALPHATEST_ON
   #pragma shader_feature_local_fragment _TILING_ON
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
   float _BlueprintWindStrength;
   struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;half4 color:COLOR;};
   struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;half3 color:TEXCOORD3;half fog:TEXCOORD4;};
   Varyings Vert(Attributes v){Varyings o;float wave=sin(v.positionOS.x*1.7+v.positionOS.z*.9+_Time.y*.8)*_BlueprintWindStrength*v.color.a;v.positionOS.x+=wave;v.positionOS.z+=wave*.35;VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.uv=v.uv;o.color=v.color.rgb;o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   half4 Frag(Varyings i):SV_Target {
    float2 uv=i.uv;
    #if defined(_TILING_ON)
    uv=frac(uv)*_BaseMap_ST.xy+_BaseMap_ST.zw;
    #endif
    half4 pixel=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv)*_BaseColor;
    #if defined(_ALPHATEST_ON)
    clip(pixel.a-_Cutoff);
    #endif
    InputData input=(InputData)0;input.positionWS=i.positionWS;input.normalWS=normalize(i.normalWS);input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);input.shadowCoord=TransformWorldToShadowCoord(i.positionWS);input.bakedGI=SampleSH(input.normalWS);input.vertexLighting=VertexLighting(i.positionWS,input.normalWS);input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);input.shadowMask=half4(1,1,1,1);
    SurfaceData surface=(SurfaceData)0;surface.albedo=pixel.rgb*i.color;surface.alpha=pixel.a;surface.smoothness=_Smoothness;surface.normalTS=half3(0,0,1);surface.occlusion=1;surface.emission=pixel.rgb*_EmissionColor.rgb;
    half4 result=UniversalFragmentPBR(input,surface);result.rgb=MixFog(result.rgb,i.fog);return result;
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
  UsePass "Universal Render Pipeline/Lit/DepthNormals"
 }
 FallBack Off
}
