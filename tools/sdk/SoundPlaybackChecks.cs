using System;using System.IO;using SpatialWallpaper.Sdk;using SpatialWallpaper.Supervision.Broker;
static class SoundPlaybackChecks {
 static int count;static void Need(bool value,string why){if(!value)throw new Exception(why);count++;}
 static byte[] Clip(params short[] samples){using(var m=new MemoryStream())using(var w=new BinaryWriter(m)){w.Write(SoundFormat.Magic);w.Write(SoundFormat.Rate);w.Write(samples.Length);foreach(short s in samples)w.Write(s);return m.ToArray();}}
 static short Read(byte[] b,int at)=>BitConverter.ToInt16(b,at);
 static void Main(string[] args){
  if(args.Length==1&&args[0]=="--device"){
   using(var sound=new SoundOutput()){sound.Configure(1,0);byte[] data=new byte[SoundFormat.PacketBytes];Console.WriteLine("READY pid="+System.Diagnostics.Process.GetCurrentProcess().Id);Console.Out.Flush();for(int n=0;n<400;n++){for(int i=0;i<SoundFormat.Frames;i++)SoundFormat.Write(data,i*2,(int)(32767*.005*Math.Sin(2*Math.PI*440*(n*SoundFormat.Frames+i)/SoundFormat.Rate)));sound.Submit(data,data.Length);System.Threading.Thread.Sleep(20);}if(sound.Error!=null)throw new Exception(sound.Error);Console.WriteLine("submitted="+sound.Submitted);}return;
  }
  var clip=new SoundClipData(Clip(1000,-1000));var mixer=new SoundMixer();mixer.PlayMusic(clip,.5f);byte[] packet=new byte[SoundFormat.PacketBytes];mixer.Render(packet);
  Need(Read(packet,0)==500&&Read(packet,2)==-500&&Read(packet,880)==500,"music-loop-gain");Need(Read(packet,882)==0,"empty-effects");
  for(int i=0;i<20;i++)mixer.PlayEffect(clip,1);Need(mixer.ActiveEffects==8,"bounded-effects");mixer.Render(packet);Need(Read(packet,882)==8000&&Read(packet,886)==0,"effects-finish-and-sum");
  mixer.StopMusic();mixer.Render(packet);Need(Array.TrueForAll(packet,b=>b==0),"stop-silent");
  bool rejected=false;try{mixer.PlayMusic(clip,float.NaN);}catch(ArgumentException){rejected=true;}Need(rejected,"nonfinite-gain");
  rejected=false;try{new SoundClipData(new byte[12]);}catch(InvalidDataException){rejected=true;}Need(rejected,"invalid-clip-header");
  var valid=Clip(1,2);Array.Resize(ref valid,valid.Length+1);rejected=false;try{new SoundClipData(valid);}catch(InvalidDataException){rejected=true;}Need(rejected,"clip-tail");
  mixer.PlayMusic(clip,1);mixer.PlayEffect(clip,1);mixer.Render(packet);byte[] output=new byte[SoundFormat.Frames*2];SoundOutput.Mix(packet,output,.25f,.5f,false);Need(Read(output,0)==750&&Read(output,2)==-750,"independent-groups");
  SoundOutput.Mix(packet,output,0,0,false);Need(Array.TrueForAll(output,b=>b==0),"mute");SoundOutput.Mix(packet,output,1,1,true);Need(Array.TrueForAll(output,b=>b==0),"pause");
  Console.WriteLine("passed "+count+" sound playback checks");
 }
}
