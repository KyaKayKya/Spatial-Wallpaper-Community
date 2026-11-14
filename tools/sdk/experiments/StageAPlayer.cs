// Trusted rendering experiment. No author program is loaded by this component.
using System;
using System.IO;
using System.Collections;
using Microsoft.Win32.SafeHandles;
using UnityEngine;
public sealed class StageAPlayer:MonoBehaviour {
 public Transform content;
 int frames;string output,pipe;Camera view;
 void Start(){var args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++){if(args[i]=="--stage-a-output")output=args[i+1];if(args[i]=="--stage-a-pipe-handle")pipe=args[i+1];}view=GetComponent<Camera>();Application.runInBackground=true;}
 void Update(){
  frames++;
  if(frames!=60||output==null)return;
  try {
   var box=FindObjectOfType<StageABox>();
   if(!box||box.disabled)throw new Exception("Cilbox-disabled");
   var proxy=content.GetComponent<Cilbox.CilboxProxy>();
   int index=Array.IndexOf(proxy.cls.instanceFieldNames,"count");
   int count=proxy.fields[index].i;
   if(count<50)throw new Exception("interpreted-updates-not-observed:"+count);
   var rt=new RenderTexture(640,360,24);var pixels=new Texture2D(640,360,TextureFormat.RGB24,false);
   view.targetTexture=rt;view.Render();RenderTexture.active=rt;
   pixels.ReadPixels(new Rect(0,0,640,360),0,0);pixels.Apply();
   if(pipe!=null){using(var channel=new FileStream(new SafeFileHandle(new IntPtr(long.Parse(pipe)),true),FileAccess.Write)){using(var writer=new BinaryWriter(channel)){var raw=pixels.GetRawTextureData();writer.Write(0x53574631);writer.Write(640);writer.Write(360);writer.Write(raw.Length);writer.Write(raw);}}}
   Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"frame.png"),pixels.EncodeToPNG());
   File.WriteAllText(Path.Combine(output,"render.json"),"{\"interpreted_count\":"+count+",\"frames\":"+frames+",\"graphics\":\""+SystemInfo.graphicsDeviceType+"\",\"device\":\""+SystemInfo.graphicsDeviceName+"\",\"width\":640,\"height\":360}");
   RenderTexture.active=null;view.targetTexture=null;Destroy(rt);Destroy(pixels);Application.Quit();
  }catch(Exception e){Debug.LogException(e);Application.Quit(20);}
 }
}
