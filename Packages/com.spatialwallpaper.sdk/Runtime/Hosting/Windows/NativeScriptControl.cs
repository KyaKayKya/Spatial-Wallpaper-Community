using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpatialWallpaper.Sdk.Hosting {
 // Bootstrap/lifetime only. Script commands use separately duplicated anonymous
 // pipes directly between renderer and script process, never this channel.
 public static class NativeScriptControl {
  [StructLayout(LayoutKind.Sequential)]struct SecurityAttributes {public int Length;public IntPtr Descriptor;[MarshalAs(UnmanagedType.Bool)]public bool Inherit;}
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text,uint version,out IntPtr descriptor,IntPtr size);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetFileSecurityW(string path,uint information,IntPtr descriptor);
  [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr memory);
  [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool CloseHandle(IntPtr handle);
  [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
  [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr data,uint bytes,out uint needed);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ConvertSidToStringSidW(IntPtr sid,out IntPtr text);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateNamedPipeW(string name,uint openMode,uint pipeMode,uint instances,uint outputBytes,uint inputBytes,uint timeout,ref SecurityAttributes security);
  static string CurrentSid(){
   IntPtr token;if(!OpenProcessToken(GetCurrentProcess(),8,out token))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   try{uint needed;GetTokenInformation(token,1,IntPtr.Zero,0,out needed);if(needed< IntPtr.Size||needed>65536)throw new InvalidDataException("script-client-token-size");IntPtr buffer=Marshal.AllocHGlobal((int)needed);
    try{if(!GetTokenInformation(token,1,buffer,needed,out needed))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());IntPtr text;if(!ConvertSidToStringSidW(Marshal.ReadIntPtr(buffer),out text))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());try{return Marshal.PtrToStringUni(text);}finally{LocalFree(text);}}finally{Marshal.FreeHGlobal(buffer);}
   }finally{CloseHandle(token);}
  }
  static IntPtr Descriptor(bool children){string inherit=children?"OICI":"";IntPtr descriptor;if(!ConvertStringSecurityDescriptorToSecurityDescriptorW("D:P(A;"+inherit+";GA;;;"+CurrentSid()+")",1,out descriptor,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return descriptor;}
  public static NamedPipeServerStream CreatePrivateServer(string name){
   IntPtr descriptor=Descriptor(false);try{
    var security=new SecurityAttributes{Length=Marshal.SizeOf(typeof(SecurityAttributes)),Descriptor=descriptor,Inherit=false};
    IntPtr handle=CreateNamedPipeW("\\\\.\\pipe\\"+name,3|0x40000000|0x00080000,8,1,16384,16384,0,ref security);
    var safe=new SafePipeHandle(handle,true);if(safe.IsInvalid){safe.Dispose();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}
    try{return new NamedPipeServerStream(PipeDirection.InOut,true,false,safe);}catch{safe.Dispose();throw;}
   }finally{LocalFree(descriptor);}
  }
  public static void ProtectDirectory(string path){IntPtr descriptor=Descriptor(true);try{if(!SetFileSecurityW(path,4|0x80000000,descriptor))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}finally{LocalFree(descriptor);}}
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(IntPtr pipe,out uint pid);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint pid);
  public static int Peer(PipeStream pipe,bool server){uint pid;bool ok=server?GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out pid):GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out pid);if(!ok||pid==0||pid>int.MaxValue)throw new InvalidDataException("script-control-peer");return (int)pid;}
  static void Exact(Stream stream,byte[] bytes){int at=0;while(at<bytes.Length){int n=stream.Read(bytes,at,bytes.Length-at);if(n==0)throw new EndOfStreamException("script-control-closed");at+=n;}}
  public static JObject Read(Stream stream){var header=new byte[4];Exact(stream,header);int length=BitConverter.ToInt32(header,0);if(length<1||length>16384)throw new InvalidDataException("script-control-size");var bytes=new byte[length];Exact(stream,bytes);return Parse(new UTF8Encoding(false,true).GetString(bytes));}
  public static JObject Parse(string text){using(var reader=new JsonTextReader(new StringReader(text)){MaxDepth=12,DateParseHandling=DateParseHandling.None}){var value=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new InvalidDataException("script-control-tail");return value;}}
  public static void Write(Stream stream,JObject value){var bytes=new UTF8Encoding(false,true).GetBytes(value.ToString(Formatting.None));if(bytes.Length<1||bytes.Length>16384)throw new InvalidDataException("script-control-size");stream.Write(BitConverter.GetBytes(bytes.Length),0,4);stream.Write(bytes,0,bytes.Length);stream.Flush();}
  // Call only from bootstrap/background threads. Unity's frame loop never waits.
  public static T Deadline<T>(Func<T> action,int milliseconds){var task=Task.Run(action);if(!task.Wait(milliseconds))throw new TimeoutException("script-control-deadline");return task.GetAwaiter().GetResult();}
 }
}
