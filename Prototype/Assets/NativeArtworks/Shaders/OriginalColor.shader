// Original Spatial Wallpaper example; CC0-1.0. https://creativecommons.org/publicdomain/zero/1.0/
Shader "SpatialWallpaper/CC0Example" { Properties { _Color("Color",Color)=(0.1,0.7,1,1) } SubShader { Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" } Pass { HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
float4 _Color;struct Attributes{float4 positionOS:POSITION;};struct Varyings{float4 positionCS:SV_POSITION;};Varyings vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);return o;}half4 frag(Varyings v):SV_Target{return _Color;}
ENDHLSL } } }
