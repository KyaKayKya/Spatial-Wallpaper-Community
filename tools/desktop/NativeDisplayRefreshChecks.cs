using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Desktop;

static class NativeDisplayRefreshChecks {
 static int Main(){try{
  var displays=WindowsDesktopWindow.Displays(true);if(displays.Length==0)throw new Exception("no-online-display");var rows=new JArray();
  foreach(var display in displays){if(display.RefreshNumerator==0||display.RefreshDenominator==0)throw new Exception("actual-target-rational-refresh-missing:"+display.Device);rows.Add(new JObject{{"id",display.Id},{"device",display.Device},{"numerator",display.RefreshNumerator},{"denominator",display.RefreshDenominator},{"sync_count_for_60",display.NativeVSyncCount}});}
  foreach(var pair in new[]{Tuple.Create("DisplayPath",72),Tuple.Create("DisplayMode",64),Tuple.Create("SourceName",84)}){var type=typeof(WindowsDesktopWindow).GetNestedType(pair.Item1,BindingFlags.NonPublic);if(type==null||Marshal.SizeOf(type)!=pair.Item2)throw new Exception("display-config-native-layout:"+pair.Item1);}
  Console.WriteLine(new JObject{{"status","passed-actual-refresh-query-no-presentation-claim"},{"displays",rows}});return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
