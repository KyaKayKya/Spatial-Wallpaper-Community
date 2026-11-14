using System;using System.Threading;using System.Runtime.InteropServices;using SpatialWallpaper.Sdk.Hosting;
static class WorkerStopPeer {
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool WriteFile(IntPtr h,byte[] b,uint n,out uint done,IntPtr overlapped);
 static int Main(string[] args){try{var identity=new WorkerIdentity(Guid.Parse(args[2]),Guid.Parse(args[3]),uint.Parse(args[4]));if(args[5]=="wrong-instance")identity=new WorkerIdentity(identity.Attempt,Guid.NewGuid(),identity.Generation);if(args[5]=="wrong-generation")identity=new WorkerIdentity(identity.Attempt,identity.Instance,identity.Generation+1);if(args[5]=="wrong")identity=new WorkerIdentity(Guid.NewGuid(),identity.Instance,identity.Generation);
  uint forbidden;if(WriteFile(new IntPtr(long.Parse(args[0])),new byte[]{0},1,out forbidden,IntPtr.Zero)||Marshal.GetLastWin32Error()!=5)return 45;
  using(var stop=new WindowsWorkerStopReader(new IntPtr(long.Parse(args[0])),identity)){uint done;if(!WriteFile(new IntPtr(long.Parse(args[1])),new byte[]{1},1,out done,IntPtr.Zero)||done!=1)return 41;
   while(true){if(args[5]!="hang"){if(stop.Error!=null)return 44;if(stop.Requested)return args[5]=="nonzero"?32:0;}Thread.Sleep(2);}
  }
 }catch{return 42;}}
}
