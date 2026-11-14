using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-500)]
public sealed class SceneRenderSettings:MonoBehaviour {
 public RenderPipelineAsset pipeline;
 RenderPipelineAsset previousDefault,previousQuality;
 void OnEnable(){previousDefault=GraphicsSettings.defaultRenderPipeline;previousQuality=QualitySettings.renderPipeline;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;}
 void OnDisable(){GraphicsSettings.defaultRenderPipeline=previousDefault;QualitySettings.renderPipeline=previousQuality;}
}
