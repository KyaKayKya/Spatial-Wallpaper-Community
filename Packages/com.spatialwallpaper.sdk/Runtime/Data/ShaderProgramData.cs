using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace SpatialWallpaper.Sdk {
 internal static class ShaderProgramData {
  const int Limit=32*1024*1024;
  static void Need(bool value,string reason){JsonData.Require(value,"shader-program:"+reason);}
  sealed class Entry {public int Offset,Length,Segment,Index;}
  internal static long Validate(IReadOnlyList<byte[]> segments,HashSet<int> programs,HashSet<int> parameters){
   Need(segments.Count>0&&programs.Count>0,"empty-program");var table=new Cursor(segments[0]);int count=table.Count(65536);Need(count>0&&count<=(table.Left/12),"entry-count");
   var entries=new List<Entry>();
   for(int i=0;i<count;i++)entries.Add(new Entry{Offset=table.Count(Limit),Length=table.Count(Limit),Segment=table.Count(segments.Count-1),Index=i});
   Need(!programs.Overlaps(parameters)&&programs.Concat(parameters).All(i=>i>=0&&i<count),"program-or-parameter-index");
   Need(programs.Count+parameters.Count==count,"unreferenced-entry");
   long parameterBudget=0;
   for(int s=0;s<segments.Count;s++){
    int end=s==0?table.Position:0;var bytes=segments[s];
    foreach(var entry in entries.Where(e=>e.Segment==s).OrderBy(e=>e.Offset)){
     Need(entry.Length>0&&entry.Offset>=end&&entry.Offset<=bytes.Length&&entry.Length<=bytes.Length-entry.Offset,"entry-range");
     for(int i=end;i<entry.Offset;i++)Need(bytes[i]==0,"entry-padding");
     var payload=new byte[entry.Length];Array.Copy(bytes,entry.Offset,payload,0,payload.Length);var reader=new Cursor(payload);Need(reader.UInt()==202012090,"version");
     if(programs.Contains(entry.Index))Gpu(reader);else Parameters(reader,ref parameterBudget);
     Need(reader.Left==0,"entry-tail");end=entry.Offset+entry.Length;
    }
    for(int i=end;i<bytes.Length;i++)Need(bytes[i]==0,"segment-tail");
   }
   return parameterBudget;
  }
  static void Gpu(Cursor r){
   int type=r.Count(22);Need(type>=15,"not-d3d11-program");r.Skip(16);
   int keywords=r.Count(256);for(int i=0;i<keywords;i++){r.String(128);r.Align();}
   var code=r.Bytes(r.Count(Limit));r.Align();Dxbc(code);
   r.UInt();int bindings=r.Count(32);for(int i=0;i<bindings;i++){r.Count(31);r.Count(31);}
  }
  static void Parameters(Cursor r,ref long budget){
   int groups=r.Count(4096);Need(groups>0,"parameter-groups");
   for(int i=0;i<groups;i++){
    r.String(4096);int declaredSize=r.Count(65536);Charge(ref budget,(long)declaredSize*16);int parameters=r.Count(16384);for(int p=0;p<parameters;p++)Parameter(r,ref budget);
    int structs=r.Count(4096);for(int s=0;s<structs;s++){
     r.String(4096);r.Count(65536);int array=r.Count(65536),size=r.Count(65536);Charge(ref budget,(long)Math.Max(1,array)*size*16);
     int fields=r.Count(16384);for(int f=0;f<fields;f++)Parameter(r,ref budget);
    }
   }
   int resources=r.Count(4096);for(int i=0;i<resources;i++){
    r.String(4096);int type=r.Count(4);r.Count(65535);r.UInt();if(type==0){uint dimension=r.UInt();Need((dimension>>1)<=6,"texture-dimension");}
   }
  }
  static void Charge(ref long budget,long bytes){Need(bytes>=0&&bytes<=Limit-budget,"aggregate-parameter-expansion");budget+=bytes;}
  static void Parameter(Cursor r,ref long budget){
   r.String(4096);r.Count(5);int rows=r.Count(4),columns=r.Count(4);r.Count(1);int array=r.Count(65536);r.Count(65536);
   Charge(ref budget,(long)Math.Max(1,array)*Math.Max(1,rows)*Math.Max(1,columns)*16);
  }
  static void Dxbc(byte[] code){
   // The pinned 2022.3 D3D11 compiler emits a fixed 38-byte Unity header.
   Need(code.Length>=70&&Encoding.ASCII.GetString(code,38,4)=="DXBC","d3d11-container");
   var bytes=new byte[code.Length-38];Array.Copy(code,38,bytes,0,bytes.Length);var r=new Cursor(bytes);r.Skip(20);Need(r.UInt()==1&&r.UInt()==bytes.Length,"dxbc-header");
   int count=r.Count(64);Need(count>0&&count<=r.Left/4,"dxbc-chunks");var offsets=new List<int>();for(int i=0;i<count;i++)offsets.Add(r.Count(bytes.Length));int end=r.Position;
   foreach(int offset in offsets.OrderBy(o=>o)){
    Need(offset>=end&&offset%4==0&&offset<=bytes.Length-8,"dxbc-chunk-range");var chunk=new Cursor(bytes){Position=offset};string kind=Encoding.ASCII.GetString(chunk.Bytes(4));int length=chunk.Count(bytes.Length);Need(length<=chunk.Left,"dxbc-chunk-size");
    if(kind=="SHEX"||kind=="SHDR"){Need(length>=8&&length%4==0,"dxbc-instructions");uint version=chunk.UInt();Need((version>>16)<=4&&chunk.UInt()==length/4,"dxbc-instruction-length");}
    end=offset+8+length;
   }
   Need(end==bytes.Length,"dxbc-tail");
  }
  sealed class Cursor {
   readonly byte[] bytes;public int Position;public int Left=>bytes.Length-Position;
   public Cursor(byte[] data){bytes=data;}
   public uint UInt(){Need(Left>=4,"truncated");uint value=BitConverter.ToUInt32(bytes,Position);Position+=4;return value;}
   public int Count(int maximum){uint n=UInt();Need(n<=maximum,"count="+n+",maximum="+maximum+",position="+Position);return (int)n;}
   public void Skip(int n){Need(n>=0&&n<=Left,"truncated");Position+=n;}
   public byte[] Bytes(int n){Need(n>=0&&n<=Left,"truncated");var data=new byte[n];Array.Copy(bytes,Position,data,0,n);Position+=n;return data;}
   public void Align(){int n=(4-Position%4)%4;foreach(byte value in Bytes(n))Need(value==0,"alignment");}
   public string String(int maximum){var data=Bytes(Count(maximum));string text;try{text=new UTF8Encoding(false,true).GetString(data);}catch(DecoderFallbackException e){throw new InvalidDataException("shader-program:string",e);}Need(text.IndexOf('\0')<0,"string-null");Align();return text;}
  }
 }
}
