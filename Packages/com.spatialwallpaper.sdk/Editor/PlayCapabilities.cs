using System;using System.IO;using System.IO.Pipes;using System.Diagnostics;using System.Runtime.InteropServices;using System.Security.Cryptography;using System.Text;using UnityEngine;using UnityEditor;using SpatialWallpaper.Sdk.Capabilities;using SpatialWallpaper.Sdk.Execution;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Sdk.Editor {
 [InitializeOnLoad]
 internal static class PlayCapabilities {
  static Process broker;static WindowsCapabilityClient client;static OwnedDomain domain;
  static GameObject pump;static PlayInput input;
  static readonly System.Collections.Generic.Dictionary<AudioClip,SoundClipData> sounds=new System.Collections.Generic.Dictionary<AudioClip,SoundClipData>();
  static SoundClipData ReadSound(AudioClip clip){if(!clip)throw new ArgumentException("sound-clip");if(sounds.TryGetValue(clip,out var value))return value;byte[] bytes=SceneResourceEncoding.Sound(clip);value=new SoundClipData(bytes);new ManagedReservation(domain.Ledger,bytes.Length+256L).Bind(value);sounds.Add(clip,value);return value;}
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool DuplicateHandle(IntPtr sourceProcess,IntPtr source,IntPtr targetProcess,out IntPtr target,uint access,bool inherit,uint options);
  [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
  // Unity can still run the author's final Update after ExitingPlayMode.
  // Keep its session bound until edit mode; domain reload and quitting still close first.
  static PlayCapabilities(){AssemblyReloadEvents.beforeAssemblyReload+=Stop;EditorApplication.quitting+=Stop;EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredEditMode)Stop();};}
  static IntPtr Duplicate(IntPtr source){if(!DuplicateHandle(GetCurrentProcess(),source,GetCurrentProcess(),out var target,0,false,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return target;}
  static string Quote(string value){if(value.IndexOf('"')>=0||value.EndsWith("\\",StringComparison.Ordinal))throw new ArgumentException("play-broker-path");return "\""+value+"\"";}
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  static void Start(){
   Stop();if(Application.platform!=RuntimePlatform.WindowsEditor)return;
   string executable=Environment.GetEnvironmentVariable("SDK_PLAY_BROKER");if(string.IsNullOrEmpty(executable))executable=Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PlayCapabilities).Assembly).resolvedPath,"Editor/Windows~/PlayBroker.exe");
   // A missing install is reported when a capability is used, allowing pure visual Play.
   if(!File.Exists(executable))return;
   IntPtr outgoing=IntPtr.Zero,incoming=IntPtr.Zero;
   try{
    Guid attempt=Guid.NewGuid(),instance=Guid.NewGuid();byte[] digest;using(var sha=SHA256.Create())digest=sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(Application.dataPath).ToUpperInvariant()));var guid=new byte[16];Array.Copy(digest,guid,16);Guid installation=new Guid(guid);
    string store=Environment.GetEnvironmentVariable("SDK_PLAY_STORE");if(string.IsNullOrEmpty(store))store=Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/SpatialWallpaper/PlayStorage"));
    using(var requests=new AnonymousPipeServerStream(PipeDirection.Out,HandleInheritability.Inheritable))using(var replies=new AnonymousPipeServerStream(PipeDirection.In,HandleInheritability.Inheritable)){
     outgoing=Duplicate(requests.SafePipeHandle.DangerousGetHandle());incoming=Duplicate(replies.SafePipeHandle.DangerousGetHandle());
     string configuration=Environment.GetEnvironmentVariable("SDK_PLAY_LLM_CONFIG"),audio=Environment.GetEnvironmentVariable("SDK_PLAY_AUDIO_CONFIG");if(string.IsNullOrEmpty(audio)&&File.Exists(PlayAudioSelection.PathName))audio=PlayAudioSelection.PathName;string parameters=requests.GetClientHandleAsString()+" "+replies.GetClientHandleAsString()+" "+attempt.ToString("N")+" "+instance.ToString("N")+" "+installation.ToString("N")+" "+Quote(store);if(!string.IsNullOrEmpty(configuration)||!string.IsNullOrEmpty(audio))parameters+=" "+Quote(configuration??"");if(!string.IsNullOrEmpty(audio))parameters+=" "+Quote(audio);
     broker=Process.Start(new ProcessStartInfo(executable,parameters){UseShellExecute=false,CreateNoWindow=true});
     requests.DisposeLocalCopyOfClientHandle();replies.DisposeLocalCopyOfClientHandle();
     domain=new OwnedDomain(instance,1);var requestHandle=outgoing;var replyHandle=incoming;outgoing=incoming=IntPtr.Zero;var identity=new WorkerIdentity(attempt,instance,1);client=new WindowsCapabilityClient(domain.Ledger,identity,requestHandle,replyHandle);var calls=new CapabilityCalls(domain.Ledger,client);input=new PlayInput(identity);calls.PlayInputSource=input.Snapshot;Storage.BindPlay(calls);Llm.BindPlay(calls);Audio.BindPlay(calls);Input.BindPlay(calls);Sound.BindPlay(calls.Sound,ReadSound);pump=new GameObject("SDK capability frames"){hideFlags=HideFlags.HideAndDontSave};UnityEngine.Object.DontDestroyOnLoad(pump);pump.AddComponent<CapabilityFramePump>().Initialize(calls);
    }
   }catch{if(outgoing!=IntPtr.Zero)CloseHandle(outgoing);if(incoming!=IntPtr.Zero)CloseHandle(incoming);Stop();throw;}
  }
  static void Stop(){if(pump!=null)UnityEngine.Object.DestroyImmediate(pump);pump=null;Sound.BindPlay(null,null);sounds.Clear();input?.Dispose();input=null;Audio.BindPlay(null);Input.BindPlay(null);Storage.BindPlay(null);Llm.BindPlay(null);client?.Dispose();if(broker!=null){try{if(!broker.WaitForExit(1000)){broker.Kill();broker.WaitForExit(1000);}}finally{broker.Dispose();broker=null;}}client=null;domain?.Dispose();domain=null;}
 }
}
