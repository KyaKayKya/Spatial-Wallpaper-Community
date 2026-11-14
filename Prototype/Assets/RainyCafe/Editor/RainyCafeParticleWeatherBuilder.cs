using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using SpatialWallpaper.RainyCafe;

public static class RainyCafeParticleWeatherBuilder
{
    const string Root = "Assets/RainyCafe/";
    static Vector3[][] Positions(RainyCafeParticleWeather weather)
    {
        var result=new Vector3[weather.layers.Length][];
        for(int i=0;i<result.Length;i++)
        {
            var particles=new ParticleSystem.Particle[weather.layers[i].main.maxParticles];
            int count=weather.layers[i].GetParticles(particles);
            Array.Sort(particles,0,count,System.Collections.Generic.Comparer<ParticleSystem.Particle>.Create((a,b)=>a.randomSeed.CompareTo(b.randomSeed)));
            result[i]=new Vector3[count];for(int j=0;j<count;j++)result[i][j]=particles[j].position;
        }
        return result;
    }
    public static void Verify(RainyCafeParticleWeather weather)
    {
        foreach(var layer in weather.layers)
            foreach(var system in layer.GetComponentsInChildren<ParticleSystem>())
                if(system.useAutoRandomSeed)throw new Exception("Rain child has nondeterministic seed");
        weather.Evaluate(0,true);weather.Evaluate(1,true);var direct=Positions(weather);
        weather.Evaluate(0,true);for(int i=1;i<=60;i++)weather.Evaluate(i/60f,true);
        var stepped=Positions(weather);float error=0;int count=0;
        for(int i=0;i<direct.Length;i++)
        {
            if(direct[i].Length!=stepped[i].Length)throw new Exception("Rain clock particle count mismatch");
            count+=direct[i].Length;
            for(int j=0;j<direct[i].Length;j++)error=Mathf.Max(error,Vector3.Distance(direct[i][j],stepped[i][j]));
        }
        if(count<100 || error>.00001f)throw new Exception("Rain clock replay mismatch: "+count+" / "+error);
        File.WriteAllText("../Validation/RainyCafe/particle-clock-check.json","{\"particles\":"+count+",\"position_error_authored_m\":"+error.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"fixed_step_hz\":60,\"all_seeds_fixed\":true}");
        weather.Evaluate(0,true);
    }
    public static RainyCafeParticleWeather Configure(Transform content)
    {
        string path = Root + "LocalWeather/Content/Prefabs/Particle FX/Light Rain.prefab";
        if (!File.Exists(path)) throw new Exception("Run python tools/rainycafe/restore_local_rain.py before building");
        var controller = content.gameObject.AddComponent<RainyCafeParticleWeather>();
        controller.layers = new ParticleSystem[3];
        var street = new GameObject("StreetLampRainOnly").AddComponent<Light>();street.transform.SetParent(content,false);
        street.transform.localPosition=new Vector3(-1.2f,2.75f-.008f-3.0375f/2,8.2f);
        street.type=LightType.Point;street.range=3.2f*(.53f/5.4f);street.renderMode=LightRenderMode.ForcePixel;
        street.intensity=.006f*RainyCafeAtmosphere.CandleIntensityEncoding;
        street.color=(new Color(.7f,.82f,1).linear/RainyCafeAtmosphere.CandleIntensityEncoding).gamma;
        street.shadows=LightShadows.None;street.cullingMask=1<<30;controller.streetLight=street;
        var plane = new GameObject("ExteriorRainCollisionPlane"); plane.transform.SetParent(content, false);
        plane.transform.localPosition = new Vector3(0, -3.0375f/2 - .05f, 0);
        for (int i=0;i<3;i++)
        {
            float near = new[] {5.2f,9f,16f}[i], far = new[] {8.8f,15.8f,28f}[i];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            foreach(var t in go.GetComponentsInChildren<Transform>()) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach(var t in go.GetComponentsInChildren<Transform>()) t.gameObject.layer=30;
            go.name="PluginRainLayer"+i;go.transform.SetParent(content,false);
            go.transform.localPosition=new Vector3(0,5.2f-.008f-3.0375f/2,(near+far)/2);
            go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
            var system=go.GetComponent<ParticleSystem>();system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=system.main;main.loop=true;main.prewarm=false;main.playOnAwake=false;main.duration=5;
            main.simulationSpace=ParticleSystemSimulationSpace.Local;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
            main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;main.startLifetime=.72f;main.startSpeed=0;
            main.startSize=new ParticleSystem.MinMaxCurve(.0055f,.012f);main.maxParticles=4096;
            main.startColor=Color.white;main.gravityModifier=0;
            system.useAutoRandomSeed=false;system.randomSeed=(uint)(8173+i*97);
            var emission=system.emission;emission.enabled=true;emission.rateOverTime=new[] {1200,1800,2400}[i];
            var shape=system.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Box;
            shape.scale=new Vector3(8,.05f,far-near);shape.position=Vector3.zero;shape.rotation=Vector3.zero;
            var velocity=system.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.Local;
            velocity.x=new ParticleSystem.MinMaxCurve(-.12f,.08f);velocity.y=new ParticleSystem.MinMaxCurve(-11,-8);
            velocity.z=new ParticleSystem.MinMaxCurve(-.05f,.1f);
            var collision=system.collision;collision.enabled=true;collision.type=ParticleSystemCollisionType.Planes;
            for(int p=collision.planeCount-1;p>=0;p--)collision.RemovePlane(p);
            collision.AddPlane(plane.transform);collision.bounce=0;collision.lifetimeLoss=1;collision.dampen=0;
            collision.sendCollisionMessages=false;
            int seedIndex=0;
            foreach(var child in go.GetComponentsInChildren<ParticleSystem>())
            {
                child.useAutoRandomSeed=false;child.randomSeed=(uint)(8173+i*97+seedIndex++*11);
                var cm=child.main;cm.playOnAwake=false;cm.scalingMode=ParticleSystemScalingMode.Hierarchy;
                if(child!=system){cm.maxParticles=512;cm.startColor=new Color(.5f,.58f,.65f,.14f);}
            }
            foreach(var renderer in go.GetComponentsInChildren<ParticleSystemRenderer>())
            {
                string materialPath=Root+"Materials/PluginRain"+i+renderer.sharedMaterial.name+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(!material){material=new Material(renderer.sharedMaterial);AssetDatabase.CreateAsset(material,materialPath);}
                material.shader=Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
                material.SetFloat("_EmissionEnabled",1);material.EnableKeyword("_EMISSION");
                material.SetTexture("_EmissionMap",Texture2D.whiteTexture);
                material.SetColor("_EmissionColor",new Color(.20f,.24f,.30f));
                material.SetFloat("_Smoothness",.6f);material.SetColor("_SpecColor",new Color(.15f,.15f,.15f));
                material.DisableKeyword("_FADING_ON");material.SetFloat("_CameraFadingEnabled",0);
                material.SetFloat("_SoftParticlesEnabled",1);material.EnableKeyword("_SOFTPARTICLES_ON");
                material.SetVector("_SoftParticleFadeParams",new Vector4(.0002f,1/.002f,0,0));
                material.SetColor("_BaseColor",new Color(.44f,.52f,.62f,new[]{.32f,.19f,.09f}[i]));
                renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            }
            var rainRenderer=system.GetComponent<ParticleSystemRenderer>();rainRenderer.renderMode=ParticleSystemRenderMode.Stretch;
            rainRenderer.velocityScale=.022f;rainRenderer.lengthScale=1.5f;
            controller.layers[i]=system;
            // Keep the licensed particle configuration local; tracked scenes contain references.
            PrefabUtility.SaveAsPrefabAssetAndConnect(go,Root+"LocalWeather/PreparedRainLayer"+i+".prefab",InteractionMode.AutomatedAction);
        }
        return controller;
    }
}
