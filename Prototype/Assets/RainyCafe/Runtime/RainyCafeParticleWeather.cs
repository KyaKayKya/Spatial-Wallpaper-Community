using UnityEngine;

namespace SpatialWallpaper.RainyCafe
{
    // The particle prefabs accept the same content clock as candles and steam.
    public sealed class RainyCafeParticleWeather : MonoBehaviour
    {
        public ParticleSystem[] layers;
        public Light streetLight;
        public bool usePluginRain = true;
        float previous = -1;
        int simulatedSteps;
        bool visible;
        public int ParticleCount
        {
            get { int count=0;foreach(var layer in layers) count+=layer.particleCount;return count; }
        }

        public void Evaluate(float time, bool enabled)
        {
            bool show = enabled && usePluginRain;
            if (streetLight)
            {
                streetLight.enabled=show;
                float scale=transform.lossyScale.x/(.53f/5.4f);
                streetLight.range=3.2f*transform.lossyScale.x;
                streetLight.intensity=.006f*RainyCafeAtmosphere.CandleIntensityEncoding*scale*scale;
            }
            if (show != visible)
            {
                foreach (var layer in layers)
                    foreach (var renderer in layer.GetComponentsInChildren<ParticleSystemRenderer>()) renderer.enabled = show;
                visible = show;
            }
            if (!show) { previous = -1; return; }
            int target=Mathf.Max(0,Mathf.FloorToInt((time+2)*60+.001f));
            // A discontinuous external clock or a long suspended frame must not
            // replay hours of rain on the main thread. Rewarm a bounded window.
            if(previous<0 || time<previous || target-simulatedSteps>240)
            {
                foreach(var layer in layers) layer.Simulate(0,true,true,false);
                simulatedSteps=Mathf.Max(0,target-240);
            }
            for(;simulatedSteps<target;simulatedSteps++)
                foreach(var layer in layers) layer.Simulate(1f/60,true,false,false);
            foreach(var layer in layers) layer.Pause(true);
            previous = time;
        }
    }
}
