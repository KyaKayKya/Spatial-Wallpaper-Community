using System;
using SpatialWallpaper.Desktop;
static class DesktopPolicyChecks {
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static int Main(){var p=new DesktopPolicy();Check(!p.Paused,"initial-not-paused");p.Manual=true;p.Locked=true;p.Locked=false;Check(p.Paused&&p.Reason=="manual","unlock-preserves-manual-pause");p.Manual=false;p.Covered=true;Check(p.Paused,"covered-pauses");p.PauseWhenCovered=false;Check(!p.Paused,"invisibility-preference-consumed");p.Suspended=true;Check(p.Paused&&p.Reason=="suspended","suspend-overrides-preference");p.Suspended=false;p.Preview=true;Check(p.Paused&&p.Reason=="preview","preview-owns-scene-handoff");
  Check(p.CaptureNeeded(true,true,true),"preview-receives-authorized-tracking");p.Locked=true;Check(!p.CaptureNeeded(true,true,true),"lock-revokes-tracking-even-during-preview");p.Locked=false;p.Suspended=true;Check(!p.CaptureNeeded(true,true,true),"suspend-revokes-tracking-even-during-preview");p.Suspended=false;Check(!p.CaptureNeeded(false,true,true),"consumers-do-not-enable-camera-without-authorization");p.Preview=false;Check(!p.CaptureNeeded(true,false,false),"no-consumers-release-camera");return 0;}
}
