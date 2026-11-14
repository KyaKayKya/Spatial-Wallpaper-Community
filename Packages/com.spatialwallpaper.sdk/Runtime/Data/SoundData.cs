using System;using System.IO;
namespace SpatialWallpaper.Sdk {
 // Normalized mono PCM16 at 22050 Hz. Two 20 ms groups travel separately so
 // only the trusted output process can apply the user's music/effect mix.
 public static class SoundFormat {
  public const uint Magic=0x31415753;public const int Rate=22050,Frames=441,PacketBytes=Frames*4,MaxSamples=Rate*300;
  public static short Read(byte[] bytes,int at)=>(short)(bytes[at]|bytes[at+1]<<8);
  public static void Write(byte[] bytes,int at,int value){value=Math.Max(short.MinValue,Math.Min(short.MaxValue,value));bytes[at]=(byte)value;bytes[at+1]=(byte)(value>>8);}
 }
 public sealed class SoundClipData {
  readonly byte[] bytes;public int Samples{get;}
  public SoundClipData(byte[] admitted){
   if(admitted==null||admitted.Length<14||BitConverter.ToUInt32(admitted,0)!=SoundFormat.Magic||BitConverter.ToInt32(admitted,4)!=SoundFormat.Rate)throw new InvalidDataException("sound-header");
   Samples=BitConverter.ToInt32(admitted,8);if(Samples<1||Samples>SoundFormat.MaxSamples||12L+Samples*2L!=admitted.Length)throw new InvalidDataException("sound-size");bytes=admitted;
  }
  internal short At(int sample)=>SoundFormat.Read(bytes,12+sample*2);
 }
 public sealed class SoundMixer {
  sealed class Voice {internal SoundClipData Clip;internal int At;internal float Gain;}
  readonly Voice music=new Voice();readonly Voice[] effects=new Voice[8];int next;
  public SoundMixer(){for(int i=0;i<effects.Length;i++)effects[i]=new Voice();}
  static void Start(Voice voice,SoundClipData clip,float gain){if(clip==null||float.IsNaN(gain)||gain<0||gain>1)throw new ArgumentException("sound-clip-or-gain");voice.Clip=clip;voice.At=0;voice.Gain=gain;}
  public void PlayMusic(SoundClipData clip,float gain)=>Start(music,clip,gain);
  public void PlayEffect(SoundClipData clip,float gain){int at=Array.FindIndex(effects,v=>v.Clip==null);if(at<0)at=next;Start(effects[at],clip,gain);next=(at+1)%effects.Length;}
  public void StopMusic(){music.Clip=null;}
  public void StopEffects(){foreach(var voice in effects)voice.Clip=null;}
  public bool Active=>music.Clip!=null||ActiveEffects>0;
  public int ActiveEffects{get{int n=0;foreach(var voice in effects)if(voice.Clip!=null)n++;return n;}}
  static int Sample(Voice voice,bool loop){if(voice.Clip==null)return 0;int sample=(int)(voice.Clip.At(voice.At++)*voice.Gain);if(voice.At==voice.Clip.Samples){voice.At=0;if(!loop)voice.Clip=null;}return sample;}
  public void Render(byte[] packet){
   if(packet==null||packet.Length!=SoundFormat.PacketBytes)throw new ArgumentException("sound-packet-size");
   for(int i=0;i<SoundFormat.Frames;i++){SoundFormat.Write(packet,i*2,Sample(music,true));int fx=0;foreach(var voice in effects)fx+=Sample(voice,false);SoundFormat.Write(packet,SoundFormat.Frames*2+i*2,fx);}
  }
 }
}
