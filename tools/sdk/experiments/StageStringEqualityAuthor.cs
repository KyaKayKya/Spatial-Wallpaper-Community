using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class StageStringEqualityAuthor:MonoBehaviour {
 public string left,right;public bool equal,unequal,returned;public int branch;
 bool Same(string a,string b){return a==b;}
 public void Start(){equal=left==right;unequal=left!=right;returned=Same(left,right);branch=left==right?1:2;}
}
