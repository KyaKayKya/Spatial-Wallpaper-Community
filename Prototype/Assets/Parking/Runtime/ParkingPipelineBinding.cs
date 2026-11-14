using UnityEngine;
using UnityEngine.Rendering;
namespace SpatialWallpaper.Parking {
 public sealed class ParkingPipelineBinding:MonoBehaviour {
  public RenderPipelineAsset pipeline;
  public bool useRestoreOverride;
  public RenderPipelineAsset restoreOverride;
  RenderPipelineAsset previous;
  void OnEnable(){if(pipeline){previous=QualitySettings.renderPipeline;QualitySettings.renderPipeline=pipeline;}}
  void OnDisable(){if(QualitySettings.renderPipeline==pipeline)QualitySettings.renderPipeline=useRestoreOverride?restoreOverride:previous;}
 }
}
