"""Prepare only Parking migration inputs; never alter the original scene assets."""
from pathlib import Path
import hashlib,json,math
from PIL import Image

SURFACE=r'''Shader "ParkingSdk/Surface" {
 Properties { _BaseMap("Albedo",2D)="white"{} _Tint("Tint",Color)=(1,1,1,1) _Textured("Textured",Float)=0 }
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Pass { Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _Tint;float _Textured;
 CBUFFER_END
 struct A{float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0;};struct V{float4 p:SV_POSITION;float3 n:TEXCOORD0;float2 uv:TEXCOORD1;float3 world:TEXCOORD2;};
 V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.n=TransformObjectToWorldNormal(a.n);o.uv=a.uv;o.world=TransformObjectToWorld(a.p.xyz);return o;}
 half4 frag(V i):SV_Target{float3 base=lerp(1,SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb,_Textured)*_Tint.rgb;float light=.18+.5*saturate(dot(normalize(i.n),normalize(float3(-.3,.8,-.4))));float fade=exp(-distance(i.world,GetCameraPositionWS())*.14);return half4(lerp(float3(.0015,.0025,.004),base*light,fade),1);}
 ENDHLSL } }
}'''

ASPHALT=r'''Shader "ParkingSdk/Asphalt" {
 Properties { _BaseMap("Asphalt",2D)="white"{} _MacroMap("Variation",2D)="gray"{} _WearMap("Wear",2D)="gray"{} }
 SubShader {Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Pass {Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);TEXTURE2D(_MacroMap);SAMPLER(sampler_MacroMap);TEXTURE2D(_WearMap);SAMPLER(sampler_WearMap);
 struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float2 ground:TEXCOORD0;};
 V vert(A a){V o;o.p=TransformObjectToHClip(a.p.xyz);o.ground=a.p.xz*(12/.53);return o;}
 half4 frag(V i):SV_Target{
  float2 p=i.ground;float3 fine=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,p/2.1).rgb;float macro=SAMPLE_TEXTURE2D(_MacroMap,sampler_MacroMap,p/30).r;float3 wear=SAMPLE_TEXTURE2D(_WearMap,sampler_WearMap,(p+float2(13,0))/26).rgb;
  float3 albedo=max(.003,fine*1.45-.018)*lerp(.8,1.18,saturate((macro-.06)/.16))*lerp(.65,1.25,wear.r);
  float2 cell=float2(p.x-round(p.x/11)*11,p.y-(10+round((p.y-10)/13)*13));float pool=exp(-dot(cell/float2(4,4.6),cell/float2(4,4.6)));
  float stripe=(1-smoothstep(.032,.065,abs(p.x-round(p.x/2.75)*2.75)))*step(.8,abs(cell.y))*step(abs(cell.y),5.2)*smoothstep(.12,.4,wear.g);
  albedo=lerp(albedo,float3(.14,.15,.145),stripe);
  float t=_Time.y;float tv0=.7+.3*sin(t*2.3);float tv1=.65+.35*sin(t*1.7+2);float screen=tv0*exp(-dot((p-float2(-.33,5.9))/float2(1,1.8),(p-float2(-.33,5.9))/float2(1,1.8)))+tv1*exp(-dot((p-float2(-.59,6.05))/float2(1,1.8),(p-float2(-.59,6.05))/float2(1,1.8)));
  float contact=(1-.45*exp(-pow(p.x/1.2,4)-pow((p.y-6.2)/.5,4)))*(1-.6*exp(-dot(cell,cell)/.15));
  float3 color=albedo*(.13+pool*.75)*float3(.78,.84,.94)*contact+screen*float3(.035,.045,.065);color=lerp(float3(.0015,.0025,.004),color,exp(-length(p)*.0035));return half4(color,1);
 }
 ENDHLSL } }
}'''

def prepare(source,common,ROOT):
 source=Path(source);common=Path(common);ROOT=Path(ROOT);original=common/'Prototype/Assets/Parking';generated=original/'Generated';source.mkdir(parents=True,exist_ok=True)
 raw=(generated/'scene.json').read_bytes();data=json.loads(raw);data['meshes']=[m for m in data['meshes'] if m['name'].startswith('TV_') or m['name']=='LampLOD']
 # Extend the original 11 x 13 metre grid to cover the declared ultra-wide,
 # near-eye views. The original finite grid is too narrow in those views.
 data['lamps']=[{'position':[x*11,0,z]} for z in range(10,360,13) for x in range(-math.ceil((16+2.3*z)/11),math.ceil((16+2.3*z)/11)+1)]
 used={s['material'] for m in data['meshes'] for s in m['submeshes']};data['materials']=[m for m in data['materials'] if m['name'] in used]
 textures={'clean_asphalt_diff_4k_jpg.png':256,'aerial_asphalt_01_macro_blurred_4k_png.png':128,'Asphalt_Weathering_26m_RGB_png.png':128}
 for m in data['materials']:
  m['textures']=[t for t in m['textures'] if 'Albedo' in t['file'] or '_diff' in t['file']]
  for t in m['textures']:textures[t['file']]=256
 sources={}
 for name,size in textures.items():
  path=generated/name;sources[name]=hashlib.sha256(path.read_bytes()).hexdigest()
  with Image.open(path) as image:
   image=image.convert('RGB');image.thumbnail((size,size),Image.Resampling.LANCZOS);image.save(source/name)
 (source/'scene.json').write_text(json.dumps(data,separators=(',',':')),encoding='utf-8')
 shaders=source.parent.parent/'Parking/Shaders';shaders.mkdir(parents=True,exist_ok=True)
 (shaders/'Surface.shader').write_text(SURFACE,encoding='utf-8');(shaders/'Asphalt.shader').write_text(ASPHALT,encoding='utf-8')
 crt=(ROOT/'Prototype/Assets/Parking/Shaders/CRT.shader').read_text(encoding='utf-8');crt=crt.replace('Parking/CRT','ParkingSdk/CRT').replace('_NoiseTime("Noise time",Float)=0 ','').replace('float _Brightness,_NoiseTime,_Seed;','float _Brightness,_Seed;').replace('_NoiseTime','_Time.y')
 (shaders/'CRT.shader').write_text(crt,encoding='utf-8')
 lamp=(ROOT/'Prototype/Assets/Parking/Shaders/Lamp.shader').read_text(encoding='utf-8');lamp=lamp.replace('Parking/Lamp','ParkingSdk/Lamp').replace('#pragma multi_compile_instancing','').replace('UNITY_VERTEX_INPUT_INSTANCE_ID','').replace('UNITY_SETUP_INSTANCE_ID(a);','').replace('Pass {HLSLPROGRAM','Pass {Cull Off HLSLPROGRAM')
 (shaders/'Lamp.shader').write_text(lamp,encoding='utf-8')
 (source/'migration-source.json').write_text(json.dumps({'scene_sha256':hashlib.sha256(raw).hexdigest(),'textures':sources,'lamps':len(data['lamps']),'floor_y':-.1490625,'depth':360*.53/12,'simplification':'four shared original LampLOD fixtures; remaining field merged pole/head quads; analytic light pools and CRT spill; no native lights or postprocessing'},indent=2),encoding='utf-8')
