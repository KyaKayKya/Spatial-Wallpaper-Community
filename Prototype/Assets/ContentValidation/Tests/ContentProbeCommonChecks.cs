#if CONTENT_PROBE
using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpatialWallpaper.ContentValidation
{
    // Representative fragments only. No scene-name branch or external behavior
    // is admitted by the loader. These tests are not four-scene acceptance.
    public static class ContentProbeCommonChecks
    {
        public static IEnumerator Run(JArray cases, Camera display, Mesh mesh, Material[] resources)
        {
            Material clockSource = null, litSource = null, reflectionSource = null;
            foreach (var material in resources)
            {
                if (material.HasProperty("_JourneyTime")) clockSource = material;
                if (material.HasProperty("_BaseColor")) litSource = material;
                if (material.HasProperty("_ReflectionTex")) reflectionSource = material;
            }
            var clock = new Material(clockSource);
            var geometry = new GameObject("CommonFragment");
            geometry.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = geometry.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = clock;
            geometry.transform.position = new Vector3(0, 0, 2);
            var target = new RenderTexture(64, 64, 24);
            target.Create();
            var oldTarget = display.targetTexture;
            display.targetTexture = target;
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_JourneyTime", 0);
            renderer.SetPropertyBlock(properties);
            display.Render();
            var first = Read(target);
            properties.SetFloat("_JourneyTime", 8);
            renderer.SetPropertyBlock(properties);
            display.Render();
            var second = Read(target);
            int changed = 0;
            for (int i = 0; i < first.Length; i++) if (!first[i].Equals(second[i])) changed++;
            cases.Add(Row("common-material-clock-rendered", changed > 0, "changedPixels=" + changed));
            display.targetTexture = oldTarget;

            var instanceMaterial = new Material(litSource) { enableInstancing = true };
            var matrices = new[] { Matrix4x4.TRS(new Vector3(-1, 0, 2), Quaternion.identity, Vector3.one), Matrix4x4.TRS(new Vector3(1, 0, 2), Quaternion.identity, Vector3.one) };
            renderer.enabled = false;
            display.targetTexture = target;
            yield return new WaitForEndOfFrame();
            var noInstances = Read(target);
            var instanceDraw = geometry.AddComponent<ProbeInstanceDraw>();
            instanceDraw.mesh = mesh;
            instanceDraw.material = instanceMaterial;
            instanceDraw.display = display;
            instanceDraw.matrices = matrices;
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            var withInstances = Read(target);
            UnityEngine.Object.Destroy(instanceDraw);
            int instancePixels = 0;
            for (int i = 0; i < noInstances.Length; i++) if (!noInstances[i].Equals(withInstances[i])) instancePixels++;
            cases.Add(Row("common-instanced-camera-rendered", SystemInfo.supportsInstancing && instancePixels > 0, "explicit display camera; instances=2;changedPixels=" + instancePixels));
            display.targetTexture = oldTarget;
            renderer.enabled = true;

            var ambient = RenderSettings.ambientLight;
            RenderSettings.ambientLight = new Color(.07f, .075f, .085f);
            bool ambientApplied = RenderSettings.ambientLight == new Color(.07f, .075f, .085f);
            RenderSettings.ambientLight = ambient;
            cases.Add(Row("common-ambient-restore", ambientApplied && RenderSettings.ambientLight == ambient, "captured and restored"));

            var reflection = new GameObject("CommonReflectionCamera").AddComponent<Camera>();
            reflection.enabled = false;
            reflection.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var reflectionMaterial = new Material(reflectionSource);
            var previousTexture = Shader.GetGlobalTexture("_ReflectionTexture");
            bool previousCulling = GL.invertCulling, rendering = false;
            int renders = 0;
            Action<ScriptableRenderContext, Camera> render = (context, source) =>
            {
                if (source != display || rendering) return;
                var mirror = Matrix4x4.identity;
                mirror.m11 = -1;
                reflection.CopyFrom(source);
                reflection.enabled = false;
                reflection.cameraType = CameraType.Reflection;
                reflection.targetTexture = target;
                reflection.worldToCameraMatrix = source.worldToCameraMatrix * mirror;
                reflection.projectionMatrix = source.projectionMatrix;
                bool saved = GL.invertCulling;
                rendering = true;
                try
                {
                    GL.invertCulling = !saved;
                    UniversalRenderPipeline.RenderSingleCamera(context, reflection);
                    reflectionMaterial.SetTexture("_ReflectionTex", target);
                    Shader.SetGlobalTexture("_ReflectionTexture", target);
                    renders++;
                }
                finally { GL.invertCulling = saved; rendering = false; }
            };
            RenderPipelineManager.beginCameraRendering += render;
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            RenderPipelineManager.beginCameraRendering -= render;
            int stopped = renders;
            Shader.SetGlobalTexture("_ReflectionTexture", previousTexture);
            UnityEngine.Object.Destroy(reflection.gameObject);
            UnityEngine.Object.Destroy(reflectionMaterial);
            UnityEngine.Object.Destroy(instanceMaterial);
            UnityEngine.Object.Destroy(geometry);
            UnityEngine.Object.Destroy(clock);
            target.Release();
            UnityEngine.Object.Destroy(target);
            yield return new WaitForEndOfFrame();
            cases.Add(Row("common-reflection-render-and-release", stopped > 0 && renders == stopped && GL.invertCulling == previousCulling && Shader.GetGlobalTexture("_ReflectionTexture") == previousTexture && !reflection, "rendered=" + stopped + "; explicit camera; texture/culling/subscription restored"));
        }

        static JObject Row(string name, bool passed, string detail) { return new JObject { ["name"] = name, ["passed"] = passed, ["detail"] = detail }; }
        static Color32[] Read(RenderTexture target)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            var pixels = texture.GetPixels32();
            UnityEngine.Object.Destroy(texture);
            RenderTexture.active = previous;
            return pixels;
        }
    }
}

#endif
