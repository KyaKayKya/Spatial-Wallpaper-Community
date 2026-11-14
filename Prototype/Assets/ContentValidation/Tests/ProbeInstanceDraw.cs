#if CONTENT_PROBE
using UnityEngine;
using UnityEngine.Rendering;

namespace SpatialWallpaper.ContentValidation
{
    public sealed class ProbeInstanceDraw : MonoBehaviour
    {
        public Mesh mesh;
        public Material material;
        public Camera display;
        public Matrix4x4[] matrices;
        void LateUpdate()
        {
            Graphics.DrawMeshInstanced(mesh, 0, material, matrices, matrices.Length, null, ShadowCastingMode.Off, false, 0, display, LightProbeUsage.Off);
        }
    }
}

#endif
