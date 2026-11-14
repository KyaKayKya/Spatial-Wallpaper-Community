using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpatialWallpaper.RainyCafe
{
    public sealed class RainyCafeCapture : MonoBehaviour
    {
        IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--cafe-capture");
            if (index < 0 || index + 1 >= args.Length) yield break;
            string output = Path.GetFullPath(args[index + 1]); Directory.CreateDirectory(output);
            bool video = Array.IndexOf(args, "--cafe-video") >= 0, hold = Array.IndexOf(args, "--cafe-hold") >= 0;
            var projection = GetComponent<WindowProjection>(); var camera = GetComponent<Camera>();
            var driver = GetComponent<PreviewEyeDriver>(); driver.allowInput = false; driver.showHelp = false;
            var content = FindObjectOfType<RainyCafeAtmosphere>(); content.externalClock = true;
            bool lightAudit = Array.IndexOf(args, "--cafe-audit-lights") >= 0;
            if (lightAudit) Shader.SetGlobalFloat("_CafeLightAudit", 1);
            var box = FindObjectOfType<DisplaySceneVolume>();
            GetComponent<AdaptiveRenderCadence>().enabled = false; OnDemandRendering.renderFrameInterval = 1; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
            for (int i = 0; i < 15; i++) yield return new WaitForEndOfFrame();
            var activePipeline = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (!activePipeline || activePipeline.maxAdditionalLightsCount < content.candles.Length)
            { Debug.LogError("Active pipeline cannot shade all cafe candles");Application.Quit(2);yield break; }
            Debug.Log("CAFE_ACTIVE_LIGHT_LIMIT " + activePipeline.name + " " + activePipeline.maxAdditionalLightsCount + " pixel " + QualitySettings.pixelLightCount);
            var eyes = new System.Collections.Generic.List<Vector3>();
            var names = new System.Collections.Generic.List<string>();
            eyes.Add(new Vector3(0, 0, -.6f)); names.Add("front");
            foreach (float x in new[] { -.2f, 0, .2f }) foreach (float y in new[] { -.1f, 0, .1f }) foreach (float z in new[] { -.35f, -.6f, -.95f })
            { eyes.Add(new Vector3(x, y, z)); names.Add($"eye-{eyes.Count - 1:D2}"); }
            var staticMeshes = content.staticSurfaces.Select(r => r.GetComponent<MeshFilter>()).ToArray();
            var rest = staticMeshes.Select(f => f.transform.localToWorldMatrix).ToArray();
            Physics.queriesHitBackfaces = true;
            foreach (var r in content.staticSurfaces)
            {
                if (r.sharedMaterial.HasProperty("_UseCutout") && r.sharedMaterial.GetFloat("_UseCutout") > .5f) continue;
                var col = r.gameObject.AddComponent<MeshCollider>();
                // PhysX fast midphase is unsafe above 2,097,152 triangles (Unity emits this warning).
                col.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices;
                col.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
            }
            Physics.SyncTransforms();
            int misses = 0, rays = 0; float floorError = 0;
            foreach (var eye in eyes)
            {
                projection.SetEye(eye);
                for (int x = 0; x <= 40; x++) for (int y = 0; y <= 24; y++)
                {
                    Vector3 windowPoint = box.FrontCorner((x / 40f - .5f) * .9999f, (y / 24f - .5f) * .9999f);
                    var ray = new Ray(camera.transform.position, windowPoint - camera.transform.position); rays++;
                    float distance = camera.farClipPlane / Mathf.Max(.001f, Vector3.Dot(ray.direction, camera.transform.forward));
                    if (!Physics.Raycast(ray, distance)) misses++;
                }
            }
            foreach (float x in new[] { -.5f, .5f })
            {
                Vector3 corner = box.FrontCorner(x, -.5f);
                if (Physics.Raycast(new Ray(corner + new Vector3(0, .0005f, .00001f), Vector3.down), out var hit, .002f)) floorError = Mathf.Max(floorError, Mathf.Abs(hit.point.y - corner.y));
                else floorError = 1;
            }
            // Validation colliders are not part of the deliverable or performance run.
            foreach (var col in content.GetComponentsInChildren<MeshCollider>()) Destroy(col);
            int count = video ? (hold ? 1350 : 900) : eyes.Count;
            var csv = new StringBuilder("frame,time,eye_x,eye_y,eye_z,corner_error_px,candle0_intensity,candle1_intensity,candle0_x,candle0_y,candle0_z,candle2_intensity,rain_particle_count\n");
            float worst = 0; int moved = 0; Vector3 previousLight = Vector3.zero; float lightTravel = 0;
            for (int i = 0; i < count; i++)
            {
                float t = video ? i / 30f : lightAudit ? 15 : 2;
                Vector3 eye = video ? (hold ? projection.referenceEye : new Vector3(.08f * Mathf.Sin(t * Mathf.PI / 5), .04f * Mathf.Sin(t * Mathf.PI / 7), -.6f + .11f * Mathf.Sin(t * Mathf.PI / 9))) : eyes[i];
                projection.SetEye(eye); content.Evaluate(t);
                if (lightAudit) Shader.SetGlobalVector("_CafeLightPosition",content.candles[2].transform.position);
                yield return null; yield return new WaitForEndOfFrame();
                for (int j = 0; j < staticMeshes.Length; j++) if (rest[j] != staticMeshes[j].transform.localToWorldMatrix) moved++;
                float error = 0;
                foreach (float x in new[] { -.5f, .5f }) foreach (float y in new[] { -.5f, .5f })
                {
                    Vector3 q = camera.WorldToViewportPoint(box.FrontCorner(x, y));
                    error = Mathf.Max(error, new Vector2((q.x - x - .5f) * camera.pixelWidth, (q.y - y - .5f) * camera.pixelHeight).magnitude);
                }
                worst = Mathf.Max(worst, error);
                var light = content.candles[0].transform.localPosition;
                if (i > 0) lightTravel += Vector3.Distance(previousLight, light); previousLight = light;
                csv.AppendLine(FormattableString.Invariant($"{i},{t},{eye.x},{eye.y},{eye.z},{error},{content.candles[0].intensity},{content.candles[1].intensity},{light.x},{light.y},{light.z},{content.candles[2].intensity},{(content.pluginWeather ? content.pluginWeather.ParticleCount : 0)}"));
                var active = RenderTexture.active; RenderTexture.active = null;
                var image = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); image.Apply(); RenderTexture.active = active;
                File.WriteAllBytes(Path.Combine(output, (video ? i.ToString("D4") : names[i]) + ".png"), image.EncodeToPNG()); Destroy(image);
            }
            bool pass = worst < .01f && moved == 0 && misses == 0 && floorError < .00002f;
            File.WriteAllText(Path.Combine(output, "capture.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(output, "geometry.txt"), $"pass={pass}; corner_error_px={worst}; static_transform_changes={moved}; ray_misses={misses}/{rays}; floor_error_m={floorError}; light_travel_authored_m={lightTravel}\n");
            File.WriteAllText(Path.Combine(output, "capture.txt"), $"Standalone Player framebuffer; {Screen.width}x{Screen.height}; {SystemInfo.graphicsDeviceName}; {SystemInfo.graphicsDeviceType}; frames={count}; deterministic 30 fps playback. Recording speed is not runtime performance.\n");
            Application.Quit(pass ? 0 : 2);
        }
    }
}
