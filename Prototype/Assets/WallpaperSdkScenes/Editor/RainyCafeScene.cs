using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class WallpaperSceneBuilder
{
    const float CafeW = 5.4f, CafeH = 3.0375f, CafeFloor = .008f, CafeScale = .53f / CafeW;
    static Dictionary<string,List<Bounds>> cafeDetailBounds;
    static bool CafeCoveredByDetail(string name,Vector3 world){
        string group=name.Split(' ')[0];var source=new Vector3(world.x/CafeScale,world.z/CafeScale,world.y/CafeScale+CafeFloor+CafeH/2);
        if(group=="Props")return source.y>4&&source.z>1.4f;
        if(cafeDetailBounds==null||!cafeDetailBounds.TryGetValue(group,out var boxes))return false;foreach(var box in boxes)if(box.Contains(source))return true;return false;
    }
    static void CafeOriginalDetail(Transform root,string name,Material material){
        var bytes=System.IO.File.ReadAllBytes(Base+"/Source/RainyCafe/"+name+".smesh");SpatialWallpaper.Sdk.ResourceData.ValidateMesh(bytes);
        var reader=typeof(SpatialWallpaper.Sdk.Execution.UnityOwnedHost).Assembly.GetType("SpatialWallpaper.Sdk.Execution.NativeResourceReader");var mesh=(UnityEngine.Mesh)reader.GetMethod("Mesh").Invoke(null,new object[]{bytes,new Action<UnityEngine.Object>(value=>{})});
        try{Mesh(root,mesh,Matrix4x4.identity,material,name);}finally{UnityEngine.Object.DestroyImmediate(mesh);}
    }
    static Texture2D CafeTexture(string name,int size,bool linear=false)
    {
        string path=Base+"/Source/RainyCafe/"+name+".png";
        var importer=(UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
        importer.textureType=UnityEditor.TextureImporterType.Default;importer.sRGBTexture=!linear;importer.maxTextureSize=Mathf.NextPowerOfTwo(size);importer.npotScale=UnityEditor.TextureImporterNPOTScale.None;importer.mipmapEnabled=false;
        importer.textureCompression=UnityEditor.TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        var source=UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);var target=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);var previous=RenderTexture.active;Texture2D copy=null;
        try{Graphics.Blit(source,target);RenderTexture.active=target;copy=new Texture2D(source.width,source.height,TextureFormat.RGBAHalf,false,true);copy.ReadPixels(new Rect(0,0,source.width,source.height),0,0);copy.Apply();UnityEditor.EditorUtility.CompressTexture(copy,TextureFormat.BC6H,UnityEditor.TextureCompressionQuality.Best);copy.wrapMode=TextureWrapMode.Repeat;copy.filterMode=FilterMode.Bilinear;copy.anisoLevel=8;var saved=Save(copy,"Cafe-"+name+".asset");copy=null;return saved;}
        finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);if(copy)UnityEngine.Object.DestroyImmediate(copy);}
    }
    static Vector3 CafePoint(float x, float depth, float height)
        => new Vector3(x, height - CafeFloor - CafeH / 2, depth) * CafeScale;

    static void CafePanel(Transform root, string name, Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var mesh = Quad(a, b, c, d);
        Mesh(root, mesh, Matrix4x4.identity, material, name);
        UnityEngine.Object.DestroyImmediate(mesh);
    }

    static void CafeCylinder(Transform root, string name, Material material, float x, float depth, float bottom, float radius, float height)
    {
        var points = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
        const int segments = 12;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
            var p = CafePoint(x + Mathf.Cos(a) * radius, depth + Mathf.Sin(a) * radius, bottom);
            var q = CafePoint(x + Mathf.Cos(b) * radius, depth + Mathf.Sin(b) * radius, bottom);
            int start = points.Count;
            points.AddRange(new[] {p, q, q + Vector3.up * height * CafeScale, p + Vector3.up * height * CafeScale,
                CafePoint(x, depth, bottom + height), p + Vector3.up * height * CafeScale, q + Vector3.up * height * CafeScale});
            uv.AddRange(new[] {Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Vector2.zero, Vector2.right, Vector2.up});
            triangles.AddRange(new[] {start, start+1, start+2, start, start+2, start+3, start+4, start+5, start+6});
        }
        var mesh = new Mesh {name = name}; mesh.SetVertices(points); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals();
        Mesh(root, mesh, Matrix4x4.identity, material, name); UnityEngine.Object.DestroyImmediate(mesh);
    }

    static void CafeWeather(Transform root, string name, int kind, int count, float x, float depth, float height, int layer = 0)
    {
        var material = Material("RainyCafeSdk/Weather", name);
        material.SetFloat("_Kind", kind); material.SetFloat("_Speed", kind == 0 ? 5 + layer : 1);
        material.SetColor("_Color",kind==0?new Color(.36f,.48f,.64f,new[]{.25f,.15f,.07f}[layer]):kind==2?new Color(1.5f,.78f,.24f,.95f):new Color(.72f,.67f,.60f,.14f));
        var points = new List<Vector3>(); var uv = new List<Vector2>(); var seeds = new List<Vector2>(); var triangles = new List<int>();
        var random = new System.Random(2791 + layer * 31 + kind * 101);
        for (int i = 0; i < count; i++)
        {
            float seed = (float)random.NextDouble();
            var center = kind == 0 ? CafePoint((float)random.NextDouble() * 8 - 4,
                depth + (float)random.NextDouble() * new[]{3.7f,6.8f,12f}[layer], CafeFloor) : CafePoint(x, depth, height);
            float width = (kind == 0 ? .007f : kind==2?.005f:.032f) * CafeScale;
            float extent = (kind == 0 ? .11f : kind==2?.013f:.048f) * CafeScale;
            int start = points.Count;
            points.AddRange(new[] {center + new Vector3(-width,-extent,0), center + new Vector3(width,-extent,0),
                center + new Vector3(width,extent,0), center + new Vector3(-width,extent,0)});
            uv.AddRange(new[] {Vector2.zero, Vector2.right, Vector2.one, Vector2.up});
            for(int j=0;j<4;j++) seeds.Add(new Vector2(seed, 0));
            triangles.AddRange(new[] {start,start+2,start+1,start,start+3,start+2});
        }
        var mesh = new Mesh {name = name}; mesh.SetVertices(points); mesh.SetUVs(0, uv); mesh.SetUVs(1, seeds); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals();
        var renderer = Mesh(root, mesh, Matrix4x4.identity, material, name);
        UnityEngine.Object.DestroyImmediate(mesh);
    }

    static void RainyCafe(Transform root)
    {
        var details=Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText(Base+"/Source/RainyCafe/details.json"));cafeDetailBounds=new Dictionary<string,List<Bounds>>();var detailMaterials=new Dictionary<string,Material>();
        foreach(var item in details){var boxes=new List<Bounds>();foreach(var pair in item["source_boxes"]){var lo=pair[0];var hi=pair[1];var min=new Vector3((float)lo[0],(float)lo[1],(float)lo[2]);var max=new Vector3((float)hi[0],(float)hi[1],(float)hi[2]);boxes.Add(new Bounds((min+max)/2,max-min+Vector3.one*.00004f));}cafeDetailBounds[(string)item["group"]]=boxes;}
        foreach (var pair in new Dictionary<string,int> {{"Floor",1280},{"Walls",640},{"Ceiling",512},
            {"Counter",640},{"Furniture",640},{"Photographs",768},{"StreetBulb",512},
            {"Props",768},{"Lamps",512},{"Plants",512},{"Foliage",512},{"Exterior",512}})
        {
            var material = Material("RainyCafeSdk/StaticSurface", "Cafe " + pair.Key);
            material.SetTexture("_LightingMap",CafeTexture(pair.Key+"Lighting",pair.Value));
            int detail=pair.Key=="Floor"?512:256;material.SetTexture("_BaseMap",CafeTexture(pair.Key+"Albedo",detail));material.SetTexture("_RoughnessMap",CafeTexture(pair.Key+"Roughness",128,true));material.SetTexture("_BumpMap",CafeTexture(pair.Key+"Normal",detail,true));
            material.SetFloat("_UseCutout",pair.Key=="Foliage"?1:0);material.SetFloat("_ExteriorFog",pair.Key=="Exterior"?1:0);
            if(pair.Key=="Foliage")material.SetTexture("_OpacityMap",CafeTexture("FoliageOpacity",512,true));
            material.SetColor("_BaseColor", Color.white);
            Model(root, pair.Key, material, CafeScale, -(CafeH / 2 + CafeFloor) * CafeScale);
            detailMaterials[pair.Key]=material;
        }
        foreach(var detail in details)CafeOriginalDetail(root,(string)detail["name"],detailMaterials[(string)detail["group"]]);
        var glass=Material("RainyCafeSdk/Condensation","Cafe window condensation");Model(root,"Glass",glass,CafeScale,-(CafeH/2+CafeFloor)*CafeScale);
        var dark = Material("RainyCafeSdk/Flat", "Cafe night"); dark.SetColor("_BaseColor", new Color(.012f,.022f,.035f));
        CafePanel(root,"Rain softened night",dark,CafePoint(-25,32,0),CafePoint(25,32,0),CafePoint(25,32,18),CafePoint(-25,32,18));
        var facade = Material("RainyCafeSdk/Flat", "Cafe distant facades"); facade.SetColor("_BaseColor",new Color(.027f,.035f,.042f));
        foreach (float side in new[]{-1f,1f})
        {
            float left=side<0?-8:2.8f,right=side<0?-2.8f:8;
            CafePanel(root,"Distant facade",facade,CafePoint(left,15,0),CafePoint(right,15,0),CafePoint(right,15,8),CafePoint(left,15,8));
        }
        var plaster = Material("RainyCafeSdk/Flat", "Cafe near plaster"); plaster.SetColor("_BaseColor",new Color(.055f,.047f,.033f));
        foreach(float side in new[]{-1f,1f})
        {
            float left=side<0?-25:2.7f,right=side<0?-2.7f:25;
            CafePanel(root,"Cafe near wall return",plaster,CafePoint(left,.03f,0),CafePoint(right,.03f,0),CafePoint(right,.03f,CafeH+.2f),CafePoint(left,.03f,CafeH+.2f));
        }
        ExtendFloor(root,3.5f,new Color(.022f,.018f,.012f));
        foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>())foreach(var surface in renderer.sharedMaterials)if(surface.shader.name=="Universal Render Pipeline/Unlit")surface.shader=Shader.Find("RainyCafeSdk/Flat");
        var ceramic = Material("RainyCafeSdk/Flat", "Cafe ceramic"); ceramic.SetColor("_BaseColor", new Color(.24f,.19f,.12f));
        var coffee = Material("RainyCafeSdk/Flat", "Cafe coffee"); coffee.SetColor("_BaseColor", new Color(.035f,.019f,.008f));
        float[,] cups = {{1.9548f,.85997f,.834f},{1.984f,2.696f,.839f}};
        for (int i=0;i<2;i++)
        {
            float x=cups[i,0],z=cups[i,1],y=cups[i,2];
            CafeCylinder(root,"Coffee cup "+i,ceramic,x,z,y-.05f,.038f,.05f);
            CafeCylinder(root,"Coffee surface "+i,coffee,x,z,y+.0005f,.031f,.001f);
            CafeWeather(root,"Coffee steam "+i,1,14,x,z,y+.006f);
        }
        int candle=0;foreach(var position in new[]{new Vector3(2.3107f,1.09f,.869f),new Vector3(1.91f,3,.785f),new Vector3(-2.4993f,2.1f,2.631f),new Vector3(-2.44f,.5f,2.631f),new Vector3(-2.44f,2.86f,2.631f),new Vector3(-2.44f,2.43f,2.071f)}){
            var light=new GameObject("Cafe candle illumination").AddComponent<Light>();light.transform.SetParent(root,false);light.transform.localPosition=CafePoint(position.x,position.y,position.z);light.type=LightType.Point;light.color=new Color(1,.52f,.21f);light.range=1.4f*CafeScale;light.intensity=.0014f;light.shadows=LightShadows.None;
            CafeWeather(root,"Cafe candle flame "+candle++,2,2,position.x,position.y,position.z);
        }
        for(int i=0;i<3;i++) CafeWeather(root,"Cafe rain "+i,0,340,0,new[]{5.1f,9,16}[i],0,i);
    }
}
