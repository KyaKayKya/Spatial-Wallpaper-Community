#if CONTENT_PROBE
using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpatialWallpaper.ContentValidation
{
    public sealed class ContentProbe : MonoBehaviour
    {
        string root;
        readonly JObject report = new JObject();
        readonly JArray cases = new JArray();
        readonly JArray errors = new JArray();

        IEnumerator Start()
        {
            root = Option("--probe-root");
            report["engine"] = Application.unityVersion;
            report["cases"] = cases;
            report["errors"] = errors;
            report["playerSourceScenes"] = SceneManager.sceneCountInBuildSettings;
            Application.logMessageReceived += LogFailure;
            ProbeCallback.Reset();
            string safe = File.ReadAllText(Path.Combine(root, "safe.json"));
            string unsafeJson = safe.Replace("\"kind\":\"cube\"", "\"kind\":\"MonoBehaviour\"");
            int testFailures = ContentProbeChecks.Run().Count;
            testFailures += ContentProbeChecks.CheckBundles(root).Count;
            Add("data-boundary-checks", testFailures == 0, "failures=" + testFailures);

            // The same process first loads a clean scene from the author project.
            var bundle = AssetBundle.LoadFromFile(Path.Combine(root, "bundles", "clean"));
            var paths = bundle.GetAllScenePaths();
            yield return SceneManager.LoadSceneAsync(paths[0], LoadSceneMode.Additive);
            var scene = SceneManager.GetSceneByPath(paths[0]);
            MeshRenderer mesh = null;
            foreach (var sceneRoot in scene.GetRootGameObjects()) if (sceneRoot.GetComponentInChildren<MeshRenderer>()) mesh = sceneRoot.GetComponentInChildren<MeshRenderer>();
            Add("cross-project-scene-bundle", mesh && mesh.sharedMaterial && mesh.lightmapIndex == 0 && LightmapSettings.lightmaps.Length > 0, "lightmaps=" + LightmapSettings.lightmaps.Length + ";renderer=" + (mesh ? mesh.name : "missing") + ";index=" + (mesh ? mesh.lightmapIndex : -1) + ";material=" + (mesh && mesh.sharedMaterial ? mesh.sharedMaterial.name : "missing"));
            yield return SceneManager.UnloadSceneAsync(scene);
            bundle.Unload(true);

            // Observation, not an accepted production loader: GetRootGameObjects
            // only becomes usable after loading, by which time callbacks ran.
            ProbeCallback.Reset();
            bundle = AssetBundle.LoadFromFile(Path.Combine(root, "bundles", "callbacks"));
            int beforeSceneLoad = ProbeCallback.Total;
            paths = bundle.GetAllScenePaths();
            yield return SceneManager.LoadSceneAsync(paths[0], LoadSceneMode.Additive);
            scene = SceneManager.GetSceneByPath(paths[0]);
            var roots = scene.GetRootGameObjects();
            int beforeInspection = ProbeCallback.Total;
            foreach (var sceneRoot in roots) foreach (var clip in sceneRoot.GetComponentsInChildren<Animation>()) clip.Play();
            yield return new WaitForSeconds(.15f);
            cases.Add(new JObject {
                ["name"] = "unsafe-scene-route-observation", ["passed"] = beforeInspection > 0 && ProbeCallback.awake > 0 && ProbeCallback.enabledCount > 0,
                ["beforeSceneLoad"] = beforeSceneLoad, ["beforeInspection"] = beforeInspection,
                ["awake"] = ProbeCallback.awake, ["onEnable"] = ProbeCallback.enabledCount,
                ["animation"] = ProbeCallback.animationEvents, ["onAfterDeserialize"] = ProbeCallback.deserialized,
                ["roots"] = roots.Length
            });
            Add("unsafe-animation-event-observation", ProbeCallback.animationEvents > 0, "callbackEvents=" + ProbeCallback.animationEvents);
            yield return SceneManager.UnloadSceneAsync(scene);
            bundle.Unload(true);

            ProbeCallback.Reset();
            Add("checked-route-rejects-scene-bundle", !ProbeData.TryValidate(File.ReadAllText(Path.Combine(root, "bundles", "callbacks")), out var bundleReason) && ProbeCallback.Total == 0, bundleReason);
            Add("checked-route-rejects-native-resource-bundle", !ProbeData.TryValidate(File.ReadAllText(Path.Combine(root, "bundles", "resources")), out bundleReason) && ProbeCallback.Total == 0, "Type-table inspection alone does not validate native configurations; data admission rejected the native input: " + bundleReason);
            if (!ProbeData.TryValidate(safe, out var reason)) throw new InvalidDataException(reason);
            var document = JObject.Parse(safe);
            var content = new GameObject("DataOwnedRoot");
            foreach (var item in document["nodes"])
            {
                var node = GameObject.CreatePrimitive(PrimitiveType.Cube);
                node.transform.SetParent(content.transform, false);
                node.transform.localPosition = Vector(item["position"]);
                node.transform.localScale = Vector3.one * (float)item["scale"];
                var material = new Material(Resources.Load<Material>("ProbeLit"));
                var color = item["color"];
                material.color = new Color((float)color[0], (float)color[1], (float)color[2], (float)color[3]);
                node.GetComponent<Renderer>().sharedMaterial = material;
                Destroy(node.GetComponent<Collider>());
            }
            Add("checked-data-activation", content.transform.childCount == 1 && ProbeCallback.Total == 0, "untrustedCallbacks=" + ProbeCallback.Total);
            Add("checked-data-rejection", !ProbeData.TryValidate(unsafeJson, out reason) && ProbeCallback.Total == 0, reason);
            yield return ContentProbeCommonChecks.Run(cases, Camera.main, content.GetComponentInChildren<MeshFilter>().sharedMesh, new[] { Resources.Load<Material>("ProbeLit"), Resources.Load<Material>("ProbeClock"), Resources.Load<Material>("ProbeReflection") });
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(root, "player.png"));
            yield return null;
            foreach (var renderer in content.GetComponentsInChildren<Renderer>()) Destroy(renderer.sharedMaterial);
            Destroy(content);
            yield return null;
            Add("checked-data-unload", !content && ProbeCallback.Total == 0, "owned content destroyed");
            report["cameraEnabledByContent"] = false;
            bool passed = true;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--inject-probe-error") >= 0) Debug.LogError("deliberate-probe-error");
            foreach (var row in cases) if (!(bool)row["passed"]) passed = false;
            report["errors"] = errors;
            if (errors.Count > 0) passed = false;
            report["passed"] = passed;
            File.WriteAllText(Path.Combine(root, "player-report.json"), report.ToString());
            Application.Quit(passed ? 0 : 1);
        }

        void Add(string name, bool passed, string detail)
        {
            cases.Add(new JObject { ["name"] = name, ["passed"] = passed, ["detail"] = detail, ["untrustedCallbacks"] = ProbeCallback.Total });
        }

        void LogFailure(string condition, string trace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            errors.Add(type + ": " + condition);
            if (type != LogType.Exception) return;
            report["passed"] = false;
            report["exception"] = condition + "\n" + trace;
            File.WriteAllText(Path.Combine(root, "player-report.json"), report.ToString());
            Application.Quit(1);
        }

        static Vector3 Vector(JToken values) { return new Vector3((float)values[0], (float)values[1], (float)values[2]); }
        static string Option(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, key);
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException(key);
            return args[index + 1];
        }
    }
}

#endif
