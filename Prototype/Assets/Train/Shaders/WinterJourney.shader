Shader "Train/WinterJourney" {
 Properties { _JourneyTime("Journey time",Float)=0 _Layer("Layer",Float)=0 _Panorama("Snow panorama",2D)="white"{} _Trees("Snow tree cutouts",2D)="white"{} _RideAmplitude("Exterior ride amplitude",Float)=2.6 _ExteriorExposure("Overcast exterior exposure",Float)=.38 }
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
 Pass { Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
 struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
 CBUFFER_START(UnityPerMaterial)
 float _JourneyTime,_Layer,_RideAmplitude,_ExteriorExposure;
 CBUFFER_END
 TEXTURE2D(_Panorama);SAMPLER(sampler_Panorama);
 TEXTURE2D(_Trees);SAMPLER(sampler_Trees);
 V vert(A v){V o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;return o;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
 float bounce(float t){return _RideAmplitude*(.0038*sin(t*2.7)+.0013*sin(t*6.1)+.00045*sin(t*12.3));}
 // Independent rows carry tracked-eye parallax and travelling parallax.
 half4 forest(float2 uv,float t,float density,float speed,float depth){
  float2 p=float2((uv.x-t*speed)*density,uv.y+bounce(t));
  float index=floor(p.x),seed=hash(float2(index,depth));
  float height=depth>.5?.40:(depth>.1?.85:1.8);
  height*=.87+seed*.23;
  float base=depth>.5?.28:(depth>.1?.03:-.65);
  float2 texUV=float2(frac(p.x),(p.y-base)/height);
  if(seed>.5)texUV.x=1-texUV.x;
  float4 tree=SAMPLE_TEXTURE2D(_Trees,sampler_Trees,texUV);
  float alpha=smoothstep(.12,.85,tree.a)*step(0,texUV.y)*step(texUV.y,1);
  // Keep nearby groups sparse so the snowy distance remains visible.
  if(depth<.1)alpha*=step(.48,seed)*.80;
  float3 col=lerp(tree.rgb*float3(.70,.84,.94),float3(.62,.73,.80),.12+depth*.72);
  return half4(col*_ExteriorExposure,alpha);
 }
 half4 frag(V i):SV_Target{
  float2 uv=i.uv;float t=_JourneyTime;
  if(_Layer<.5){
   float y=uv.y+bounce(t),horizon=.43+.025*sin((uv.x-t*.008)*11);
   float3 sky=lerp(float3(.69,.79,.85),float3(.38,.52,.63),saturate(y));
   float3 ground=lerp(float3(.59,.70,.77),float3(.77,.84,.87),saturate(y*2));
   float3 col=lerp(ground,sky,smoothstep(horizon-.025,horizon+.025,y));
   // The panorama contributes only faint distant texture under atmospheric fog.
   float3 distant=SAMPLE_TEXTURE2D(_Panorama,sampler_Panorama,float2(uv.x*.8-t*.008,(y-horizon)*1.5+.5)).rgb;
   col=lerp(col,distant*float3(.80,.91,1),.13*exp(-abs(y-horizon)*15));
   col+=noise(float2(uv.x*8-t*.025,y*10))*.025;
   return half4(col*_ExteriorExposure,1);
  }
  if(_Layer<1.5)return forest(uv,t,2.4,.035,.85);
  if(_Layer<2.5)return forest(uv,t,1.1,.24,.25);
  if(_Layer<3.5)return forest(uv,t,.50,1.15,0);
  if(_Layer<4.5){
   uv.y+=bounce(t);
   float x=-.3+fmod(t+2,5.5)*3.6;
   float pole=(1-smoothstep(.004,.009,abs(uv.x-x)))*step(.12,uv.y)*step(uv.y,.93);
   float arm=(1-smoothstep(.004,.009,abs(uv.y-.82)))*step(abs(uv.x-x),.09);
   return half4(float3(.12,.17,.20)*_ExteriorExposure,max(pole,arm)*.85);
  }
  float snow=0;
  [unroll]for(int j=0;j<3;j++){
   float scale=12+j*11;float2 p=uv*float2(scale,scale*.8)-float2(t*(21+j*23),-t*(3.5+j));
   float2 cell=floor(p);float2 local=frac(p)-float2(hash(cell),hash(cell+2));
   float streak=exp(-abs(local.y+local.x*.20)*(45+j*12)-abs(local.x)*5);
   snow+=streak*(.55+j*.22);
  }
  float edge=pow(saturate(abs(uv.x-.5)*2),8)+pow(saturate(abs(uv.y-.5)*2),12);
  float frost=saturate(edge*.34)*(.7+.3*hash(floor(uv*170)));
  // Condensation stays attached to the glass, unlike the passing snow.
  float mist=(.05+.09*saturate(edge))*(.65+.35*sin(uv.x*19+sin(uv.y*14)));
  float2 drops=uv*float2(73,31);float2 cell=floor(drops);
  float2 q=frac(drops)-float2(.25+.5*hash(cell),.25+.5*hash(cell+7));
  float droplets=exp(-dot(q*float2(18,10),q*float2(18,10)))*step(.78,hash(cell+19))*.18;
  return half4(float3(.82,.91,.96)*min(1,_ExteriorExposure*1.25),saturate(snow*1.8+frost+mist+droplets));
 }
 ENDHLSL
 } }
}
