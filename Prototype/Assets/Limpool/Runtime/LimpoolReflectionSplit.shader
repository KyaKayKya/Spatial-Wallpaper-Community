Shader "Hidden/Limpool/ReflectionSplit" {
 Properties { _MainTex("Reflection",2D)="black"{} _RadianceLimit("Detail radiance limit",Float)=.25 }
 SubShader {
  Cull Off ZWrite Off ZTest Always
  CGINCLUDE
  #include "UnityCG.cginc"
  sampler2D _MainTex; float _RadianceLimit;
  float3 Detail(float3 c){return c*min(1,_RadianceLimit/max(max(c.r,c.g),max(c.b,1e-5)));}
  float4 Base(v2f_img i):SV_Target {return float4(Detail(tex2D(_MainTex,i.uv).rgb),1);}
  float4 Highlights(v2f_img i):SV_Target {float3 c=tex2D(_MainTex,i.uv).rgb;return float4(c-Detail(c),1);}
  ENDCG
  Pass { CGPROGRAM
   #pragma vertex vert_img
   #pragma fragment Base
  ENDCG }
  Pass { CGPROGRAM
   #pragma vertex vert_img
   #pragma fragment Highlights
  ENDCG }
 }
}
