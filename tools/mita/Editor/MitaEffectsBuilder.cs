using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using SpatialWallpaper.Mita;

public static class MitaEffectsBuilder {
 [Serializable] sealed class ParticleMaterial {public string texture;public Color color;}
 [Serializable] sealed class Config {public ParticleMaterial click,wink;public string wink_path;}
 static Material Material(string name,ParticleMaterial data,bool additive=false){
  var material=new Material(Shader.Find("SpatialWallpaper/MitaParticles"));material.name=name;
  material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(data.texture);material.color=data.color;
  material.SetInt("_DstBlend",(int)(additive?BlendMode.One:BlendMode.OneMinusSrcAlpha));material.SetFloat("_LuminanceAlpha",additive?0:1);material.SetFloat("_Cutoff",additive?0:.5f);material.renderQueue=3100;
  string path="Assets/Generated/"+name+".mat";AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(material,path);return material;
 }
 public static void Apply(GameObject actor,MitaBehaviour behaviour){
  var config=JsonUtility.FromJson<Config>(File.ReadAllText("Assets/Xlmy/Effects/effects.json"));
  var effects=behaviour.gameObject.AddComponent<MitaEffects>();effects.view=behaviour.view;effects.projection=behaviour.projection;
  effects.clickAudio=behaviour.gameObject.AddComponent<AudioSource>();effects.clickAudio.playOnAwake=false;effects.clickAudio.clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Xlmy/Audio/MitaMouseClick.wav");
  if(!effects.clickAudio.clip)throw new InvalidOperationException("Missing original click audio");
  var click=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Xlmy/Effects/Click.prefab"));click.transform.SetParent(behaviour.transform,false);
  effects.click=click.GetComponent<ParticleSystem>();var clickMain=effects.click.main;clickMain.playOnAwake=false;effects.click.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
  click.GetComponent<ParticleSystemRenderer>().sharedMaterial=Material("OriginalClickParticles",config.click);
  var wink=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Xlmy/Effects/Wink.prefab"));wink.transform.SetParent(actor.transform.Find(config.wink_path),false);
  effects.wink=wink.GetComponent<ParticleSystem>();wink.GetComponent<ParticleSystemRenderer>().sharedMaterial=Material("OriginalWinkParticles",config.wink,true);
  var winkMain=effects.wink.main;winkMain.playOnAwake=false;effects.wink.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
  actor.AddComponent<MitaAnimationEvents>().effects=effects;
 }
}
