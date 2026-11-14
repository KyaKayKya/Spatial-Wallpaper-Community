namespace SpatialWallpaper.Desktop {
 public sealed class DesktopDisplay {
  public string Id,Device;public int X,Y,Width,Height;
  public uint RefreshNumerator,RefreshDenominator;
  public int NativeVSyncCount{get{
   if(RefreshDenominator==0)return 0;
   for(int count=1;count<=4;count++)if((ulong)RefreshNumerator==60ul*RefreshDenominator*(uint)count||(ulong)RefreshNumerator*1001==60000ul*RefreshDenominator*(uint)count)return count;
   return 0;
  }}
  public bool Matches(int x,int y,int width,int height)=>X==x&&Y==y&&Width==width&&Height==height;
 }
}
