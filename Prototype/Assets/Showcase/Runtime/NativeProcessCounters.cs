using System;
using System.Runtime.InteropServices;
// Windows player counters: Mono's Process.WorkingSet64 can return zero.
public static class NativeProcessCounters {
 [StructLayout(LayoutKind.Sequential)] struct MemoryCounters {
  public uint cb,pageFaults;
  public UIntPtr peakWorkingSet,workingSet,peakPaged,paged,peakNonPaged,nonPaged,pagefile,peakPagefile,privateUsage;
 }
 [StructLayout(LayoutKind.Sequential)] struct FileTime {
  public uint low,high;
  public double Seconds {get{return ((ulong)high<<32|low)*1e-7;}}
 }
 [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
 [DllImport("psapi.dll",SetLastError=true)] static extern bool GetProcessMemoryInfo(IntPtr process,ref MemoryCounters counters,uint size);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetProcessTimes(IntPtr process,out FileTime creation,out FileTime exit,out FileTime kernel,out FileTime user);
 public static long WorkingSet {get;private set;}=-1;
 public static long PrivateBytes {get;private set;}=-1;
 public static double CpuSeconds {get;private set;}=double.NaN;
 public static void Sample(){
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
  var p=GetCurrentProcess();var m=new MemoryCounters();m.cb=(uint)Marshal.SizeOf(typeof(MemoryCounters));
  if(GetProcessMemoryInfo(p,ref m,m.cb)){WorkingSet=(long)m.workingSet.ToUInt64();PrivateBytes=(long)m.privateUsage.ToUInt64();}
  else {WorkingSet=PrivateBytes=-1;}
  FileTime c,e,k,u;CpuSeconds=GetProcessTimes(p,out c,out e,out k,out u)?k.Seconds+u.Seconds:double.NaN;
#endif
 }
}
