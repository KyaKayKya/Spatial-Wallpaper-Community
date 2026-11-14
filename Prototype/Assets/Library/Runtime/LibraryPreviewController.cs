using System;
using System.Collections;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpatialWallpaper.Library;

// One preview scene and one camera owner. The Tracker stays owned by CalibrationRuntime.
public sealed partial class LibraryPreviewController:MonoBehaviour {
 public CalibrationRuntime Runtime;
 public event Action Changed;
 IWallpaperHost wallpaperHost=new UnavailableWallpaperHost();
 public WallpaperStopAction StopWallpaper {get;private set;}
 public WallpaperApplyAction ApplyWallpaper {get;private set;}
 public IWallpaperHost WallpaperHost {get=>wallpaperHost;set{if(StopWallpaper!=null)StopWallpaper.Changed-=WallpaperChanged;if(ApplyWallpaper!=null)ApplyWallpaper.Changed-=WallpaperChanged;wallpaperHost=value;StopWallpaper=new WallpaperStopAction(value);ApplyWallpaper=new WallpaperApplyAction(value);StopWallpaper.Changed+=WallpaperChanged;ApplyWallpaper.Changed+=WallpaperChanged;}}
 IReadOnlyList<Artwork> customCatalog;
 public IReadOnlyList<Artwork> Catalog {get=>customCatalog??ArtworkCatalog.All;set=>customCatalog=value;}
 public LibraryBindings Bindings {get;private set;}
 public string TargetDisplayId {get;private set;}
 public string SelectedPresetId=>string.IsNullOrEmpty(TargetDisplayId)?null:Bindings.GetPreset(Selected.Id,TargetDisplayId);
 public bool NoWallpaperSelected=>Selected==ArtworkCatalog.NoWallpaper;
 public Artwork Selected {get;private set;}=ArtworkCatalog.All[0];
 public Artwork Loaded {get;private set;}
 public bool Loading {get;private set;}
 public bool CameraMode {get;private set;}
 public bool KeepHeight {get;private set;}=true;
 public string Error {get;private set;}
 public Vector2 MouseEye {get;private set;}
 WindowProjection calibrationProjection;Scene managerScene,previewScene;
 Transform content;Vector3 authoredScale;float authoredDepth;
 Coroutine operation;bool closeRequested,desktopPreviewOwned;GameObject[] suspended;
 string framingKey;
 public bool KeepHeightFor(string artwork,string display)=>PlayerPrefs.GetInt(PreferencePrefix+"Framing."+artwork+"."+display,0)==0;
 string PreferencePrefix {
  get{
   string test=CalibrationRuntime.Argument("--library-smoke");if(!string.IsNullOrEmpty(test))return "ArtworkLibrary.Test."+Path.GetFileName(test)+".";
   string data=CalibrationRuntime.Argument("--data-root");return string.IsNullOrEmpty(data)?"ArtworkLibrary.":"ArtworkLibrary.Data."+Uri.EscapeDataString(Path.GetFullPath(data))+".";
  }
 }
 void Awake(){Application.wantsToQuit+=ContentWantsToQuit;calibrationProjection=Runtime.projection;managerScene=gameObject.scene;WallpaperHost=wallpaperHost;Bindings=new LibraryBindings(k=>PlayerPrefs.GetString(PreferencePrefix+k,null),(k,v)=>{PlayerPrefs.SetString(PreferencePrefix+k,v);PlayerPrefs.Save();});}
 void WallpaperChanged(){Changed?.Invoke();}
 public void RefreshWallpaperState(){Changed?.Invoke();}
 public void Select(Artwork artwork){Selected=artwork;TargetDisplayId=Bindings.ResolveTarget(artwork.Id,TargetDisplayId);PlayerPrefs.SetString(PreferencePrefix+"Selected",artwork.Id);if(NoWallpaperSelected)ClosePreview();Changed?.Invoke();}
 public void InitializeTarget(string fallback){TargetDisplayId=Bindings.ResolveTarget(Selected.Id,fallback);}
 public void SelectTarget(string id){TargetDisplayId=id;Bindings.SetTarget(Selected.Id,id);Changed?.Invoke();}
 public void SelectCalibrationPreset(string id){Bindings.SetPreset(Selected.Id,TargetDisplayId,id);Changed?.Invoke();}
 public void SelectNoWallpaper(){Select(ArtworkCatalog.NoWallpaper);}
 void Start(){var id=PlayerPrefs.GetString(PreferencePrefix+"Selected","");if(id=="no-wallpaper"){SelectNoWallpaper();return;}var found=ArtworkCatalog.All.FirstOrDefault(x=>x.Id==id);if(found!=null)Select(found);}
 public void OpenPreview(bool camera){if(Loading||NoWallpaperSelected)return;CameraMode=camera;closeRequested=false;operation=StartCoroutine(Load(Selected));}
 IEnumerator Load(Artwork artwork){
  var load=LoadCore(artwork);Exception failure=null;
  while(true){bool next;object current;try{next=load.MoveNext();current=next?load.Current:null;}catch(Exception e){failure=e;break;}if(!next)yield break;yield return current;}
  Debug.LogWarning("Artwork preview load "+artwork.Id+": "+failure);
  yield return Release();Error="作品加载失败，请重试预览。";Loading=false;operation=null;Changed?.Invoke();
 }
 IEnumerator LoadCore(Artwork artwork){
  Loading=true;Error=null;Changed?.Invoke();
  yield return Release();
  if(releaseFailed)yield break;
  var desktop=GetComponent<SpatialWallpaper.Desktop.DesktopManager>();
  if(desktop&&desktop.enabled){desktopPreviewOwned=true;var handoff=desktop.SetPreview(true);while(!handoff.IsCompleted)yield return null;if(handoff.IsFaulted){Debug.LogWarning("Preview release failed: "+handoff.Exception);Error="无法准备预览，请重试。桌面后台可能正在启动或恢复。";var cancel=desktop.SetPreview(false);while(!cancel.IsCompleted)yield return null;if(cancel.IsFaulted)Debug.LogWarning(cancel.Exception);else desktopPreviewOwned=false;Loading=false;Changed?.Invoke();yield break;}}
  if(artwork.IsContent&&!UseNativePreview(artwork)){var sdk=LoadContentPreview(artwork);while(!sdk.IsCompleted)yield return null;if(sdk.IsFaulted)throw sdk.Exception.GetBaseException();if(sdk.IsCanceled)throw new OperationCanceledException();if(closeRequested){yield return Release();}else Loaded=artwork;Loading=false;operation=null;Changed?.Invoke();yield break;}
  if(UseNativePreview(artwork)){var native=LoadNativePreview(artwork);while(!native.IsCompleted)yield return null;if(native.IsFaulted)throw native.Exception.GetBaseException();if(native.IsCanceled)throw new OperationCanceledException();if(closeRequested){yield return Release();}else Loaded=artwork;Loading=false;operation=null;Changed?.Invoke();yield break;}
  if(!Application.CanStreamedLevelBeLoaded(artwork.ScenePath)){yield return Release();Error="作品场景未包含在当前构建中，请重新构建作品库。";Loading=false;operation=null;Changed?.Invoke();yield break;}
  suspended=managerScene.GetRootGameObjects().Where(x=>x!=gameObject&&x.activeSelf&&!x.GetComponent<Canvas>()&&!x.GetComponent<UnityEngine.EventSystems.EventSystem>()).ToArray();
  foreach(var root in suspended)root.SetActive(false);
  SpatialWallpaper.Blueprint.BlueprintLibrary.PendingArtwork=artwork.Id;
  var load=SceneManager.LoadSceneAsync(artwork.ScenePath,LoadSceneMode.Additive);yield return load;
  previewScene=SceneManager.GetSceneByPath(artwork.ScenePath);
  if(closeRequested){yield return Release();Loading=false;operation=null;Changed?.Invoke();yield break;}
  SceneManager.SetActiveScene(previewScene);
  var roots=previewScene.GetRootGameObjects();
  var blueprint=roots.SelectMany(x=>x.GetComponentsInChildren<SpatialWallpaper.Blueprint.BlueprintScene>(true)).FirstOrDefault();
  if(blueprint){while(!blueprint.Ready&&blueprint.Error==null&&!closeRequested)yield return null;if(artwork.Id!="blueprint"&&!blueprint.Ready)throw new InvalidOperationException(blueprint.Error??"blueprint-load-cancelled");}
  var projection=roots.SelectMany(x=>x.GetComponentsInChildren<WindowProjection>(true)).Single();
  content=roots.SelectMany(x=>x.GetComponentsInChildren<Transform>(true)).Single(x=>x.name=="SceneModel"||x.name=="PoolContent"||x.name=="LimpoolContent");
  authoredScale=content.localScale;authoredDepth=projection.contentDepth;
  Runtime.projection=projection;Runtime.ExternalExhibitRoot=content;Runtime.MousePreview=!CameraMode;framingKey=null;KeepHeight=true;
  projection.viewportManagedExternally=true;Runtime.SetExhibit(true);Loaded=artwork;MouseEye=Vector2.zero;
  Loading=false;operation=null;Changed?.Invoke();
  if(blueprint&&artwork.Id=="blueprint")SpatialWallpaper.Blueprint.BlueprintEditorPanel.Open(blueprint,revision=>Select(SpatialWallpaper.Blueprint.BlueprintLibrary.Artwork(revision)),()=>Changed?.Invoke());
 }
 bool releaseFailed;
 IEnumerator Release(){
  releaseFailed=false;var release=ReleaseCore();Exception failure=null;
  while(true){bool next;object current;try{next=release.MoveNext();current=next?release.Current:null;}catch(Exception error){failure=error;break;}if(!next)yield break;yield return current;}
  releaseFailed=true;Error="预览释放失败，请重试返回或关闭："+failure.GetBaseException().Message;Loading=false;operation=null;Debug.LogWarning(failure);Changed?.Invoke();
 }
 IEnumerator ReleaseCore(){
  var native=ReleaseNativePreview();while(!native.IsCompleted)yield return null;if(native.IsFaulted)throw native.Exception.GetBaseException();
  while(!sdkResize.IsCompleted)yield return null;
  if(sdkPreview){var releasing=sdkPreview.RetireViewportAsync();while(!releasing.IsCompleted)yield return null;if(releasing.IsFaulted)throw releasing.Exception.GetBaseException();}sdkMeasured=false;
  Runtime.projection=calibrationProjection;Runtime.ExternalExhibitRoot=null;Runtime.MousePreview=false;
  Runtime.SetExhibit(false);Loaded=null;content=null;
  SceneManager.SetActiveScene(managerScene);
  if(previewScene.IsValid()&&previewScene.isLoaded)yield return SceneManager.UnloadSceneAsync(previewScene);
  previewScene=default;
  if(suspended!=null){foreach(var root in suspended)if(root)root.SetActive(true);suspended=null;}
  var desktop=GetComponent<SpatialWallpaper.Desktop.DesktopManager>();
  if(desktop&&desktop.enabled&&desktopPreviewOwned){var restore=desktop.SetPreview(false);while(!restore.IsCompleted)yield return null;if(restore.IsFaulted)throw restore.Exception.GetBaseException();desktopPreviewOwned=false;}
 }
 public void ClosePreview(){
  closeRequested=true;
  if(Loading){closeRequested=true;return;}
  if(Loaded!=null||sdkPreview!=null&&!sdkPreview.SandboxResourcesReleased||nativePreview!=null||releaseFailed||desktopPreviewOwned){Loading=true;StartCoroutine(Close());}
 }
 IEnumerator Close(){yield return Release();Loading=false;Changed?.Invoke();}
 public void SetMode(bool camera){CameraMode=camera;Runtime.MousePreview=!camera;Changed?.Invoke();}
 public void Drag(Vector2 delta){if(Loading||CameraMode||Loaded==null)return;MouseEye=new Vector2(Mathf.Clamp(MouseEye.x+delta.x,-.5f,.5f),Mathf.Clamp(MouseEye.y+delta.y,-.5f,.5f));}
 public void ResetEye(){MouseEye=Vector2.zero;}
 public void SetFraming(bool keepHeight){KeepHeight=keepHeight;if(framingKey!=null)PlayerPrefs.SetInt(framingKey,keepHeight?0:1);Changed?.Invoke();}
 public void Resize(float width,float height=-1){
  if(Loaded!=null&&UseNativePreview(Loaded)){if(height<0)height=width*9/16;string nativeKey=PreferencePrefix+"Framing."+Loaded.Id+"."+Runtime.DisplayId;if(framingKey!=nativeKey){framingKey=nativeKey;KeepHeight=PlayerPrefs.GetInt(nativeKey,0)==0;Changed?.Invoke();}if(!CameraMode){Runtime.projection.SetEye(new Vector3(MouseEye.x*width,MouseEye.y*height,-.67f));Runtime.projection.SetExternalFreshness(false);}return;}
  if(Loaded?.IsContent==true){if(height<0)height=width*9/16;string sdkKey=PreferencePrefix+"Framing."+Loaded.Id+"."+Runtime.DisplayId;if(framingKey!=sdkKey){framingKey=sdkKey;KeepHeight=PlayerPrefs.GetInt(sdkKey,0)==0;Changed?.Invoke();}sdkPreview.PreserveWidth=!KeepHeight;if(!CameraMode){Runtime.projection.SetEye(new Vector3(MouseEye.x*width,MouseEye.y*height,-.67f));Runtime.projection.SetExternalFreshness(false);}return;}
  if(!content)return;if(height<0)height=width*9/16;
  string key=PreferencePrefix+"Framing."+Loaded.Id+"."+Runtime.DisplayId;
  if(framingKey!=key){framingKey=key;KeepHeight=PlayerPrefs.GetInt(key,0)==0;Changed?.Invoke();}
  float factor=(float)ArtworkFraming.Scale(width,height,KeepHeight);content.localScale=authoredScale*factor;Runtime.projection.contentDepth=authoredDepth*factor;
  if(!CameraMode){Runtime.projection.SetEye(new Vector3(MouseEye.x*width,MouseEye.y*height,-.67f));Runtime.projection.SetExternalFreshness(false);}
 }
 void OnDestroy(){Application.wantsToQuit-=ContentWantsToQuit;if(StopWallpaper!=null)StopWallpaper.Changed-=WallpaperChanged;if(ApplyWallpaper!=null)ApplyWallpaper.Changed-=WallpaperChanged;if(Runtime){Runtime.ExternalExhibitRoot=null;Runtime.MousePreview=false;}}
}
