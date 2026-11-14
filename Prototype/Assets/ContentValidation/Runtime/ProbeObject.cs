#if CONTENT_PROBE
using UnityEngine;

namespace SpatialWallpaper.ContentValidation
{
    public sealed class ProbeObject : ScriptableObject
    {
        void OnEnable() { ProbeCallback.enabledCount++; }
    }
}

#endif
