using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
namespace SpatialWallpaper.Sdk.Execution {
 // Adapted from the repository's schema2 ContentLoader decoders. Input must be admitted immutable bytes.
 internal static class NativeResourceReader {
  public static Mesh Mesh(byte[] bytes,Action<UnityEngine.Object> track){
   using(var r=new BinaryReader(new MemoryStream(bytes))){r.ReadUInt32();int count=r.ReadInt32(),submeshes=r.ReadInt32();
    var mesh=new Mesh();track(mesh);
    mesh.SetVertexBufferParams(count,new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3),new VertexAttributeDescriptor(VertexAttribute.Normal,VertexAttributeFormat.Float32,3),new VertexAttributeDescriptor(VertexAttribute.Tangent,VertexAttributeFormat.Float32,4),new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2),new VertexAttributeDescriptor(VertexAttribute.TexCoord1,VertexAttributeFormat.Float32,2),new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.Float32,4));
    // The immutable snapshot owns packed bytes; native scratch stays in the
    // original native estimate. No managed channel or encoded-byte copy.
    var vertices=new NativeArray<float>(checked(count*18),Allocator.Temp,NativeArrayOptions.UninitializedMemory);try{
     // Unity orders Color before texture coordinates in an interleaved stream.
     for(int i=0;i<count;i++){int at=i*18;for(int j=0;j<10;j++)vertices[at+j]=r.ReadSingle();float u=r.ReadSingle(),v=r.ReadSingle(),u2=r.ReadSingle(),v2=r.ReadSingle();for(int j=0;j<4;j++)vertices[at+10+j]=r.ReadSingle();vertices[at+14]=u;vertices[at+15]=v;vertices[at+16]=u2;vertices[at+17]=v2;}
     mesh.SetVertexBufferData(vertices,0,0,vertices.Length);
    }finally{vertices.Dispose();}
    long indexStart=checked(12L+count*72L);r.BaseStream.Position=indexStart;int total=0;
    for(int i=0;i<submeshes;i++){int n=r.ReadInt32();total=checked(total+n);r.BaseStream.Position+=n*4L;}
    mesh.SetIndexBufferParams(total,IndexFormat.UInt32);var descriptors=new SubMeshDescriptor[submeshes];r.BaseStream.Position=indexStart;
    var indices=new NativeArray<int>(total,Allocator.Temp,NativeArrayOptions.UninitializedMemory);try{
     int at=0;for(int i=0;i<submeshes;i++){int n=r.ReadInt32();descriptors[i]=new SubMeshDescriptor(at,n);for(int j=0;j<n;j++)indices[at++]=r.ReadInt32();}
     mesh.SetIndexBufferData(indices,0,0,total);mesh.SetSubMeshes(descriptors);
    }finally{indices.Dispose();}
    mesh.RecalculateBounds();return mesh;
   }
  }
  public static Texture Texture(byte[] bytes,Action<UnityEngine.Object> track){
   using(var r=new BinaryReader(new MemoryStream(bytes))){r.ReadUInt32();int w=r.ReadInt32(),h=r.ReadInt32(),format=r.ReadInt32();bool linear=r.ReadInt32()!=0;var wrap=(TextureWrapMode)r.ReadInt32();var filter=(FilterMode)r.ReadInt32();int faces=r.ReadInt32(),mips=r.ReadInt32(),aniso=r.ReadInt32();float bias=r.ReadSingle();
    if(format==24&&!SystemInfo.SupportsTextureFormat(TextureFormat.BC6H))throw new InvalidDataException("bc6-platform-unsupported");
    if(faces==1){var texture=new Texture2D(w,h,(TextureFormat)format,mips>1,linear);track(texture);texture.wrapMode=wrap;texture.filterMode=filter;texture.anisoLevel=aniso;texture.mipMapBias=bias;var pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);try{texture.LoadRawTextureData(IntPtr.Add(pinned.AddrOfPinnedObject(),44),bytes.Length-44);texture.Apply(false,false);}finally{pinned.Free();}return texture;}
    var cube=new Cubemap(w,(TextureFormat)format,mips>1);track(cube);cube.wrapMode=wrap;cube.filterMode=filter;cube.anisoLevel=aniso;cube.mipMapBias=bias;
    for(int mip=0;mip<mips;mip++){int count=Math.Max(1,w>>mip)*Math.Max(1,h>>mip)*(format==4?4:8);for(int face=0;face<6;face++)cube.SetPixelData(r.ReadBytes(count),mip,(CubemapFace)face);}cube.Apply(false,false);return cube;
   }
  }
 }
}
