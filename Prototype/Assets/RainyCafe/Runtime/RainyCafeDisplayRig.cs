using System;
using System.IO;
using UnityEngine;

namespace SpatialWallpaper.RainyCafe
{
    // Preserve the authored vertical span; crop or reveal the near side returns horizontally.
    [DefaultExecutionOrder(-300)]
    public sealed class RainyCafeDisplayRig : MonoBehaviour
    {
        public WindowProjection projection;
        public Transform sceneModel;
        public DisplaySceneVolume volume;
        bool automaticAspect;
        int previousWidth, previousHeight;
        void Awake()
        {
            var profile = new PhysicalDisplayRig.Profile();
            profile.heightMetres = profile.widthMetres * Screen.height / Mathf.Max(1f, Screen.width);
            string path = Path.Combine(Application.streamingAssetsPath, "display-profile.json");
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--display-profile");
            if (index >= 0 && index + 1 < args.Length) path = args[index + 1];
            if (File.Exists(path))
            {
                try { profile = JsonUtility.FromJson<PhysicalDisplayRig.Profile>(File.ReadAllText(path)); }
                catch (Exception e) { Debug.LogWarning("Display profile: " + e.Message); }
            }
            if (profile == null) profile = new PhysicalDisplayRig.Profile();
            automaticAspect = index < 0 && !profile.measured;
            if (automaticAspect) profile.heightMetres = profile.widthMetres * Screen.height / Mathf.Max(1f, Screen.width);
            previousWidth = Screen.width; previousHeight = Screen.height;
            if (!Configure(profile)) Debug.LogWarning("Invalid RainyCafe display profile; authored window retained.");
        }
        void Update()
        {
            if (!automaticAspect || (previousWidth == Screen.width && previousHeight == Screen.height)) return;
            previousWidth = Screen.width; previousHeight = Screen.height;
            Configure(new PhysicalDisplayRig.Profile { widthMetres = projection.displayWidth,
                heightMetres = projection.displayWidth * Screen.height / Mathf.Max(1f, Screen.width), referenceEyeMetres = projection.referenceEye });
        }
        public bool Configure(PhysicalDisplayRig.Profile p)
        {
            if (p == null || !projection || !sceneModel || !volume ||
                float.IsNaN(p.widthMetres) || float.IsNaN(p.heightMetres) ||
                p.widthMetres < .05f || p.widthMetres > 2 || p.heightMetres < .05f || p.heightMetres > 2) return false;
            float oldWidth = projection.displayWidth, oldHeight = projection.displayHeight;
            projection.displayWidth = p.widthMetres; projection.displayHeight = p.heightMetres;
            if (!projection.SetEye(p.referenceEyeMetres))
            { projection.displayWidth = oldWidth; projection.displayHeight = oldHeight; return false; }
            projection.referenceEye = p.referenceEyeMetres;
            float scale = p.heightMetres / .298125f;
            sceneModel.localScale = Vector3.one * scale;
            sceneModel.localPosition = Vector3.zero;
            volume.size = new Vector3(p.widthMetres, p.heightMetres, 33 * (.53f / 5.4f) * scale);
            projection.contentDepth = 34 * (.53f / 5.4f) * scale;
            projection.SetEye(p.referenceEyeMetres);
            return true;
        }
    }
}
