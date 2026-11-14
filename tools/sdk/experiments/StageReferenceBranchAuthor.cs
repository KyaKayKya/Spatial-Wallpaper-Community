using UnityEngine;using SpatialWallpaper.Sdk;using System.Collections.Generic;
[WallpaperScript]
public sealed class StageReferenceBranchAuthor:MonoBehaviour {
 public string value;public int[] items;public List<int> list;
 public string copied;public int equal,unequal,arrayResult,listResult;public bool present;
 bool HasValue(string text){return text!=null;}
 public void Start(){
  copied=value==null?"missing":value;
  equal=value==null?1:2;unequal=value!=null?3:4;
  arrayResult=items==null?5:6;listResult=list!=null?7:8;
  present=HasValue(value);
 }
}
