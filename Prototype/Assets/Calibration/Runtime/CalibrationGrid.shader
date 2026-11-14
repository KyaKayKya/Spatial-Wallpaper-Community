Shader "Spatial/CalibrationGrid" {
 Properties {
  _BaseColor("Surface",Color)=(.17,.21,.27,1)
  _LineColor("Grid",Color)=(.46,.57,.67,1)
  _GridSize("Grid spacing",Float)=.03
  _Checker("Checker cube",Float)=0
  _FadeStart("Depth fade start",Float)=.0075
  _FadeEnd("Depth fade end",Float)=.3
 }
 SubShader {
  Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
  Cull Off
  Pass {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
   struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float depth:TEXCOORD1;};
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor,_LineColor;float _GridSize,_Checker,_FadeStart,_FadeEnd;
   CBUFFER_END
   V vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;o.depth=a.positionOS.z;return o;}
   half4 frag(V i):SV_Target {
    float2 p=i.uv/max(_GridSize,.0001);
    float2 edge=abs(frac(p-.5)-.5)/max(fwidth(p),.001);
    float gridMask=1-saturate(min(edge.x,edge.y));
    if(_Checker>.5)gridMask=max(gridMask,((int)floor(p.x)+(int)floor(p.y))%2==0?.8:0);
    // Same authored-depth power falloff as MiniShotGame/FilteredGrid, adapted to room depth.
    float fade=_Checker>.5?1:pow(1-saturate((i.depth-_FadeStart)/max(_FadeEnd-_FadeStart,.00001)),2.25);
    // The room is also displayed through a RawImage: depth fades RGB only.
    return half4(lerp(_BaseColor.rgb,_LineColor.rgb,gridMask)*fade,1);
   }
   ENDHLSL
  }
 }
}
