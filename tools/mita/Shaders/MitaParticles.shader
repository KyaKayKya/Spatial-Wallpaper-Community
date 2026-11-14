Shader "SpatialWallpaper/MitaParticles" {
 Properties {
  _MainTex ("Texture",2D)="white" {}
  _Color ("Color",Color)=(1,1,1,1)
  _LuminanceAlpha ("Grayscale mask",Float)=0
  _Cutoff ("Cutoff",Float)=0
  _DstBlend ("Destination blend",Float)=1
 }
 SubShader {
  Tags {"Queue"="Transparent+100" "RenderType"="Transparent" "IgnoreProjector"="True"}
  Blend One [_DstBlend]
  ZWrite Off Cull Off
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _MainTex;float4 _MainTex_ST,_Color;float _LuminanceAlpha,_Cutoff;
   struct Input {float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
   struct Output {float4 vertex:SV_POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;};
   Output vert(Input v){Output o;o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color*_Color;o.uv=TRANSFORM_TEX(v.uv,_MainTex);return o;}
   float4 frag(Output i):SV_Target {
    float4 t=tex2D(_MainTex,i.uv);
    float alpha=lerp(t.a,t.r,_LuminanceAlpha)*i.color.a;
    clip(alpha-_Cutoff);
    return float4(t.rgb*i.color.rgb*i.color.a,alpha);
   }
   ENDCG
  }
 }
}
