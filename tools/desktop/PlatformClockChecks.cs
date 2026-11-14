using System;
using System.Diagnostics;
using System.IO;
using SpatialTracking;
static class PlatformClockChecks {
 static int Main(string[] args){
  string root=Path.GetFullPath(Path.Combine(args[0],"../../.."));
  bool windows=System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
  string python=Environment.GetEnvironmentVariable("SPATIAL_TEST_PYTHON")??Path.Combine(root,"tracker",windows?".venv/Scripts/python.exe":".venv/bin/python");
  var info=new ProcessStartInfo(python,"-c \"from spatial_tracker.clock import CLOCK_DOMAIN,qpc_us; print(CLOCK_DOMAIN,qpc_us())\""){UseShellExecute=false,RedirectStandardOutput=true,WorkingDirectory=Path.Combine(root,"tracker")};info.EnvironmentVariables["PYTHONPATH"]=Path.Combine(root,"tracker/src");
  ulong before=QpcClock.Now;string output;
  using(var child=Process.Start(info)){output=child.StandardOutput.ReadToEnd().Trim();child.WaitForExit();if(child.ExitCode!=0)throw new Exception("python-clock-failed");}
  ulong after=QpcClock.Now;var values=output.Split(' ');ulong sampled=ulong.Parse(values[1]);
  if(values[0]!=QpcClock.Domain||sampled+2<before||sampled>after+2)throw new Exception("cross-process-clock-domain-or-epoch-mismatch: "+output+" C#="+before+","+after);
  Console.WriteLine("PASS Python and C# native clock share domain and same-boot epoch: "+QpcClock.Domain);return 0;
 }
}
