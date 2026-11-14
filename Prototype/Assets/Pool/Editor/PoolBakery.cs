using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Run after PoolBuilder.Create. Bake must run with graphics, without -quit.
public static class PoolBakery
{
    const string Root = "Assets/Pool/";
    const string ScenePath = Root + "Scenes/Pool.unity";
    const string ShowcasePath = "Assets/Showcase/Scenes/Showcase.unity";
    const double TimeoutSeconds = 45 * 60;
    static ftRenderLightmap baker;
    static bool finished, failed, exiting;
    static double started;
    static string failure;

    public static void Bake()
    {
        try
        {
            exiting = finished = failed = false;
            failure = null;
            started = EditorApplication.timeSinceStartup;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SetPipelines();
            QualitySettings.renderPipeline = GraphicsSettings.defaultRenderPipeline;
            ConfigureArchitecture();
            ConfigureLights();

            baker = ScriptableObject.CreateInstance<ftRenderLightmap>();
            baker.LoadRenderSettings();
            ftRenderLightmap.scenePath = Path.GetFullPath("../.local-assets/bakery-temp");
            Directory.CreateDirectory(ftRenderLightmap.scenePath);
            ftBuildGraphics.scenePath = ftRenderLightmap.scenePath;
            ftRenderLightmap.scenePathQuoted = "\"" + ftRenderLightmap.scenePath + "\"";
            baker.userRenderMode = ftRenderLightmap.RenderMode.FullLighting;
            ftRenderLightmap.renderDirMode = ftRenderLightmap.RenderDirMode.DominantDirection;
            baker.texelsPerUnit = ftBuildGraphics.texelsPerUnit = 4096;
            ftRenderLightmap.bounces = 4;
            baker.giSamples = 64;
            baker.denoise = true;
            baker.fixSeams = true;
            baker.exeMode = true;
            baker.unloadScenesInDeferredMode = false;
            ftBuildGraphics.autoAtlas = true;
            ftBuildGraphics.minAutoResolution = 1024;
            ftBuildGraphics.maxAutoResolution = 4096;
            ftBuildGraphics.overwriteWarning = false;
            ftRenderLightmap.useScenePath = false;
            ftRenderLightmap.outputPath = "Pool/Lighting/Bakery";
            ftRenderLightmap.clientMode = false;
            ftRenderLightmap.unityInBatchMode = Application.isBatchMode;
            ftRenderLightmap.suppressPopups = true;
            ftLightmaps.GetProjectSettings().autoRenderRefProbes = false;
            ftLightmaps.GetProjectSettings().optimizedLODs = false;
            baker.SaveRenderSettings();
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();

            ftRenderLightmap.OnFinishedFullRender += OnFinished;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += WatchBake;
            Debug.Log("POOL_BAKERY_START: FullLighting/DominantDirection, 4096 texels/unit.");
            baker.RenderButton(false);
        }
        catch (Exception exception) { Fail(exception.ToString()); }
    }

    static void ConfigureArchitecture()
    {
        var architecture = GameObject.Find("PoolArchitecture");
        if (!architecture) throw new InvalidOperationException("PoolArchitecture is missing; run PoolBuilder.Create first.");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new InvalidOperationException("URP Lit shader is unavailable.");
        var albedo = Require<Texture2D>(Root + "Textures/ivory_tiles_basecolor.png");
        var normal = Require<Texture2D>(Root + "Textures/ivory_tiles_normal.png");
        var normalImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) as TextureImporter;
        if (normalImporter && normalImporter.textureType != TextureImporterType.NormalMap)
        {
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
        }
        const string materialPath = Root + "Materials/BakeryCeramic.mat";
        Directory.CreateDirectory(Root + "Materials");
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            material = new Material(shader) { name = "BakeryCeramic" };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", albedo);
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_BumpScale", .65f);
        material.SetFloat("_Metallic", 0);
        material.SetFloat("_Smoothness", .32f);
        material.SetFloat("_SmoothnessTextureChannel", 0);
        material.EnableKeyword("_NORMALMAP");
        material.DisableKeyword("_EMISSION");
        material.DisableKeyword("_METALLICSPECGLOSSMAP");
        material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        EditorUtility.SetDirty(material);

