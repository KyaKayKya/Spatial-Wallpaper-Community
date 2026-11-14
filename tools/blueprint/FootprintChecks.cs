using System;
using System.IO;
using System.Threading;
using SpatialWallpaper.Blueprint;
class FootprintChecks {
 static int Main(string[] args){try{var d=BlueprintReader.Read(File.ReadAllBytes(args[0]),"sample.zip",CancellationToken.None);var layout=BlueprintLayout.Fit(d);var s=new BlueprintSettings();var a=BlueprintFootprint.For(d,layout,s);s.Scale=2;s.OffsetX=.4f;s.OffsetZ=.3f;s.OffsetY=-.1f;var b=BlueprintFootprint.For(d,layout,s);if(Math.Abs((b.MaxX-b.MinX)/(a.MaxX-a.MinX)-2)>.0001||Math.Abs((b.MinX+b.MaxX)*.5f-.4f)>.0001||Math.Abs(b.BaseY-layout.Y+.1f)>.0001)throw new Exception("final transform not reflected");Console.WriteLine("PASS footprint-follows-scale-position-and-foundation");s.Yaw=90;var rotated=BlueprintFootprint.For(d,layout,s);if(Math.Abs((rotated.MaxX-rotated.MinX)-(b.MaxZ-b.MinZ))>.0001)throw new Exception("yaw ignored");Console.WriteLine("PASS footprint-rotates-width-and-depth");if(!rotated.Contains(.4f,layout.Z+.3f)||rotated.Contains(4,4))throw new Exception("footprint mask");Console.WriteLine("PASS footprint-mask-bounds");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
}
