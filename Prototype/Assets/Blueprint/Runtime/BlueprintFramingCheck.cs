using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Globalization;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
namespace SpatialWallpaper.Blueprint {
 // Explicit developer-only UI and Player acceptance; stripped from the library scene.
 public sealed class BlueprintFramingCheck:MonoBehaviour {
  string output;JArray results=new JArray();BlueprintScene scene;WindowProjection projection;Camera camera;
  static void Require(bool value,string message){if(!value)throw new Exception(message);}
  static void Click(string name){FindObjectsOfType<Evo.UI.Button>().Single(b=>b.name==name).onClick.Invoke();}
  static TMP_InputField Field(string name)=>FindObjectsOfType<TMP_InputField>().Single(f=>f.name==name);
  static void Number(string name,float value){var input=Field("Blueprint "+name);input.text=value.ToString("R",CultureInfo.InvariantCulture);input.onEndEdit.Invoke(input.text);}
  Bounds Bounds(){var renderers=scene.Building.GetComponentsInChildren<MeshRenderer>();var b=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))b.Encapsulate(renderer.bounds);return b;}
  Rect Projected(){var b=Bounds();Vector2 low=Vector2.one*float.PositiveInfinity,high=Vector2.one*float.NegativeInfinity;for(int i=0;i<8;i++){var p=camera.WorldToViewportPoint(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));Require(p.z>0,"visible mesh crossed observer");low=Vector2.Min(low,p);high=Vector2.Max(high,p);}return Rect.MinMaxRect(low.x,low.y,high.x,high.y);}
  float Corners(){float error=0;for(int i=0;i<4;i++){var p=camera.WorldToViewportPoint(projection.screenFrame.TransformPoint(new Vector3((i%2==0?-1:1)*projection.displayWidth/2,(i/2==0?-1:1)*projection.displayHeight/2,0)));error=Mathf.Max(error,new Vector2((p.x-i%2)*camera.pixelWidth,(p.y-i/2)*camera.pixelHeight).magnitude);}return error;}
  IEnumerator Shot(string name,bool editor){yield return new WaitForEndOfFrame();var previous=RenderTexture.active;if(!editor)RenderTexture.active=camera.targetTexture;int w=editor?Screen.width:camera.targetTexture.width,h=editor?Screen.height:camera.targetTexture.height;var image=new Texture2D(w,h,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,w,h),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);RenderTexture.active=previous;}
  IEnumerator Start(){output=BlueprintScene.Argument("--blueprint-framing-check");if(output==null)yield break;Directory.CreateDirectory(output);scene=FindObjectOfType<BlueprintScene>();projection=GetComponent<WindowProjection>();camera=GetComponent<Camera>();GetComponent<PreviewEyeDriver>().enabled=false;GetComponent<AdaptiveRenderCadence>().enabled=false;projection.viewportManagedExternally=true;OnDemandRendering.renderFrameInterval=1;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;var run=Run();Exception error=null;while(true){bool next;object current;try{next=run.MoveNext();current=next?run.Current:null;}catch(Exception e){error=e;break;}if(!next)break;yield return current;}File.WriteAllText(Path.Combine(output,"checks.json"),new JObject{["pass"]=error==null,["standalone"]=!Application.isEditor,["simulated_eye_only"]=true,["cases"]=results,["error"]=error?.ToString()}.ToString());Application.Quit(error==null?0:2);}
  IEnumerator Run(){
   float deadline=Time.realtimeSinceStartup+90;while(!scene.Ready&&scene.Error==null&&Time.realtimeSinceStartup<deadline)yield return null;Require(scene.Ready,scene.Error??"initial import timeout");
   var cases=JArray.Parse(File.ReadAllText(BlueprintScene.Argument("--blueprint-framing-manifest")));
   var padded=(JObject)cases.Single(c=>(string)c["name"]=="padded-offset-cabin");var legacy=scene.LoadPath((string)padded["path"],null,1);while(!legacy.IsCompleted)yield return null;Require(legacy.Result&&scene.Placement.Width==161&&scene.Placement.Height==64&&scene.BlockOrigin.localPosition==new Vector3(-80.5f,0,-64.5f),"legacy layout changed");var legacyScale=scene.Building.localScale;var legacyPosition=scene.Building.localPosition;var legacyReload=scene.LoadPath(scene.SourcePath,scene.Document.SourceHash);while(!legacyReload.IsCompleted)yield return null;Require(legacyReload.Result&&scene.Settings.LayoutVersion==1&&scene.Building.localScale==legacyScale&&scene.Building.localPosition==legacyPosition,"legacy reopen changed composition");results.Add(new JObject{["name"]="legacy-padded-layout",["pass"]=true});
   foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(540,960)}){
    Screen.SetResolution(size.x,size.y,false);yield return new WaitForSeconds(.5f);
    foreach(JObject sample in cases){
     var record=new JObject{["name"]=(string)sample["name"],["kind"]=(string)sample["kind"],["screen"]=size.x+"x"+size.y,["expected"]=(string)sample["expected"]};results.Add(record);
     BlueprintEditorPanel.Open(scene);yield return null;Click("文件");yield return null;
     string previousSource=scene.SourcePath;var previousBuilding=scene.Building;Field("Blueprint import path").text=(string)sample["path"];Click("导入蓝图");yield return null;
     deadline=Time.realtimeSinceStartup+90;while(!scene.Ready&&scene.Error==null&&Time.realtimeSinceStartup<deadline)yield return null;yield return null;
     if((string)sample["expected"]!="supported"){
      Require(scene.Error!=null&&scene.Ready&&scene.SourcePath==previousSource&&scene.Building==previousBuilding,"failed import replaced active source");
      Require(scene.Error.Contains((string)sample["expected"]=="rejected-empty"?"empty-blueprint":"banner"),"unexpected rejection: "+scene.Error);record["rejection"]=scene.Error;record["pass"]=true;Click("取消");yield return null;continue;
     }
     Require(scene.Ready&&scene.Error==null,scene.Error??"import timeout");record["dimensions"]=new JArray(scene.Document.Width,scene.Document.Height,scene.Document.Length);record["visible_dimensions"]=new JArray(scene.Placement.Width,scene.Placement.Height,scene.Placement.Length);record["blocks"]=scene.Document.BlockCount;record["triangles"]=scene.Triangles;record["source_hash"]=scene.Document.SourceHash;
     Click("构图");yield return null;Click("恢复默认构图");yield return null;Number("水平旋转",(string)sample["name"]=="long-bridge"?75:(string)sample["name"]=="wide-gallery"?5:20);yield return null;
     float low=.1f,high=5;
     for(int iteration=0;iteration<14;iteration++){
      float scale=(low+high)*.5f;Number("统一缩放",scale);Number("前后位置",-.2f);yield return new WaitForEndOfFrame();var bounds=Bounds();float factor=scene.transform.lossyScale.x;
      float x=scene.Settings.OffsetX-bounds.center.x/factor,y=scene.Settings.OffsetY-bounds.center.y/factor;Require(Mathf.Abs(x)<=2&&Mathf.Abs(y)<=1,"composition translation exceeds available controls");Number("左右位置",x);Number("上下位置",y);yield return new WaitForEndOfFrame();var rect=Projected();float fill=Mathf.Max(rect.width,rect.height);if(fill<.62f)low=scale;else high=scale;
     }
     var final=Projected();float coverage=Mathf.Max(final.width,final.height);record["fill"]=coverage;record["viewport_bounds"]=new JArray(final.xMin,final.yMin,final.xMax,final.yMax);record["settings"]=JObject.Parse(scene.Settings.ToJson());record["preview_resolution"]=camera.targetTexture.width+"x"+camera.targetTexture.height;
     Require(coverage>.58f&&coverage<.67f&&final.xMin>.03f&&final.yMin>.03f&&final.xMax<.97f&&final.yMax<.97f,"cannot frame visible mesh with existing controls");
     float worst=0;var matrix=scene.Building.localToWorldMatrix;var reference=projection.screenFrame.localToWorldMatrix;var rotation=camera.transform.rotation;
     var eyes=new[]{new Vector3(0,0,-.6f),new Vector3(-.16f,0,-.6f),new Vector3(.16f,0,-.6f),new Vector3(0,.10f,-.6f),new Vector3(0,-.06f,-.6f),new Vector3(0,0,-.4f),new Vector3(0,0,-.95f)};
     for(int i=0;i<eyes.Length;i++){projection.SetEye(eyes[i]);yield return new WaitForEndOfFrame();worst=Mathf.Max(worst,Corners());Require(scene.Building.localToWorldMatrix==matrix&&projection.screenFrame.localToWorldMatrix==reference&&camera.transform.rotation==rotation,"eye changed content/frame/rotation");if(i==0||i==1||i==2)yield return Shot((string)sample["name"]+"-"+size.x+"x"+size.y+"-eye"+i,false);}
     Require(worst<.01f,"fixed window corner drift");record["corner_error_px"]=worst;record["eye_cases"]=eyes.Length;record["eye_content_changes"]=0;projection.SetEye(eyes[0]);yield return new WaitForEndOfFrame();yield return Shot((string)sample["name"]+"-"+size.x+"x"+size.y+"-editor",true);
     var position=scene.Building.localPosition;var localScale=scene.Building.localScale;var localRotation=scene.Building.localRotation;Require(localScale.x==localScale.y&&localScale.x==localScale.z,"non-uniform model scale");Field("Blueprint work name").text=(string)sample["name"]+" "+size.x+"x"+size.y;Click("完成");yield return null;yield return null;
     var work=scene.Work;Require(work!=null,"work not saved");var reopened=scene.LoadPath(BlueprintLibrary.Store.SourcePath(work.SourceHash),work.SourceHash);while(!reopened.IsCompleted)yield return null;Require(reopened.Result&&Vector3.Distance(scene.Building.localPosition,position)<.00001f&&Vector3.Distance(scene.Building.localScale,localScale)<.00001f&&Quaternion.Angle(scene.Building.localRotation,localRotation)<.01f,"save/reopen changed composition");record["artwork_id"]="blueprint-"+work.WorkId+"-"+work.RevisionId;record["save_reopen"]=true;record["pass"]=true;
    }
   }
  }
 }
}
