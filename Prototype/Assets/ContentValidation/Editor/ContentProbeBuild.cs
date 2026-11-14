#if CONTENT_PROBE
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SpatialWallpaper.ContentValidation
{
    public static class ContentProbeBuild
    {
        // Invoked in two generated clean projects. Neither contains author art,
        // tracking, Evo or a copy of the unmerged five-parameter implementation.
        public static void Run()
        {
            string output = Environment.GetEnvironmentVariable("CONTENT_PROBE_OUTPUT");
            string role = Environment.GetEnvironmentVariable("CONTENT_PROBE_ROLE");
            Directory.CreateDirectory("Assets/Generated");
            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, "Assets/Generated/ProbeRenderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipeline, "Assets/Generated/ProbePipeline.asset");
            UnityEngine.Rendering.GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            QualitySettings.vSyncCount = 0;
            if (role == "author") BuildAuthor(output);
            else BuildPlayer(output);
        }

        static void BuildAuthor(string output)
        {
            var builds = new AssetBundleBuild[4];
            string lightmapPath = null;
            for (int i = 0; i < 2; i++)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                string name = i == 0 ? "clean" : "callbacks";
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "AuthoredCube";
                cube.transform.position = new Vector3(0, 0, 2);
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.color = Color.cyan;
                AssetDatabase.CreateAsset(material, "Assets/Generated/" + name + ".mat");
                var renderer = cube.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                GameObjectUtility.SetStaticEditorFlags(cube, StaticEditorFlags.ContributeGI);
                var sun = new GameObject("BakedLight").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.lightmapBakeType = LightmapBakeType.Baked;
                sun.transform.rotation = Quaternion.Euler(45, 30, 0);
                if (i != 0)
                {
                    var nested = new GameObject("NestedConfiguredCallback");
                    nested.transform.SetParent(cube.transform);
                    var callback = nested.AddComponent<ProbeCallback>();
                    callback.configuredReference = "controlled-harmless-probe";
                    var clip = new AnimationClip { legacy = true };
                    clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0, .1f, 0));
                    AssetDatabase.CreateAsset(clip, "Assets/Generated/ProbeAnimation.anim");
                    AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent { time = .02f, functionName = "AnimationCallback" } });
                    EditorUtility.SetDirty(clip);
                    AssetDatabase.SaveAssets();
                    if (AnimationUtility.GetAnimationEvents(clip).Length != 1) throw new InvalidOperationException("Probe animation event was not saved");
                    var animation = nested.AddComponent<Animation>();
                    animation.AddClip(clip, "ProbeAnimation");
                    animation.clip = clip;
                    animation.playAutomatically = true;
                    animation.cullingType = AnimationCullingType.AlwaysAnimate;
                }
                string path = "Assets/Generated/" + name + ".unity";
                EditorSceneManager.SaveScene(scene, path);
                var settings = new LightingSettings {
                    bakedGI = true, realtimeGI = false,
                    lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
                    lightmapResolution = 4, lightmapMaxSize = 128
                };
                AssetDatabase.CreateAsset(settings, "Assets/Generated/" + name + "-lighting.asset");
                Lightmapping.lightingSettings = settings;
                if (!Lightmapping.Bake()) throw new InvalidOperationException("Probe lightmap bake failed");
                if (i == 0) lightmapPath = AssetDatabase.GetAssetPath(LightmapSettings.lightmaps[0].lightmapColor);
                EditorSceneManager.SaveScene(scene, path);
                builds[i] = new AssetBundleBuild { assetBundleName = name, assetNames = new[] { path } };
            }
            var sourceCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var resourceMesh = UnityEngine.Object.Instantiate(sourceCube.GetComponent<MeshFilter>().sharedMesh);
            AssetDatabase.CreateAsset(resourceMesh, "Assets/Generated/ResourceMesh.asset");
            var behavior = ScriptableObject.CreateInstance<ProbeObject>();
            AssetDatabase.CreateAsset(behavior, "Assets/Generated/HiddenObject.asset");
            var clock = new Material(Shader.Find("Train/WinterJourney"));
            AssetDatabase.CreateAsset(clock, "Assets/Generated/Clock.mat");
            var reflection = new Material(Shader.Find("SpatialWallpaper/PoolReflection"));
            AssetDatabase.CreateAsset(reflection, "Assets/Generated/Reflection.mat");
            AssetDatabase.CreateAsset(new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/clean.mat")), "Assets/Generated/ResourceLit.mat");
            AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<Texture2D>(lightmapPath)), "Assets/Generated/ResourceLightmap.asset");
            builds[2] = new AssetBundleBuild { assetBundleName = "resources", assetNames = new[] { "Assets/Generated/ResourceMesh.asset", "Assets/Generated/ResourceLit.mat", "Assets/Generated/Clock.mat", "Assets/Generated/Reflection.mat", "Assets/Generated/ResourceLightmap.asset" } };
            builds[3] = new AssetBundleBuild { assetBundleName = "hidden-resource", assetNames = new[] { "Assets/Generated/HiddenObject.asset" } };
            Directory.CreateDirectory(Path.Combine(output, "bundles"));
            var manifest = BuildPipeline.BuildAssetBundles(Path.Combine(output, "bundles"), builds, BuildAssetBundleOptions.UncompressedAssetBundle, BuildTarget.StandaloneWindows64);
            if (!manifest) throw new InvalidOperationException("Bundle build failed");
            File.WriteAllText(Path.Combine(output, "safe.json"), "{\"engine\":\"2022.3.62f3\",\"pipeline\":\"14.0.12\",\"nodes\":[{\"kind\":\"cube\",\"position\":[0,0,2],\"scale\":1,\"color\":[1,0.5,0,1]}]}");
        }

        static void BuildPlayer(string output)
        {
            Directory.CreateDirectory("Assets/Resources");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true };
            AssetDatabase.CreateAsset(material, "Assets/Resources/ProbeLit.mat");
            AssetDatabase.CreateAsset(new Material(Shader.Find("Train/WinterJourney")), "Assets/Resources/ProbeClock.mat");
            AssetDatabase.CreateAsset(new Material(Shader.Find("SpatialWallpaper/PoolReflection")), "Assets/Resources/ProbeReflection.mat");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Probe").AddComponent<ContentProbe>();
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -3);
            camera.backgroundColor = new Color(.03f, .03f, .03f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(30, -30, 0);
            EditorSceneManager.SaveScene(scene, "Assets/Generated/Host.unity");
            PlayerSettings.companyName = "SpatialWallpaper";
            PlayerSettings.productName = "ContentProbe";
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.stripEngineCode = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { "Assets/Generated/Host.unity" }, target = BuildTarget.StandaloneWindows64,
                locationPathName = Path.Combine(output, "player", "ContentProbe.exe"), options = BuildOptions.Development
            });
            File.WriteAllText(Path.Combine(output, "build-result.txt"), report.summary.result + " errors=" + report.summary.totalErrors);
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Probe Player build failed");
        }
    }
}

#endif
