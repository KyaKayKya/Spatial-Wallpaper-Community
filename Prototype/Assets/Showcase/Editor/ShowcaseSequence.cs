using UnityEngine;
using UnityEditor;
using System.IO;
public static class ShowcaseSequence {
 static int frame;
 const int Frames=240;
 static bool running;
 public static string Begin(){
  if(running)return "Already capturing";
  Directory.CreateDirectory(Path.Combine(ShowcaseBuilder.Evidence,"frames"));frame=0;running=true;
  EditorApplication.update+=Tick;return "Capturing 240 frames at 1280x720; playback 30 fps";
 }
 static void Tick(){
  if(!running)return;
  try{
   float t=(float)frame/(Frames-1)*Mathf.PI*2;
   var eye=new Vector3(.20f*Mathf.Sin(t),.065f*Mathf.Sin(t*2),-.6f+.10f*Mathf.Sin(t));
   ShowcaseBuilder.Capture("frames/"+frame.ToString("D4"),eye,1280,720);
   frame++;
   if(frame>=Frames){running=false;EditorApplication.update-=Tick;File.WriteAllText(Path.Combine(ShowcaseBuilder.Evidence,"sequence-complete.txt"),"240 Unity-rendered frames; 1280x720; 30 fps playback; simulated eye. Offline capture is not a frame-rate benchmark.");}
  }catch(System.Exception ex){running=false;EditorApplication.update-=Tick;Debug.LogException(ex);}
 }
 public static string Status(){return "frames="+frame+" running="+running;}
}
