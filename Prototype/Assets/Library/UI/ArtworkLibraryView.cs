using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SpatialWallpaper.Library;

public sealed partial class CalibrationDemoView {
 LibraryPreviewController library;
 string search="";
 RectTransform libraryGrid,detail;
 ScrollRect libraryScroll;
 float scrollPosition=1;
 TMP_Text libraryMessage,modeStatus;
 Evo.UI.Button libraryImport;
 bool resumeCameraPreview;
 void BuildCompactNavigation(float nav){
  string[] names={"作品库","校准","设置"};
  for(int i=0;i<3;i++){
   int index=i;string name=names[i];bool selected=i==0?(currentPage=="作品库"||currentPage=="作品预览"):currentPage==name;
   var button=ui.Button(canvas,"",12,128+i*52,nav-24,()=>{if(index==0)Leave(NavigateLibrary);else if(index==1){if(currentPage=="作品预览")resumeCameraPreview=library.CameraMode;library.ClosePreview();runtime.SetExhibit(false);currentPage="校准";Build();}else Leave(()=>{if(currentPage=="作品预览")resumeCameraPreview=library.CameraMode;library.ClosePreview();currentPage="设置";Build();});},selected);button.name=name;
   ui.Icon(button.transform,new[]{"Layout","Scan","Settings"}[i],nav<100?(nav-48)/2:12,8,24,selected?Color.white:CalibrationWidgets.Ink);
   if(nav>=100)ui.Text(button.transform,name,48,0,nav-84,40,14,selected?Color.white:CalibrationWidgets.Ink);
  }
 }
 void NavigateLibrary(){
  pendingLibraryArtwork=null;pendingLibraryDisplay=null;
  resumeCameraPreview=false;if(library){library.ClosePreview();currentPage="作品库";}else{currentPage="展示柜";runtime.SetExhibit(true);}Build();
 }
 void BuildLibrarySurface(){
  // Rebuild notifications only on operation completion; tracking state updates text.
  library.Changed-=LibraryChanged;
  libraryTargets=LibraryDisplayCatalog.List();
  if(string.IsNullOrEmpty(library.TargetDisplayId)&&libraryTargets.Length>0)library.InitializeTarget(libraryTargets.FirstOrDefault(t=>t.Id==runtime.DisplayId)?.Id??libraryTargets[0].Id);
  library.Changed+=LibraryChanged;
  InitializeLibraryContent();
  if(currentPage=="作品预览"){BuildArtworkPreview();return;}
  if(libraryMarket){BuildMarketSurface();return;}
  float right=pageWidth<850?280:300,gridWidth=pageWidth-right-24;
  ui.Button(page,"导入蓝图",0,0,104,()=>{library.Select(ArtworkCatalog.BlueprintTemplate);EnterArtworkPreview(false);});
  libraryImport=ui.Button(page,"导入 SDK",116,0,104,()=>ChooseLibraryContent(false));libraryImport.SetInteractable(ContentClient?.Available==true&&!contentOperation);
  ui.Button(page,"社区市场",232,0,104,()=>{libraryMarket=true;Build();});
  libraryCount=ui.Text(page,"",348,0,Mathf.Max(120,gridWidth-348),40,14,CalibrationWidgets.Muted);
  var searchInput=ui.Input(page,search,0,60,Mathf.Max(140,gridWidth));searchInput.name="Search artworks";
  var placeholder=ui.Text(searchInput.transform,"搜索",10,0,gridWidth-20,40,14,CalibrationWidgets.Muted);searchInput.placeholder=placeholder;
  searchInput.onValueChanged.AddListener(value=>{search=value;scrollPosition=1;libraryPages.Jump(1);RebuildGrid();});
  float listHeight=pageHeight-248;
  var viewport=ui.Panel(page,"Artwork list viewport",0,116,gridWidth-20,listHeight,Color.clear);viewport.gameObject.AddComponent<RectMask2D>();
  libraryGrid=ui.Rect(viewport,"Artwork grid",0,0,gridWidth-20,0);
  libraryScroll=viewport.gameObject.AddComponent<ScrollRect>();libraryScroll.content=libraryGrid;libraryScroll.viewport=viewport;libraryScroll.horizontal=false;libraryScroll.movementType=ScrollRect.MovementType.Clamped;
  libraryScroll.scrollSensitivity=80;
  libraryScroll.onValueChanged.AddListener(v=>scrollPosition=v.y);
  ConfigureLibraryScrollbar(page,viewport,gridWidth-10,116,listHeight);
  libraryPager=ui.Rect(page,"Library pagination",0,pageHeight-116,gridWidth,88);
  var detailViewport=ui.Panel(page,"Selected artwork details viewport",pageWidth-right,60,right,pageHeight-108,Color.white);
  detailViewport.gameObject.AddComponent<RectMask2D>();detail=ui.Rect(detailViewport,"Selected artwork details",0,0,right-12,pageHeight-108);
  var detailScroll=detailViewport.gameObject.AddComponent<ScrollRect>();detailScroll.viewport=detailViewport;detailScroll.content=detail;detailScroll.horizontal=false;detailScroll.scrollSensitivity=50;detailScroll.movementType=ScrollRect.MovementType.Clamped;
  var rail=ui.Panel(detailViewport,"Details scrollbar",right-10,0,8,pageHeight-108,CalibrationWidgets.Surface);var handle=ui.Panel(rail,"Details scroll handle",0,0,8,32,CalibrationWidgets.Line);var scrollbar=rail.gameObject.AddComponent<Scrollbar>();scrollbar.handleRect=handle;scrollbar.targetGraphic=handle.GetComponent<Graphic>();scrollbar.direction=Scrollbar.Direction.BottomToTop;detailScroll.verticalScrollbar=scrollbar;
  RebuildGrid();detailIdentity=null;LibraryChanged();
  status.rectTransform.anchoredPosition=new Vector2(0,-pageHeight+28);status.rectTransform.sizeDelta=new Vector2(pageWidth,28);
 }
 void RebuildGrid(){
  if(libraryMarket){RebuildMarketList();return;}
  if(!libraryGrid)return;Clear(libraryGrid);
  var matches=library.Catalog.Where(x=>string.IsNullOrWhiteSpace(search)||x.Name.IndexOf(search.Trim(),StringComparison.OrdinalIgnoreCase)>=0).ToArray();libraryPages.SetTotal(matches.Length);
  var entries=matches.Skip(libraryPages.First).Take(LibraryPaging.ArtworksPerPage).ToArray();
  libraryCount.text=string.IsNullOrWhiteSpace(search)?library.Catalog.Count+" 件作品":"匹配 "+matches.Length+" / 共 "+library.Catalog.Count+" 件";
  float width=libraryGrid.sizeDelta.x;int columns=LibraryPaging.Columns(width);float cardWidth=Mathf.Min(320,(width-16*(columns-1))/columns),cardHeight=cardWidth*9/16+44;float inset=(width-(columns*cardWidth+16*(columns-1)))/2;
  libraryGrid.sizeDelta=new Vector2(width,Mathf.Max(libraryScroll.viewport.rect.height,Mathf.Ceil((entries.Length+1)/(float)columns)*(cardHeight+16)+(entries.Length==0?172:0)));
  var cards=new[]{ArtworkCatalog.NoWallpaper}.Concat(entries).ToArray();
  for(int slot=0;slot<cards.Length;slot++){
   var entry=cards[slot];
   var card=ui.RoundedPanel(libraryGrid,"Artwork "+entry.Id,inset+(slot%columns)*(cardWidth+16),(slot/columns)*(cardHeight+16),cardWidth,cardHeight,Color.white,8);
   Thumbnail(card,entry,8,8,cardWidth-16,(cardWidth-16)*9/16);
   float textY=8+(cardWidth-16)*9/16+8;
   ui.Text(card,entry.Name,12,textY,cardWidth-24,28,16);
   var button=card.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=card.GetComponent<Graphic>();button.transition=Selectable.Transition.None;
   var colors=button.colors;colors.highlightedColor=new Color32(228,239,255,255);colors.selectedColor=colors.highlightedColor;button.colors=colors;
   button.onClick.AddListener(()=>{library.Select(entry);});
   var click=card.gameObject.AddComponent<LibraryCardClick>();click.Apply=ApplyLibrarySelection;
   AddCardState(card,()=>library.Selected.Id==entry.Id,()=>(library.WallpaperHost.ActiveArtworkId??ArtworkCatalog.NoWallpaper.Id)==entry.Id);
  }
  if(entries.Length==0){ui.Text(libraryGrid,"没有找到匹配的作品",16,cardHeight+32,width-32,40,18);ui.Button(libraryGrid,"清除搜索",16,cardHeight+88,200,()=>{search="";Build();});}
  BuildLibraryPagination();Canvas.ForceUpdateCanvases();libraryScroll.verticalNormalizedPosition=scrollPosition;
  bool overflow=libraryGrid.rect.height>libraryScroll.viewport.rect.height+1;libraryScroll.verticalScrollbar.interactable=overflow;
 }
 void Thumbnail(Transform parent,Artwork entry,float x,float y,float w,float h){
  var box=ui.Panel(parent,"Thumbnail "+entry.Id,x,y,w,h,new Color32(226,231,237,255));
  box.GetComponent<Image>().raycastTarget=false;
  var tex=Resources.Load<Texture2D>("Library/"+(entry.CoverId??entry.Id))??SpatialWallpaper.Blueprint.BlueprintLibrary.Cover(entry.Id);
  if(tex){var image=ui.Rect(box,"Scene photograph",0,0,w,h).gameObject.AddComponent<RawImage>();image.texture=tex;image.raycastTarget=false;}
  else ui.Text(box,"缩略图不可用",12,0,w-24,h,14,CalibrationWidgets.Muted);
 }
 void EnterArtworkPreview(bool camera){
  if(library.Loading||library.NoWallpaperSelected)return;
  Leave(()=>{currentPage="作品预览";library.OpenPreview(camera);Build();});
 }
 void LibraryChanged(){if(!this||!library)return;if(currentPage=="作品库"&&libraryMarket){RefreshMarketDetails();return;}if(currentPage=="作品库"&&detail){string identity=library.Selected.Id+"|"+library.Selected.RevisionId+"|"+library.TargetDisplayId+"|"+library.SelectedPresetId+"|"+library.WallpaperHost.Available+"|"+library.ApplyWallpaper.Busy+"|"+library.StopWallpaper.Busy+"|"+library.ApplyWallpaper.Message+"|"+libraryActionError;if(librarySoundGesture){libraryDetailsPending=true;RefreshLibrary();return;}if(identity!=detailIdentity){detailIdentity=identity;Clear(detail);BuildArtworkDetails(detail.sizeDelta.x);}RefreshLibrary();}else if(currentPage=="作品预览")Build();}
 void BuildArtworkPreview(){
  ui.Button(page,"返回作品库",0,100,132,NavigateLibrary);
  var mouse=ui.Button(page,"鼠标观察",148,100,116,()=>library.SetMode(false),!library.CameraMode);mouse.SetInteractable(!library.Loading);
  var camera=ui.Button(page,"摄像头",280,100,100,()=>library.SetMode(true),library.CameraMode);camera.SetInteractable(!library.Loading);
  var heightFit=ui.Button(page,"保上下",396,100,96,()=>library.SetFraming(true),library.KeepHeight);heightFit.SetInteractable(!library.Loading);
  var widthFit=ui.Button(page,"保左右",508,100,96,()=>library.SetFraming(false),!library.KeepHeight);widthFit.SetInteractable(!library.Loading);
  if(library.Loaded!=null&&library.Loaded.ScenePath==ArtworkCatalog.BlueprintTemplate.ScenePath)ui.Button(page,"编辑蓝图",620,100,104,()=>{var blueprint=FindObjectOfType<SpatialWallpaper.Blueprint.BlueprintScene>();SpatialWallpaper.Blueprint.BlueprintEditorPanel.Open(blueprint,revision=>{library.Select(SpatialWallpaper.Blueprint.BlueprintLibrary.Artwork(revision));},()=>Build());});
  float areaHeight=Mathf.Max(120,pageHeight-300),w=pageWidth,h=areaHeight;
  var box=ui.Panel(page,"Artwork preview area",0,160,pageWidth,areaHeight,Color.black);
  var r=ui.Rect(box,"Artwork spatial image",(pageWidth-w)/2,(areaHeight-h)/2,w,h);preview=r.gameObject.AddComponent<RawImage>();preview.enabled=library.Loaded!=null;
  if(!library.NativePreview&&!library.ContentPreview){if(!texture){texture=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);texture.Create();}preview.texture=texture;AddArtworkDrag(r);}else preview.enabled=false;
  libraryMessage=ui.Text(box,"",16,12,pageWidth-32,64,16,Color.white);
  if(!string.IsNullOrEmpty(library.Error))ui.Button(box,"重试预览",16,84,120,()=>library.OpenPreview(library.CameraMode));
  modeStatus=ui.Text(page,"",0,pageHeight-124,pageWidth,36,14,CalibrationWidgets.Muted);
  var reset=ui.Button(page,"复位视角",0,pageHeight-76,120,library.ResetEye);reset.SetInteractable(!library.Loading&&!library.CameraMode);
  var full=ui.Button(page,"全屏预览",136,pageHeight-76,120,ToggleFullscreen,true);full.SetInteractable(!library.Loading&&library.Loaded!=null);
  ui.Button(page,"前往设置",272,pageHeight-76,120,()=>{resumeCameraPreview=library.CameraMode;library.ClosePreview();currentPage="设置";Build();});
  ui.Button(page,"校准",408,pageHeight-76,96,()=>{resumeCameraPreview=library.CameraMode;library.ClosePreview();runtime.SetExhibit(false);currentPage="校准";Build();});
 }
 void RefreshLibrary(){
  if(currentPage=="作品库"&&libraryImport){bool enabled=ContentClient?.Available==true&&!contentOperation;if(libraryImport.IsInteractable()!=enabled)libraryImport.SetInteractable(enabled);}
  if(currentPage=="作品库")status.text=!library.WallpaperHost.Available?"桌面后台正在连接或恢复，暂时不能应用。":library.WallpaperHost.ActiveArtworkId==null?"系统壁纸":library.Catalog.FirstOrDefault(x=>x.Id==library.WallpaperHost.ActiveArtworkId)?.Name??"桌面壁纸";
  else{
   status.text=library.Loading?"正在加载作品…":library.CameraMode?"摄像头体验 · "+Service.CameraName:"鼠标预览 · 不影响桌面壁纸";
   if(libraryMessage)libraryMessage.text=library.Error??(library.Loading?"正在准备场景…":"");
   if(modeStatus)modeStatus.text=!library.CameraMode?"按住画面内左键拖动，模拟左右、上下观察位置。":Service.Applied==null?"尚未完成校准 · 前往设置应用相机，再到校准页保存并完成。":runtime.projection.TrackingFresh?"正在跟随 · "+Service.Applied.name:!Service.Fresh?"追踪未开启或暂时不可用 · "+Service.Message:"已应用预设与当前设备或显示器不匹配，请重新校准。";
  }
 }
 void AddArtworkDrag(RectTransform r){
  var drag=r.gameObject.AddComponent<CalibrationDragSurface>();Vector2 last=Vector2.zero;
  drag.Begin=p=>last=p;drag.Drag=p=>{if(RectTransformUtility.RectangleContainsScreenPoint(r,p)){var delta=p-last;library.Drag(new Vector2(delta.x/Mathf.Max(1,r.rect.width*DisplayViewport.UiScale),delta.y/Mathf.Max(1,r.rect.height*DisplayViewport.UiScale)));}last=p;};
 }
 void BuildFullscreenArtworkDrag(Transform root,float width,float height){
  float w=width,h=height;
  var r=ui.Panel(root,"Fullscreen artwork input",(width-w)/2,(height-h)/2,w,h,Color.clear);AddArtworkDrag(r);
 }
 public bool TryMapArtworkCursor(Vector2 screen,out Vector2 cursor){
  cursor=screen;if(fullscreen)return new Rect(0,0,Screen.width,Screen.height).Contains(screen);
  if(!preview||!preview.enabled||!texture)return false;
  var rect=preview.rectTransform;
  if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,screen,null,out var local)||!rect.rect.Contains(local))return false;
  var uv=(local-rect.rect.min)/rect.rect.size;
  cursor=new Vector2(uv.x*texture.width,uv.y*texture.height);return true;
 }
 void MeasureLibrary(){
  if(currentPage=="作品库"){RefreshLibrary();RefreshContentPlaying();return;}
  if(FindObjectOfType<SpatialWallpaper.Blueprint.BlueprintEditorPanel>())return;
  if(currentPage=="作品预览"&&(library.ContentPreview||library.NativePreview)){
   Rect region;if(fullscreen)region=new Rect(0,0,Screen.width,Mathf.Max(1,Screen.height-64*DisplayViewport.UiScale));else{if(!preview)return;var corners=new Vector3[4];preview.rectTransform.GetWorldCorners(corners);region=new Rect(corners[0].x,corners[0].y,corners[2].x-corners[0].x,corners[2].y-corners[0].y);preview.enabled=false;}
   if(library.NativePreview)library.MeasureNativePreview(region);else library.MeasureContentPreview(region);if(runtime.ViewportValid)library.Resize(runtime.projection.displayWidth,runtime.projection.displayHeight);RefreshLibrary();return;
  }
  if(currentPage!="作品预览"||library.Loading||library.Loaded==null)return;
  Rect aperture;
  var camera=runtime.projection.GetComponent<Camera>();
  if(fullscreen){
   aperture=new Rect(0,0,Screen.width,Screen.height);camera.targetTexture=null;camera.pixelRect=aperture;
  }else{
   if(!preview)return;preview.enabled=true;
   var corners=new Vector3[4];preview.rectTransform.GetWorldCorners(corners);aperture=new Rect(corners[0].x,corners[0].y,corners[2].x-corners[0].x,corners[2].y-corners[0].y);
   int w=Mathf.Max(1,Mathf.RoundToInt(aperture.width)),h=Mathf.Max(1,Mathf.RoundToInt(aperture.height));
   if(texture.width!=w||texture.height!=h){texture.Release();texture.width=w;texture.height=h;texture.Create();}
   camera.targetTexture=texture;camera.rect=new Rect(0,0,1,1);
  }
  Measure(aperture);if(runtime.ViewportValid)library.Resize(runtime.projection.displayWidth,runtime.projection.displayHeight);
  RefreshLibrary();
 }
}
