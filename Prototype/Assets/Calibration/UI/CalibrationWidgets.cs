using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Small uGUI/TMP factory; business actions remain in CalibrationRuntime/CalibrationSession.
public sealed class CalibrationWidgets {
 public readonly TMP_FontAsset Font;
 static Evo.UI.IconLibrary icons;
 static Sprite IconSprite(string name){if(!icons)icons=Resources.Load<Evo.UI.IconLibrary>("ManagerIcons");if(!icons)throw new InvalidOperationException("Missing prepared Evo UI resources");return icons.GetSprite(name)??throw new InvalidOperationException("Missing Evo icon: "+name);}
 public static readonly Color Ink=new Color32(32,39,53,255),Muted=new Color32(89,100,116,255),Blue=new Color32(20,104,232,255),Surface=new Color32(243,245,248,255),Line=new Color32(205,213,223,255);
 public CalibrationWidgets(TMP_FontAsset font){Font=font;}
 public RectTransform Rect(Transform parent,string name,float x,float y,float w,float h){
  var go=new GameObject(name,typeof(RectTransform));var r=(RectTransform)go.transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;
 }
 public RectTransform Panel(Transform parent,string name,float x,float y,float w,float h,Color color){var r=Rect(parent,name,x,y,w,h);r.gameObject.AddComponent<Image>().color=color;return r;}
 public RectTransform RoundedPanel(Transform parent,string name,float x,float y,float w,float h,Color color,float radius=6){var r=Rect(parent,name,x,y,w,h);var shape=r.gameObject.AddComponent<Evo.UI.ProceduralRect>();shape.cornerRadius=new Vector4(radius,radius,radius,radius);shape.color=color;shape.SetAllDirty();return r;}
 public Image Icon(Transform parent,string name,float x,float y,float size,Color color){var r=Rect(parent,name,x,y,size,size);var image=r.gameObject.AddComponent<Image>();image.sprite=IconSprite(name);image.preserveAspect=true;image.color=color;image.raycastTarget=false;return image;}
 public TMP_Text Text(Transform parent,string text,float x,float y,float w,float h,float size=14,Color? color=null){
  var r=Rect(parent,text,x,y,w,h);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=Font;t.fontSize=size;t.text=text;t.color=color??Ink;t.raycastTarget=false;t.alignment=TextAlignmentOptions.MidlineLeft;t.overflowMode=TextOverflowModes.Ellipsis;return t;
 }
 public Evo.UI.Button Button(Transform parent,string label,float x,float y,float w,Action action,bool primary=false){
  var r=RoundedPanel(parent,label,x,y,w,40,primary?Blue:Color.white);
  var t=Text(r,label,12,0,w-24,40,14,primary?Color.white:Ink);t.alignment=TextAlignmentOptions.Center;
  var b=r.gameObject.AddComponent<Evo.UI.Button>();b.customContent=true;b.dynamicScale=false;b.enableRipple=false;b.enableTrail=false;
  b.onClick.AddListener(()=>action());
  var visual=r.gameObject.AddComponent<CalibrationButtonVisual>();visual.button=b;visual.background=r.GetComponent<Graphic>();visual.label=t;visual.Selected=primary;
  b.onPointerEnter.AddListener(()=>visual.Hovered=true);b.onPointerExit.AddListener(()=>visual.Hovered=false);
  b.onSelect.AddListener(()=>visual.Focused=true);b.onDeselect.AddListener(()=>visual.Focused=false);
  return b;
 }
 public TMP_InputField Input(Transform parent,string value,float x,float y,float width){
  var r=Panel(parent,"Input",x,y,width,40,Color.white);var t=Text(r,value,10,0,width-20,40);
  var input=r.gameObject.AddComponent<TMP_InputField>();input.textViewport=(RectTransform)t.transform;input.textComponent=t;input.fontAsset=Font;input.text=value;input.pointSize=14;input.caretColor=Blue;input.customCaretColor=true;return input;
 }
 public Evo.UI.Slider Slider(Transform parent,float x,float y,float width,float low,float high,float value,Action<float> change,Action begin,Action end){
  var r=Rect(parent,"Slider",x,y,width,36);var rail=Panel(r,"Rail",0,16,width,4,Line);rail.GetComponent<Image>().raycastTarget=false;
  var area=Rect(r,"Handle area",8,8,width-16,20);var handle=RoundedPanel(area,"Handle",0,0,20,0,Blue,10);handle.pivot=new Vector2(.5f,.5f);
  var slider=r.gameObject.AddComponent<Evo.UI.Slider>();slider.minValue=low;slider.maxValue=high;slider.handleRect=handle;slider.targetGraphic=handle.GetComponent<Graphic>();slider.direction=UnityEngine.UI.Slider.Direction.LeftToRight;slider.SetValueWithoutNotify(value);
  slider.onValueChanged.AddListener(v=>change(v));
  var hook=r.gameObject.AddComponent<CalibrationGesture>();hook.Begin=begin;hook.End=end;return slider;
 }
}
public sealed class CalibrationButtonVisual:MonoBehaviour {
 public Evo.UI.Button button;public Graphic background;public TMP_Text label;
 public Graphic StepCircle;public TMP_Text StepNumber;
 public bool Selected,Hovered,Focused;
 void LateUpdate(){
  if(!button||!background||!label)return;
  bool enabled=button.IsInteractable();
  if(StepCircle){
   background.color=Hovered||Focused?new Color32(228,239,255,255):Color.clear;
   StepCircle.color=Selected?CalibrationWidgets.Blue:Color.white;
   StepNumber.color=Selected?Color.white:CalibrationWidgets.Muted;
   label.color=Selected?CalibrationWidgets.Blue:CalibrationWidgets.Ink;
   label.fontStyle=Selected?FontStyles.Bold:FontStyles.Normal;return;
  }
  background.color=!enabled?new Color32(231,235,240,255):Selected?(Hovered?new Color32(15,88,202,255):CalibrationWidgets.Blue):Hovered||Focused?new Color32(228,239,255,255):Color.white;
  label.color=!enabled?new Color32(114,126,141,255):Selected?Color.white:CalibrationWidgets.Ink;
 }
}
public sealed class CalibrationGesture:MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IEndDragHandler,IDeselectHandler {
 public Action Begin,End;
 public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)Begin?.Invoke();}
 public void OnPointerUp(PointerEventData e){End?.Invoke();}
 public void OnEndDrag(PointerEventData e){End?.Invoke();}
 public void OnDeselect(BaseEventData e){End?.Invoke();}
}
public sealed class CalibrationDragSurface:MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler {
 public Action<Vector2> Begin,Drag;public Action End;
 public void OnBeginDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)Begin?.Invoke(e.position);}
 public void OnDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)Drag?.Invoke(e.position);}
 public void OnEndDrag(PointerEventData e){End?.Invoke();}
}
