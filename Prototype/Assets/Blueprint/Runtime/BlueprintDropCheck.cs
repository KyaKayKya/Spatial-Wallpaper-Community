using System;
using System.IO;
using System.Collections;
using UnityEngine;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Blueprint {
 public sealed class BlueprintDropCheck:MonoBehaviour {
  IEnumerator Start(){string output=BlueprintScene.Argument("--blueprint-drop-check");if(output==null)yield break;Directory.CreateDirectory(output);yield return null;var scene=FindObjectOfType<BlueprintScene>();BlueprintEditorPanel.Open(scene);yield return null;File.WriteAllText(Path.Combine(output,"waiting.json"),new JObject{["pid"]=System.Diagnostics.Process.GetCurrentProcess().Id,["waiting_for_native_drop"]=true}.ToString());float deadline=Time.realtimeSinceStartup+40;while(!scene.Ready&&Time.realtimeSinceStartup<deadline)yield return null;bool pass=scene.Ready&&scene.Document.BlockCount==2090;File.WriteAllText(Path.Combine(output,"drop-checks.json"),new JObject{["pass"]=pass,["standalone"]=!Application.isEditor,["native_receiver"]=true,["blocks"]=scene.Ready?scene.Document.BlockCount:0,["error"]=scene.Ready?null:scene.Error}.ToString());yield return new WaitForEndOfFrame();var tex=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(output,"native-drop-editor.png"),tex.EncodeToPNG());Destroy(tex);Application.Quit(pass?0:2);}
 }
}
