using UnityEngine;using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class StageBooleanJoinAuthor:MonoBehaviour {
 public bool a,b,c,negated,conjunction,disjunction,conditional,returned,argument,shortOr,shortAnd,bitwise;
 public int effects,left=2,right=-1,mask,xor;
 bool Compose(bool x,bool y,bool z){return !x||!y||z;}
 bool Echo(bool value){return value;}
 bool Touch(){effects=effects+1;return b;}
 public void Start(){
  bool local=a;
  negated=!a||!b||c;conjunction=local&&b;disjunction=local||b;conditional=a?b:false;
  returned=Compose(a,b,c);argument=Echo(a||b);shortOr=a||Touch();shortAnd=a&&Touch();bitwise=a&b|c;
  mask=left|right;xor=left^right;
 }
}