        int count = 0;
        foreach (var renderer in architecture.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (IsDynamic(renderer)) { ExcludeFromBake(renderer); continue; }
            var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (!mesh) throw new InvalidOperationException("Missing mesh: " + renderer.name);
            if (mesh.uv2.Length != mesh.vertexCount)
                throw new InvalidOperationException("Missing lightmap UV2: " + renderer.name + ". Generate secondary UVs in the model importer before baking.");
            renderer.sharedMaterials = Enumerable.Repeat(material, Math.Max(1, renderer.sharedMaterials.Length)).ToArray();
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) | StaticEditorFlags.ContributeGI);
            renderer.receiveGI = ReceiveGI.Lightmaps;
            renderer.scaleInLightmap = 1;
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            EditorUtility.SetDirty(renderer);
            count++;
        }
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true))
            if (IsDynamic(renderer)) ExcludeFromBake(renderer);
        if (count == 0) throw new InvalidOperationException("No architecture renderers available for baking.");
        Debug.Log("POOL_BAKERY_ARCHITECTURE: " + count + " renderers use unlit albedo inputs and URP Lit.");
    }

    static bool IsDynamic(Renderer renderer)
    {
        for (var t = renderer.transform; t; t = t.parent)
            if (t.name.IndexOf("ring", StringComparison.OrdinalIgnoreCase) >= 0 ||
                t.name == "WaterSurface" || t.name == "WaterFront" || t.name == "WaterReflection") return true;
        return false;
    }

    static void ExcludeFromBake(MeshRenderer renderer)
    {
        GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
            GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & ~StaticEditorFlags.ContributeGI);
        renderer.receiveGI = ReceiveGI.LightProbes;
        renderer.lightmapIndex = -1;
    }

    static void ConfigureLights()
    {
        var sunObject = GameObject.Find("PoolSun");
        var sun = sunObject ? sunObject.GetComponent<Light>() : null;
        if (!sun || sun.type != LightType.Directional) throw new InvalidOperationException("PoolSun directional light is missing.");
        var direct = sun.GetComponent<BakeryDirectLight>();
        if (!direct) direct = sun.gameObject.AddComponent<BakeryDirectLight>();
        direct.color = sun.color;
        direct.intensity = sun.intensity;
        direct.samples = 64;
        direct.shadowSpread = .01f;
        direct.indirectIntensity = 1;
        direct.bakeToIndirect = false;
        sun.lightmapBakeType = LightmapBakeType.Baked;
        // Runtime highlights on moving water and vinyl; architecture is already lit in the maps.
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true))
            if (IsDynamic(renderer) && renderer.name != "WaterFront")
                renderer.gameObject.layer = renderer.name.IndexOf("ring", StringComparison.OrdinalIgnoreCase) >= 0 ? 8 : 4;
        var dynamicObject = GameObject.Find("PoolDynamicSun");
        if (!dynamicObject) dynamicObject = new GameObject("PoolDynamicSun");
        dynamicObject.transform.SetParent(sun.transform.parent, false);
        dynamicObject.transform.rotation = sun.transform.rotation;
        var dynamicSun = dynamicObject.GetComponent<Light>();
        if (!dynamicSun) dynamicSun = dynamicObject.AddComponent<Light>();
        dynamicSun.type = LightType.Directional;
        dynamicSun.color = sun.color;
        dynamicSun.intensity = sun.intensity;
        dynamicSun.cullingMask = (1 << 4) | (1 << 8);
        dynamicSun.shadows = LightShadows.None;

        var skyObject = GameObject.Find("PoolBakerySky");
        if (!skyObject) skyObject = new GameObject("PoolBakerySky");
        skyObject.transform.SetParent(sun.transform.parent, false);
        var sky = skyObject.GetComponent<BakerySkyLight>();
        if (!sky) sky = skyObject.AddComponent<BakerySkyLight>();
        sky.color = new Color(.68f, .77f, .87f);
        sky.intensity = .18f;
        sky.samples = 64;
        sky.hemispherical = true;
        sky.bakeToIndirect = true;
        sky.indirectIntensity = 1;
        EditorSceneManager.MarkSceneDirty(sun.gameObject.scene);
    }

    static void OnFinished(object sender, EventArgs args) { finished = true; }
    static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        failed = true;
        failure = message + "\n" + stack;
    }

    static void WatchBake()
    {
        try
        {
            if (exiting) return;
            if (failed) { Fail(failure); return; }
            if (EditorApplication.timeSinceStartup - started > TimeoutSeconds)
            { Fail("Bakery exceeded the 45 minute timeout."); return; }
            if (ftRenderLightmap.bakeInProgress) return;
            if (EditorApplication.timeSinceStartup - started < 2) return;
            if (!finished || ftRenderLightmap.userCanceled)
            { Fail("Bakery stopped without its successful completion event."); return; }
            if (LightmapSettings.lightmaps.Length == 0 ||
                !LightmapSettings.lightmaps.Any(map => map.lightmapColor && map.lightmapDir))
            { Fail("Bakery completed but no color + dominant-direction lightmaps were assigned."); return; }
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            WriteResult("bake-result.txt", "Succeeded; lightmaps=" + LightmapSettings.lightmaps.Length);
            StopWatching();
            Debug.Log("POOL_BAKERY_SUCCEEDED: saved baked Pool.unity.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Fail(exception.ToString()); }
    }

    static void StopWatching()
    {
        exiting = true;
        EditorApplication.update -= WatchBake;
        Application.logMessageReceived -= OnLog;
        ftRenderLightmap.OnFinishedFullRender -= OnFinished;
    }

    static void Fail(string message)
    {
        StopWatching();
        Debug.LogError("POOL_BAKERY_FAILED: " + message);
        try { WriteResult("bake-result.txt", "Failed: " + message); }
        finally { EditorApplication.Exit(1); }
    }

    static T Require<T>(string path) where T : UnityEngine.Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!asset) throw new InvalidOperationException("Required asset missing: " + path);
        return asset;
    }

    static void SetPipelines()
    {
        // URP includes variants from Graphics and Quality pipeline assets.
        GraphicsSettings.defaultRenderPipeline = Require<UniversalRenderPipelineAsset>(Root + "Settings/PoolURP.asset");
        QualitySettings.renderPipeline = Require<UniversalRenderPipelineAsset>("Assets/Showcase/Settings/ShowcaseURP.asset");
    }

    static void WriteResult(string name, string result)
    {
        Directory.CreateDirectory("../Validation/Pool");
        File.WriteAllText("../Validation/Pool/" + name, result);
    }

    public static void Build()
    {
        try
        {
            // A plain prefab cannot carry scene lightmap bindings. Remove the obsolete pre-bake template.
            AssetDatabase.DeleteAsset(Root + "Prefabs/PoolContent.prefab");
            // Deliberately do not call PoolBuilder.Create: it replaces bake inputs.
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!LightmapSettings.lightmaps.Any(map => map.lightmapColor && map.lightmapDir))
                throw new InvalidOperationException("Saved Pool scene has no Bakery directional lightmaps. Run PoolBakery.Bake first.");
            var waterMesh = Require<Mesh>(Root + "Models/WaterGrid.asset");
            waterMesh.RecalculateTangents();
            EditorUtility.SetDirty(waterMesh);
            // Keep the waterline between treads, away from coplanar contact shimmer.
            var surface = GameObject.Find("WaterSurface").transform;
            surface.localPosition = Vector3.up * .12f;
            var cutaway = GameObject.Find("WaterFront").transform;
            cutaway.localPosition = Vector3.up * .12f;
            cutaway.localScale = new Vector3(1, 1, 1.62f / 1.5f);
            EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            var water = Require<Material>(Root + "Materials/PoolWater.mat");
            water.SetFloat("_NormalMapAIntensity", .55f);
            water.SetFloat("_ReflectionDistortion", .5f);
            water.SetFloat("_ReflectionRoughness", .08f);
            water.SetFloat("_Caustics", 1);
            water.EnableKeyword("_CAUSTICS_ON");
            water.SetFloat("_SpeedMul", 0);
            water.DisableKeyword("_SPEED_MUL_ON");
            EditorUtility.SetDirty(water);
            var front = Require<Material>(Root + "Materials/CutawayTint.mat");
            front.SetFloat("_AbsorptionScale", 1);
            front.SetTexture("_Caustics", Require<Texture2D>(Root + "Textures/Caustics.tif"));
            EditorUtility.SetDirty(front);
            SetPipelines();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Builds/Pool");
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath, ShowcasePath },
                "Builds/Pool/Pool.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            bool success = report.summary.result == BuildResult.Succeeded && report.summary.totalErrors == 0;
            WriteResult("build-result.txt", report.summary.result + " errors=" + report.summary.totalErrors);
            EditorApplication.Exit(success ? 0 : 1);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            try { WriteResult("build-result.txt", "Failed: " + exception); }
            finally { EditorApplication.Exit(1); }
        }
    }
}
