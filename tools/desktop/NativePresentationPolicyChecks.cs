using System;
using SpatialWallpaper.Desktop;

static class NativePresentationPolicyChecks {
 static int checks;
 static void Check(uint numerator,uint denominator,int expected){var target=new DesktopDisplay{RefreshNumerator=numerator,RefreshDenominator=denominator};if(target.NativeVSyncCount!=expected)throw new Exception("native-sync-policy:"+numerator+"/"+denominator);checks++;}
 static int Main(){try{
  for(uint count=1;count<=4;count++){Check(60*count,1,(int)count);Check(60000*count,1001,(int)count);Check(120*count,2,(int)count);}
  foreach(uint rate in new uint[]{0,30,48,50,75,90,100,144,165,360})Check(rate,1,0);
  Check(60,0,0);Check(uint.MaxValue,uint.MaxValue,0);Check(59940,1000,0);
  var first=new DesktopDisplay{RefreshNumerator=120,RefreshDenominator=1};var second=new DesktopDisplay{RefreshNumerator=144,RefreshDenominator=1};
  if(first.NativeVSyncCount!=2||second.NativeVSyncCount!=0)throw new Exception("native-sync-targets-not-independent");checks++;
  first.RefreshNumerator=60;if(first.NativeVSyncCount!=1||second.NativeVSyncCount!=0)throw new Exception("native-sync-refresh-change-not-local");checks++;
  Console.WriteLine("native presentation policy checks passed: "+checks);return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
