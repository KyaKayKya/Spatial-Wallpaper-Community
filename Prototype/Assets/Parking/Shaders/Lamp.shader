Shader "Parking/Lamp" {
 Properties { _LampEmission("Lamp emission",Color)=(1.95,2.4,3,1) }
 SubShader {Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
 Pass {HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _LampEmission;
 CBUFFER_END
 struct A{float4 positionOS:POSITION;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V{float4 pos:SV_POSITION;float3 world:TEXCOORD0;};
 V vert(A a){UNITY_SETUP_INSTANCE_ID(a);V o;o.world=TransformObjectToWorld(a.positionOS.xyz);o.pos=TransformWorldToHClip(o.world);return o;}
 half4 frag(V i):SV_Target{return half4(_LampEmission.rgb*exp(-distance(i.world,GetCameraPositionWS())*.42),1);}
 ENDHLSL}
 }
}
