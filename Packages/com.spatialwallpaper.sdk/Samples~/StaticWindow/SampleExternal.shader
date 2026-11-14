Shader "SdkSample/External" {
 Properties { _Color("Color",Color)=(1,.35,.05,1) _Texture("Texture",2D)="white"{} _Cube("Reflection",Cube)="black"{} }
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
 Pass {
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A { float4 positionOS:POSITION;float2 uv:TEXCOORD0; };
 struct V { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0; };
 float4 _Color;TEXTURE2D(_Texture);SAMPLER(sampler_Texture);TEXTURECUBE(_Cube);SAMPLER(sampler_Cube);
 V vert(A v){V o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;return o;}
 half4 frag(V i):SV_Target{return _Color*SAMPLE_TEXTURE2D(_Texture,sampler_Texture,i.uv)+SAMPLE_TEXTURECUBE_LOD(_Cube,sampler_Cube,float3(0,0,1),0)*.1;}
 ENDHLSL
 } }
}
