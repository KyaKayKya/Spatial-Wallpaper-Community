Shader "RainyCafeSdk/Weather"
{
 Properties { _Color("Tint",Color)=(.4,.5,.7,.2) _Kind("Rain or steam",Float)=0 _Speed("Speed",Float)=5 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent"}
  Pass
  {
   Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _Color; float _Kind, _Speed;
   CBUFFER_END
   struct A {float4 p:POSITION; float2 uv:TEXCOORD0; float2 seed:TEXCOORD1;};
   struct V {float4 p:SV_POSITION; float2 uv:TEXCOORD0; float fade:TEXCOORD1;};
   V vert(A i)
   {
    V o; float3 p=i.p.xyz; const float scale=.53/5.4;
    if(_Kind<.5)
    {
     float fall=frac(i.seed.x+_Time.y*_Speed/8);
     p.y+=(1-fall)*8*scale; p.x+=(1-fall)*.12*scale;
     o.fade=saturate(fall*12)*saturate((1-fall)*12);
    }
    else if(_Kind<1.5)
    {
     float age=frac(i.seed.x+_Time.y*(.14+i.seed.x*.065));
     p.y+=age*.34*scale;
     p.x+=sin(age*6+i.seed.x*13)*age*.035*scale;
     p.z+=cos(age*5+i.seed.x*9)*age*.018*scale;
     o.fade=sin(age*3.14159265)*.4;
    }
    else o.fade=.9+.1*sin(_Time.y*9+i.seed.x*31);
    o.p=TransformObjectToHClip(p); o.uv=i.uv; return o;
   }
   half4 frag(V i):SV_Target
   {
    float2 q=i.uv*2-1;
    float alpha;if(_Kind<.5)alpha=pow(saturate(1-abs(q.x)),2)*saturate(1-abs(q.y));else if(_Kind<1.5)alpha=pow(saturate(1-dot(q,q)),3);else{q.x*=1.1+.65*q.y;alpha=pow(saturate(1-dot(q,q)),1.4);}
    return half4(_Color.rgb,_Color.a*alpha*i.fade);
   }
   ENDHLSL
  }
 }
}
