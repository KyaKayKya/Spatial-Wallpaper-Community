using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using SpatialWallpaper.Desktop;

namespace SpatialWallpaper.Mita {
 public sealed class MitaDesktop : MonoBehaviour {
  WindowsDesktopWindow desktop;
  public bool managed;
  public DesktopDisplay managedDisplay;
  bool pauseHeld;
  bool clickHeld;
  int gestureKeys;
  [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
  [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
  [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
  [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,System.Text.StringBuilder name,int size);
  [Serializable] sealed class Proof {public int pid,width,height;public long hwnd;public bool attached,verified,detached;}
  public bool TryGetCursor(out Vector2 position){
   position=Vector2.zero;if(managedDisplay==null&&(desktop==null||desktop.Window==IntPtr.Zero))return false;
   if(!GetCursorPos(out var cursor))throw new InvalidOperationException("Cannot read desktop cursor");
   if(managedDisplay!=null){position=new Vector2((float)(cursor.X-managedDisplay.X)/managedDisplay.Width,1f-(float)(cursor.Y-managedDisplay.Y)/managedDisplay.Height);return true;}
   var bounds=desktop.Bounds;position=new Vector2((float)(cursor.X-bounds.Left)/(bounds.Right-bounds.Left),1f-(float)(cursor.Y-bounds.Top)/(bounds.Bottom-bounds.Top));return true;
  }
  IEnumerator Start(){
   if(managed)yield break;
   var args=Environment.GetCommandLineArgs();
   if(Array.IndexOf(args,"--desktop")<0)yield break;
   yield return null;
   var displays=WindowsDesktopWindow.Displays();
   int index=Array.IndexOf(args,"--display");int display=index>=0?int.Parse(args[index+1]):0;
   if(display<0||display>=displays.Length)throw new ArgumentOutOfRangeException("--display");
   var window=WindowsDesktopWindow.FindOwnWindow();if(window==IntPtr.Zero)throw new InvalidOperationException("Unity window unavailable");
   DisplayViewport.RegisterRenderWindow(window);
   desktop=new WindowsDesktopWindow();desktop.Attach(window,displays[display].Id);
   var proof=new Proof{pid=Process.GetCurrentProcess().Id,hwnd=window.ToInt64(),attached=true,verified=desktop.Verify(),width=displays[display].Width,height=displays[display].Height};
   string output=Environment.GetEnvironmentVariable("MITA_OUTPUT");Directory.CreateDirectory(output);
   File.WriteAllText(Path.Combine(output,"desktop-active.json"),JsonUtility.ToJson(proof,true));
   if(Array.IndexOf(args,"--desktop-test")>=0){
    yield return new WaitForSeconds(12);
    proof.verified&=desktop.Verify();
    desktop.Detach();proof.detached=desktop.Window==IntPtr.Zero;
    File.WriteAllText(Path.Combine(output,"desktop-proof.json"),JsonUtility.ToJson(proof,true));Application.Quit();
   }
  }
  void Update(){
   if(desktop==null&&managedDisplay==null)return;
   bool click=(GetAsyncKeyState(1)&0x8000)!=0;
   if(click&&!clickHeld&&TryGetCursor(out var cursor)){
    var name=new System.Text.StringBuilder(128);GetClassName(GetForegroundWindow(),name,128);
    var behaviour=GetComponent<MitaBehaviour>();
    if(behaviour.InputEnabled&&(name.ToString()=="Progman"||name.ToString()=="WorkerW"))behaviour.Touch(new Vector2(cursor.x*Screen.width,cursor.y*Screen.height));
   }
   clickHeld=click;
   bool modifiers=(GetAsyncKeyState(0x11)&0x8000)!=0&&(GetAsyncKeyState(0x12)&0x8000)!=0;
   int keys=0;
   if(modifiers&&GetComponent<MitaBehaviour>().InputEnabled)for(int i=0;i<6;i++){
    if((GetAsyncKeyState(0x31+i)&0x8000)==0)continue;
    keys|=1<<i;if((gestureKeys&(1<<i))==0)GetComponent<MitaGestures>().Play(i);
   }
   gestureKeys=keys;
   if(managed)return;
   if(modifiers&&(GetAsyncKeyState(0x51)&0x8000)!=0)Application.Quit();
   bool pause=modifiers&&(GetAsyncKeyState(0x50)&0x8000)!=0;
   if(pause&&!pauseHeld)GetComponent<MitaBehaviour>().TogglePause();pauseHeld=pause;
  }
  void OnApplicationQuit(){desktop?.Dispose();}
  void OnDestroy(){desktop?.Dispose();}
 }
}
