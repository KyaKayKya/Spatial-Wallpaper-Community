using System;using System.IO;using System.Threading.Tasks;using UnityEngine;using UnityEditor;
namespace SpatialWallpaper.Sdk.Editor {
 public sealed class SandboxPreviewWindow:EditorWindow {
  const string RuntimePreference="SpatialWallpaper.RuntimeDirectory.v1";SceneDefinition definition;string runtime;int resolution;Vector2 physical=new Vector2(.53f,.298125f);string error;Vector2 scroll;Task command=Task.CompletedTask;Newtonsoft.Json.Linq.JArray audioSources=new Newtonsoft.Json.Linq.JArray();string[] audioLabels=new[]{"不采集音频"};int audioChoice;
  [MenuItem("Spatial Wallpaper/Sandbox Preview")]public static void Open(){GetWindow<SandboxPreviewWindow>("沙箱预览");}
  void OnEnable(){runtime=EditorPrefs.GetString(RuntimePreference,"");if(Selection.activeGameObject)definition=Selection.activeGameObject.GetComponentInParent<SceneDefinition>();RefreshAudio();}
  void RefreshAudio(){try{if(Application.platform!=RuntimePlatform.WindowsEditor)return;audioSources=SpatialWallpaper.Sdk.Hosting.TrustedAudioSource.Applications();var selected=PlayAudioSelection.Read();audioChoice=0;var labels=new System.Collections.Generic.List<string>{"不采集音频"};for(int i=0;i<audioSources.Count;i++){labels.Add((string)audioSources[i]["name"]);if(Newtonsoft.Json.Linq.JToken.DeepEquals(selected,audioSources[i]["source"]))audioChoice=i+1;}if(selected!=null&&audioChoice==0){audioSources.Add(new Newtonsoft.Json.Linq.JObject{{"name","所选应用已退出或没有窗口"},{"source",selected}});labels.Add("所选应用已退出或没有窗口");audioChoice=labels.Count-1;}audioLabels=labels.ToArray();}catch(Exception fault){error=fault.GetBaseException().Message;}}
  void OnInspectorUpdate(){Repaint();}
  async void Execute(Func<Task> action){try{command=action();await command;error=null;}catch(OperationCanceledException){}catch(Exception fault){error=fault.GetBaseException().Message;}Repaint();}
  Task Launch(bool restart){int width=resolution==0?640:resolution==1?1280:1920,height=resolution==0?360:resolution==1?720:1080;string attempts=Path.Combine(SandboxPreviewController.DefaultDataDirectory(),"attempts");return restart?SandboxPreviewController.RestartAsync(runtime,attempts,width,height,physical.x,physical.y):SandboxPreviewController.StartAsync(runtime,attempts,width,height,physical.x,physical.y);}
  void OnGUI(){
   EditorGUILayout.LabelField("制作场景 → 检查快照 → 沙箱预览 → 导出",EditorStyles.boldLabel);EditorGUILayout.HelpBox("作者脚本只在受限 Worker 中执行。预览画布位于独立窗口；普通 Play 不能代替沙箱验证。",MessageType.Info);
   definition=(SceneDefinition)EditorGUILayout.ObjectField("场景定义",definition,typeof(SceneDefinition),true);
   using(new EditorGUILayout.HorizontalScope()){runtime=EditorGUILayout.TextField("运行时发行目录",runtime);if(GUILayout.Button("选择",GUILayout.Width(54))){string selected=EditorUtility.OpenFolderPanel("选择完整运行时发行目录",runtime,"");if(selected!=""){runtime=selected;EditorPrefs.SetString(RuntimePreference,runtime);}}}
   resolution=EditorGUILayout.Popup("虚拟屏幕分辨率",resolution,new[]{"640 × 360","1280 × 720","1920 × 1080"});physical=EditorGUILayout.Vector2Field("物理尺寸（米）",physical);
   using(new EditorGUI.DisabledScope(!SandboxPreviewController.ResourcesReleased||EditorApplication.isPlayingOrWillChangePlaymode)){using(new EditorGUILayout.HorizontalScope()){int next=EditorGUILayout.Popup("音频来源应用",audioChoice,audioLabels);if(next!=audioChoice)try{PlayAudioSelection.Select(next==0?null:audioSources[next-1]["source"]);audioChoice=next;error=null;}catch(Exception fault){error=fault.GetBaseException().Message;}if(GUILayout.Button("刷新应用",GUILayout.Width(80)))RefreshAudio();}}
   EditorGUILayout.HelpBox("只采集所选应用及其子进程的派生音频特征。未选择时静音；来源退出后不自动换源。此选择用于下一次普通 Play 和沙箱预览。",MessageType.None);
   bool idle=command.IsCompleted&&SandboxPreviewController.ResourcesReleased;
   using(new EditorGUI.DisabledScope(!idle||!definition)){if(GUILayout.Button("检查并制作预览快照"))try{SandboxPreviewController.Prepare(definition,SandboxPreviewController.DefaultDataDirectory());error=null;}catch(Exception fault){error=fault.GetBaseException().Message;}}
   using(new EditorGUILayout.HorizontalScope()){
    using(new EditorGUI.DisabledScope(!idle||SandboxPreviewController.BuildId==null||string.IsNullOrWhiteSpace(runtime))){if(GUILayout.Button("沙箱预览"))Execute(()=>Launch(false));}
    using(new EditorGUI.DisabledScope(!command.IsCompleted||SandboxPreviewController.BuildId==null||SandboxPreviewController.RecoveryPending||string.IsNullOrWhiteSpace(runtime)||EditorApplication.isPlayingOrWillChangePlaymode)){if(GUILayout.Button("重启预览"))Execute(()=>Launch(true));}
    using(new EditorGUI.DisabledScope(SandboxPreviewController.ResourcesReleased)){if(GUILayout.Button("停止"))Execute(()=>SandboxPreviewController.StopAsync());}
    using(new EditorGUI.DisabledScope(!SandboxPreviewController.CanExport)){if(GUILayout.Button("导出已验证快照")){string path=EditorUtility.SaveFilePanel("导出已经沙箱呈现的同一快照","","wallpaper","spatial");if(path!="")try{SandboxPreviewController.Export(path);error=null;}catch(Exception fault){error=fault.GetBaseException().Message;}}}
   }
   EditorGUILayout.LabelField("状态",SandboxPreviewController.State);if(SandboxPreviewController.BuildId!=null){EditorGUILayout.SelectableLabel("构建："+SandboxPreviewController.BuildId,EditorStyles.textField,GUILayout.Height(20));EditorGUILayout.LabelField("能力",string.Join(", ",SandboxPreviewController.Capabilities));EditorGUILayout.HelpBox("修改场景后重新制作快照。导出保留已预览的同一份字节；素材许可由作者声明，LLM 使用确定性本机模拟，外部服务未验证。",MessageType.None);}
   if(SandboxPreviewController.RecoveryPending&&command.IsCompleted&&GUILayout.Button("重试恢复并确认旧预览退出"))Execute(()=>SandboxPreviewController.RetryRecoveryAsync());
   string message=error??SandboxPreviewController.Error;if(message!=null)EditorGUILayout.HelpBox(message,MessageType.Error);var receipt=SandboxPreviewController.LastReceipt;if(receipt!=null){scroll=EditorGUILayout.BeginScrollView(scroll);EditorGUILayout.TextArea(receipt.ToString(),GUILayout.MinHeight(90));EditorGUILayout.EndScrollView();}
  }
 }
}
