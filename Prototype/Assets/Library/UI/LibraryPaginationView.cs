using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SpatialWallpaper.Library;

public sealed partial class CalibrationDemoView {
 readonly LibraryPaging libraryPages=new LibraryPaging(0);
 RectTransform libraryPager;TMP_Text libraryCount;
 Image Glyph(Transform parent,LibrarySymbol symbol,float x,float y,float size,Color color){
  string name=symbol==LibrarySymbol.Check?"Check (Circle Fill)":symbol==LibrarySymbol.Monitor?"Display":symbol==LibrarySymbol.Target?"Scan":symbol.ToString();return ui.Icon(parent,name,x,y,size,color);
 }
 Evo.UI.Button IconButton(Transform parent,LibrarySymbol symbol,string name,float x,float y,float width,Action click,bool enabled=true){var button=ui.Button(parent,"",x,y,width,click);button.name=name;Glyph(button.transform,symbol,(width-20)/2,10,20,enabled?CalibrationWidgets.Ink:CalibrationWidgets.Muted);button.SetInteractable(enabled);return button;}
 void JumpLibraryPage(int target){if(!libraryPages.Jump(target))return;scrollPosition=1;RebuildGrid();}
 void BuildLibraryPagination(){
  Clear(libraryPager);float width=libraryPager.sizeDelta.x;var tokens=LibraryPaging.PageTokens(libraryPages.Page,libraryPages.PageCount);bool narrow=width<540;
  const float cell=36,gap=4;float group=2*(cell+gap)+tokens.Sum(p=>PageButtonWidth(p)+gap)-gap;float jumpWidth=150;float x=Mathf.Max(0,(width-group-(narrow?0:jumpWidth+16))/2);
  if(libraryPages.PageCount==1){float start=Mathf.Max(0,(width-156)/2);IconButton(libraryPager,LibrarySymbol.Left,"上一页",start,0,36,()=>{},false);var label=ui.Text(libraryPager,"1 / 1",start+44,0,68,40,14,CalibrationWidgets.Muted);label.alignment=TextAlignmentOptions.Center;IconButton(libraryPager,LibrarySymbol.Right,"下一页",start+120,0,36,()=>{},false);return;}
  IconButton(libraryPager,LibrarySymbol.Left,"上一页",x,0,cell,()=>JumpLibraryPage(libraryPages.Page-1),libraryPages.Page>1);x+=cell+gap;
  foreach(int token in tokens){float size=PageButtonWidth(token);if(token==0){var t=ui.Text(libraryPager,"…",x,0,size,40,14);t.alignment=TextAlignmentOptions.Center;}else{int p=token;var b=ui.Button(libraryPager,p.ToString(),x,0,size,()=>JumpLibraryPage(p),p==libraryPages.Page);b.name="Page "+p;}x+=size+gap;}
  IconButton(libraryPager,LibrarySymbol.Right,"下一页",x,0,cell,()=>JumpLibraryPage(libraryPages.Page+1),libraryPages.Page<libraryPages.PageCount);
  float jumpX=narrow?Mathf.Max(0,(width-jumpWidth)/2):x+cell+16,jumpY=narrow?44:0;
  ui.Text(libraryPager,"跳至",jumpX,jumpY,36,40,14);var input=ui.Input(libraryPager,libraryPages.Page.ToString(),jumpX+40,jumpY,64);input.name="Jump page";input.contentType=TMP_InputField.ContentType.IntegerNumber;input.characterLimit=7;
  ui.Text(libraryPager,"页",jumpX+112,jumpY,24,40,14);
  input.onSubmit.AddListener(value=>{if(int.TryParse(value,out int target)&&libraryPages.Jump(target)){scrollPosition=1;RebuildGrid();}else{input.GetComponent<Image>().color=new Color32(255,218,213,255);input.SetTextWithoutNotify(libraryPages.Page.ToString());}});
 }
 static float PageButtonWidth(int page)=>Mathf.Max(36,24+page.ToString().Length*10);
 void ConfigureLibraryScrollbar(RectTransform parent,RectTransform viewport,float x,float y,float height){
  var prefab=Resources.Load<GameObject>("ManagerScrollbar");
  var track=(RectTransform)Instantiate(prefab,parent,false).transform;track.name="Artwork scroll track";
  track.anchorMin=track.anchorMax=track.pivot=new Vector2(0,1);track.anchoredPosition=new Vector2(x,-y);track.sizeDelta=new Vector2(12,height);
  var bar=track.GetComponent<Evo.UI.Scrollbar>();bar.direction=Scrollbar.Direction.BottomToTop;
  libraryScroll.verticalScrollbar=bar;libraryScroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;bar.numberOfSteps=0;
 }
}
