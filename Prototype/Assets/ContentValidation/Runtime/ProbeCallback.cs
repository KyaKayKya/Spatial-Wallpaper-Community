#if CONTENT_PROBE
using System.Threading;
using UnityEngine;

namespace SpatialWallpaper.ContentValidation
{
    // Harmless instrumentation compiled into the test Player. This demonstrates
    // how an already installed type can execute from untrusted serialized data.
    public sealed class ProbeCallback : MonoBehaviour, ISerializationCallbackReceiver
    {
        public static int awake, enabledCount, animationEvents, deserialized;
        public string configuredReference;
        public void Awake() { Interlocked.Increment(ref awake); }
        public void OnEnable() { Interlocked.Increment(ref enabledCount); }
        [UnityEngine.Scripting.Preserve]
        public void AnimationCallback() { Interlocked.Increment(ref animationEvents); }
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { Interlocked.Increment(ref deserialized); }
        public static void Reset() { awake = enabledCount = animationEvents = deserialized = 0; }
        public static int Total { get { return awake + enabledCount + animationEvents + deserialized; } }
    }
}

#endif
