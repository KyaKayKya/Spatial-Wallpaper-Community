using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System;
using System.IO;
namespace SpatialWallpaper.RainyCafe
{
    public sealed class RainyCafeRenderBinding : MonoBehaviour
    {
        public RainyCafeAtmosphere content;
        public AdaptiveRenderCadence cadence;
        public UniversalRenderPipelineAsset pipeline;
        RenderPipelineAsset previousPipeline;
        bool requested;
        [Serializable] sealed class CaptureSettings
        {
            public string quality, pipelineName;
            public int frameRateLimit, vSyncCount, msaa;
            public float renderScale;
            public bool hdr, animation, candleShadows;
            public string rainImplementation;
            public float candleIntensityEncoding;
            public int maxAdditionalLights;
        }
        void OnEnable()
        {
            previousPipeline = QualitySettings.renderPipeline;
            if (pipeline) QualitySettings.renderPipeline = pipeline;
        }
        void Start()
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args,"--output");
            if(Array.IndexOf(args,"--benchmark")<0 || index<0 || index+1>=args.Length)return;
            var settings = new CaptureSettings { quality=QualitySettings.names[QualitySettings.GetQualityLevel()], pipelineName=pipeline.name,
                frameRateLimit=Application.targetFrameRate, vSyncCount=QualitySettings.vSyncCount, msaa=pipeline.msaaSampleCount,
                renderScale=pipeline.renderScale, hdr=pipeline.supportsHDR, animation=content.animate, candleShadows=content.candleShadows,
                rainImplementation=content.pluginWeather && content.pluginWeather.usePluginRain ? "COZY Light Rain adapted particles" : "procedural legacy",
                candleIntensityEncoding=RainyCafeAtmosphere.CandleIntensityEncoding,maxAdditionalLights=pipeline.maxAdditionalLightsCount };
            Directory.CreateDirectory(args[index+1]); File.WriteAllText(Path.Combine(args[index+1],"quality.json"),JsonUtility.ToJson(settings,true));
        }
        void Update()
        {
            bool active = content && content.isActiveAndEnabled && content.NeedsContinuousRendering;
            if (active == requested) return;
            cadence.RequestContinuous(this, active); requested = active;
        }
        void OnDisable()
        {
            if (cadence) cadence.RequestContinuous(this, false);
            if (pipeline && QualitySettings.renderPipeline == pipeline) QualitySettings.renderPipeline = previousPipeline;
            requested = false;
        }
    }
}
