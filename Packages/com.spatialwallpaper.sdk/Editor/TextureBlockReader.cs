using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace SpatialWallpaper.Sdk.Editor {
 internal static class TextureBlockReader {
  public static byte[] ReadBc6(Texture2D source){
   if(source.isReadable)return source.GetRawTextureData();
   string path=AssetDatabase.GetAssetPath(source);if(string.IsNullOrEmpty(path)||AssetDatabase.IsSubAsset(source))throw new InvalidDataException("bc6-imported-texture-required");
   string name="SpatialExport-"+Guid.NewGuid().ToString("N"),folder="Assets/"+name;AssetDatabase.CreateFolder("Assets",name);
   try{
    string copy=folder+"/texture"+Path.GetExtension(path);if(!AssetDatabase.CopyAsset(path,copy))throw new InvalidDataException("bc6-temporary-copy-failed");var importer=AssetImporter.GetAtPath(copy) as TextureImporter;if(!importer)throw new InvalidDataException("bc6-importer-required");importer.isReadable=true;importer.SaveAndReimport();
    var readable=AssetDatabase.LoadAssetAtPath<Texture2D>(copy);if(readable.graphicsFormat!=source.graphicsFormat||readable.width!=source.width||readable.height!=source.height||readable.mipmapCount!=source.mipmapCount)throw new InvalidDataException("bc6-copy-layout-changed");
    VerifyPixels(source,readable);return readable.GetRawTextureData();
   }finally{AssetDatabase.DeleteAsset(folder);}
  }
  // Readability must not alter the original compressed appearance. Compare all
  // decoded HDR texels before accepting the temporary asset's compressed blocks.
  static void VerifyPixels(Texture2D source,Texture2D copy){
   var previous=RenderTexture.active;RenderTexture target=null;Texture2D pixels=null;
   try{target=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);pixels=new Texture2D(source.width,source.height,TextureFormat.RGBAHalf,false,true);RenderTexture.active=target;Graphics.Blit(source,target);pixels.ReadPixels(new Rect(0,0,source.width,source.height),0,0);pixels.Apply();var expected=pixels.GetRawTextureData();Graphics.Blit(copy,target);pixels.ReadPixels(new Rect(0,0,source.width,source.height),0,0);pixels.Apply();if(!expected.SequenceEqual(pixels.GetRawTextureData()))throw new InvalidDataException("bc6-copy-pixels-changed");}
   finally{RenderTexture.active=previous;if(target)RenderTexture.ReleaseTemporary(target);if(pixels)UnityEngine.Object.DestroyImmediate(pixels);}
  }
 }
}
