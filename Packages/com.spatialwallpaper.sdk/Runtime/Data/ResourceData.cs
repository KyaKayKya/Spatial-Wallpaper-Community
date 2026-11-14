using System;
using System.IO;
namespace SpatialWallpaper.Sdk {
 public static class ResourceData {
  public static void ValidateMesh(byte[] bytes){
   using(var r=new BinaryReader(new MemoryStream(bytes))){
    JsonData.Require(r.ReadUInt32()==0x314d5753,"mesh-header");int vertices=r.ReadInt32(),submeshes=r.ReadInt32();JsonData.Require(vertices>0&&vertices<=1000000&&submeshes>0&&submeshes<=64,"mesh-count");
    // position(3), normal(3), tangent(4), uv(2), lightmap uv(2), color(4)
    JsonData.Require((long)vertices*18*4<=bytes.Length-12,"mesh-vertex-range");
    for(int i=0;i<vertices*18;i++){float value=r.ReadSingle();JsonData.Require(!float.IsNaN(value)&&!float.IsInfinity(value)&&Math.Abs(value)<=1000000,"mesh-number");}
    for(int s=0;s<submeshes;s++){int n=r.ReadInt32();JsonData.Require(n>0&&n%3==0&&n<=3000000&&(long)n*4<=r.BaseStream.Length-r.BaseStream.Position,"mesh-index-count");for(int j=0;j<n;j++){int index=r.ReadInt32();JsonData.Require(index>=0&&index<vertices,"mesh-index");}}
    JsonData.Require(r.BaseStream.Position==bytes.Length,"mesh-tail");
   }
  }
  public static void ValidateTexture(byte[] bytes){
   using(var r=new BinaryReader(new MemoryStream(bytes))){
    JsonData.Require(r.ReadUInt32()==0x32545753,"texture-header");int width=r.ReadInt32(),height=r.ReadInt32(),format=r.ReadInt32(),linear=r.ReadInt32(),wrap=r.ReadInt32(),filter=r.ReadInt32(),faces=r.ReadInt32(),mips=r.ReadInt32(),aniso=r.ReadInt32();float bias=r.ReadSingle();
    JsonData.Require(width>0&&height>0&&width<=8192&&height<=8192&&(format==4||format==17||format==24)&&linear>=0&&linear<=1&&wrap>=0&&wrap<=3&&filter>=0&&filter<=2,"texture-config");
    if(format==24)JsonData.Require(width%4==0&&height%4==0&&width<=4096&&height<=4096&&faces==1&&mips==1&&linear==1,"bc6-config");
    int full=1;for(int size=Math.Max(width,height);size>1;size>>=1)full++;
    JsonData.Require((faces==1||faces==6&&width==height&&format==17&&linear==1)&&(mips==1||mips==full)&&aniso>=0&&aniso<=16&&!float.IsNaN(bias)&&Math.Abs(bias)<=16,"texture-mips-or-sampling");
    long sizeBytes=44;for(int mip=0;mip<mips;mip++)sizeBytes+=format==24?(long)((width+3)/4)*((height+3)/4)*16:(long)Math.Max(1,width>>mip)*Math.Max(1,height>>mip)*(format==4?4:8)*faces;
    JsonData.Require(sizeBytes==bytes.Length,"texture-size");
    if(format==17)for(int i=44;i<bytes.Length;i+=2)JsonData.Require((BitConverter.ToUInt16(bytes,i)&0x7C00)!=0x7C00,"texture-non-finite-half");
    // BC6H UF16 cannot encode NaN/Inf. Restrict mode bits to the 14 defined
    // D3D11 modes; reject the four reserved encodings before GPU upload.
    if(format==24)for(int i=44;i<bytes.Length;i+=16){int mode=bytes[i]&31;JsonData.Require((mode&3)!=3||mode<19,"bc6-reserved-mode");}
   }
  }
 }
}
