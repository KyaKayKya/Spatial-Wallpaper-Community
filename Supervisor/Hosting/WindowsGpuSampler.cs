using System;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Threading;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Supervision.Windows {
 // OS WDDM accounting, sampled away from every deadline/termination thread. No Worker-supplied counters.
 public sealed class WindowsGpuSampler {
  readonly object gate=new object();readonly Func<long> now;readonly Dictionary<int,long> registered=new Dictionary<int,long>();readonly Dictionary<int,Tuple<long,long>> snapshots=new Dictionary<int,Tuple<long,long>>();
  readonly Thread thread;int closed,error;
  [StructLayout(LayoutKind.Sequential)]struct Value{internal uint Status;internal long Bytes;}
  [StructLayout(LayoutKind.Sequential)]struct Item{internal IntPtr Name;internal Value Counter;}
  [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern int PdhOpenQueryW(string source,UIntPtr user,out IntPtr query);
  [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern int PdhAddEnglishCounterW(IntPtr query,string path,UIntPtr user,out IntPtr counter);
  [DllImport("pdh.dll")]static extern int PdhCollectQueryData(IntPtr query);
  [DllImport("pdh.dll")]static extern int PdhGetFormattedCounterArrayW(IntPtr counter,uint format,ref uint size,out uint count,IntPtr values);
  [DllImport("pdh.dll")]static extern int PdhCloseQuery(IntPtr query);
  public int LastError=>Volatile.Read(ref error);
  public WindowsGpuSampler(Func<long> monotonicMilliseconds){now=monotonicMilliseconds??throw new ArgumentNullException(nameof(monotonicMilliseconds));thread=new Thread(Run){IsBackground=true,Name="SpatialWallpaper GPU accounting"};thread.Start();}
  public void Register(int pid){if(pid<1)throw new ArgumentException("GPU-process");lock(gate){if(registered.Count>=RuntimeLimits.CacheAttempts||registered.ContainsKey(pid))throw new InvalidOperationException("GPU-process-capacity");registered.Add(pid,now());snapshots.Remove(pid);}}
  public void Unregister(int pid){lock(gate){registered.Remove(pid);snapshots.Remove(pid);}}
  public bool Read(int pid,out long bytes,out long sampledAt){lock(gate){if(snapshots.TryGetValue(pid,out var value)){bytes=value.Item1;sampledAt=value.Item2;return true;}bytes=-1;sampledAt=0;return false;}}
  static void Check(int status){if(status!=0)throw new InvalidOperationException("GPU-counter-status:"+unchecked((uint)status).ToString("x8"));}
  static Dictionary<int,long> Values(IntPtr counter,IntPtr buffer){
   uint size=RuntimeLimits.CounterBufferBytes,count;int status=PdhGetFormattedCounterArrayW(counter,0x400,ref size,out count,buffer);if(status==unchecked((int)0x800007d5)||status==unchecked((int)0x800007d1))return new Dictionary<int,long>();Check(status);
   int stride=Marshal.SizeOf(typeof(Item));if(count>RuntimeLimits.CounterInstances||count*(long)stride>size||size>RuntimeLimits.CounterBufferBytes)throw new InvalidOperationException("GPU-counter-size");var result=new Dictionary<int,long>();
   for(int i=0;i<count;i++){
    var item=(Item)Marshal.PtrToStructure(IntPtr.Add(buffer,i*stride),typeof(Item));if(item.Counter.Status!=0&&item.Counter.Status!=1)continue;
    long offset=item.Name.ToInt64()-buffer.ToInt64();if(offset<0||offset>=size||offset%2!=0)throw new InvalidOperationException("GPU-counter-name");
    int length=0;while(length<256&&offset+length*2+2<=size&&Marshal.ReadInt16(item.Name,length*2)!=0)length++;
    if(length>=256||offset+length*2+2>size)throw new InvalidOperationException("GPU-counter-name-size");string name=Marshal.PtrToStringUni(item.Name,length);
    if(!name.StartsWith("pid_",StringComparison.Ordinal))continue;int end=name.IndexOf('_',4);if(end<=4||!int.TryParse(name.Substring(4,end-4),out var pid)||pid<1)continue;
    if(item.Counter.Bytes<0)throw new InvalidOperationException("negative-GPU-counter");long previous;result.TryGetValue(pid,out previous);result[pid]=checked(previous+item.Counter.Bytes);
   }
   return result;
  }
  void Run(){IntPtr query=IntPtr.Zero,buffer=IntPtr.Zero;try{
   Check(PdhOpenQueryW(null,UIntPtr.Zero,out query));IntPtr dedicated,shared;Check(PdhAddEnglishCounterW(query,@"\GPU Process Memory(*)\Dedicated Usage",UIntPtr.Zero,out dedicated));Check(PdhAddEnglishCounterW(query,@"\GPU Process Memory(*)\Shared Usage",UIntPtr.Zero,out shared));buffer=Marshal.AllocHGlobal(RuntimeLimits.CounterBufferBytes);
   while(Volatile.Read(ref closed)==0){
    try{long sampledAt=now();Check(PdhCollectQueryData(query));var d=Values(dedicated,buffer);var s=Values(shared,buffer);
     lock(gate){foreach(var registration in registered){long a,b;if(sampledAt<registration.Value||!d.TryGetValue(registration.Key,out a)||!s.TryGetValue(registration.Key,out b))continue;snapshots[registration.Key]=Tuple.Create(checked(a+b),sampledAt);}}
     Interlocked.Exchange(ref error,0);
    }catch{Interlocked.Exchange(ref error,1);}Thread.Sleep(RuntimeLimits.CounterPollMs);
   }
  }catch{Interlocked.Exchange(ref error,2);}finally{if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);if(query!=IntPtr.Zero)PdhCloseQuery(query);}}
  // Do not wait for a potentially stuck OS provider on a monitor/UI/cleanup thread.
  public void Close()=>Interlocked.Exchange(ref closed,1);
 }
}
