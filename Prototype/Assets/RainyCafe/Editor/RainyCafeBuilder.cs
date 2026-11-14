using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using SpatialWallpaper.RainyCafe;

public static class RainyCafeBuilder
{
    const string Root = "Assets/RainyCafe/";
    const float W = 5.4f, H = 3.0375f, Scale = .53f / W, Floor = .008f;
    static Vector3 Authored(float x, float depth, float height) => new Vector3(x, height - Floor - H / 2, depth);
    static Material Material(string name, string shader)
    {
        string path = Root + "Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var s = Shader.Find(shader); if (!s) throw new Exception("Missing shader: " + shader);
        if (!m) { m = new Material(s); AssetDatabase.CreateAsset(m, path); } else m.shader = s;
        return m;
    }
    static Texture2D Texture(string path, bool linear, bool normal = false)
    {
        var i = (TextureImporter)AssetImporter.GetAtPath(path);
        i.sRGBTexture = !linear; i.maxTextureSize = 4096; i.mipmapEnabled = true; i.anisoLevel = 4;
        i.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        i.textureCompression = TextureImporterCompression.CompressedHQ; i.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Mesh SaveMesh(string name, List<Vector3> points, List<Vector2> uv, List<Vector4> seeds, List<int> indices)
    {
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(points); mesh.SetUVs(0, uv); mesh.SetUVs(1, seeds); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals();
        // Weather vertex motion stays within this conservative bound.
        mesh.bounds = new Bounds(new Vector3(0, 3, 15), new Vector3(15, 15, 35));
        string path = Root + "Meshes/" + name + ".asset";
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old) { EditorUtility.CopySerialized(mesh, old); UnityEngine.Object.DestroyImmediate(mesh); return old; }
        AssetDatabase.CreateAsset(mesh, path); return mesh;
    }
    static Renderer Weather(string name, Transform parent, Material material, int count, int kind, float depth0 = 0, float depth1 = 0)
    {
        var points = new List<Vector3>(); var uv = new List<Vector2>(); var seeds = new List<Vector4>(); var indices = new List<int>();
        var random = new System.Random(2947 + kind * 31 + (int)(depth0 * 13));
        for (int i = 0; i < count; i++)
        {
            float seed = (float)random.NextDouble();
            Vector3 center = kind == 0 ? new Vector3((float)random.NextDouble() * 8 - 4, -H / 2, Mathf.Lerp(depth0, depth1, (float)random.NextDouble())) : Vector3.zero;
            float width = kind == 0 ? .007f : kind == 1 ? .035f : .005f;
            float height = kind == 0 ? .11f : kind == 1 ? .055f : .013f;
            float angle = kind == 0 ? 0 : i % 2 * Mathf.PI / 2;
            var right = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * width;
            int start = points.Count;
            points.Add(center - right - Vector3.up * height); points.Add(center + right - Vector3.up * height);
            points.Add(center + right + Vector3.up * height); points.Add(center - right + Vector3.up * height);
            uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            for (int j = 0; j < 4; j++) seeds.Add(new Vector4(seed, 0, 0, 0));
            indices.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(name, points, uv, seeds, indices);
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return r;
    }
    static void ConfigurePipeline()
    {
        string path = Root + "Settings/RainyCafeURP.asset";
        if (!File.Exists(path)) AssetDatabase.CopyAsset("Assets/Showcase/Settings/ShowcaseURP.asset", path);
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
        string rendererPath=Root+"Settings/RainyCafeForward.asset";
        if(!File.Exists(rendererPath)) AssetDatabase.CopyAsset(AssetDatabase.GUIDToAssetPath("61e54d6f0936dad44bd2de643f26ca65"),rendererPath);
        var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        var rendererSettings=new SerializedObject(renderer);
        rendererSettings.FindProperty("m_CopyDepthMode").intValue=0;
        rendererSettings.ApplyModifiedPropertiesWithoutUndo();
        var so = new SerializedObject(pipeline);
        so.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue=renderer;
        so.FindProperty("m_AdditionalLightsRenderingMode").intValue = 1;
        so.FindProperty("m_AdditionalLightsPerObjectLimit").intValue = 8;
        so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
        so.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 1024;
        so.FindProperty("m_ShadowDistance").floatValue = 4;
        so.FindProperty("m_RequireDepthTexture").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        GraphicsSettings.renderPipelineAsset = pipeline; QualitySettings.renderPipeline = pipeline;
    }
    static void LocalShadowMesh(Transform content, Renderer source, Vector3[] candles)
    {
        if (source.name == "Foliage") { source.shadowCastingMode = ShadowCastingMode.Off; return; }
        var sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
        var vertices = sourceMesh.vertices; var triangles = sourceMesh.triangles;
        var local = vertices.Select(v => content.InverseTransformPoint(source.transform.TransformPoint(v))).ToArray();
        var points = new List<Vector3>(); var indices = new List<int>(); var remap = new Dictionary<int, int>();
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = local[triangles[i]], b = local[triangles[i + 1]], c = local[triangles[i + 2]];
            var bounds = new Bounds(a, Vector3.zero); bounds.Encapsulate(b); bounds.Encapsulate(c);
            if (!candles.Select((p, index) => bounds.SqrDistance(p) < Mathf.Pow(RainyCafeAtmosphere.CandleRange(index) + .1f, 2)).Any(inside => inside)) continue;
            for (int j = 0; j < 3; j++)
            {
                int old = triangles[i + j];
                if (!remap.TryGetValue(old, out int next)) { next = points.Count; remap.Add(old, next); points.Add(local[old]); }
                indices.Add(next);
            }
        }
        if (indices.Count == 0) return;
        var mesh = new Mesh { name = source.name + "LocalShadows", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(points); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        string path = Root + "Meshes/" + mesh.name + ".asset"; var oldMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (oldMesh) { EditorUtility.CopySerialized(mesh, oldMesh); UnityEngine.Object.DestroyImmediate(mesh); mesh = oldMesh; } else AssetDatabase.CreateAsset(mesh, path);
        var go = new GameObject(mesh.name); go.transform.SetParent(content, false); go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = source.sharedMaterial; r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        source.shadowCastingMode = ShadowCastingMode.Off;
    }
    static Renderer CopyShelfCandle(Transform content, Renderer source, Vector3 offset, int index)
    {
        var original = source.GetComponent<MeshFilter>().sharedMesh;
        var matrix = content.worldToLocalMatrix * source.transform.localToWorldMatrix;
        var vertices = original.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
        var bounds = new Bounds(Authored(-2.5f, 2.1f, 2.59f), new Vector3(.14f, .16f, .14f));
        var triangles = original.triangles;
        var selected = new List<int>();
        for (int t = 0; t < triangles.Length; t += 3)
            if (bounds.Contains(vertices[triangles[t]]) && bounds.Contains(vertices[triangles[t + 1]]) && bounds.Contains(vertices[triangles[t + 2]]))
                selected.AddRange(new[] { triangles[t], triangles[t + 1], triangles[t + 2] });
        if (selected.Count < 12) throw new Exception("Missing source shelf candle mesh: " + source.name);
        var used = selected.Distinct().ToArray(); var remap = used.Select((old, next) => new { old, next }).ToDictionary(v => v.old, v => v.next);
        var sourceUv = original.uv; var sourceNormals = original.normals;
        var mesh = new Mesh { name = source.name + "CandleCopy" + index, indexFormat = IndexFormat.UInt32 };
        mesh.vertices = used.Select(v => vertices[v] + offset).ToArray();
        mesh.uv = used.Select(v => sourceUv[v]).ToArray();
        var normalMatrix = matrix.inverse.transpose;
        mesh.normals = used.Select(v => normalMatrix.MultiplyVector(sourceNormals[v]).normalized).ToArray();
        mesh.triangles = selected.Select(v => remap[v]).ToArray(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        string path = Root + "Meshes/" + mesh.name + ".asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved) { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); mesh = saved; }
        else AssetDatabase.CreateAsset(mesh, path);
        var go = new GameObject(mesh.name); go.transform.SetParent(content, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = source.sharedMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return renderer;
    }
    public static void Build()
    {
        var previousGraphics = GraphicsSettings.renderPipelineAsset;
        var previousQuality = QualitySettings.renderPipeline;
        int previousWidth = PlayerSettings.defaultScreenWidth, previousHeight = PlayerSettings.defaultScreenHeight;
        foreach (string dir in new[] { "Materials", "Scenes", "Prefabs", "Meshes", "Settings" }) Directory.CreateDirectory(Root + dir);
        Directory.CreateDirectory("../Validation/RainyCafe"); AssetDatabase.Refresh();
        try {
        ConfigurePipeline();
        var manifest = JObject.Parse(File.ReadAllText(Root + "Generated/manifest.json"));
        foreach (var item in manifest["sha256"].Children<JProperty>())
        {
            using (var hash = SHA256.Create()) using (var file = File.OpenRead(Root + "Generated/" + item.Name))
                if (BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant() != (string)item.Value) throw new Exception("Stale export: " + item.Name);
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var frame = new GameObject("DisplayWindow_XY_Z0"); var model = new GameObject("SceneModel"); model.transform.SetParent(frame.transform, false);
        var box = frame.AddComponent<DisplaySceneVolume>(); box.size = new Vector3(.53f, .298125f, 33 * Scale); box.showGuide = false;
        var content = new GameObject("RainyCafeContent"); content.transform.SetParent(model.transform, false); content.transform.localScale = Vector3.one * Scale;
        var atmosphere = content.AddComponent<RainyCafeAtmosphere>();
        var opaque = new List<Renderer>(); var glass = new List<Renderer>();
        foreach (var entry in manifest["objects"])
        {
            string name = (string)entry["name"], modelPath = Root + "Generated/" + name + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath); importer.isReadable = true; importer.importTangents = ModelImporterTangents.CalculateMikk; importer.SaveAndReimport();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            go.name = name; go.transform.SetParent(content.transform, false); go.transform.localPosition = new Vector3(0, -H / 2 - Floor, 0);
            go.transform.localRotation = Quaternion.Euler(0, 180, 0) * go.transform.localRotation;
            Vector3 minimum = Vector3.one * float.PositiveInfinity, maximum = Vector3.one * float.NegativeInfinity;
            foreach(var filter in go.GetComponentsInChildren<MeshFilter>()) foreach(var vertex in filter.sharedMesh.vertices)
            {
                Vector3 point = content.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                minimum=Vector3.Min(minimum,point); maximum=Vector3.Max(maximum,point);
            }
            for(int axis=0;axis<3;axis++)
                if(Mathf.Abs(minimum[axis]-(float)entry["bounds"][0][axis])>.002f || Mathf.Abs(maximum[axis]-(float)entry["bounds"][1][axis])>.002f)
                    throw new Exception("Imported axis/scale mismatch: "+name+" "+minimum+" "+maximum);
            bool transparent = name == "Glass" || name == "SmallGlass" || name == "StreetGlass";
            var mat = Material(name, name == "Glass" ? "RainyCafe/Condensation" : transparent ? "Universal Render Pipeline/Lit" : "RainyCafe/StaticSurface");
            if (name == "SmallGlass" || name == "StreetGlass")
            {
                mat.SetFloat("_Surface", 1); mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0); mat.SetFloat("_Smoothness", .86f); mat.SetColor("_BaseColor", new Color(.65f,.68f,.65f,.12f));
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = 3020;
            }
            if (!transparent)
            {
                mat.SetFloat("_UseCutout", name == "Foliage" ? 1 : 0);
                if (name == "Foliage") mat.SetTexture("_OpacityMap", Texture(Root + "Generated/FoliageOpacity.png", true, false));
                mat.SetFloat("_ExteriorFog", name == "Exterior" ? 1 : 0);
                foreach (var pair in new[] { new[] { "_BaseMap", "Albedo" }, new[] { "_LightingMap", "Lighting" }, new[] { "_RoughnessMap", "Roughness" }, new[] { "_BumpMap", "Normal" } })
                    mat.SetTexture(pair[0], Texture(Root + "Generated/" + name + pair[1] + ".png", pair[1] == "Roughness" || pair[1] == "Normal", pair[1] == "Normal"));
            }
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode = transparent ? ShadowCastingMode.Off : ShadowCastingMode.TwoSided;
                if (transparent) glass.Add(r); else opaque.Add(r);
            }
        }
        // Real near wall returns close the wider output aperture without widening the cafe.
        // At the authored 16:9 aperture these begin just outside the visible window.
        for (int side = -1; side <= 1; side += 2)
        {
            var returnWall = GameObject.CreatePrimitive(PrimitiveType.Cube); returnWall.name = side < 0 ? "LeftNearWallReturn" : "RightNearWallReturn";
            UnityEngine.Object.DestroyImmediate(returnWall.GetComponent<Collider>());
            returnWall.transform.SetParent(content.transform, false);
            // Place the return inside the original wall thickness, not on its inner face.
            returnWall.transform.localPosition = new Vector3(side * 4.72f, 0, .03f);
            returnWall.transform.localScale = new Vector3(4, H + .1f, .06f);
            var plaster = Material(returnWall.name, "Universal Render Pipeline/Unlit");
            plaster.SetColor("_BaseColor", side < 0 ? new Color(.035f,.041f,.029f) : new Color(.075f,.058f,.039f));
            var renderer = returnWall.GetComponent<MeshRenderer>(); renderer.sharedMaterial = plaster; opaque.Add(renderer);
            var threshold = GameObject.CreatePrimitive(PrimitiveType.Cube); threshold.name = returnWall.name + "Threshold";
            UnityEngine.Object.DestroyImmediate(threshold.GetComponent<Collider>());
            threshold.transform.SetParent(content.transform, false);
            threshold.transform.localPosition = new Vector3(side * 4.7f, -H / 2 - .025f, .03f);
            threshold.transform.localScale = new Vector3(4, .05f, .06f);
            threshold.GetComponent<MeshRenderer>().sharedMaterial = plaster; opaque.Add(threshold.GetComponent<MeshRenderer>());
        }
        var shadowCenters = manifest["candles"].Select(v => Authored((float)v[0], (float)v[1], (float)v[2])).ToArray();
        var candleAnchors = shadowCenters.Concat(new[] { Authored(-2.44f, .50f, 2.631f), Authored(-2.44f, 2.86f, 2.631f), Authored(-2.44f, 2.43f, 2.071f) }).ToArray();
        foreach (var source in opaque.Concat(glass).ToArray())
        {
            if (source.name != "Counter" && source.name != "SmallGlass") continue;
            for (int index = 3; index < candleAnchors.Length; index++)
            {
                var copy = CopyShelfCandle(content.transform, source, candleAnchors[index] - shadowCenters[2], index);
                if (source.name == "Counter") opaque.Add(copy); else glass.Add(copy);
            }
        }
        atmosphere.staticSurfaces = opaque.ToArray(); atmosphere.glass = glass.ToArray();
        foreach (var r in opaque) LocalShadowMesh(content.transform, r, shadowCenters);
        atmosphere.rain = new Renderer[3];
        for (int i = 0; i < 3; i++)
        {
            var mat = Material("Rain" + i, "RainyCafe/Weather"); mat.SetFloat("_Kind", 0); mat.SetFloat("_Speed", 5 + i);
            mat.SetColor("_Color", new Color(.36f, .48f, .64f, new[] { .25f, .15f, .07f }[i])); mat.renderQueue = 3000 + 2 - i;
            atmosphere.rain[i] = Weather("RainLayer" + i, content.transform, mat, 340, 0, new[] { 5.1f, 9f, 16f }[i], new[] { 8.8f, 15.8f, 28f }[i]);
        }
        int candleCount = candleAnchors.Length;
        atmosphere.pluginWeather = RainyCafeParticleWeatherBuilder.Configure(content.transform);
        atmosphere.candles = new Light[candleCount]; atmosphere.flames = new Renderer[candleCount]; atmosphere.flameRoots = new Transform[candleCount];
        for (int i = 0; i < candleCount; i++)
        {
            var anchor = candleAnchors[i];
            var light = new GameObject("CandleLocalLight" + i).AddComponent<Light>(); light.transform.SetParent(content.transform, false); light.transform.localPosition = anchor;
            light.type = LightType.Point; light.color = new Color(1, .52f, .21f); light.range = 1.15f * Scale;
            light.renderMode = LightRenderMode.ForcePixel;
            light.intensity = i == 0 ? .0017f : .0014f;
            light.shadows = LightShadows.Soft; light.shadowCustomResolution = 256; light.shadowNearPlane = .001f; light.shadowBias = .015f; light.shadowNormalBias = .04f;
            var lightData = new SerializedObject(light.gameObject.AddComponent<UniversalAdditionalLightData>());
            lightData.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = 0;
            lightData.FindProperty("m_UsePipelineSettings").boolValue = false;
            lightData.ApplyModifiedPropertiesWithoutUndo();
            atmosphere.candles[i] = light;
            var mat = Material("Flame" + i, "RainyCafe/Weather"); mat.SetFloat("_Kind", 2); mat.renderQueue = 3030;
            mat.SetColor("_Color", new Color(1.5f,.78f,.24f,.95f));
            atmosphere.flames[i] = Weather("Flame" + i, content.transform, mat, 2, 2);
            atmosphere.flameRoots[i] = atmosphere.flames[i].transform; atmosphere.flameRoots[i].localPosition = anchor;
        }
        atmosphere.steam = new Renderer[manifest["cups"].Count()];
        for (int i = 0; i < atmosphere.steam.Length; i++)
        {
            var mat = Material("Steam" + i, "RainyCafe/Weather"); mat.SetFloat("_Kind", 1); mat.SetColor("_Color", new Color(.63f, .60f, .55f, .16f)); mat.renderQueue = 3025;
            var r = Weather("CoffeeSteam" + i, content.transform, mat, 14, 1);
            var pos = (JArray)manifest["cups"][i]; r.transform.localPosition = Authored((float)pos[0], (float)pos[1], (float)pos[2]); atmosphere.steam[i] = r;
        }
        PrefabUtility.SaveAsPrefabAsset(content, Root + "Prefabs/RainyCafeContent.prefab");
        var camera = new GameObject("DisplayCamera").AddComponent<Camera>(); camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.012f, .019f, .027f); camera.allowHDR = true;
        camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        var projection = camera.gameObject.AddComponent<WindowProjection>(); projection.screenFrame = frame.transform; projection.contentDepth = 34 * Scale; projection.SetEye(projection.referenceEye);
        var cadence = camera.gameObject.AddComponent<AdaptiveRenderCadence>(); cadence.projection = projection;
        var driver = camera.gameObject.AddComponent<PreviewEyeDriver>(); driver.projection = projection; driver.cadence = cadence; driver.exhibit = content; driver.showHelp = false;
        var bench = camera.gameObject.AddComponent<ShowcaseBenchmark>(); bench.projection = projection; bench.cadence = cadence;
        var binding = camera.gameObject.AddComponent<RainyCafeRenderBinding>(); binding.content = atmosphere; binding.cadence = cadence;
        binding.pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Root + "Settings/RainyCafeURP.asset");
        var rig = frame.AddComponent<RainyCafeDisplayRig>(); rig.projection = projection; rig.sceneModel = model.transform; rig.volume = box;
        camera.gameObject.AddComponent<RainyCafeCapture>();
        var lightReference = new GameObject("StaticLightingReference").AddComponent<Light>(); lightReference.type = LightType.Directional; lightReference.intensity = 0;
        RenderSettings.skybox = null; RenderSettings.fog = false; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black; RenderSettings.reflectionIntensity = .15f;
        PlayerSettings.colorSpace = ColorSpace.Linear; PlayerSettings.enableFrameTimingStats = true; PlayerSettings.runInBackground = true;
        PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900; PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), Root + "Scenes/RainyCafe.unity"); AssetDatabase.SaveAssets();
        Check();
        var report = BuildPipeline.BuildPlayer(new[] { Root + "Scenes/RainyCafe.unity" }, "Builds/RainyCafe/RainyCafe.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("RainyCafe Player build failed");
        foreach (string shader in new[] { "RainyCafe/StaticSurface", "RainyCafe/Weather", "RainyCafe/Condensation" })
            if (ShaderUtil.ShaderHasError(Shader.Find(shader))) throw new Exception("Shader compilation failed: " + shader);
        } finally {
        PlayerSettings.defaultScreenWidth = previousWidth; PlayerSettings.defaultScreenHeight = previousHeight;
        GraphicsSettings.renderPipelineAsset = previousGraphics; QualitySettings.renderPipeline = previousQuality; AssetDatabase.SaveAssets();
        }
    }
    public static void Check()
    {
        EditorSceneManager.OpenScene(Root + "Scenes/RainyCafe.unity");
        var p = UnityEngine.Object.FindObjectOfType<WindowProjection>(); var c = p.GetComponent<Camera>(); var box = UnityEngine.Object.FindObjectOfType<DisplaySceneVolume>();
        var content = UnityEngine.Object.FindObjectOfType<RainyCafeAtmosphere>();
        if (!content || content.candles.Length != 6 || content.steam.Length < 2 || content.rain.Length != 3) throw new Exception("Incomplete atmosphere");
        foreach (string name in new[] { "LeftNearWallReturn", "RightNearWallReturn" })
        {
            var wall = content.transform.Find(name);
            float innerFace = Mathf.Abs(wall.localPosition.x) - wall.localScale.x / 2;
            if (innerFace < 2.71f || innerFace > 2.82f) throw new Exception("Near wall must overlap inside wall thickness without coplanar inner faces");
        }
        float error = 0;
        foreach (float x in new[] { -.2f, 0, .2f }) foreach (float y in new[] { -.1f, 0, .1f }) foreach (float z in new[] { -.35f, -.6f, -.95f })
        {
            p.SetEye(new Vector3(x, y, z));
            foreach (float a in new[] { -.5f, .5f }) foreach (float b in new[] { -.5f, .5f })
            {
                var q = c.WorldToViewportPoint(box.FrontCorner(a, b)); error = Mathf.Max(error, new Vector2((q.x - a - .5f) * 1600, (q.y - b - .5f) * 900).magnitude);
            }
        }
        if (error > .01f) throw new Exception("Fixed-window corner error: " + error);
        p.SetEye(p.referenceEye);
        var transforms = content.staticSurfaces.Select(r => r.transform.localToWorldMatrix).ToArray();
        if (transforms.Length < 5 || box.GetComponent<Renderer>()) throw new Exception("Invalid content or visible guide");
        content.SendMessage("Awake"); content.Evaluate(0);
        var initialLight = content.candles[0].transform.localPosition;
        var initialIntensity = content.candles[0].intensity;
        content.Evaluate(3.7f);
        if (Vector3.Distance(initialLight,content.candles[0].transform.localPosition)<.00001f || Mathf.Abs(initialIntensity-content.candles[0].intensity)<.000001f)
            throw new Exception("Candle position and illumination must both animate");
        for (int i=0;i<content.staticSurfaces.Length;i++)
            if (transforms[i]!=content.staticSurfaces[i].transform.localToWorldMatrix) throw new Exception("Cafe interior moved with weather");
        content.animate=false; content.Evaluate(0); var held=content.candles[0].transform.localPosition;
        content.Evaluate(100); if(held!=content.candles[0].transform.localPosition) throw new Exception("Static clock did not hold");
        content.animate=true; content.Evaluate(0);
        RainyCafeParticleWeatherBuilder.Verify(content.pluginWeather);
        File.WriteAllText("../Validation/RainyCafe/import-checks.json", new JObject { ["corner_error_px"] = error, ["eyes"] = 27, ["uniform_scale"] = Scale, ["static_renderers"] = transforms.Length, ["auxiliary_renderer"] = false }.ToString());
    }
}
