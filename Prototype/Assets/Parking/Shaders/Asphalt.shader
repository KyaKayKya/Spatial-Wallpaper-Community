Shader "Parking/Asphalt" {
 Properties { _BaseMap("Fine asphalt",2D)="white"{} _MacroMap("Road variation",2D)="gray"{} _NormalMap("Aggregate",2D)="bump"{} _WearMap("Wear",2D)="gray"{} _RoughMap("Scanned roughness",2D)="gray"{} _Lamp("Main lamp",Float)=1 _TV0("TV0",Vector)=(0,0,0,1) _TV1("TV1",Vector)=(0,0,0,1) _TVVisible("Televisions visible",Float)=1 }
 SubShader {Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
 Pass { Name "ForwardLit" Tags{"LightMode"="UniversalForward"} HLSLPROGRAM
 #pragma target 4.5
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
 #pragma multi_compile _ _FORWARD_PLUS
 #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile _ LIGHTMAP_ON
 #pragma multi_compile _ DIRLIGHTMAP_COMBINED
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 TEXTURE2D(_MacroMap);SAMPLER(sampler_MacroMap);
 TEXTURE2D(_NormalMap);SAMPLER(sampler_NormalMap);
 TEXTURE2D(_WearMap);SAMPLER(sampler_WearMap);
 TEXTURE2D(_RoughMap);SAMPLER(sampler_RoughMap);
 CBUFFER_START(UnityPerMaterial)
 float _Lamp,_TVVisible;float4 _TV0,_TV1;
 CBUFFER_END
 struct A{float4 positionOS:POSITION;float2 lightmapUV:TEXCOORD1;};struct V{float4 pos:SV_POSITION;float3 p:TEXCOORD0;float3 world:TEXCOORD1;float2 lightmapUV:TEXCOORD2;};
 V vert(A a){V o;o.pos=TransformObjectToHClip(a.positionOS.xyz);o.p=a.positionOS.xyz;o.world=TransformObjectToWorld(a.positionOS.xyz);o.lightmapUV=a.lightmapUV*unity_LightmapST.xy+unity_LightmapST.zw;return o;}
 float screenLight(float3 p,float3 eye,float3 n,float4 tv){
  float2 d=p.xz-tv.xz;
  float spill=exp(-dot(d/float2(.65,1.15),d/float2(.65,1.15)))*step(p.z,tv.z+.15)*.075;
  float3 r=reflect(normalize(p-eye),n);
  float t=(tv.z-p.z)/max(r.z,.0001);
  float valid=step(.001,r.z)*step(0,t);
  t=clamp(t,0,10000);
  float3 hit=p+r*t;
  float2 delta=(hit.xy-tv.xy)/float2(.27,.23);
  float reflection=exp(-dot(delta,delta)*1.8)*valid*exp(-t*.16);
  return tv.w*(spill+reflection*.19);
 }
 half4 frag(V i):SV_Target{
  float2 p=i.p.xz;
  float3 a=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,p/2.1).rgb;
  float3 macro=SAMPLE_TEXTURE2D(_MacroMap,sampler_MacroMap,p/30).rgb;
  float3 wear=SAMPLE_TEXTURE2D(_WearMap,sampler_WearMap,(p+float2(13,0))/26).rgb;
  float fine=dot(a,float3(.2126,.7152,.0722));
  float3 albedo=max(.003,lerp(fine.xxx,a,.35)*1.45-.018)*lerp(.8,1.18,saturate((dot(macro,float3(.2126,.7152,.0722))-.06)/.16))*lerp(.65,1.25,wear.r);
  float2 cell=float2(p.x-round(p.x/11)*11,p.y-(10+round((p.y-10)/13)*13));
  float pool=exp(-dot(cell/float2(4.0,4.6),cell/float2(4.0,4.6)));
  float stripeMask=1-smoothstep(.032,.065,abs(p.x-round(p.x/2.75)*2.75));
  float ends=step(.8,abs(cell.y))*step(abs(cell.y),5.2);
  float paint=stripeMask*ends*smoothstep(.018,.10,fine)*smoothstep(.12,.4,wear.g);
  albedo=lerp(albedo,float3(.14,.15,.145)*lerp(.5,1.1,saturate(fine*5)),paint);
  float3 normalMap=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,p/2.1));
  float3 n=normalize(float3(normalMap.x*1.85,normalMap.z,normalMap.y*1.85));
  float3 eye=TransformWorldToObject(GetCameraPositionWS());
  float screen=screenLight(i.p,eye,n,_TV0)+screenLight(i.p,eye,n,_TV1);
  // Small contact occlusion beneath the unique TV cluster; not a moving shadow bake.
  float contact=(1-_TVVisible*.48*exp(-pow(p.x/1.2,4)-pow((p.y-6.2)/.5,4)))*(1-.65*exp(-dot(cell,cell)/.15));
  InputData inputData=(InputData)0;
  inputData.positionWS=i.world;inputData.normalWS=TransformObjectToWorldNormal(n);inputData.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.world);
  inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.pos);inputData.shadowMask=half4(1,1,1,1);
  #if defined(LIGHTMAP_ON)
   inputData.bakedGI=SampleLightmap(i.lightmapUV,inputData.normalWS)*(1-_TVVisible*.48*exp(-pow(p.x/1.2,4)-pow((p.y-6.2)/.5,4)));
  #else
   inputData.bakedGI=SampleSH(inputData.normalWS)*contact;
  #endif
  SurfaceData surface=(SurfaceData)0;
  surface.albedo=albedo;surface.metallic=0;surface.specular=half3(.04,.04,.04);surface.smoothness=1-lerp(.77,.95,SAMPLE_TEXTURE2D(_RoughMap,sampler_RoughMap,p/30).r);surface.normalTS=half3(0,0,1);surface.occlusion=contact;surface.alpha=1;
  float3 color=UniversalFragmentPBR(inputData,surface).rgb;
  // Only distant fixtures use the inexpensive irradiance field; nearby fixtures are real shadowed lights.
  float distant=smoothstep(27,46,p.y);
  color+=albedo*pool*.38*float3(.78,.84,.94)*distant*contact;
  color+=screen*float3(.72,.82,1)*.7;
  float distanceFade=exp(-length(p)*.0035);
  color=lerp(float3(.0015,.0025,.004),color,distanceFade);
  return half4(color,1);
 }
 ENDHLSL }
 }
}
