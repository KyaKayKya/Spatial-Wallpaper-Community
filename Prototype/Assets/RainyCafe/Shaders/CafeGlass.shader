Shader "RainyCafe/Condensation"
{
 Properties {_Clock("Time",Float)=0 _Amount("Water amount",Range(0,1))=.6}
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20"}
  Pass {
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float _Clock,_Amount;
   CBUFFER_END
   struct A {float4 p:POSITION;};struct V {float4 p:SV_POSITION;float3 local:TEXCOORD0;};
   V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.local=TransformObjectToWorld(i.p.xyz)/(.53/5.4);return o;}
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   half4 frag(V i):SV_Target{
    float2 uv=i.local.xy;float2 cell=floor(uv*19);float rnd=hash(cell);float2 q=frac(uv*19)-.5;
    float beads=pow(saturate(1-length(q*float2(4,2.8))),3)*step(.96,rnd);
    float lane=floor(uv.x*8);float seed=hash(float2(lane,3));float dropY=frac(uv.y*.35+_Clock*(.009+seed*.008)+seed);
    float slide=exp(-pow((frac(uv.x*8)-.5)*35,2))*pow(saturate(1-abs(dropY-.5)*70),2)*step(.72,seed);
    float mist=.025+.045*pow(saturate(abs(uv.y-.35)/1.5),5);
    float alpha=_Amount*(mist+beads*.12+slide*.16);
    return half4(.25+beads*.22,.32+beads*.22,.38+beads*.22,alpha);
   }
   ENDHLSL
  }
 }
}
