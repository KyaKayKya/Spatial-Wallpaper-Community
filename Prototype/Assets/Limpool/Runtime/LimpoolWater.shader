Shader "SpatialWallpaper/LimpoolWater" {
 Properties { _NormalMapA("Ripple normal",2D)="bump"{} _ReflectionIntensity("Reflection",Range(0,1))=.75 _DebugMode("Debug",Float)=0 _WaterDepthClip("Diagnostic depth clipping",Float)=0 _SpecularStrength("Sun specular",Float)=.38 _WaterTime("Diagnostic time (-1 uses animation)",Float)=-1 _FresnelNormalStrength("Reflection weight normal strength",Range(0,1))=.264 _FresnelOverride("Diagnostic reflection weight",Float)=-1 }
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
 // Keep the real water intersection when it is closer than the near plane.
 // Depth is clamped, while perspective position and surface shading stay intact.
 // The water mesh ends before this scene's far plane; far clamping is not used.
 Pass { ZWrite On Cull Off ZClip [_WaterDepthClip]
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
 TEXTURE2D(_NormalMapA);SAMPLER(sampler_NormalMapA);
 TEXTURE2D(_ReflectionTexture);SAMPLER(sampler_ReflectionTexture);
 TEXTURE2D(_ReflectionHighlights);SAMPLER(sampler_ReflectionHighlights);
 TEXTURE2D(_ReflectionRaw);SAMPLER(sampler_ReflectionRaw);
 float4 _ReflectionTexture_TexelSize;
 float _ReflectionRadianceSplit;
 CBUFFER_START(UnityPerMaterial)
float _ReflectionIntensity,_DebugMode,_WaterDepthClip,_SpecularStrength,_WaterTime,_FresnelNormalStrength,_FresnelOverride;
 CBUFFER_END
 float _LimpoolReflectionFilter;
 float _LimpoolAngularRoughness;
 struct A {float4 positionOS:POSITION;};
 struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 local:TEXCOORD1;};
 V Vert(A a){
  V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.local=a.positionOS.xyz;o.positionCS=TransformWorldToHClip(o.world);
  // A planar surface through the eye has zero projected area. Large triangles
  // plus depth clamping otherwise rasterize numerical stripes at coplanarity.
  // Keep the eye unchanged and let the analytical medium handle this 1um band.
  if(abs(_WorldSpaceCameraPos.y-TransformObjectToWorld(float3(0,0,0)).y)<=.000001)o.positionCS=float4(0,0,0,1);
  return o;
 }
 float3 Frag(V i):SV_Target {
  float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);
  float2 p=i.local.xz;
  bool underwater=_WorldSpaceCameraPos.y<i.world.y;
  // The virtual pool starts behind the display. Its front coverage is useful
  // from above, but must not become a ceiling between a low eye and the screen.
  if(underwater)clip(p.y);
  if(_DebugMode==14)return float3(0,1,0);
  float waterTime=_WaterTime<0?_Time.y:_WaterTime;
  float3 na=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMapA,sampler_NormalMapA,p*.135+waterTime*float2(.018,.0102)));
  float3 nb=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMapA,sampler_NormalMapA,p*float2(.24,.21)+waterTime*float2(-.012,.015)));
  float distanceFade=1/(1+length(i.world-_WorldSpaceCameraPos)*.8);
  float2 swell=float2(cos(dot(p,float2(1.9,3.2))+.90*waterTime),sin(dot(p,float2(-2.1,2.7))+.72*waterTime))*.055;
  float2 slope=((na.xy+nb.xy)*1.85+swell)*distanceFade;
  float3 n=normalize(float3(slope.x,1,slope.y));
  float3 view=normalize(_WorldSpaceCameraPos-i.world);
  float2 offset=slope/_ScaledScreenParams.xy*3;
  float2 refrUV=clamp(uv+offset,.001,.999);
  float raw=SampleSceneDepth(refrUV);
  float3 hit=ComputeWorldSpacePosition(refrUV,raw,UNITY_MATRIX_I_VP);
  // Distorted samples belonging to foreground geometry must not tint the water.
  if(hit.y>i.world.y+.0005){refrUV=uv;raw=SampleSceneDepth(uv);hit=ComputeWorldSpacePosition(uv,raw,UNITY_MATRIX_I_VP);}
  float sceneScale=length(mul((float3x3)unity_ObjectToWorld,float3(1,0,0)));
  float metres=min(length(hit-i.world)/sceneScale,18);
  if(_DebugMode==4)return float3(metres*.1,sceneScale*5,0);
  float3 transmittance=exp(-metres*float3(1.4,.65,.48));
  Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
  // The offscreen near coverage continues the enclosed pool's illumination.
  // The distant sea receives a restrained diffuse contribution.
  float lit=sun.shadowAttenuation*smoothstep(0,1.2,p.y)*(1-.75*smoothstep(7.8,15,p.y));
  float rippledLight=pow(saturate(.5+2.5*(na.x-nb.y)),3);
  float3 scatter=lerp(float3(.012,.065,.105),float3(.025,.34,.29),lit);
  scatter+=lit*float3(.018,.38,.29)*rippledLight*2.2;
  float3 transmission=underwater?SampleSceneColor(refrUV):SampleSceneColor(refrUV)*transmittance+scatter*(1-transmittance);
  float2 reflectUV=clamp(uv+slope*float2(.018,.045),.001,.999);
  // A bias alone allows a magnified bright window to collapse to individual texels.
  // Keep a resolution-independent roughness footprint, also covering pixel derivatives.
  float2 dx=ddx(reflectUV)*_ReflectionTexture_TexelSize.zw;
  float2 dy=ddy(reflectUV)*_ReflectionTexture_TexelSize.zw;
  float roughnessPixels=max(1,_ReflectionTexture_TexelSize.w*.015);
  if(_LimpoolAngularRoughness>0)roughnessPixels=max(roughnessPixels,.5*abs(UNITY_MATRIX_P._m11)*_LimpoolAngularRoughness*_ReflectionTexture_TexelSize.w);
  float lod=.5*log2(max(roughnessPixels*roughnessPixels,max(dot(dx,dx),dot(dy,dy))));
  if(_LimpoolReflectionFilter==1)lod=log2(roughnessPixels);
  if(_DebugMode==9)return (lod/8).xxx;
  float3 reflection=SAMPLE_TEXTURE2D_LOD(_ReflectionTexture,sampler_ReflectionTexture,reflectUV,lod).rgb;
  if(_ReflectionRadianceSplit>0){
   // Integrate the high-radiance lobe over a fixed angular footprint. Keep the
   // bounded architectural detail in its narrower lobe; no history/eye lag.
   float highlightPixels=max(roughnessPixels,.5*abs(UNITY_MATRIX_P._m11)*.06*_ReflectionTexture_TexelSize.w);
   reflection+=SAMPLE_TEXTURE2D_LOD(_ReflectionHighlights,sampler_ReflectionHighlights,reflectUV,max(lod,log2(highlightPixels))).rgb;
  }
  if(_DebugMode==10)return SAMPLE_TEXTURE2D_LOD(_ReflectionTexture,sampler_ReflectionTexture,uv,log2(roughnessPixels)).rgb;
  if(_DebugMode==13){float3 sum=SAMPLE_TEXTURE2D_LOD(_ReflectionTexture,sampler_ReflectionTexture,uv,0).rgb+SAMPLE_TEXTURE2D_LOD(_ReflectionHighlights,sampler_ReflectionHighlights,uv,0).rgb;float3 rawReflection=SAMPLE_TEXTURE2D_LOD(_ReflectionRaw,sampler_ReflectionRaw,uv,0).rgb;return max(abs(sum-rawReflection)-(.002+.001*abs(rawReflection)),0)*100;}
  if(_DebugMode==1)return lit.xxx;
  if(_DebugMode==2)return transmission;
  if(_DebugMode==3)return reflection;
  // Micro-normal mip changes must not drive the full grazing-angle mix. Keep
  // the original slopes for ripple distortion and use an attenuated mix normal.
  float3 weightNormal=normalize(float3(slope.x*_FresnelNormalStrength,1,slope.y*_FresnelNormalStrength));
  if(underwater)weightNormal=-weightNormal;
  float fresnel=.025+.975*pow(1-saturate(dot(weightNormal,view)),5);
  // Water-to-air transmission has a critical angle. A finite transition avoids
  // binary micro-normal flicker; this is a filtered approximation, not ray tracing.
  if(underwater)fresnel=lerp(fresnel,1,1-smoothstep(.64,.68,saturate(dot(weightNormal,view))));
  if(_FresnelOverride>=0)fresnel=_FresnelOverride;
  if(_DebugMode==7)return n*.5+.5;
  if(_DebugMode==8)return min(.60,fresnel).xxx;
  float3 color=lerp(transmission,reflection,(underwater?fresnel:min(.60,fresnel))*_ReflectionIntensity);
  if(underwater)return color;
  float3 halfVector=normalize(view+sun.direction);
  float variance=dot(ddx(n),ddx(n))+dot(ddy(n),ddy(n));
  float exponent=lerp(130,32,saturate(variance*40));
  float spec=pow(saturate(dot(n,halfVector)),exponent)*lit;
  if(_DebugMode==6)return (spec*_SpecularStrength).xxx;
  color+=sun.color*spec*_SpecularStrength;
  float3 atmosphere=lerp(float3(.12,.24,.32),reflection,saturate(_ReflectionIntensity*1.25));
  return lerp(color,atmosphere,smoothstep(4,70,length(i.world-_WorldSpaceCameraPos)));
 }
 ENDHLSL
 } }
}
