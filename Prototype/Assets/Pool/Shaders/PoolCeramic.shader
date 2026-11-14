Shader "SpatialWallpaper/PoolCeramic" {
 Properties {
  _BaseMap("Ceramic",2D)="white"{} _BumpMap("Normal",2D)="bump"{} _Roughness("Roughness",2D)="white"{}
  _WaterLevel("Water height",Float)=-.0856
  _Animation("Animation",Float)=1
  _WorldScale("Metres to display",Float)=.049074
  _DiffuseBake("Cycles diffuse transport",2D)="black"{}
  _Caustics("Focused water light",2D)="black"{}
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
  Pass {
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
   TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
   TEXTURE2D(_Roughness);SAMPLER(sampler_Roughness);
   TEXTURE2D(_DiffuseBake);SAMPLER(sampler_DiffuseBake);
   TEXTURE2D(_Caustics);SAMPLER(sampler_Caustics);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseMap_ST;float _WaterLevel;float _Animation;float _WorldScale;
   CBUFFER_END
   struct A {float4 p:POSITION;float3 n:NORMAL;float4 t:TANGENT;float2 uv:TEXCOORD0;float2 bake:TEXCOORD1;};
   struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float4 tangent:TEXCOORD2;float2 uv:TEXCOORD3;float2 bake:TEXCOORD4;};
   V vert(A a){V v;v.world=TransformObjectToWorld(a.p.xyz);v.p=TransformWorldToHClip(v.world);v.normal=TransformObjectToWorldNormal(a.n);v.tangent=float4(TransformObjectToWorldDir(a.t.xyz),a.t.w*GetOddNegativeScale());v.uv=a.uv;v.bake=a.bake;return v;}
   half4 frag(V v):SV_Target {
    float3 n=normalize(v.normal),t=normalize(v.tangent.xyz),b=cross(n,t)*v.tangent.w;
    float3 nt=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,v.uv),.55);
    n=normalize(nt.x*t+nt.y*b+nt.z*n);
    float scale=max(_WorldScale,.0001);
    float depth=(_WaterLevel-v.world.y)/scale;
    float submerged=smoothstep(-.02,.08,depth);
    float3 baked=SAMPLE_TEXTURE2D(_DiffuseBake,sampler_DiffuseBake,v.bake).rgb;
    baked*=1+.65*smoothstep(.08,.4,dot(baked,float3(.2126,.7152,.0722)));
    float time=_Time.y*_Animation;
    float3 axis=abs(v.normal);float2 uv=(axis.y>.5?v.world.xz:axis.x>.5?v.world.zy:v.world.xy)/scale*.22;
    uv+=.025*float2(sin(uv.y*6+time*.7),sin(uv.x*7-time*.6));
    float a=SAMPLE_TEXTURE2D(_Caustics,sampler_Caustics,uv+time*float2(.018,.013)).b;
    float c=SAMPLE_TEXTURE2D(_Caustics,sampler_Caustics,uv*1.13-time*float2(.011,.017)).b;
    float ripple=a+c+6*a*c;
    float lit=saturate(dot(baked,float3(.2126,.7152,.0722))*1.5);
    float wallBounce=exp(-abs(depth+.45)*1.8)*.06*(1-axis.y);
    // Beer-Lambert absorption darkens long underwater paths without lifting occluded corners.
    float path=max(depth,0)+max(v.world.z/scale,0)*.38;
    float3 transmission=exp(-float3(.85,.25,.21)*path);
    float3 diffuse=lerp(baked,baked*transmission*float3(.30,.70,.65),submerged);
    SurfaceData s=(SurfaceData)0;s.albedo=0;s.alpha=1;s.occlusion=1;s.smoothness=clamp(1-SAMPLE_TEXTURE2D(_Roughness,sampler_Roughness,v.uv).r,.12,.65);s.normalTS=float3(0,0,1);
    s.emission=diffuse+ripple*lit*(submerged*.55*transmission*float3(.12,.65,.53)+wallBounce*float3(1.0,.6,.18));
    InputData input=(InputData)0;input.positionWS=v.world;input.normalWS=n;input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(v.world);input.shadowCoord=TransformWorldToShadowCoord(v.world);
    input.bakedGI=0;input.shadowMask=1;input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(v.p);
    return UniversalFragmentPBR(input,s);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
