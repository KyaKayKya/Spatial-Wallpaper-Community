using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace SpatialWallpaper.Blueprint {
 public static class BlueprintFilePicker {
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]sealed class OpenFile {public int size=Marshal.SizeOf(typeof(OpenFile));public IntPtr owner,instance;public string filter;public IntPtr customFilter;public int customMax,index=1;public StringBuilder file=new StringBuilder(32768);public int maximum=32768;public IntPtr titleBuffer;public int titleMaximum;public string directory,title="选择 Minecraft 蓝图";public int flags=0x80000|0x1000|0x800|8;public short fileOffset,extensionOffset;public string extension;public IntPtr custom,hook,template,reserved;public int reservedNumber,extendedFlags;}
  [DllImport("comdlg32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetOpenFileNameW([In,Out]OpenFile dialog);
  [DllImport("comdlg32.dll")]static extern uint CommDlgExtendedError();
  [DllImport("user32.dll")]static extern IntPtr GetActiveWindow();
  public static Task<string> ChooseAsync(string title="选择 Minecraft 蓝图",string filter="Minecraft 蓝图\0*.litematic;*.schem;*.schematic;*.nbt;*.zip\0所有文件\0*.*\0\0"){if(Application.platform!=RuntimePlatform.WindowsPlayer&&Application.platform!=RuntimePlatform.WindowsEditor)throw new BlueprintImportException("此平台请使用文件路径导入");IntPtr owner=GetActiveWindow();var result=new TaskCompletionSource<string>();var thread=new Thread(()=>{try{result.TrySetResult(Choose(owner,title,filter));}catch(Exception e){result.TrySetException(e);}}){IsBackground=true,Name="Wallpaper file picker"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return result.Task;}
  static string Choose(IntPtr owner,string title,string filter){var dialog=new OpenFile{owner=owner,title=title,filter=filter};if(GetOpenFileNameW(dialog))return dialog.file.ToString();uint error=CommDlgExtendedError();if(error!=0)throw new BlueprintImportException("file-picker-error: "+error);return null;}
 }
}
