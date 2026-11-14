using System;using System.Runtime.InteropServices;
namespace SpatialWallpaper.Sdk.Capabilities {
 // Unity Mono Stopwatch has a process-relative origin. Wire capture timestamps
 // require the OS QPC origin, shared with WASAPI and the native audio sampler.
 public static class WindowsMachineClock {
  [DllImport("kernel32.dll")]static extern bool QueryPerformanceCounter(out long value);
  [DllImport("kernel32.dll")]static extern bool QueryPerformanceFrequency(out long value);
  [DllImport("kernel32.dll")]static extern ulong GetTickCount64();
  public static long Milliseconds()=>checked((long)GetTickCount64());
  static readonly long Frequency=ReadFrequency();
  static long ReadFrequency(){if(!QueryPerformanceFrequency(out long value)||value<=0)throw new InvalidOperationException("machine-clock-frequency");return value;}
  public static ulong Qpc100ns(){if(!QueryPerformanceCounter(out long ticks)||ticks<0)throw new InvalidOperationException("machine-clock-counter");return checked((ulong)(ticks/Frequency)*10000000UL+(ulong)(ticks%Frequency)*10000000UL/(ulong)Frequency);}
 }
}
