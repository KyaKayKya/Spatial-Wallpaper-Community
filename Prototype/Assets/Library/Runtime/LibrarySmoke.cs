using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpatialWallpaper.Library;
using TMPro;
using System.Threading.Tasks;
public sealed class LibrarySmoke:MonoBehaviour {
 public LibraryPreviewController Library;
 string output;CalibrationDemoView view;
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 bool failed;int corners,adaptedCorners,facadeRays;
 sealed class StopHost:IWallpaperHost {
  public bool Available=>true;public string ActiveArtworkId{get;private set;}="pool";public int Calls;
  public TaskCompletionSource<WallpaperResult> Pending=new TaskCompletionSource<WallpaperResult>();
  public Task<WallpaperResult> ApplyAsync(WallpaperRequest request)=>throw new NotSupportedException();
  public async Task<WallpaperResult> StopAsync(){Calls++;var result=await Pending.Task;if(result.Succeeded)ActiveArtworkId=null;return result;}
 }
 sealed class ApplyHost:IWallpaperHost {
  public bool Available=>true;public string ActiveArtworkId{get;private set;}="pool";public int Calls;public WallpaperRequest Request;
  public TaskCompletionSource<WallpaperResult> Pending=new TaskCompletionSource<WallpaperResult>();
  public async Task<WallpaperResult> ApplyAsync(WallpaperRequest request){Calls++;Request=request;var result=await Pending.Task;if(result.Succeeded)ActiveArtworkId=request.ArtworkId;return result;}
  public Task<WallpaperResult> StopAsync()=>throw new NotSupportedException();
 }
 void Require(bool value,string message){if(!value)throw new Exception(message);}
 void OnEnable(){Application.logMessageReceived+=Log;}
 void OnDisable(){Application.logMessageReceived-=Log;}
 void Log(string message,string trace,LogType type){if(output!=null&&(type==LogType.Exception||type==LogType.Error)){failed=true;File.AppendAllText(Path.Combine(output,"errors.txt"),message+"\n"+trace+"\n");}}
 IEnumerator Start(){
  output=CalibrationRuntime.Argument("--library-smoke");if(string.IsNullOrEmpty(output)){output=null;yield break;}Directory.CreateDirectory(output);
  view=GetComponent<CalibrationDemoView>();yield return new WaitForSeconds(1);
  Require(!Library.Loading&&Library.Loaded==null,"Startup loaded a scene");
  Require((string)typeof(CalibrationDemoView).GetField("currentPage",flags).GetValue(view)=="作品库","Library is not startup page");
  yield return Shot("library-wide.png");
  var noWallpaper=FindObjectsOfType<UnityEngine.UI.Button>().Single(x=>x.name=="Artwork no-wallpaper");Require(noWallpaper.transform.GetSiblingIndex()==0,"No-wallpaper card is not pinned first");
  var cover=Resources.Load<Texture2D>("Library/no-wallpaper");Require(cover&&noWallpaper.GetComponentInChildren<UnityEngine.UI.RawImage>().texture==cover,"No-wallpaper did not use common thumbnail resource");
  Require(noWallpaper.GetComponent<Evo.UI.ProceduralRect>()&&noWallpaper.GetComponent<CalibrationTooltip>()==null,"Card did not reuse Evo surface or has redundant tooltip");
  Require(FindObjectsOfType<CalibrationTooltip>().All(t=>t.Label!="作品库"&&t.Label!="单击查看，双击应用"),"Redundant navigation/card tooltip");
  var icons=Resources.Load<Evo.UI.IconLibrary>("ManagerIcons");Require(icons&&icons.GetSprite("Check (Circle Fill)").texture.width>=128,"Low-resolution or missing Evo selection icon");noWallpaper.onClick.Invoke();yield return null;
  Require(Library.NoWallpaperSelected&&Library.Loaded==null&&ArtworkCatalog.All.Count==8,"No-wallpaper became a content scene or new artworks are missing");Library.OpenPreview(false);yield return null;Require(Library.Loaded==null&&!Library.Loading,"No-wallpaper allowed preview");
  Require(!FindObjectsOfType<Evo.UI.Button>().Any(x=>x.name=="预览"),"No-wallpaper shows scene actions");
  var inactive=FindObjectsOfType<Evo.UI.Button>().Single(x=>x.name=="当前使用 系统壁纸");Require(!inactive.IsInteractable(),"Unavailable host allows restore");yield return Shot("no-wallpaper.png");
  var noSearch=FindObjectsOfType<TMP_InputField>().Single(x=>x.name=="Search artworks");noSearch.text="池核";yield return null;Require(FindObjectsOfType<UnityEngine.UI.Button>().Any(x=>x.name=="Artwork no-wallpaper"),"Search hid global no-wallpaper action");noSearch.text="";yield return null;
  var originalHost=Library.WallpaperHost;var stopHost=new StopHost();Library.WallpaperHost=stopHost;Library.SelectNoWallpaper();yield return null;
  Click("恢复原壁纸");yield return null;Require(Library.StopWallpaper.Busy&&!Library.StopWallpaper.Succeeded&&stopHost.ActiveArtworkId=="pool","Restore claimed success before acknowledgement");
  stopHost.Pending.SetResult(new WallpaperResult(false,"恢复失败，请重试"));yield return null;Require(!Library.StopWallpaper.Succeeded&&Library.StopWallpaper.CanRestore&&stopHost.ActiveArtworkId=="pool","Failed stop lost active wallpaper");
  stopHost.Pending=new TaskCompletionSource<WallpaperResult>();Click("恢复原壁纸");stopHost.Pending.SetResult(new WallpaperResult(true,"ack"));yield return null;Require(Library.StopWallpaper.Succeeded&&stopHost.ActiveArtworkId==null&&stopHost.Calls==2,"Restore retry did not acknowledge");
  Library.WallpaperHost=originalHost;Library.Select(ArtworkCatalog.All[0]);yield return null;
  yield return Pagination();
  foreach(var size in new[]{new Vector2Int(1000,700),new Vector2Int(800,640),new Vector2Int(500,640)}){Screen.SetResolution(size.x,size.y,false);yield return new WaitForSeconds(.6f);Require(Screen.width>=800&&Screen.height>=640,"Library minimum size not enforced");yield return Shot("library-"+size.x+".png");}
  Screen.SetResolution(1440,960,false);yield return new WaitForSeconds(.5f);
  string draft=Newtonsoft.Json.JsonConvert.SerializeObject(Library.Runtime.Session.Draft);
  var search=FindObjectsOfType<TMP_InputField>().Single(x=>x.name=="Search artworks");search.text="不存在的作品";yield return null;Require(!FindObjectsOfType<UnityEngine.UI.Button>().Any(IsContentCard),"Search failed");
  search.text="";yield return null;
  var managerDefaultPipeline=UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;var managerQualityPipeline=QualitySettings.renderPipeline;
  foreach(var entry in ArtworkCatalog.All){
   Library.Select(entry);yield return null;Click("预览");yield return Ready();
   Require(Library.Loaded==entry,"Wrong artwork loaded");Require(SceneManager.sceneCount==2,"Preview scenes leaked");
   Require(FindObjectsOfType<WindowProjection>().Length==1,"Multiple active projections");
   Require(FindObjectsOfType<Camera>().Count(x=>x.enabled&&x.name!="PoolReflectionCamera")==1,"Multiple output cameras");
   Require(FindObjectsOfType<PreviewEyeDriver>().Length==0&&FindObjectsOfType<PhysicalDisplayRig>().Length==0&&FindObjectsOfType<SpatialWallpaper.RainyCafe.RainyCafeDisplayRig>().Length==0,"Standalone driver retained in library preview");
   Require(Resources.Load<Texture2D>("Library/"+entry.Id),"Missing artwork cover: "+entry.Id);
   if(entry.Id=="mita"){
    yield return new WaitForSeconds(6.7f);
    var mita=FindObjectOfType<SpatialWallpaper.Mita.MitaBehaviour>();
    Require(mita.managed&&!FindObjectOfType<SpatialWallpaper.Mita.MitaTracking>()&&!FindObjectOfType<SpatialWallpaper.Mita.MitaDisplayRig>(),"Mita retained standalone ownership");
    Require(!UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline&&!QualitySettings.renderPipeline,"Mita original materials require Built-in pipeline");
    var look=mita.GetComponent<SpatialWallpaper.Mita.MitaLook>();var point=mita.view.WorldToScreenPoint((look.leftEye.position+look.rightEye.position)*.5f);
    var image=FindObjectsOfType<UnityEngine.UI.RawImage>().Single(x=>x.name=="Artwork spatial image");var imageCorners=new Vector3[4];image.rectTransform.GetWorldCorners(imageCorners);
    var screen=(Vector2)imageCorners[0]+new Vector2(point.x/mita.view.pixelWidth*(imageCorners[2].x-imageCorners[0].x),point.y/mita.view.pixelHeight*(imageCorners[2].y-imageCorners[0].y));
    Require(mita.TryMapCursor(screen,out var mapped)&&(mapped-(Vector2)point).magnitude<1,"Mita screen-to-preview cursor mapping failed");
    Require(!mita.TryMapCursor(new Vector2(imageCorners[0].x-10,imageCorners[0].y),out _),"Mita accepts cursor outside preview");
    Require(mita.Touch(mapped),"Mita face click not accepted in library viewport");yield return null;
    Require(look.Source==SpatialWallpaper.Mita.MitaAttentionSource.Click,"Mita click did not request user glance");
    var effects=mita.GetComponent<SpatialWallpaper.Mita.MitaEffects>();
    Require(effects.Clicks==1&&((Vector2)mita.view.WorldToScreenPoint(effects.click.transform.position)-mapped).magnitude<.25f,"Mita click effect missed preview cursor");
    yield return new WaitForSeconds(.7f);
   }
   Library.Drag(new Vector2(.2f,.1f));yield return null;Require(Library.MouseEye.x>.1f,"Mouse input ignored");
   Library.ResetEye();yield return null;Require(Library.MouseEye==Vector2.zero,"Reset failed");
   yield return Shot(entry.Id+"-preview.png");
   Click("全屏预览");yield return new WaitForSeconds(.8f);
   foreach(bool keepHeight in new[]{true,false}){
    Library.SetFraming(keepHeight);yield return new WaitForEndOfFrame();yield return null;
    var fullCamera=Library.Runtime.projection.GetComponent<Camera>();var pixels=fullCamera.pixelRect;
    Require(pixels.x==0&&pixels.y==0&&Mathf.Abs(pixels.width-Screen.width)<1&&Mathf.Abs(pixels.height-Screen.height)<1,"Fullscreen does not fill render buffer");
    Require(Mathf.Abs((float)Library.Runtime.projection.displayWidth/Library.Runtime.projection.displayHeight-(float)Screen.width/Screen.height)<.001f,"Fullscreen physical aperture aspect mismatch");
    Require(!FindObjectsOfType<RectTransform>().Any(x=>x.name=="Controls"),"Fullscreen still reserves control strip");
    var back=FindObjectsOfType<Evo.UI.Button>().Single(x=>x.name=="返回作品预览");Require(back.GetComponent<UnityEngine.UI.Graphic>().color.a==0,"Fullscreen back has colored background");
    yield return Shot(entry.Id+(keepHeight?"-fullscreen-height.png":"-fullscreen-width.png"));
   }
   Library.SetFraming(true);yield return Shot(entry.Id+"-fullscreen.png");
   var drag=FindObjectsOfType<CalibrationDragSurface>().Single();var rect=(RectTransform)drag.transform;var points=new Vector3[4];rect.GetWorldCorners(points);
   var pointer=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left,position=(points[0]+points[2])/2};
   var before=Library.MouseEye;drag.OnBeginDrag(pointer);pointer.position+=new Vector2(100,40);drag.OnDrag(pointer);drag.OnEndDrag(pointer);yield return null;Require(Library.MouseEye!=before,"Fullscreen mouse drag ignored");
   Click("返回作品预览");yield return new WaitForSeconds(.8f);
   // Inspect actual content reference box corners in the scene's authored frame.
   view.enabled=false;Library.Resize(.53f);var p=Library.Runtime.projection;p.displayWidth=.53f;p.displayHeight=.298125f;
   var camera=p.GetComponent<Camera>();
   var geometry=entry.Id=="limpool"?GameObject.Find("LimpoolArchitecture").GetComponentsInChildren<MeshFilter>():entry.Id=="rainycafe"?FindObjectOfType<SpatialWallpaper.RainyCafe.RainyCafeAtmosphere>().staticSurfaces.Select(x=>x.GetComponent<MeshFilter>()).ToArray():entry.Id=="mita"?new[]{GameObject.Find("Floor").GetComponent<MeshFilter>(),GameObject.Find("OriginalMenuBackdrop").GetComponent<MeshFilter>()}:Array.Empty<MeshFilter>();
   var geometryMatrices=geometry.Select(x=>x.transform.localToWorldMatrix).ToArray();int eyeIndex=0;
   var boundaryEyes=entry.Id=="limpool"?new[]{new Vector3(-.16f,0,-.6f),new Vector3(.16f,0,-.6f),new Vector3(0,.1f,-.6f),new Vector3(0,-.06f,-.6f),new Vector3(0,0,-.4f),new Vector3(0,0,-.95f)}:entry.Id=="rainycafe"?new[]{new Vector3(-.2f,0,-.6f),new Vector3(.2f,0,-.6f),new Vector3(0,.1f,-.6f),new Vector3(0,-.1f,-.6f),new Vector3(0,0,-.35f),new Vector3(0,0,-.95f)}:new[]{new Vector3(-.2f,0,-.67f),new Vector3(.2f,0,-.67f),new Vector3(0,.12f,-.67f),new Vector3(0,-.12f,-.67f),new Vector3(0,0,-.2f),new Vector3(0,0,-1.2f)};
   if(entry.Id=="limpool"){
    var medium=camera.GetComponent<LimpoolUnderwater>();Require(medium&&medium.water&&medium.screenFrame,"Limpool underwater binding missing from preview copy");
    float height=p.screenFrame.InverseTransformPoint(medium.water.position).y;
    boundaryEyes=boundaryEyes.Concat(new[]{new Vector3(-.16f,height-.02f,-.6f),new Vector3(.16f,height-.055f,-.6f),new Vector3(0,height,-.6f)}).ToArray();
   }
   if(entry.Id=="limpool"||entry.Id=="rainycafe"||entry.Id=="mita"){
    var floor=geometry.Single(x=>x.name.StartsWith(entry.Id=="limpool"?"Floor_Submerged":"Floor")&&x.name!="FloorLocalShadows").GetComponent<Renderer>().bounds;
    var aperture=FindObjectsOfType<DisplaySceneVolume>().Single();
    foreach(float x in new[]{-.5f,.5f}){var q=aperture.FrontCorner(x,-.5f);Require(Mathf.Abs(floor.max.y-q.y)<.0001f&&floor.min.x<=q.x+.0001f&&floor.max.x>=q.x-.0001f&&floor.min.z<=q.z+.0001f&&floor.max.z>=q.z,"Actual floor does not meet aperture: "+entry.Id);}
    Require(!aperture.showGuide&&!aperture.GetComponent<Renderer>(),"Visible aperture guide: "+entry.Id);
   }
   foreach(var eye in boundaryEyes){
    Require(p.SetEye(eye),"Rejected valid eye");yield return new WaitForEndOfFrame();
    var volume=FindObjectsOfType<DisplaySceneVolume>().Single();
    foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
     var v=camera.WorldToViewportPoint(volume.FrontCorner(x,y));
     float error=new Vector2((v.x-x-.5f)*camera.pixelWidth,(v.y-y-.5f)*camera.pixelHeight).magnitude;
     Require(error<.01f,"Content aperture moved: "+entry.Id+" error="+error);corners++;
    }
    for(int i=0;i<geometry.Length;i++)Require(geometryMatrices[i]==geometry[i].transform.localToWorldMatrix,"Eye moved content: "+entry.Id);
    if(geometry.Length>0)yield return Shot(entry.Id+"-boundary-"+(eyeIndex++)+".png");
   }
   var oldTarget=camera.targetTexture;var target=new RenderTexture(640,360,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
   var shellColliders=new System.Collections.Generic.List<MeshCollider>();bool previousBackfaces=Physics.queriesHitBackfaces;
   if(entry.Id=="rainycafe"){
    Physics.queriesHitBackfaces=true;
    foreach(var mesh in geometry.Where(x=>x.name=="Floor"||x.name=="Walls"||x.name=="Ceiling"||x.name.Contains("NearWallReturn"))){var collider=mesh.gameObject.AddComponent<MeshCollider>();collider.cookingOptions=MeshColliderCookingOptions.CookForFasterSimulation|MeshColliderCookingOptions.EnableMeshCleaning|MeshColliderCookingOptions.WeldColocatedVertices;collider.sharedMesh=mesh.sharedMesh;shellColliders.Add(collider);}
   }
   foreach(bool keepHeight in new[]{true,false})foreach(float aspect in new[]{16f/9,16f/10,4f/3,21f/9,9f/16}){
    Library.SetFraming(keepHeight);float w=.53f,h=w/aspect;p.displayWidth=w;p.displayHeight=h;Library.Resize(w,h);
    target.Release();target.width=640;target.height=Mathf.RoundToInt(640/aspect);target.Create();
    var volume=FindObjectsOfType<DisplaySceneVolume>().Single();
    Physics.SyncTransforms();
    foreach(var eye in boundaryEyes){
     Require(p.SetEye(eye),"Adapted eye rejected");yield return new WaitForEndOfFrame();
     if(entry.Id=="rainycafe")for(int x=0;x<5;x++)for(int y=0;y<5;y++){
      var sample=new Vector3((x/4f-.5f)*w*.999f,(y/4f-.5f)*h*.999f,0);float framing=(float)ArtworkFraming.Scale(w,h,keepHeight);
      // Only the revealed area outside the authored aperture needs facade coverage.
      // Rays through the original cafe windows may legitimately reach the night sky.
      if(Mathf.Abs(sample.x)<=.265f*framing&&Mathf.Abs(sample.y)<=.1490625f*framing)continue;
      facadeRays++;
      var point=p.screenFrame.TransformPoint(sample);var ray=new Ray(camera.transform.position,point-camera.transform.position);
      Require(Physics.Raycast(ray,camera.farClipPlane),"Cafe shell coverage missing: keepHeight="+keepHeight+" aspect="+aspect+" eye="+eye+" sample="+x+","+y);
     }
     foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
      var point=volume.transform.TransformPoint(new Vector3(x*w/volume.transform.lossyScale.x,y*h/volume.transform.lossyScale.y,0));var v=camera.WorldToViewportPoint(point);
      float error=new Vector2((v.x-x-.5f)*camera.pixelWidth,(v.y-y-.5f)*camera.pixelHeight).magnitude;Require(error<.02f,"Adapted content frame drift: "+error);adaptedCorners++;
     }
    }
    if(geometry.Length>0){var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=previous;File.WriteAllBytes(Path.Combine(output,entry.Id+"-adapt-"+(keepHeight?"height":"width")+"-"+aspect.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+".png"),image.EncodeToPNG());Destroy(image);}
   }
   foreach(var collider in shellColliders)Destroy(collider);Physics.queriesHitBackfaces=previousBackfaces;
   Library.SetFraming(true);camera.targetTexture=oldTarget;target.Release();Destroy(target);view.enabled=true;
   Click("返回作品库");yield return Ready();Require(Library.Loaded==null&&SceneManager.sceneCount==1,"Scene not released");
   Require(!Resources.FindObjectsOfTypeAll<Camera>().Any(c=>c.name=="PoolReflectionCamera"),"Pool reflection leaked");
   Require(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline==managerDefaultPipeline&&QualitySettings.renderPipeline==managerQualityPipeline,"Scene pipeline leaked: "+entry.Id);
  }
  Require(draft==Newtonsoft.Json.JsonConvert.SerializeObject(Library.Runtime.Session.Draft),"Mouse preview changed calibration");
  // Cancellation while asynchronous load is pending must not strand a scene.
  Library.OpenPreview(false);Library.ClosePreview();yield return Ready();Require(Library.Loaded==null&&SceneManager.sceneCount==1,"Loading cancellation leaked");
  // Camera mode uses the existing synthetic Tracker over the same TCP host.
  foreach(var entry in ArtworkCatalog.All){
   Library.Select(entry);Click("预览");yield return Ready();Click("摄像头");yield return Ready();yield return new WaitForSeconds(.4f);
   var runtime=Library.Runtime;Require(runtime.Fresh,"Synthetic camera not fresh: "+runtime.Diagnostic);
   runtime.BindCurrentSource();yield return null;Require(runtime.Save("作品库合成验证",false,true),"Cannot apply synthetic calibration: "+runtime.Message);yield return null;
   Require(runtime.projection.TrackingFresh,"Camera did not drive "+entry.Id);
   if(entry.Id=="mita"){
    yield return new WaitForSeconds(6.7f);var look=FindObjectOfType<SpatialWallpaper.Mita.MitaLook>();look.Glance();yield return null;
    Require(look.TrackingFresh&&look.Source==SpatialWallpaper.Mita.MitaAttentionSource.Click&&(look.RequestedTarget-runtime.projection.GetComponent<Camera>().transform.position).magnitude<.001f,"Mita gaze did not consume host tracking eye");
   }
   yield return Shot(entry.Id+"-camera.png");
   Click("全屏预览");yield return new WaitForSeconds(.8f);Require(runtime.projection.TrackingFresh,"Fullscreen lost camera tracking: "+entry.Id);yield return Shot(entry.Id+"-camera-fullscreen.png");Click("返回作品预览");yield return new WaitForSeconds(.8f);
   Click("返回作品库");yield return Ready();
  }
  Click("预览");yield return Ready();Click("摄像头");yield return Ready();Click("校准");yield return Ready();yield return new WaitForSeconds(.5f);Click("距离校正");yield return null;Click("保存");yield return Ready();Require(Library.Loaded==Library.Selected&&Library.CameraMode,"Calibration did not resume selected camera preview");Click("返回作品库");yield return Ready();
  yield return BindingAndApply();
  Require(!Library.WallpaperHost.Available&&Library.WallpaperHost.ActiveArtworkId==null,"Preview claimed desktop state");
  if(failed)throw new Exception("Player logged errors");
  File.WriteAllText(Path.Combine(output,"facade-coverage.txt"),"PASS RainyCafe expanded aperture facade coverage: "+facadeRays+" mesh rays across five aspect ratios, two framing modes and six eyes. Original aperture rays may reach the legitimate outdoor sky and are excluded. Validation colliders were removed before camera-mode checks.");
  File.WriteAllText(Path.Combine(output,"result.txt"),"PASS 60/660 artwork paging; direct pages/Enter/invalid jumps/ellipsis; bounded 12 cards; persistent scrollbar/pager/right panel; 4/3/2/1 responsive columns; real preset dropdown; per-artwork/display preset persistence; single click never applies; double-click pending/failure/retry/ack duplicate protection with fake host; pinned no-wallpaper selection/search/no-preview; inactive host disabled; asynchronous stop pending/failure/retry/ack; eight artwork names; search; minimum window recovery; mouse/reset/fullscreen drag; all eight fullscreen/return; "+corners+" authored content aperture corners; "+adaptedCorners+" adapted content basis corners; actual Limpool/RainyCafe/Mita floor lower corners and static geometry; hidden reference guides; no standalone eye/display drivers; scene pipeline restoration; additive scene and pool reflection release; cancellation; eight synthetic TCP camera previews/fullscreen; calibration continuation/isolation; unavailable native desktop boundary. Fixture catalog repeats eight real thumbnails for capacity tests, not newly installed artwork. Human camera accuracy and native wallpaper restoration not evaluated.");
  Application.Quit(0);
 }
 IEnumerator BindingAndApply(){
  var runtime=Library.Runtime;var first=runtime.Store.List().First();var second=runtime.Store.Save(first,"独立绑定验证",true);string draft=Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Session.Draft),applied=Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Applied);
  string target=LibraryDisplayCatalog.List().First().Id;
  Library.Select(ArtworkCatalog.All[0]);Library.SelectTarget(target);Library.SelectCalibrationPreset(first.id);Library.SelectTarget("other-display-fixture");Library.SelectCalibrationPreset(second.id);Library.SelectTarget(target);
  Require(Library.SelectedPresetId==first.id,"Preset selection leaked between displays");Library.Select(ArtworkCatalog.All[1]);Library.SelectTarget(target);Library.SelectCalibrationPreset(second.id);Library.Select(ArtworkCatalog.All[0]);Require(Library.SelectedPresetId==first.id,"Preset selection leaked between artworks");
  yield return null;Click("Calibration preset");yield return null;Require(FindObjectsOfType<Evo.UI.Button>().Any(b=>b.name==second.name),"Shared preset dropdown did not open real choices");yield return Shot("library-preset-dropdown.png");Click(second.name);yield return null;
  var actualFirst=runtime.Store.List().Where(p=>p.schema_version==2&&p.Valid()&&p.manual.Bound).First();Click("Calibration preset");yield return null;Click(actualFirst.name);yield return null;
  var dropdown=FindObjectsOfType<Evo.UI.Button>().Single(d=>d.name=="Calibration preset");Require(Library.SelectedPresetId==actualFirst.id&&dropdown.GetComponentInChildren<TMP_Text>().text==actualFirst.name,"Preset caption differs from actual binding");Library.SelectCalibrationPreset(first.id);
  Require(draft==Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Session.Draft)&&applied==Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Applied),"Library choice changed global calibration");
  var oldHost=Library.WallpaperHost;var host=new ApplyHost();Library.WallpaperHost=host;Library.Select(ArtworkCatalog.All[0]);yield return null;
  var card=FindObjectsOfType<UnityEngine.UI.Button>().Single(x=>x.name=="Artwork showcase");
  var pointer=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left,clickCount=1};
  UnityEngine.EventSystems.ExecuteEvents.Execute(card.gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);yield return null;Require(host.Calls==0,"Single click applied wallpaper");Require(card&&card.gameObject.activeInHierarchy,"Selection destroyed double-click target");
  pointer.clickCount=2;UnityEngine.EventSystems.ExecuteEvents.Execute(card.gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);yield return null;
  Require(host.Calls==1&&Library.ApplyWallpaper.Busy&&host.Request.DisplayId==target&&host.Request.CalibrationPresetId==first.id,"Double click did not apply selected preset/target");
  UnityEngine.EventSystems.ExecuteEvents.Execute(card.gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);Require(host.Calls==1,"Double click duplicated pending request");
  Library.Select(ArtworkCatalog.All[1]);host.Pending.SetResult(new WallpaperResult(false,"加载失败，请重试"));yield return null;Require(!Library.ApplyWallpaper.Succeeded&&host.ActiveArtworkId=="pool"&&Library.Selected.Id=="pool","Failed completion changed selection or desktop");
  Library.Select(ArtworkCatalog.All[0]);host.Pending=new TaskCompletionSource<WallpaperResult>();Click("应用到桌面");yield return null;host.Pending.SetResult(new WallpaperResult(true,"完成"));yield return null;Require(host.Calls==2&&Library.ApplyWallpaper.Succeeded&&host.ActiveArtworkId=="showcase","Explicit apply retry did not acknowledge");
  Library.WallpaperHost=oldHost;Library.Select(ArtworkCatalog.All[0]);yield return null;yield return Shot("library-refined.png");
 }
 IEnumerator Pagination(){
  var model=(LibraryPaging)typeof(CalibrationDemoView).GetField("libraryPages",flags).GetValue(view);
  Library.Catalog=Enumerable.Range(0,60).Select(i=>ArtworkCatalog.All[i%ArtworkCatalog.All.Count]).ToArray();Rebuild();Screen.SetResolution(1920,1080,false);yield return new WaitForSeconds(.6f);
  Require(model.PageCount==6&&FindObjectsOfType<UnityEngine.UI.Button>().Count(IsContentCard)==11,"60 artwork page not bounded");
  Click("Page 3");yield return null;Require(model.Page==3,"Direct page did not jump");yield return Shot("library-sixty.png");
  var jump=FindObjectsOfType<TMP_InputField>().Single(x=>x.name=="Jump page");jump.onSubmit.Invoke("6");yield return null;Require(model.Page==6&&FindObjectsOfType<UnityEngine.UI.Button>().Count(IsContentCard)==5,"Jump or last page count wrong");
  jump=FindObjectsOfType<TMP_InputField>().Single(x=>x.name=="Jump page");jump.onSubmit.Invoke("100");yield return null;Require(model.Page==6,"Invalid jump changed page");
  Library.Catalog=Enumerable.Range(0,660).Select(i=>ArtworkCatalog.All[i%ArtworkCatalog.All.Count]).ToArray();model.SetTotal(660);model.Jump(30);Rebuild();yield return null;Require(model.PageCount==60&&model.Page==30,"660 artwork page count wrong");Require(FindObjectsOfType<TMP_Text>().Count(t=>t.text=="…")>=2,"Missing page ellipses");yield return Shot("library-many-pages.png");
  foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1440,960),new Vector2Int(1000,700),new Vector2Int(800,640)}){
   Screen.SetResolution(size.x,size.y,false);yield return new WaitForSeconds(.6f);
   var grid=(RectTransform)typeof(CalibrationDemoView).GetField("libraryGrid",flags).GetValue(view);var scroll=(UnityEngine.UI.ScrollRect)typeof(CalibrationDemoView).GetField("libraryScroll",flags).GetValue(view);
   int columns=LibraryPaging.Columns(grid.sizeDelta.x);Require(columns==(size.x==1920?4:size.x==1440?3:size.x==1000?2:1),"Wrong responsive columns at "+size);
   Require(scroll.verticalScrollbar&&scroll.verticalScrollbar.gameObject.activeInHierarchy,"Missing persistent scrollbar");
   var pager=(RectTransform)typeof(CalibrationDemoView).GetField("libraryPager",flags).GetValue(view);var corners=new Vector3[4];pager.GetWorldCorners(corners);Require(corners[0].y>=0&&corners[2].x<=Screen.width+1,"Pagination outside window");
   if(columns<3){Require(grid.sizeDelta.y>scroll.viewport.rect.height,"Narrow page cannot scroll");scroll.verticalNormalizedPosition=0;yield return null;Require(scroll.verticalNormalizedPosition<.01f,"Cannot reach last cards");}
   yield return Shot("library-layout-"+columns+".png");
  }
  Library.Catalog=ArtworkCatalog.All;model.Jump(1);Rebuild();Screen.SetResolution(1440,960,false);yield return new WaitForSeconds(.5f);
 }
 void Rebuild(){typeof(CalibrationDemoView).GetMethod("Build",flags).Invoke(view,null);}
 static bool IsContentCard(UnityEngine.UI.Button button)=>button.name.StartsWith("Artwork ")&&button.name!="Artwork no-wallpaper";
 IEnumerator Ready(){float start=Time.realtimeSinceStartup;while(Library.Loading){if(Time.realtimeSinceStartup-start>45)throw new Exception("Preview timeout");yield return null;}yield return new WaitForSeconds(.3f);Require(Library.Error==null,Library.Error);}
 IEnumerator Shot(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name));yield return null;}
 static void Click(string name){var b=FindObjectsOfType<Evo.UI.Button>().First(x=>x.name==name);if(!b.IsInteractable())throw new Exception("Disabled: "+name);b.onClick.Invoke();}
}
