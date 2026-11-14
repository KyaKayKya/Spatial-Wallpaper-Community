using System;using System.IO;using UnityEngine;using UnityEngine.Rendering;using SpatialWallpaper.Content;
namespace SpatialWallpaper.Sdk.Editor {
 internal static class SceneResourceEncoding {
  internal static byte[] Sound(AudioClip clip){
   if(!clip||clip.channels<1||clip.channels>8||clip.samples<1||clip.length>300)throw new InvalidDataException("sound-clip-config");
   int count=checked((int)Math.Ceiling((double)clip.samples*SoundFormat.Rate/clip.frequency));var samples=new float[checked(clip.samples*clip.channels)];
   if(!clip.GetData(samples,0))throw new InvalidDataException("sound-clip-not-readable-use-decompress-on-load");
   using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream)){
    writer.Write(SoundFormat.Magic);writer.Write(SoundFormat.Rate);writer.Write(count);
    for(int i=0;i<count;i++){double at=(double)i*clip.frequency/SoundFormat.Rate;int a=Math.Min((int)at,clip.samples-1),b=Math.Min(a+1,clip.samples-1);double sum=0;
     for(int c=0;c<clip.channels;c++)sum+=samples[a*clip.channels+c]*(1-(at-a))+samples[b*clip.channels+c]*(at-a);
     sum/=clip.channels;if(double.IsNaN(sum)||double.IsInfinity(sum))throw new InvalidDataException("sound-nonfinite-sample");writer.Write((short)Math.Round(Math.Max(-1,Math.Min(1,sum))*32767));
    }
    var bytes=stream.ToArray();new SoundClipData(bytes);return bytes;
   }
  }
  internal static byte[] Mesh(Mesh mesh){
   if(!mesh)throw new InvalidDataException("missing-mesh");
    // Unity's editor permits reading imported mesh data. Never change the
    // author's importer/read-write settings just to export a package.
    Vector3[] p=mesh.vertices,n=mesh.normals;Vector4[] t=mesh.tangents;Vector2[] uv=mesh.uv,uv2=mesh.uv2;Color[] c=mesh.colors;
    using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream)){
     w.Write(0x314d5753u);w.Write(p.Length);w.Write(mesh.subMeshCount);
     for(int i=0;i<p.Length;i++){Write(w,p[i]);Write(w,n.Length==p.Length?n[i]:Vector3.up);Write(w,t.Length==p.Length?t[i]:new Vector4(1,0,0,1));Write(w,uv.Length==p.Length?uv[i]:Vector2.zero);Write(w,uv2.Length==p.Length?uv2[i]:Vector2.zero);Write(w,c.Length==p.Length?(Vector4)c[i]:Vector4.one);}
     for(int s=0;s<mesh.subMeshCount;s++){if(mesh.GetTopology(s)!=MeshTopology.Triangles)throw new InvalidDataException("non-triangle-mesh");var indices=mesh.GetTriangles(s);w.Write(indices.Length);foreach(int index in indices)w.Write(index);}
     var bytes=stream.ToArray();ResourceData.ValidateMesh(bytes);return bytes;
    }
   }
  internal static byte[] Texture(Texture texture){
   if(!texture||(!(texture is Texture2D)&&!(texture is Cubemap)))throw new InvalidDataException("only-static-texture2d-or-cubemap");int faces=texture is Cubemap?6:1;
    bool hdr=faces==6||texture.graphicsFormat.ToString().Contains("Float");bool linear=faces==6||!UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);
    bool bc6=texture is Texture2D&&texture.graphicsFormat==UnityEngine.Experimental.Rendering.GraphicsFormat.RGB_BC6H_UFloat&&texture.mipmapCount==1;
    var previous=RenderTexture.active;RenderTexture rt=null;
    Texture2D copy=null;Material cubeReader=null;
    try{
     var shader=Shader.Find("Hidden/SpatialSDK/CubeReadback");if(!shader)throw new InvalidDataException("texture-readback-shader-missing");cubeReader=new Material(shader);if(faces==6)cubeReader.SetTexture("_Cube",texture);
     using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream)){
      w.Write(0x32545753u);w.Write(texture.width);w.Write(texture.height);w.Write((int)(bc6?TextureFormat.BC6H:hdr?TextureFormat.RGBAHalf:TextureFormat.RGBA32));w.Write(linear?1:0);w.Write((int)texture.wrapMode);w.Write((int)texture.filterMode);w.Write(faces);w.Write(texture.mipmapCount);w.Write(texture.anisoLevel);w.Write(texture.mipMapBias);
      if(bc6)w.Write(TextureBlockReader.ReadBc6((Texture2D)texture));
      else for(int mip=0;mip<texture.mipmapCount;mip++){
       int width=Math.Max(1,texture.width>>mip),height=Math.Max(1,texture.height>>mip);rt=RenderTexture.GetTemporary(width,height,0,hdr?RenderTextureFormat.ARGBHalf:RenderTextureFormat.ARGB32,linear?RenderTextureReadWrite.Linear:RenderTextureReadWrite.sRGB);copy=new Texture2D(width,height,hdr?TextureFormat.RGBAHalf:TextureFormat.RGBA32,false,linear);
       for(int f=0;f<faces;f++){
        cubeReader.SetFloat("_Face",faces==1?-1:f);cubeReader.SetFloat("_Mip",mip);Graphics.Blit(faces==1?texture:null,rt,cubeReader);
        RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,width,height),0,0);copy.Apply();w.Write(copy.GetRawTextureData());
       }
       RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);rt=null;UnityEngine.Object.DestroyImmediate(copy);copy=null;
      }
      var bytes=stream.ToArray();ResourceData.ValidateTexture(bytes);return bytes;
     }
    }finally{RenderTexture.active=previous;if(rt)RenderTexture.ReleaseTemporary(rt);if(copy)UnityEngine.Object.DestroyImmediate(copy);if(cubeReader)UnityEngine.Object.DestroyImmediate(cubeReader);}
   }

  static void Write(BinaryWriter w,Vector3 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
  static void Write(BinaryWriter w,Vector4 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);w.Write(v.w);}
  static void Write(BinaryWriter w,Vector2 v){w.Write(v.x);w.Write(v.y);}
 }
}
