using UnityEngine;
using SpatialWallpaper.Sdk;
[WallpaperScript]
public sealed class StageEAudioFailureAuthor:MonoBehaviour {
 public float low,mid,high,air,peak;public int frames,zeroFrames,nonzeroFrames;public bool measure;
 void Update(){low=SpatialWallpaper.Sdk.Audio.GetBand(0);mid=SpatialWallpaper.Sdk.Audio.GetBand(1);high=SpatialWallpaper.Sdk.Audio.GetBand(2);air=SpatialWallpaper.Sdk.Audio.GetBand(3);if(low>peak)peak=low;frames++;bool silent=low==0&&mid==0&&high==0&&air==0;if(!silent)nonzeroFrames++;if(measure)zeroFrames=silent?zeroFrames+1:0;}
}
