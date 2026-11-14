using System;using System.Reflection;using System.Runtime.InteropServices;using UnityEngine;using UnityEditor;using SpatialWallpaper.Sdk.Capabilities;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Editor.Native;
namespace SpatialWallpaper.Sdk.Editor {
 // Fixed Unity 2022.3 Game View geometry adapter. Missing/private API changes or
 // unverified display scaling fail closed; no whole-Editor keyboard capture.
 internal sealed class PlayInput:IDisposable {
  [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
  [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
  [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
  [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
  readonly InputBroker broker=new InputBroker();readonly InputTarget target;
  readonly Type gameView=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
  EditorWindow view;IntPtr hwnd;WindowsPreviewInput native;bool closed;
  internal PlayInput(WorkerIdentity identity){target=broker.Register(identity,false);EditorApplication.update+=Update;}
  bool Region(bool focus,out int left,out int top,out int width,out int height){
   left=top=width=height=0;
   if(closed||Application.unityVersion!="2022.3.62f3"||!EditorApplication.isPlaying||EditorApplication.isPaused||view==null||EditorWindow.mouseOverWindow!=view||focus&&EditorWindow.focusedWindow!=view||EditorGUIUtility.pixelsPerPoint!=1f||GetDpiForWindow(hwnd)!=96||!GetCursorPos(out var cursor))return false;
   try{
    var flags=BindingFlags.Instance|BindingFlags.NonPublic;var visible=(Rect)gameView.GetProperty("viewInWindow",flags).GetValue(view);var image=(Rect)gameView.GetProperty("targetInView",flags).GetValue(view);
    Vector2 origin=view.position.position;visible.position+=origin;image.position+=origin+new Vector2(0,visible.y-origin.y);
    if(!visible.Contains(new Vector2(cursor.X,cursor.Y))||!image.Contains(new Vector2(cursor.X,cursor.Y)))return false;
    left=Mathf.RoundToInt(image.x);top=Mathf.RoundToInt(image.y);width=Mathf.RoundToInt(image.width);height=Mathf.RoundToInt(image.height);return width>0&&height>0;
   }catch{return false;}
  }
  void Update(){
   if(closed)return;var candidate=EditorWindow.mouseOverWindow;
   if(!EditorApplication.isPlaying||EditorApplication.isPaused||candidate==null||candidate.GetType()!=gameView||EditorGUIUtility.pixelsPerPoint!=1f||!GetCursorPos(out var point)){target.Revoke();return;}
   IntPtr next=WindowFromPoint(point);if(Application.unityVersion!="2022.3.62f3"||GetDpiForWindow(next)!=96){target.Revoke();return;}
   if(candidate==view&&next==hwnd&&native!=null)return;
   native?.Dispose();native=null;target.Revoke();view=candidate;hwnd=next;
   try{native=new WindowsPreviewInput(hwnd,target,Region,()=>view.Focus());}catch{view=null;hwnd=IntPtr.Zero;target.Revoke();}
  }
  internal byte[] Snapshot(){Update();return target.Snapshot();}
  public void Dispose(){if(closed)return;closed=true;EditorApplication.update-=Update;native?.Dispose();native=null;broker.Dispose();}
 }
}
