using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
public sealed class LibraryDisplayTarget {
 public string Id,Name;
 public LibraryDisplayTarget(string id,string name){Id=id;Name=name;}
}
public static class LibraryDisplayCatalog {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Device {
  public int size;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string text;public int flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string key;
 }
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool EnumDisplayDevices(string name,uint index,ref Device info,uint flags);
 public static LibraryDisplayTarget[] List(){
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
  return System.Array.ConvertAll(SpatialWallpaper.Desktop.MacDesktopSurface.Displays(),d=>new LibraryDisplayTarget(d.Id,"显示器 "+d.Device));
#else
  if(Application.platform!=RuntimePlatform.WindowsPlayer)return new[]{new LibraryDisplayTarget("editor-preview","编辑器显示器")};
  var result=new List<LibraryDisplayTarget>();
  for(uint i=0;;i++){
   var adapter=new Device{size=Marshal.SizeOf(typeof(Device))};if(!EnumDisplayDevices(null,i,ref adapter,0))break;
   if((adapter.flags&1)==0||(adapter.flags&8)!=0)continue;
   var monitor=new Device{size=Marshal.SizeOf(typeof(Device))};
   if(EnumDisplayDevices(adapter.name,0,ref monitor,1)&&!string.IsNullOrEmpty(monitor.id))result.Add(new LibraryDisplayTarget(monitor.id,"显示器 "+(result.Count+1)));
  }
  return result.ToArray();
#endif
 }
}
