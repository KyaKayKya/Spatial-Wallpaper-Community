using System;using UnityEngine;
namespace SpatialWallpaper.Sdk {
 public static class Sound {
  static SoundMixer play;static Func<AudioClip,SoundClipData> read;
  internal static void BindPlay(SoundMixer mixer,Func<AudioClip,SoundClipData> reader){play=mixer;read=reader;}
  static SoundMixer Current()=>play??throw new InvalidOperationException("sound-unavailable");
  public static void PlayMusic(AudioClip clip,float volume){Current().PlayMusic(read(clip),volume);}
  public static void PlayEffect(AudioClip clip,float volume){Current().PlayEffect(read(clip),volume);}
  public static void StopMusic(){Current().StopMusic();}
  public static void StopEffects(){Current().StopEffects();}
 }
}
