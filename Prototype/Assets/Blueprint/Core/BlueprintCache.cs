using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SpatialWallpaper.Blueprint {
 public static class BlueprintCache {
  const int MaxCompressed=64*1024*1024,MaxDecoded=128*1024*1024;
  static void Text(BinaryWriter w,string s){byte[] bytes=Encoding.UTF8.GetBytes(s??"");if(bytes.Length>8192)throw new BlueprintImportException("cache-string-budget");w.Write((ushort)bytes.Length);w.Write(bytes);}
  static string Text(BinaryReader r){int size=r.ReadUInt16();if(size>8192)throw new BlueprintImportException("cache-string-budget");var bytes=r.ReadBytes(size);if(bytes.Length!=size)throw new EndOfStreamException();return Encoding.UTF8.GetString(bytes);}
  public static byte[] Write(CompiledBlueprint c,CancellationToken token){
   using(var raw=new MemoryStream()){
    using(var writer=new BinaryWriter(raw,Encoding.UTF8,true)){
     writer.Write(0x31504253);Text(writer,c.CacheKey);Text(writer,c.SourceHash);Text(writer,c.CatalogFingerprint);
     foreach(int v in new[]{c.Width,c.Height,c.Length,c.BlockCount,c.DataVersion,c.EntityCount,c.BlockEntityCount})writer.Write(v);
     writer.Write(c.MissingStates.Count);foreach(string s in c.MissingStates)Text(writer,s);writer.Write(c.Parts.Count);
     foreach(var p in c.Parts){token.ThrowIfCancellationRequested();Text(writer,p.Name);writer.Write((byte)p.Kind);writer.Write(p.Positions.Length/3);writer.Write(p.Indices.Length);
      foreach(float v in p.Positions)writer.Write(v);foreach(float v in p.Normals)writer.Write(v);foreach(float v in p.Uv)writer.Write(v);writer.Write(p.Colors);foreach(int i in p.Indices)writer.Write(checked((ushort)i));
      if(raw.Length>MaxDecoded-32)throw new BlueprintImportException("cache-budget");
     }
    }
    byte[] bytes=raw.ToArray(),digest;using(var sha=SHA256.Create())digest=sha.ComputeHash(bytes);
    using(var output=new MemoryStream()){
     using(var zip=new GZipStream(output,CompressionLevel.Fastest,true)){zip.Write(bytes,0,bytes.Length);zip.Write(digest,0,digest.Length);}
     if(output.Length>MaxCompressed)throw new BlueprintImportException("cache-budget");return output.ToArray();
    }
   }
  }
  public static CompiledBlueprint Read(byte[] input,string expectedKey){
   try{
    if(input==null||input.Length>MaxCompressed)throw new BlueprintImportException("cache-budget");byte[] bytes;
    using(var gzip=new GZipStream(new MemoryStream(input,false),CompressionMode.Decompress))using(var raw=new MemoryStream()){
     var buffer=new byte[32768];int n;while((n=gzip.Read(buffer,0,buffer.Length))>0){if(raw.Length+n>MaxDecoded)throw new BlueprintImportException("cache-budget");raw.Write(buffer,0,n);}bytes=raw.ToArray();
    }
    if(bytes.Length<36)throw new BlueprintImportException("cache-invalid");using(var sha=SHA256.Create()){var hash=sha.ComputeHash(bytes,0,bytes.Length-32);for(int i=0;i<32;i++)if(hash[i]!=bytes[bytes.Length-32+i])throw new BlueprintImportException("cache-digest");}
    using(var r=new BinaryReader(new MemoryStream(bytes,0,bytes.Length-32,false))){
     if(r.ReadInt32()!=0x31504253)throw new BlueprintImportException("cache-version");string key=Text(r);if(key!=expectedKey)throw new BlueprintImportException("cache-identity");
     var c=new CompiledBlueprint{CacheKey=key,SourceHash=Text(r),CatalogFingerprint=Text(r),Width=r.ReadInt32(),Height=r.ReadInt32(),Length=r.ReadInt32(),BlockCount=r.ReadInt32(),DataVersion=r.ReadInt32(),EntityCount=r.ReadInt32(),BlockEntityCount=r.ReadInt32()};NbtInput.CheckDimensions(c.Width,c.Height,c.Length);
     int count=r.ReadInt32();if(count<0||count>NbtInput.MaxPalette)throw new BlueprintImportException("cache-budget");for(int i=0;i<count;i++)c.MissingStates.Add(Text(r));
     int parts=r.ReadInt32(),total=0;if(parts<0||parts>8192)throw new BlueprintImportException("cache-budget");
     for(int i=0;i<parts;i++){
      var p=new BlueprintMeshPart{Name=Text(r),Kind=(BlockRenderKind)r.ReadByte()};int vertices=r.ReadInt32(),indices=r.ReadInt32();total+=vertices;
      if(p.Kind<BlockRenderKind.Opaque||p.Kind>BlockRenderKind.Glow||vertices<0||vertices>60000||total>BlockMeshCompiler.MaxVertices||indices<0||indices%3!=0||indices>vertices/4*6||(long)vertices*36+(long)indices*2>r.BaseStream.Length-r.BaseStream.Position)throw new BlueprintImportException("cache-budget");
      p.Positions=Floats(r,vertices*3,-2,514);p.Normals=Floats(r,vertices*3,-1.01f,1.01f);p.Uv=Floats(r,vertices*2,0,1);p.Colors=r.ReadBytes(vertices*4);if(p.Colors.Length!=vertices*4)throw new EndOfStreamException();p.Indices=new int[indices];for(int j=0;j<indices;j++){int value=r.ReadUInt16();if(value>=vertices)throw new BlueprintImportException("cache-index");p.Indices[j]=value;}c.Parts.Add(p);
     }
     if(r.BaseStream.Position!=r.BaseStream.Length)throw new BlueprintImportException("cache-trailing-data");return c;
    }
   }catch(BlueprintImportException){throw;}catch(Exception e)when(e is IOException||e is ArgumentException||e is OverflowException){throw new BlueprintImportException("cache-invalid: "+e.Message);}
  }
  static float[] Floats(BinaryReader r,int count,float low,float high){var values=new float[count];for(int i=0;i<count;i++){float v=r.ReadSingle();if(float.IsNaN(v)||float.IsInfinity(v)||v<low||v>high)throw new BlueprintImportException("cache-value");values[i]=v;}return values;}
 }
}
