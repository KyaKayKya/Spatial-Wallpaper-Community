using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace SpatialWallpaper.Desktop {
 [DefaultExecutionOrder(-900)]public sealed class DesktopManager:MonoBehaviour {
  public DesktopHostClient Client{get;private set;}
  CalibrationRuntime runtime;LibraryPreviewController library;bool polling,disposed,connecting;float nextPoll,nextConnect;string fault;
  DesktopManagerLease lease;
  bool Stopping=>disposed||DesktopLifetime.ShutdownRequested(DesktopRuntime.DataRoot);
  void FinishFullExit(){if(disposed)return;disposed=true;GetComponent<CalibrationDemoView>().QuitFromDesktop();}
  bool previewActive,pagePreview,scenePreview;readonly System.Threading.SemaphoreSlim previewGate=new System.Threading.SemaphoreSlim(1,1);
  void Awake(){
   if(DesktopRuntime.IsRuntime||!string.IsNullOrEmpty(CalibrationRuntime.Argument("--library-smoke"))){enabled=false;return;}
   lease=DesktopManagerLease.TryAcquire(DesktopRuntime.DataRoot,out var owner);if(lease==null){DesktopPlatform.ActivateManager(owner);enabled=false;GetComponent<CalibrationDemoView>().enabled=false;GetComponent<CalibrationRuntime>().enabled=false;Application.Quit();return;}
   if(CalibrationRuntime.Argument("--desktop-tray-open")=="true"){
    if(DesktopLifetime.ShutdownRequested(DesktopRuntime.DataRoot)){enabled=false;GetComponent<CalibrationDemoView>().enabled=false;GetComponent<CalibrationRuntime>().enabled=false;Application.Quit();return;}
   }else DesktopLifetime.BeginLaunch(DesktopRuntime.DataRoot);
   runtime=GetComponent<CalibrationRuntime>();library=GetComponent<LibraryPreviewController>();Client=new DesktopHostClient();runtime.RemoteDesktop=Client;
  }
  IEnumerator Start(){library.WallpaperHost=Client;Client.Changed+=library.RefreshWallpaperState;yield return null;yield return CompleteStartup(true);if(Stopping){FinishFullExit();yield break;}Connect();}
  public static IEnumerator CompleteStartup(bool visible)=>DesktopPlatform.CompleteStartup(visible);
  async void Connect(){
   if(connecting||Stopping)return;connecting=true;
   try{
    // A stale discovery file is not proof of a live owner. The Runtime lock
    // rejects a duplicate launch without touching the existing instance.
    try{await Client.Connect();}catch(Exception){if(Stopping){FinishFullExit();return;}Launch(true);}
    if(Client.Available){await UpdatePreview();return;}
    float deadline=Time.realtimeSinceStartup+15;Exception last=null;
    while(!Stopping&&Time.realtimeSinceStartup<deadline){try{await Client.Connect();if(Client.Available){await UpdatePreview();if(pagePreview)runtime.projection.GetComponent<Camera>().enabled=true;fault=null;library.RefreshWallpaperState();return;}}catch(Exception e){last=e;}await Task.Delay(250);}
    if(Stopping){FinishFullExit();return;}
    throw new IOException("桌面后台连接失败",last);
   }catch(Exception e){fault=e.Message;runtime.Report(fault);}finally{connecting=false;nextConnect=Time.realtimeSinceStartup+3;}
  }
  async void Update(){
   if(Stopping){FinishFullExit();return;}if(!Client.Available){if(!connecting&&Time.realtimeSinceStartup>=nextConnect)Connect();return;}
   if(polling||Time.realtimeSinceStartup<nextPoll)return;polling=true;
   try{await Client.Poll();if((bool?)Client.State["quitting"]==true){FinishFullExit();return;}runtime.UpdateRemoteCamera(Client.State);fault=null;}
   catch(Exception e){if(Stopping){FinishFullExit();return;}fault=e.Message;runtime.Report("桌面连接中断："+fault);}
   finally{polling=false;nextPoll=Time.realtimeSinceStartup+.05f;}
  }
  public async Task SetPreview(bool enabled){
   scenePreview=enabled;await UpdatePreview();
  }
  public async void SetPagePreview(bool enabled){
   pagePreview=enabled;var camera=runtime.projection.GetComponent<Camera>();if(enabled)camera.enabled=false;
   try{await UpdatePreview();if(enabled)camera.enabled=true;}catch(Exception e){runtime.Report("预览交接失败："+e.Message);}
  }
  async Task UpdatePreview(){
   float deadline=Time.realtimeSinceStartup+20;
   while(!Client.Available){if(Stopping)throw new ObjectDisposedException("DesktopManager");if(Time.realtimeSinceStartup>deadline)throw new IOException("桌面后台未能就绪，请重试预览");await Task.Delay(50);}
   await previewGate.WaitAsync();try{bool enabled=pagePreview||scenePreview;Client.TrackingRequested=enabled;if(enabled==previewActive)return;if(enabled)await Client.BeginPreview();else await Client.EndPreview();previewActive=enabled;}finally{previewGate.Release();}
  }
  public static void LaunchManager(){
   if(DesktopLifetime.ShutdownRequested(DesktopRuntime.DataRoot))return;
   int owner=DesktopManagerLease.ActiveOwner(DesktopRuntime.DataRoot);
   if(owner>0&&WindowsDesktopWindow.ActivateManager(owner))return;
   Launch(false);
  }
  static string Quote(string text){if(text.Contains("\"")||text.EndsWith("\\"))throw new ArgumentException("invalid-launch-path");return "\""+text+"\"";}
  static int Launch(bool runtimeMode){
   string executable;using(var process=Process.GetCurrentProcess())executable=process.MainModule.FileName;
   executable=DesktopPlatform.LaunchExecutable(executable);
   string args="--desktop-enabled true --desktop-data-root "+Quote(DesktopRuntime.DataRoot)+" --content-data-root "+Quote(DesktopRuntime.ContentRoot);
   if(runtimeMode)args+=" --desktop-runtime true";else args+=" --desktop-tray-open true";
   foreach(string name in new[]{"--data-root","--tracking-python","--tracking-root","--tracking-recording","--tracking-scenario","--native-presentation-diagnostics","--sdk-runtime","--sdk-attempts","--main-manager-probe"}){string value=CalibrationRuntime.Argument(name);if(!string.IsNullOrEmpty(value))args+=" "+name+" "+Quote(value);}
   args+=" -screen-fullscreen 0 -logFile "+Quote(Path.Combine(DesktopRuntime.DataRoot,runtimeMode?"runtime.log":"manager.log"));Directory.CreateDirectory(DesktopRuntime.DataRoot);
   using(var child=Process.Start(new ProcessStartInfo(executable,args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden}))return child.Id;
  }
  void OnDestroy(){disposed=true;if(Client!=null){if(library)Client.Changed-=library.RefreshWallpaperState;Client.Dispose();}lease?.Dispose();}
 }
}
