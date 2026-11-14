Shader "MiniShotGame/FilteredGrid" {
 Properties {
  _Vertical ("Use vertical wall coordinates",Float)=0
  _Span ("Authored aperture span",Float)=1.6
  _Divisions ("Aperture divisions",Float)=12
 }
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
  Pass {
   Cull Off
   ZWrite On
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
    float _Vertical, _Span, _Divisions;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; };
   struct Varyings { float4 positionCS:SV_POSITION; float3 authored:TEXCOORD0; half4 color:COLOR; };
   Varyings Vert(Attributes v) {
    Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.authored=v.positionOS.xyz;o.color=v.color;return o;
   }
   // Integral of a periodic pulse. Integrating over the pixel footprint keeps
   // subpixel lines at their average coverage instead of switching on and off.
   float PulseIntegral(float x,float duty) { return floor(x)*duty+min(frac(x),duty); }
   float FilteredLine(float position,float spacing,float radius,float projectedScale) {
    // Match the old round wire's projected thickness, including grazing views.
    radius=max(radius,radius*projectedScale*length(float2(ddx(position),ddy(position))));
    float x=(position+radius)/spacing;
    float width=max(fwidth(position)/spacing,0.00001);
    float duty=2*radius/spacing;
    return saturate((PulseIntegral(x+width*.5,duty)-PulseIntegral(x-width*.5,duty))/width);
   }
   half4 Frag(Varyings v):SV_Target {
    float3 world=TransformObjectToWorld(v.authored);
    float objectScale=length(mul((float3x3)unity_ObjectToWorld,float3(1,0,0)));
    float projectedScale=objectScale*abs(UNITY_MATRIX_P._m11)*_ScreenParams.y*.5/max(-TransformWorldToView(world).z,.001);
    float across=lerp(v.authored.x,v.authored.y,_Vertical)+_Span*.5;
    float longitudinal=FilteredLine(across,_Span/_Divisions,.00145,projectedScale);
    float crossSection=FilteredLine(v.authored.z,.15,.00165,projectedScale);
    float fade=pow(1-saturate((v.authored.z-.05)/1.95),2.25);
    float3 grid=max(float3(.40,.41,.43)*longitudinal,float3(.46,.47,.49)*crossSection)*fade;
    return half4(max(v.color.rgb,grid),1);
   }
   ENDHLSL
  }
 }
}
