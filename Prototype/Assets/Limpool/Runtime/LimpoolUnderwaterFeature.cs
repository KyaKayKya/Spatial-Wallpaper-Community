using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class LimpoolUnderwaterFeature:ScriptableRendererFeature {
 public Material material;
 WaterPass pass;
 public override void Create(){pass?.Dispose();pass=new WaterPass(material);}
 public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data){
  var camera=data.cameraData.camera;
  if(camera.cameraType!=CameraType.Game||!camera.TryGetComponent<LimpoolUnderwater>(out var medium)||!medium.enabled||!medium.water||!medium.screenFrame||!material)return;
  // Match the water shader's coplanar precision band, including its upper side.
  if(camera.transform.position.y>medium.water.position.y+.000001f)return;
  pass.medium=medium;renderer.EnqueuePass(pass);
 }
 protected override void Dispose(bool disposing){pass?.Dispose();}
 sealed class WaterPass:ScriptableRenderPass {
  readonly Material effect;
  RTHandle copy;
  public LimpoolUnderwater medium;
  public WaterPass(Material source){
   if(source)effect=new Material(source);
   renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing;
   // LimpoolURP already requires opaque depth. A late ConfigureInput(Depth)
   // moves URP 14's depth copy after transparents, leaving water/volumes reading
   // the previous eye's depth earlier in the frame.
   ConfigureInput(ScriptableRenderPassInput.Color);
  }
  public override void OnCameraSetup(CommandBuffer cmd,ref RenderingData data){
   var desc=data.cameraData.cameraTargetDescriptor;desc.depthBufferBits=0;desc.msaaSamples=1;
   RenderingUtils.ReAllocateIfNeeded(ref copy,desc,FilterMode.Bilinear,TextureWrapMode.Clamp,name:"LimpoolUnderwaterColor");
  }
  public override void Execute(ScriptableRenderContext context,ref RenderingData data){
   effect.SetMatrix("_WorldToPoolScreen",medium.screenFrame.worldToLocalMatrix);
   effect.SetFloat("_PoolWaterHeight",medium.screenFrame.InverseTransformPoint(medium.water.position).y);
   effect.SetFloat("_PoolMetreScale",medium.water.lossyScale.x);
   var source=data.cameraData.renderer.cameraColorTargetHandle;
   var cmd=CommandBufferPool.Get("Limpool underwater absorption");
   try{
    Blitter.BlitCameraTexture(cmd,source,copy,effect,0);
    Blitter.BlitCameraTexture(cmd,copy,source);
    context.ExecuteCommandBuffer(cmd);
   }finally{CommandBufferPool.Release(cmd);}
  }
  public void Dispose(){copy?.Release();CoreUtils.Destroy(effect);}
 }
}
