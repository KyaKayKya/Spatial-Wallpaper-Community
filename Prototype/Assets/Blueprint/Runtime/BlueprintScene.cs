using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace SpatialWallpaper.Blueprint {
 public sealed class BlueprintScene:MonoBehaviour {
  public BlueprintLighting Lighting;
  public bool Ready {get;private set;}public string Error {get;private set;}
  public int Triangles {get;private set;}public int MeshObjects {get;private set;}
  public long CacheBytes {get;private set;}public bool CacheHit {get;private set;}public double ImportMilliseconds {get;private set;}
  public BlueprintDocument Document {get;private set;}public BlueprintPlacement Placement {get;private set;}
  public Transform Building {get;private set;}public Transform BlockOrigin {get;private set;}public BlueprintResources ResourcesOwner {get;private set;}
  public BlueprintSettings Settings {get;private set;}=new BlueprintSettings();BlueprintEnvironment environment;
  public string SourcePath {get;private set;}
  public BlueprintWorkRevision Work {get;set;}
  CancellationTokenSource cancellation=new CancellationTokenSource();BlueprintMeshView view;
  public static string Argument(string key){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:null;}
  async void Start(){
   string pending=BlueprintLibrary.PendingArtwork;BlueprintLibrary.PendingArtwork=null;Work=BlueprintLibrary.Resolve(pending);if(Work!=null)Settings=Work.Settings.Clone();
   string path=Work==null?Argument("--blueprint"):BlueprintLibrary.Store.SourcePath(Work.SourceHash);if(string.IsNullOrEmpty(path)){Error="请导入一个 Minecraft 蓝图开始创作。";Debug.Log("Blueprint template ready for import");return;}
   await LoadPath(path,Work?.SourceHash);
   if(Argument("--blueprint-edit")=="true")BlueprintEditorPanel.Open(this);
  }
  public async Task<bool> LoadPath(string path,string expectedSource=null,int? layoutVersion=null){
   environment?.PersistPlayback();bool previousReady=Ready;Ready=false;Error=null;var clock=System.Diagnostics.Stopwatch.StartNew();var token=cancellation.Token;
   try{
    string catalogJson=Resources.Load<TextAsset>("Blueprint/Catalog").text;string cacheRoot=Path.Combine(BlueprintLibrary.Root,"cache");
    BlueprintDocument document=null;CompiledBlueprint compiled=null;ModelCatalog catalog=null;bool hit=false;long bytes=0;string snapshot=null;bool useWorker=Application.platform==RuntimePlatform.WindowsPlayer||Application.platform==RuntimePlatform.WindowsEditor;string executable=Path.Combine(Application.streamingAssetsPath,"BlueprintTools","BlueprintImport.exe");
    await Task.Run(()=>{
     if(useWorker){var imported=BlueprintImportProcess.Run(executable,path,catalogJson,Path.GetDirectoryName(cacheRoot),token);document=imported.Document;compiled=imported.Compiled;catalog=ModelCatalog.Load(catalogJson);hit=imported.CacheHit;bytes=imported.CacheBytes;snapshot=imported.SourcePath;if(expectedSource!=null&&document.SourceHash!=expectedSource)throw new BlueprintImportException("saved-blueprint-source-changed");return;}
     var source=new FileInfo(path);if(!source.Exists||source.Length>NbtInput.MaxInputBytes)throw new BlueprintImportException("blueprint-file-budget");document=BlueprintReader.Read(File.ReadAllBytes(path),Path.GetFileName(path),token);if(expectedSource!=null&&document.SourceHash!=expectedSource)throw new BlueprintImportException("saved-blueprint-source-changed");catalog=ModelCatalog.Load(catalogJson);
     string key=BlockMeshCompiler.CacheKeyFor(document.SourceHash,catalog.Fingerprint);compiled=BlueprintCacheFiles.Read(cacheRoot,key,out bytes);hit=compiled!=null;
     if(compiled==null){compiled=BlockMeshCompiler.Compile(document,catalog,token);bytes=BlueprintCacheFiles.Write(cacheRoot,compiled,token);}
    },token);
    token.ThrowIfCancellationRequested();if(!this)return false;
    int version=layoutVersion??(expectedSource==null?2:Settings.LayoutVersion);var placement=BlueprintLayout.Fit(document,version);
    if(!compiled.CanApply)throw new BlueprintImportException("未完整支持此蓝图："+string.Join("；",compiled.MissingStates)+(compiled.EntityCount>0?"；包含未支持的实体":""));
    ResourcesOwner?.Dispose();if(Building)Destroy(Building.gameObject);ResourcesOwner=new BlueprintResources(catalog);Document=document;Placement=placement;Settings.LayoutVersion=version;Triangles=compiled.TriangleCount;CacheBytes=bytes;CacheHit=hit;
    Building=new GameObject("Blueprint building").transform;Building.SetParent(transform,false);Building.localPosition=new Vector3(Placement.X,Placement.Y,Placement.Z);Building.localScale=Vector3.one*Placement.Scale;Building.localRotation=Quaternion.Euler(0,Placement.Yaw,0);
    var blocks=new GameObject("Canonical block origin").transform;BlockOrigin=blocks;blocks.SetParent(Building,false);blocks.localPosition=new Vector3(-Placement.CenterX,-Placement.MinY,-Placement.CenterZ);view=blocks.gameObject.AddComponent<BlueprintMeshView>();view.Load(compiled,ResourcesOwner,true);MeshObjects=view.RendererCount;
    compiled.Parts.Clear();Settings.Look=Argument("--blueprint-look")??Settings.Look;Settings.Quality=IntArgument("--blueprint-quality",Settings.Quality);if(Work==null&&Settings.Quality==0)Settings.FrameRate=30;Settings.Hour=FloatArgument("--blueprint-hour",Settings.Hour);Settings.Yaw=FloatArgument("--blueprint-yaw",Settings.Yaw);Settings.Weather=Argument("--blueprint-weather")??Settings.Weather;Settings.Terrain=Argument("--blueprint-terrain")??Settings.Terrain;
    if(Lighting)Lighting.Initialize(ResourcesOwner);ApplySettings(Settings);if(!environment)environment=gameObject.AddComponent<BlueprintEnvironment>();environment.Configure(this);
    SourcePath=snapshot??path;ImportMilliseconds=clock.Elapsed.TotalMilliseconds;Ready=true;Debug.Log("BLUEPRINT_READY "+document.BlockCount+" blocks, "+Triangles+" triangles, "+CacheBytes+" cache bytes, warm="+CacheHit);return true;
   }catch(OperationCanceledException){Ready=previousReady;return false;}catch(Exception e){Ready=previousReady;Error=e.GetBaseException().Message;if(e is BlueprintImportException)Debug.LogWarning("Blueprint import: "+Error);else Debug.LogError("Blueprint import: "+Error);return false;}
  }
  public float CycleHour=>environment?environment.CycleHour:Settings.Hour;
  public BlueprintSettings SettingsForSave(BlueprintSettings draft){var copy=draft.Clone();if(copy.TimeMode=="cycle"&&environment)copy.Hour=environment.CycleHour;return copy;}
  public void PersistPlayback(){if(environment)environment.PersistPlayback();}
  public void ApplySettings(BlueprintSettings settings){settings.Validate();Settings=settings.Clone();if(!Building)return;var footprint=BlueprintFootprint.For(Document,Placement,Settings);if(footprint.MinZ<.075f)Settings.OffsetZ+=.075f-footprint.MinZ;Building.localPosition=new Vector3(Placement.X+Settings.OffsetX,Placement.Y+Settings.OffsetY,Placement.Z+Settings.OffsetZ);Building.localScale=Vector3.one*(Placement.Scale*Settings.Scale);Building.localRotation=Quaternion.Euler(0,Placement.Yaw+Settings.Yaw,0);if(Lighting){Lighting.Apply(Settings.Look,Settings.Quality,Settings.Hour);Lighting.Lanterns(Document,BlockOrigin,Placement.Scale*Settings.Scale,Settings.Quality);}if(environment)environment.Apply();}
  static int IntArgument(string key,int fallback)=>int.TryParse(Argument(key),out int v)?Mathf.Clamp(v,0,2):fallback;
  static float FloatArgument(string key,float fallback)=>float.TryParse(Argument(key),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float v)&&!float.IsNaN(v)&&!float.IsInfinity(v)?v:fallback;
  void OnDestroy(){cancellation.Cancel();cancellation.Dispose();ResourcesOwner?.Dispose();}
 }
}
