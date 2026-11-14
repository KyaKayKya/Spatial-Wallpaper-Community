using System;
using UnityEngine;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Sdk.Native.Unity {
 public sealed class NativeSoundBus:IDisposable {
  sealed class Voice {internal AudioSource Source;internal float Gain;internal AudioClip Clip;}
  readonly GameObject root;
  public const int MaxMusicLayers=4;
  readonly Voice[] music;
  readonly Voice[] effects;
  SoundMix mix=new SoundMix();int next;bool disposed;
  public NativeSoundBus(Transform parent,int effectVoices=8){
   if(!parent||effectVoices<1||effectVoices>32)throw new ArgumentException("native-sound-owner-or-voices");root=new GameObject("Native SDK audio");root.transform.SetParent(parent,false);music=new Voice[MaxMusicLayers];for(int index=0;index<music.Length;index++)music[index]=Create("Music "+index,true);effects=new Voice[effectVoices];for(int index=0;index<effects.Length;index++)effects[index]=Create("Effect "+index,false);
  }
  Voice Create(string name,bool loop){var node=new GameObject(name);node.transform.SetParent(root.transform,false);var source=node.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=loop;source.spatialBlend=0;return new Voice{Source=source};}
  void Live(){if(disposed)throw new ObjectDisposedException(nameof(NativeSoundBus));}
  public void RequirePrepared(AudioClip clip,float gain){Live();if(!clip||float.IsNaN(gain)||float.IsInfinity(gain)||gain<0||gain>16)throw new InvalidOperationException("native-sound-clip-or-gain");if(clip.loadState!=AudioDataLoadState.Loaded)throw new InvalidOperationException("native-sound-not-prepared");}
  void Start(Voice voice,AudioClip clip,float gain,bool isMusic){voice.Source.Stop();voice.Clip=clip;voice.Gain=Mathf.Clamp01(gain);voice.Source.clip=clip;voice.Source.volume=voice.Gain*(isMusic?mix.MusicGain:mix.EffectsGain);voice.Source.Play();}
  public void PlayMusic(AudioClip clip,float gain){RequirePrepared(clip,gain);var voice=Array.Find(music,value=>value.Clip==clip)??Array.Find(music,value=>!value.Clip);if(voice==null)throw new InvalidOperationException("native-music-layer-budget");Start(voice,clip,gain,true);}
  public AudioClip[] MusicClips{get{Live();return Array.ConvertAll(Array.FindAll(music,value=>value.Clip),value=>value.Clip);}}
  public void PlayEffect(AudioClip clip,float gain){RequirePrepared(clip,gain);int index=Array.FindIndex(effects,voice=>!voice.Source.isPlaying);if(index<0)index=next;Start(effects[index],clip,gain,false);next=(index+1)%effects.Length;}
  public void ApplyUserMix(SoundMix value){Live();mix=value??throw new ArgumentNullException(nameof(value));foreach(var voice in music)voice.Source.volume=voice.Gain*mix.MusicGain;foreach(var voice in effects)voice.Source.volume=voice.Gain*mix.EffectsGain;}
  public void StopMusic(){Live();foreach(var voice in music){voice.Source.Stop();voice.Source.clip=null;voice.Clip=null;}}
  public void StopEffects(){Live();foreach(var voice in effects){voice.Source.Stop();voice.Source.clip=null;voice.Clip=null;}}
  public float MusicVolume{get{Live();return music[0].Source.volume;}}
  public float[] EffectVolumes{get{Live();var values=new float[effects.Length];for(int index=0;index<effects.Length;index++)values[index]=effects[index].Source.volume;return values;}}
  public void Dispose(){if(disposed)return;StopMusic();StopEffects();disposed=true;if(root){if(Application.isPlaying)UnityEngine.Object.Destroy(root);else UnityEngine.Object.DestroyImmediate(root);}}
 }
}
