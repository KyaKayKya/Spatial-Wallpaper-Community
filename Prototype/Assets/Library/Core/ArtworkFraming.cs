namespace SpatialWallpaper.Library {
 // D50: scale every spatial axis equally; the aperture remains the actual viewport.
 public static class ArtworkFraming {
  public static double Scale(double viewportWidth,double viewportHeight,bool keepHeight)=>SpatialWallpaper.Sdk.Framing.Scale(.53,.298125,viewportWidth,viewportHeight,keepHeight);
 }
}
