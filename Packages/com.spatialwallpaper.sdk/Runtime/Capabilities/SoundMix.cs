using System;using System.IO;using System.Linq;using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Sdk.Capabilities {
 public sealed class SoundMix {
  public readonly float Music,Effects;public readonly bool MusicMuted,EffectsMuted;
  public float MusicGain=>MusicMuted?0:Music;public float EffectsGain=>EffectsMuted?0:Effects;
  public SoundMix(float music=.25f,float effects=.5f,bool musicMuted=false,bool effectsMuted=false){RequireGain(music);RequireGain(effects);Music=music;Effects=effects;MusicMuted=musicMuted;EffectsMuted=effectsMuted;}
  public static float RequireGain(JToken token){if(token==null||token.Type!=JTokenType.Float&&token.Type!=JTokenType.Integer)throw new InvalidDataException("sound-mix-number");float value=(float)token;RequireGain(value);return value;}
  static void RequireGain(float value){if(float.IsNaN(value)||value<0||value>1)throw new InvalidDataException("sound-mix-range");}
  public JObject Json=>new JObject{{"music",Music},{"effects",Effects},{"music_muted",MusicMuted},{"effects_muted",EffectsMuted}};
  public static SoundMix Parse(JToken token){var value=token as JObject;if(value==null||!value.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(new[]{"effects","effects_muted","music","music_muted"})||value["music_muted"].Type!=JTokenType.Boolean||value["effects_muted"].Type!=JTokenType.Boolean)throw new InvalidDataException("sound-mix-fields");return new SoundMix(RequireGain(value["music"]),RequireGain(value["effects"]),(bool)value["music_muted"],(bool)value["effects_muted"]);}
 }
}
