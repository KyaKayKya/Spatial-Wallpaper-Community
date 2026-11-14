using System;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

internal static class WindowsDesktopStartup {
 const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run",Name="SpatialWallpaper";
 public static bool Available=>Application.platform==RuntimePlatform.WindowsPlayer&&string.IsNullOrEmpty(CalibrationRuntime.Argument("--calibration-smoke"))&&string.IsNullOrEmpty(CalibrationRuntime.Argument("--library-smoke"));
 public static bool Enabled {
  get{if(!Available)return false;IntPtr key;int error=RegOpenKeyEx(CurrentUser,Key,0,1,out key);if(error==2)return false;Check(error);
   try{uint type=0,size=0;error=RegQueryValueEx(key,Name,IntPtr.Zero,ref type,null,ref size);if(error==2)return false;Check(error);var data=new byte[size];Check(RegQueryValueEx(key,Name,IntPtr.Zero,ref type,data,ref size));return type==1&&Encoding.Unicode.GetString(data).TrimEnd('\0')==Command();}finally{RegCloseKey(key);}
  }
 }
 public static void Set(bool enabled){
  if(!Available)throw new InvalidOperationException("启动项只支持正式运行窗口");
  IntPtr key;uint disposition;Check(RegCreateKeyEx(CurrentUser,Key,0,null,0,2,IntPtr.Zero,out key,out disposition));
  try{if(enabled){var data=Encoding.Unicode.GetBytes(Command()+"\0");Check(RegSetValueEx(key,Name,0,1,data,data.Length));}else{int error=RegDeleteValue(key,Name);if(error!=2)Check(error);}}finally{RegCloseKey(key);}
 }
 static readonly IntPtr CurrentUser=new IntPtr(unchecked((int)0x80000001));
 static void Check(int error){if(error!=0)throw new Win32Exception(error);}
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode)]static extern int RegOpenKeyEx(IntPtr key,string name,int options,int access,out IntPtr result);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode)]static extern int RegCreateKeyEx(IntPtr key,string name,int reserved,string cls,int options,int access,IntPtr security,out IntPtr result,out uint disposition);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode)]static extern int RegQueryValueEx(IntPtr key,string name,IntPtr reserved,ref uint type,byte[] data,ref uint size);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode)]static extern int RegSetValueEx(IntPtr key,string name,int reserved,int type,byte[] data,int size);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode)]static extern int RegDeleteValue(IntPtr key,string name);
 [DllImport("advapi32.dll")]static extern int RegCloseKey(IntPtr key);
 static string Command(){
  string result=Quote(Process.GetCurrentProcess().MainModule.FileName);
  foreach(string option in new[]{"--tracking-python","--tracking-root","--data-root"}){
   string value=CalibrationRuntime.Argument(option);if(value.Length>0)result+=" "+option+" "+Quote(value);
  }
  return result;
 }
 static string Quote(string value){
  // CommandLineToArgvW quoting, including trailing backslashes in directory paths.
  var result=new System.Text.StringBuilder("\"");int slashes=0;
  foreach(char ch in value){if(ch=='\\'){slashes++;continue;}if(ch=='\"'){result.Append('\\',slashes*2+1).Append(ch);}else{result.Append('\\',slashes).Append(ch);}slashes=0;}
  return result.Append('\\',slashes*2).Append('"').ToString();
 }
}
