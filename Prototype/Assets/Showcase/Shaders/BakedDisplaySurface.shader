Shader "SpatialWallpaper/BakedDisplaySurface" {
 Properties {
  [NoScaleOffset] _AgXLut("Accepted AgX display transform",2D)="white"{}
  [NoScaleOffset] _Radiance("Baked diffuse radiance (linear HDR)",2D)="white"{}
  _RadianceGain("Radiance exposure compensation",Range(.1,4))=2
  _DisplayContrast("Display contrast (approved preview)",Range(1,2.4))=2.2
  _Roughness("Reflection roughness",Range(0,1))=.55
  _Reflection("Reflection amount",Range(0,1))=.35
 }
 SubShader {
  Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry"}
  Pass {
   Name "DisplaySurface"
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile _ _REFLECTION_PROBE_BOX_PROJECTION
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_AgXLut);SAMPLER(sampler_AgXLut);
   TEXTURE2D(_Radiance);SAMPLER(sampler_Radiance);
   float _RadianceCapture;
   CBUFFER_START(UnityPerMaterial)
   half _Roughness;half _Reflection;half _RadianceGain;half _DisplayContrast;
   CBUFFER_END
   struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 bakeUV:TEXCOORD1;};
   struct Varyings {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;};
   Varyings Vert(Attributes v){
    Varyings o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);
    o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.uv=v.bakeUV;return o;
   }
   half4 Frag(Varyings i):SV_Target{
    half3 n=normalize(i.normalWS);half3 v=GetWorldSpaceNormalizeViewDir(i.positionWS);
    half3 diffuse=SAMPLE_TEXTURE2D(_Radiance,sampler_Radiance,i.uv).rgb;
    if(_RadianceCapture>.5)return half4(diffuse,1);
    half3 spec=GlossyEnvironmentReflection(reflect(-v,n),i.positionWS,_Roughness,1);
    half fresnel=.035h+.15h*Pow4(1-saturate(dot(n,v)));
    float3 c=(diffuse+spec*fresnel*_Reflection)*_RadianceGain;
    float3 q=saturate((log2(max(c,0.0009765625))+10)/16)*63;
    float slice=floor(q.b);
    float2 uv=float2((q.r+slice*64+.5)/4096,(q.g+.5)/64);
    float3 a=SAMPLE_TEXTURE2D(_AgXLut,sampler_AgXLut,uv).rgb;
    float3 b=SAMPLE_TEXTURE2D(_AgXLut,sampler_AgXLut,uv+float2(64.0/4096,0)).rgb;
    // Explicit creative grade: the approved preview had stronger midtone contrast.
    // This is separate from the single sRGB decode required by the linear renderer.
    float3 display=pow(saturate(lerp(a,b,frac(q.b))),_DisplayContrast);
    return half4(SRGBToLinear(display),1);
   }
   ENDHLSL
  }
 }
}
