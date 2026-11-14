Shader "Hidden/SpatialSDK/CubeReadback" {
 Properties { _MainTex("Texture",2D)="white"{} _Cube("Cube",Cube)="black"{} _Face("Face",Float)=0 _Mip("Mip",Float)=0 }
 SubShader { Pass { ZTest Always ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 samplerCUBE _Cube;sampler2D _MainTex;float _Face,_Mip;
 half4 frag(v2f_img input):SV_Target {
  if(_Face<-.5)return tex2Dlod(_MainTex,float4(input.uv,0,_Mip));
  float2 uv=input.uv*2-1;float3 d;
  if(_Face<.5)d=float3(1,-uv.y,-uv.x);
  else if(_Face<1.5)d=float3(-1,-uv.y,uv.x);
  else if(_Face<2.5)d=float3(uv.x,1,uv.y);
  else if(_Face<3.5)d=float3(uv.x,-1,-uv.y);
  else if(_Face<4.5)d=float3(uv.x,-uv.y,1);
  else d=float3(-uv.x,-uv.y,-1);
  return texCUBElod(_Cube,float4(d,_Mip));
 }
 ENDHLSL
 } }
}
