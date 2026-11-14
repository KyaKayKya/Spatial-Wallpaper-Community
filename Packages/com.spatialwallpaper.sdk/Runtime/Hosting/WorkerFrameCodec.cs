using System;using System.IO;
namespace SpatialWallpaper.Sdk.Hosting {
 public sealed class ProjectionConfiguration {
  public long Revision{get;}public int Width{get;}public int Height{get;}public float DisplayWidth{get;}public float DisplayHeight{get;}public float EyeX{get;}public float EyeY{get;}public float EyeZ{get;}public bool PreserveWidth{get;}
  static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
  public ProjectionConfiguration(long revision,int width,int height,float displayWidth,float displayHeight,float eyeX,float eyeY,float eyeZ,bool preserveWidth=false){
   WorkerFrameCodec.PayloadBytes(width,height);
   if(revision<1||!Finite(displayWidth)||!Finite(displayHeight)||displayWidth<.01f||displayHeight<.01f||displayWidth>10||displayHeight>10||Math.Abs((double)displayWidth/displayHeight-(double)width/height)>.0001||!Finite(eyeX)||!Finite(eyeY)||!Finite(eyeZ)||eyeZ>=-.05f||(double)eyeX*eyeX+(double)eyeY*eyeY+(double)eyeZ*eyeZ>100)throw new ArgumentException("projection-values");
   Revision=revision;Width=width;Height=height;DisplayWidth=displayWidth;DisplayHeight=displayHeight;EyeX=eyeX;EyeY=eyeY;EyeZ=eyeZ;PreserveWidth=preserveWidth;
  }
 }
 public static class WorkerProjectionCodec {
  public const int Bytes=96,Version=1;
  static void Need(bool value,string reason){if(!value)throw new InvalidDataException(reason);}
  static void Put(byte[] bytes,int at,ulong value,int count){for(int i=0;i<count;i++)bytes[at+i]=(byte)(value>>(i*8));}
  static ulong Get(byte[] bytes,int at,int count){ulong value=0;for(int i=0;i<count;i++)value|=(ulong)bytes[at+i]<<(i*8);return value;}
  static void Float(byte[] bytes,int at,float value){var bits=BitConverter.GetBytes(value);if(!BitConverter.IsLittleEndian)Array.Reverse((Array)bits);Buffer.BlockCopy(bits,0,bytes,at,4);}
  static float Float(byte[] bytes,int at){var bits=new byte[4];Buffer.BlockCopy(bytes,at,bits,0,4);if(!BitConverter.IsLittleEndian)Array.Reverse((Array)bits);return BitConverter.ToSingle(bits,0);}
  public static byte[] Encode(WorkerIdentity identity,ProjectionConfiguration value){
   if(identity==null||value==null)throw new ArgumentNullException();var bytes=new byte[Bytes];Put(bytes,0,0x53575031,4);Put(bytes,4,1,2);Buffer.BlockCopy(identity.AttemptBytes,0,bytes,8,16);Buffer.BlockCopy(identity.InstanceBytes,0,bytes,24,16);Put(bytes,40,identity.Generation,4);Put(bytes,44,(ulong)value.Revision,8);Put(bytes,52,(uint)value.Width,4);Put(bytes,56,(uint)value.Height,4);Float(bytes,60,value.DisplayWidth);Float(bytes,64,value.DisplayHeight);Float(bytes,68,value.EyeX);Float(bytes,72,value.EyeY);Float(bytes,76,value.EyeZ);Put(bytes,80,value.PreserveWidth?1u:0u,4);return bytes;
  }
  public static ProjectionConfiguration Decode(byte[] bytes,WorkerIdentity identity,long previous){
   Need(bytes!=null&&bytes.Length==Bytes&&identity!=null&&previous>=0,"projection-buffer");Need(Get(bytes,0,4)==0x53575031&&Get(bytes,4,4)==1,"projection-version");
   int difference=0;for(int i=0;i<16;i++)difference|=(bytes[8+i]^identity.AttemptBytes[i])|(bytes[24+i]^identity.InstanceBytes[i]);Need(difference==0&&Get(bytes,40,4)==identity.Generation,"projection-identity");
   ulong revision=Get(bytes,44,8);Need(revision>0&&revision<=long.MaxValue&&revision>(ulong)previous,"projection-revision");Need(Get(bytes,80,4)<=1,"projection-fit");for(int i=84;i<Bytes;i++)Need(bytes[i]==0,"projection-reserved");
   try{return new ProjectionConfiguration((long)revision,checked((int)Get(bytes,52,4)),checked((int)Get(bytes,56,4)),Float(bytes,60),Float(bytes,64),Float(bytes,68),Float(bytes,72),Float(bytes,76),Get(bytes,80,4)==1);}
   catch(ArgumentException error){throw new InvalidDataException("projection-values",error);}catch(OverflowException error){throw new InvalidDataException("projection-values",error);}
  }
 }
 // Created only by the trusted receiver after validating a complete transfer.
 // Pixel storage never escapes: a later caller cannot relabel or mutate it.
 public sealed class RuntimeFrame {
  readonly byte[] pixels;public WorkerIdentity Identity{get;}public string BuildId{get;}public int Width{get;}public int Height{get;}public long Sequence{get;}public long ProjectionRevision{get;}
  internal RuntimeFrame(WorkerIdentity identity,string build,int width,int height,long sequence,long projectionRevision,byte[] ownedPixels){Identity=identity;BuildId=build;Width=width;Height=height;Sequence=sequence;ProjectionRevision=projectionRevision;pixels=ownedPixels;}
  public void CopyPixels(byte[] destination){if(destination==null||destination.Length!=pixels.Length)throw new ArgumentException("presenter-buffer-size");Buffer.BlockCopy(pixels,0,destination,0,pixels.Length);}
  internal void CopyPixelWords(int[] destination){if(destination==null||(long)destination.Length*4!=pixels.Length)throw new ArgumentException("presenter-buffer-size");Buffer.BlockCopy(pixels,0,destination,0,pixels.Length);}
 }
 // A distinct inherited, read-only control lane. No author binding exposes this protocol.
 public static class WorkerStopCodec {
  public const int Bytes=48;
  public static byte[] Encode(WorkerIdentity identity){if(identity==null)throw new ArgumentNullException(nameof(identity));var b=new byte[Bytes];b[0]=83;b[1]=87;b[2]=83;b[3]=1;Buffer.BlockCopy(identity.AttemptBytes,0,b,4,16);Buffer.BlockCopy(identity.InstanceBytes,0,b,20,16);for(int i=0;i<4;i++)b[36+i]=(byte)(identity.Generation>>(8*i));b[40]=1;return b;}
  public static void Validate(byte[] bytes,WorkerIdentity identity){var expected=Encode(identity);if(bytes==null||bytes.Length!=Bytes)throw new InvalidDataException("stop-size");int difference=0;for(int i=0;i<Bytes;i++)difference|=bytes[i]^expected[i];if(difference!=0)throw new InvalidDataException("stop-identity-or-format");}
 }
 public sealed class WorkerIdentity {
  public Guid Attempt{get;}public Guid Instance{get;}public uint Generation{get;}
  internal readonly byte[] AttemptBytes,InstanceBytes;
  public WorkerIdentity(Guid attempt,Guid instance,uint generation){
   if(attempt==Guid.Empty||instance==Guid.Empty||generation==0)throw new ArgumentException("worker-identity");
   Attempt=attempt;Instance=instance;Generation=generation;AttemptBytes=attempt.ToByteArray();InstanceBytes=instance.ToByteArray();
  }
 }
 // RGBA8 rows are top-to-bottom. Pixel lane only. IPC handle ownership binds the installation; identities reject stale/cross-instance messages.
 public static class WorkerFrameCodec {
  public const int Version=2,HeaderBytes=72,MaxPayloadBytes=64*1024*1024;
  const uint Magic=0x53574632;
  static void Need(bool value,string reason){if(!value)throw new InvalidDataException(reason);}
  public static int PayloadBytes(int width,int height){
   Need(width>0&&height>0&&width<=8192&&height<=8192,"frame-dimensions");
   long bytes=(long)width*height*4;Need(bytes<=MaxPayloadBytes,"frame-payload-limit");return (int)bytes;
  }
  public static void FlipVerticalRows(byte[] pixels,int width,int height,byte[] scratch){
   int bytes=PayloadBytes(width,height),stride=checked(width*4);
   Need(pixels!=null&&pixels.Length==bytes&&scratch!=null&&scratch.Length>=stride&&!ReferenceEquals(pixels,scratch),"frame-row-buffer");
   for(int y=0;y<height/2;y++){
    int top=y*stride,bottom=(height-1-y)*stride;
    Buffer.BlockCopy(pixels,top,scratch,0,stride);Buffer.BlockCopy(pixels,bottom,pixels,top,stride);Buffer.BlockCopy(scratch,0,pixels,bottom,stride);
   }
  }
  static uint U32(byte[] data,int offset)=>(uint)data[offset]|(uint)data[offset+1]<<8|(uint)data[offset+2]<<16|(uint)data[offset+3]<<24;
  static ulong U64(byte[] data,int offset){ulong value=0;for(int i=0;i<8;i++)value|=(ulong)data[offset+i]<<(i*8);return value;}
  static void Put(byte[] data,int offset,ulong value,int count){for(int i=0;i<count;i++)data[offset+i]=(byte)(value>>(i*8));}
  static bool Match(byte[] header,int offset,byte[] expected){int difference=0;for(int i=0;i<16;i++)difference|=header[offset+i]^expected[i];return difference==0;}
  public static void Encode(byte[] header,WorkerIdentity identity,int width,int height,long sequence,long projectionRevision=0){
   Need(header!=null&&header.Length==HeaderBytes&&identity!=null,"frame-header-buffer");Need(sequence>0,"frame-sequence");int bytes=PayloadBytes(width,height);
   Need(projectionRevision>=0,"frame-projection-revision");Put(header,0,Magic,4);Put(header,4,2,2);Put(header,6,1,2);Put(header,8,(uint)width,4);Put(header,12,(uint)height,4);Put(header,16,(uint)bytes,4);Put(header,64,(ulong)projectionRevision,8);
   Buffer.BlockCopy(identity.AttemptBytes,0,header,20,16);Buffer.BlockCopy(identity.InstanceBytes,0,header,36,16);Put(header,52,identity.Generation,4);Put(header,56,(ulong)sequence,8);
  }
  public static long Validate(byte[] header,WorkerIdentity identity,int width,int height,long previousSequence){
   long revision;return Validate(header,identity,width,height,previousSequence,out revision);
  }
  public static long Validate(byte[] header,WorkerIdentity identity,int width,int height,long previousSequence,out long projectionRevision){
   Need(header!=null&&header.Length==HeaderBytes&&identity!=null,"frame-header-buffer");Need(previousSequence>=0,"frame-previous-sequence");int bytes=PayloadBytes(width,height);
   Need(U32(header,0)==Magic&&header[4]==2&&header[5]==0&&header[6]==1&&header[7]==0,"frame-version-or-format");
   Need(U32(header,8)==(uint)width&&U32(header,12)==(uint)height&&U32(header,16)==(uint)bytes,"frame-shape-or-length");
   Need(Match(header,20,identity.AttemptBytes)&&Match(header,36,identity.InstanceBytes)&&U32(header,52)==identity.Generation,"frame-session");
   ulong revision=U64(header,64);Need(revision<=long.MaxValue,"frame-projection-revision");projectionRevision=(long)revision;
   ulong next=U64(header,56);Need(next>0&&next<=long.MaxValue&&next>(ulong)previousSequence,"frame-sequence");return (long)next;
  }
 }
}
