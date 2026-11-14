using System;
using UnityEngine;

namespace SpatialWallpaper.RainyCafe
{
    // Content accepts a clock and switches. It never reads eye tracking or changes the display.
    public sealed class RainyCafeAtmosphere : MonoBehaviour
    {
        public bool animate = true, rainEnabled = true, glassEnabled = true, steamEnabled = true;
        public bool candleEnabled = true, candleShadows = true, staticLighting = true, externalClock;
        public Renderer[] rain, glass, steam, flames, staticSurfaces;
        public Light[] candles;
        public Transform[] flameRoots;
        public RainyCafeParticleWeather pluginWeather;
        public static float CandleRange(int index) => index == 0 ? 1.85f : index == 1 ? 1.4f : .95f;
        // Miniature point lights otherwise cross Unity's low-intensity culling threshold.
        // Preserve linear radiance while keeping the native intensity safely above it.
        public const float CandleIntensityEncoding = 64;
        Vector3[] anchors;
        MaterialPropertyBlock properties;
        float localTime;
        public bool NeedsContinuousRendering => animate && (rainEnabled || glassEnabled || steamEnabled || candleEnabled);

        void Awake()
        {
            properties = new MaterialPropertyBlock();
            anchors = new Vector3[candles.Length];
            for (int i = 0; i < candles.Length; i++) anchors[i] = candles[i].transform.localPosition;
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--cafe-static") >= 0)
            {
                animate = false; rainEnabled = false; glassEnabled = false; steamEnabled = false; candleEnabled = false;
            }
            if (Array.IndexOf(args, "--cafe-no-shadows") >= 0) candleShadows = false;
            if (Array.IndexOf(args, "--cafe-legacy-rain") >= 0 && pluginWeather) pluginWeather.usePluginRain=false;
        }

        void Update()
        {
            // Additive loads do not reset Time.timeSinceLevelLoad. A newly loaded
            // cafe starts its own clock, independent of the manager's lifetime.
            if (!externalClock) { localTime += Time.deltaTime; Evaluate(localTime); }
        }

        public void Evaluate(float time)
        {
            float t = animate ? time : 0;
            float displayScale = transform.lossyScale.x / (.53f / 5.4f);
            bool pluginRain = pluginWeather && pluginWeather.usePluginRain;
            SetWeather(rain, rainEnabled && !pluginRain, t);
            if (pluginWeather) pluginWeather.Evaluate(t,rainEnabled);
            SetWeather(glass, glassEnabled, t);
            SetWeather(steam, steamEnabled, t);
            SetWeather(flames, candleEnabled, t);
            foreach (var r in staticSurfaces)
            {
                properties.Clear(); properties.SetFloat("_StaticStrength", staticLighting ? 1 : 0);
                r.SetPropertyBlock(properties);
            }
            for (int i = 0; i < candles.Length; i++)
            {
                float phase = i * 19.731f;
                float slow = Mathf.PerlinNoise(t * .71f, phase + 2) - .5f;
                float fine = Mathf.PerlinNoise(t * 2.1f, phase + 8) - .5f;
                float sway = Mathf.PerlinNoise(t * .9f, phase + 21) - .5f;
                Vector3 offset = new Vector3(slow * .009f, fine * .003f, sway * .007f);
                Light light = candles[i]; light.enabled = candleEnabled;
                light.shadows = candleShadows && i < 3 ? LightShadows.Soft : LightShadows.None;
                light.transform.localPosition = anchors[i] + offset;
                float energy = 1 + slow * .24f + fine * .08f;
                light.range = CandleRange(i) * transform.lossyScale.x;
                light.shadowNearPlane = .001f * displayScale;
                Color tint = new Color(1, .52f, .21f);
                light.color = QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? (tint.linear / CandleIntensityEncoding).gamma : tint / CandleIntensityEncoding;
                light.intensity = (i == 0 ? .0017f : i == 2 ? .0010f : .0014f) * energy * displayScale * displayScale * CandleIntensityEncoding;
                flameRoots[i].localPosition = anchors[i] + offset;
                flameRoots[i].localScale = new Vector3(1 - slow * .2f, 1 + slow * .22f + fine * .08f, 1);
                properties.Clear(); properties.SetColor("_Color", new Color(1.5f * energy, .78f * energy, .24f * energy, .95f));
                flames[i].SetPropertyBlock(properties);
            }
        }

        void SetWeather(Renderer[] renderers, bool visible, float time)
        {
            foreach (var r in renderers)
            {
                r.enabled = visible;
                properties.Clear(); properties.SetFloat("_Clock", time); r.SetPropertyBlock(properties);
            }
        }
    }
}
