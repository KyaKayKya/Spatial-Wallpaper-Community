using System;using System.IO;
namespace SpatialWallpaper.Sdk.Execution {
 // Conservative candidate estimates. They do not measure driver allocations or enforce process limits.
 public static class ResourceEstimates {
  public static ResourceCost Mesh(int vertices,int indices){
   if(vertices<0||vertices>1000000||indices<0||indices>3000000)throw new ArgumentOutOfRangeException("mesh-dimensions");
   return new ResourceCost(checked(4096L+vertices*96L+indices*8L),checked(4096L+vertices*144L+indices*8L),checked(65536L+vertices*144L+indices*8L),1);
  }
  public static ResourceCost EncodedMesh(byte[] bytes){
   SpatialWallpaper.Sdk.ResourceData.ValidateMesh(bytes);
   using(var reader=new BinaryReader(new MemoryStream(bytes))){
    reader.ReadUInt32();int vertices=reader.ReadInt32(),submeshes=reader.ReadInt32();reader.BaseStream.Position=checked(12L+vertices*72L);long indices=0;
    for(int i=0;i<submeshes;i++){int count=reader.ReadInt32();indices=checked(indices+count);reader.BaseStream.Position=checked(reader.BaseStream.Position+count*4L);}
    if(indices>3000000)throw new ArgumentOutOfRangeException("combined-mesh-indices");
    var cost=Mesh(vertices,(int)indices);return ResourceCost.Add(cost,new ResourceCost(checked(128L+bytes.Length*2L),0,0,0));
   }
  }
  // The packed immutable snapshot is already charged. Import uses native
  // scratch, retaining the complete existing native and GPU estimates.
  internal static ResourceCost BorrowedMesh(byte[] bytes){var cost=EncodedMesh(bytes);return new ResourceCost(16384,cost.NativeBytes,cost.GpuBytes,cost.Objects);}
  public static ResourceCost Texture(int width,int height,int format,int faces,int mips){
   if(width<1||height<1||width>8192||height>8192||faces!=1&&faces!=6||faces==6&&width!=height||format!=4&&format!=17&&format!=24)
    throw new ArgumentOutOfRangeException("texture-dimensions-or-format");
   int full=1;for(int size=Math.Max(width,height);size>1;size>>=1)full++;
   if(mips!=1&&mips!=full||format==24&&(faces!=1||mips!=1||width%4!=0||height%4!=0||width>4096||height>4096))
    throw new ArgumentOutOfRangeException("texture-mips-or-blocks");
   long payload=0;
   for(int mip=0;mip<mips;mip++){
    long w=Math.Max(1,width>>mip),h=Math.Max(1,height>>mip);
    payload=checked(payload+(format==24?((w+3)/4)*((h+3)/4)*16:w*h*(format==4?4:8)*faces));
   }
   return new ResourceCost(checked(4096+payload*8),checked(4096+payload*2),checked(65536+payload*2),1);
  }
  public static ResourceCost EncodedTexture(byte[] bytes){
   SpatialWallpaper.Sdk.ResourceData.ValidateTexture(bytes);
   using(var reader=new BinaryReader(new MemoryStream(bytes))){
    reader.ReadUInt32();int w=reader.ReadInt32(),h=reader.ReadInt32(),format=reader.ReadInt32();
    reader.BaseStream.Position=28;int faces=reader.ReadInt32(),mips=reader.ReadInt32();return Texture(w,h,format,faces,mips);
   }
  }
  // The trusted importer pins the already charged immutable snapshot; it does
  // not create a second managed pixel array. Native/driver estimates stay full.
  internal static ResourceCost BorrowedTexture(byte[] bytes){
   var cost=EncodedTexture(bytes);return BitConverter.ToInt32(bytes,28)==1?new ResourceCost(4096,cost.NativeBytes,cost.GpuBytes,cost.Objects):cost;
  }
  public static ResourceCost Material()=>new ResourceCost(4096,32768,65536,1);
  public static ResourceCost ShadowAtlases(int mainSize,int additionalSize){
   foreach(int size in new[]{mainSize,additionalSize})if(size<0||size>8192||size!=0&&(size&(size-1))!=0)throw new ArgumentOutOfRangeException("shadow-atlas-size");
   long pixels=checked((long)mainSize*mainSize+(long)additionalSize*additionalSize);int count=(mainSize==0?0:1)+(additionalSize==0?0:1);
   // Candidate estimate: two depth-buffer allocations at up to four bytes per pixel.
   // The fixed renderer owns these atlases, shared by its lights; never one atlas per light.
   return new ResourceCost(16384,checked(65536L+pixels*8),checked(65536L+pixels*8),count);
  }
  public static ResourceCost Node(int components){
   if(components<1||components>64)throw new ArgumentOutOfRangeException(nameof(components));
   return new ResourceCost(checked(1024L+components*1024L),checked(8192L+components*8192L),checked(4096L+components*4096L),1);
  }
  public static ResourceCost PostProcessing(int width,int height,int lutSize,bool effects){
   if(width<1||height<1||width>8192||height>8192||lutSize<16||lutSize>64)throw new ArgumentOutOfRangeException("post-processing-size");
   // Two HDR LUT allocations plus full-resolution equivalent scratch targets.
   // Bloom's paired mip chains and color/post targets share this fixed renderer estimate.
   long bytes=checked(65536L+(long)lutSize*lutSize*lutSize*16+(effects?(long)width*height*32:0));
   return new ResourceCost(16384,bytes,bytes,effects?24:2);
  }
  public static ResourceCost ShaderBundle(long encodedBytes,long decodedBytes){
   if(encodedBytes<1||encodedBytes>33554432||decodedBytes<1||decodedBytes>3L*33554432)throw new ArgumentOutOfRangeException("shader-size-or-decoded-budget");
   return new ResourceCost(checked(4096+encodedBytes*2+decodedBytes*4),checked(65536+decodedBytes*16),checked(65536+decodedBytes*64),1);
  }
 }
}
