Shader "RainyCafe/Weather"
{
 Properties {_Color("Tint",Color)=(.4,.55,.7,.3) _Kind("0 rain 1 steam 2 flame",Float)=0 _Clock("Time",Float)=0 _Speed("Speed",Float)=1}
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent"}
  Pass {
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _Color;float _Kind,_Clock,_Speed;
   CBUFFER_END
   struct A {float4 p:POSITION;float2 uv:TEXCOORD0;float4 seed:TEXCOORD1;};
   struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float fade:TEXCOORD1;float light:TEXCOORD2;};
   V vert(A i){V o;float3 p=i.p.xyz;float fade=1;
    if(_Kind<.5){float fall=frac(i.seed.x+_Clock*_Speed/8);p.y+=(1-fall)*8;p.x+=.12*(1-fall);fade=saturate(fall*12)*saturate((1-fall)*12);}
    else if(_Kind<1.5){float age=frac(i.seed.x+_Clock*(.14+i.seed.x*.065));float spread=.35+age*.9;p.xz*=spread;p.y=(p.y+.055)*(.35+age*.7)+age*.34;p.x+=sin(age*6+i.seed.x*13)*age*.035;p.z+=cos(age*5+i.seed.x*9)*age*.018;fade=sin(age*3.14159)*.4;}
    o.p=TransformObjectToHClip(p);o.uv=i.uv;o.fade=fade;float3 wp=TransformObjectToWorld(p);o.light=1+1.8*exp(-length(wp-float3(-.1178,.118, .805))*9);return o;}
   half4 frag(V i):SV_Target{
    float2 q=i.uv*2-1;float a;
    if(_Kind<.5)a=pow(saturate(1-abs(q.x)),2)*saturate(1-abs(q.y));
    else if(_Kind<1.5)a=pow(saturate(1-dot(q,q)),3);
    else {q.x*=1.1+.65*q.y;a=pow(saturate(1-dot(q,q)),1.4);}
    return half4(_Color.rgb*(_Kind<.5?i.light:1),_Color.a*a*i.fade);
   }
   ENDHLSL
  }
 }
}
