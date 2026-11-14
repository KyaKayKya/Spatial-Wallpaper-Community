using System;
using System.IO;
namespace SpatialWallpaper.Sdk.Capabilities {
 public enum AudioFeatureState { Active=1,SourceEnded=2,SourceRejected=3,Unavailable=4,Fault=5 }
 // Four coarse bands only. All freshness checks use the original capture QPC
 // converted to 100 ns units, shared across processes on the same machine.
 public sealed class AudioFrames {
  readonly byte[] bands=new byte[4];ulong revision,captured,clock;
  public AudioFeatureState State{get;private set;}=AudioFeatureState.Unavailable;
  static uint U32(byte[] b,int at)=>(uint)(b[at]|b[at+1]<<8|b[at+2]<<16|b[at+3]<<24);
  static ulong U64(byte[] b,int at)=>U32(b,at)|((ulong)U32(b,at+4)<<32);
  void Clear(AudioFeatureState state){Array.Clear(bands,0,4);captured=0;State=state;}
  public float GetBand(int index){if(index<0||index>=4)throw new ArgumentOutOfRangeException(nameof(index));return bands[index]/15f;}
  public void Advance(byte[] bytes,ulong now,bool connected){
   if(now<clock||!connected){Clear(AudioFeatureState.Unavailable);return;}clock=now;
   if(captured!=0&&now-captured>4000000)Clear(State);
   if(bytes==null)return;
   try{
    if(bytes.Length!=40||U32(bytes,0)!=0x46415753||U32(bytes,4)!=2)throw new InvalidDataException("audio-snapshot-shape");
    ulong incoming=U64(bytes,8),sample=U64(bytes,24);uint state=U32(bytes,32);bool nonzero=false;
    if(incoming==0||state<1||state>5||sample>now)throw new InvalidDataException("audio-snapshot-fields");
    for(int i=36;i<40;i++){if(bytes[i]>15)throw new InvalidDataException("audio-feature-range");nonzero|=bytes[i]!=0;}
    if(state!=1&&(sample!=0||nonzero)||nonzero&&sample==0)throw new InvalidDataException("audio-snapshot-state");
    if(incoming<=revision)return;
    revision=incoming;Clear((AudioFeatureState)state);
    if(state!=1||sample==0||now-sample>4000000)return;
    captured=sample;Array.Copy(bytes,36,bands,0,4);
   }catch{Clear(AudioFeatureState.Fault);throw;}
  }
 }
}
