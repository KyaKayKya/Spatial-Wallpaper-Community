Shader "Parking/CRT" {
 Properties { _Brightness("Brightness",Float)=1 _NoiseTime("Noise time",Float)=0 _Seed("Seed",Float)=17 }
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
 Pass { HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float _Brightness,_NoiseTime,_Seed;
 CBUFFER_END
 struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
 struct V{float4 pos:SV_POSITION;float2 uv:TEXCOORD0;};
 V vert(A a){V o;o.pos=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;return o;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 half4 frag(V i):SV_Target {
  float t=floor(_NoiseTime*24);float2 p=floor(i.uv*float2(290,220));
  float noise=hash(p+float2(t*13.1,_Seed+t*.7));
  float scan=.88+.12*sin(i.uv.y*1382.3);
  float roll=1-.23*exp(-pow(frac(i.uv.y+_NoiseTime*.19)-.5,2)*650);
  float vignette=saturate(1-.6*dot(i.uv-.5,i.uv-.5));
  return half4(float3(.75,.88,1)*(.12+noise*.88)*scan*roll*vignette*_Brightness*3,1);
 }
 ENDHLSL }
 }
}
