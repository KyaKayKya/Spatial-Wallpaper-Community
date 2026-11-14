namespace SpatialWallpaper.Desktop {
 public sealed class DesktopPolicy {
  public bool Manual,Locked,Suspended,Covered,Preview,PauseWhenCovered=true;
  public string Reason=>Locked?"locked":Suspended?"suspended":Preview?"preview":Manual?"manual":PauseWhenCovered&&Covered?"covered":null;
  public bool Paused=>Reason!=null;
  public bool CaptureNeeded(bool authorized,bool managerConsumer,bool activeWallpaper)=>authorized&&!Locked&&!Suspended&&(managerConsumer||Preview||activeWallpaper&&!Manual&&(!PauseWhenCovered||!Covered));
 }
}
