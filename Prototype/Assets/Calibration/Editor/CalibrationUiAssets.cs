using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Evo.UI;

// Reference the installed Evo assets; no copied plugin source or custom icon renderer.
public static class CalibrationUiAssets {
 public static void Prepare(){
  const string folder="Assets/Calibration/Resources";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
  const string path=folder+"/ManagerIcons.asset";
  var source=AssetDatabase.LoadAssetAtPath<IconLibrary>("Assets/Evo/Evo UI/Editor/Resources/Icon Library/Default.asset");
  if(!source)throw new InvalidOperationException("Restore the local Evo UI library before building");
  var target=AssetDatabase.LoadAssetAtPath<IconLibrary>(path);if(!target){target=ScriptableObject.CreateInstance<IconLibrary>();AssetDatabase.CreateAsset(target,path);}
  string[] names={"Layout","Scan","Settings","Check (Circle Fill)","Display","Left","Right","Down","Eye","Search","Play","Refresh","Arrow Left"};
  // Evo names vary by icon; require an exact installed item rather than draw a substitute.
  target.icons=names.Select(name=>source.GetItem(name)??throw new InvalidOperationException("Missing Evo icon: "+name)).ToList();
  var circle=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Evo/Evo UI/Sprites/Borders/Radial/Filled/Radial Filled - 256px.png");
  target.icons.Add(new IconLibrary.Item{ID="Circle",resolutions=new System.Collections.Generic.List<IconLibrary.Item.Resolution>{new IconLibrary.Item.Resolution{resolution=256,sprite=circle}}});
  foreach(var item in target.icons){
   var sprite=item.GetSprite();if(!sprite)throw new InvalidOperationException("Missing Evo sprite: "+item.ID);
   var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite));
   importer.maxTextureSize=256;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;importer.SaveAndReimport();
  }
  foreach(var name in new[]{"undo-2","rotate-ccw","maximize"}){
   string iconPath=folder+"/Icons/"+name+".png";
   var importer=(TextureImporter)AssetImporter.GetAtPath(iconPath);
   if(importer==null)throw new InvalidOperationException("Run tools/desktop/prepare_icons.cjs before building");
   importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=true;importer.filterMode=FilterMode.Trilinear;importer.alphaIsTransparency=true;importer.SaveAndReimport();
   target.icons.Add(new IconLibrary.Item{ID=name,resolutions=new System.Collections.Generic.List<IconLibrary.Item.Resolution>{new IconLibrary.Item.Resolution{resolution=256,sprite=AssetDatabase.LoadAssetAtPath<Sprite>(iconPath)}}});
  }
  EditorUtility.SetDirty(target);
  const string scrollbarPath=folder+"/ManagerScrollbar.prefab";
  var scrollbar=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Evo/Evo UI/Prefabs/Scrollbar/Scrollbar (Vertical).prefab");
  if(!scrollbar)throw new InvalidOperationException("Missing Evo vertical scrollbar");
  PrefabUtility.SaveAsPrefabAsset(scrollbar,scrollbarPath);
  const string materialPath=folder+"/ManagerSurface.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
  if(!material){var shader=Shader.Find("Evo/UI/Procedural Rect");if(!shader)throw new InvalidOperationException("Missing Evo rounded UI shader");AssetDatabase.CreateAsset(new Material(shader),materialPath);}
  AssetDatabase.SaveAssets();
 }
}
