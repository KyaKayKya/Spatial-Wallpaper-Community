using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public sealed class CalibrationTooltip:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,ISelectHandler,IDeselectHandler {
 public string Label;public TMP_FontAsset Font;GameObject tip;bool pointer,focus;float began;
 public void OnPointerEnter(PointerEventData e){pointer=true;began=Time.unscaledTime;}
 public void OnPointerExit(PointerEventData e){pointer=false;Hide();}
 public void OnSelect(BaseEventData e){focus=true;began=Time.unscaledTime;}
 public void OnDeselect(BaseEventData e){focus=false;Hide();}
 void Update(){if(!tip&&(pointer||focus)&&Time.unscaledTime-began>.4f)Show();}
 void Show(){
  var root=GetComponentInParent<Canvas>().rootCanvas.transform as RectTransform;
  var ui=new CalibrationWidgets(Font);float width=Mathf.Min(root.rect.width-24,Mathf.Max(130,Label.Length*14+24));
  var corners=new Vector3[4];((RectTransform)transform).GetWorldCorners(corners);var p=root.InverseTransformPoint(corners[1]);
  float x=Mathf.Clamp(p.x-root.rect.xMin,12,root.rect.width-width-12),y=Mathf.Max(8,root.rect.yMax-p.y-42);
  var box=ui.Panel(root,"Control tooltip",x,y,width,34,CalibrationWidgets.Ink);box.GetComponent<Image>().raycastTarget=false;
  ui.Text(box,Label,12,0,width-24,34,13,Color.white);tip=box.gameObject;
 }
 void Hide(){if(tip)Destroy(tip);tip=null;}
 void OnDisable(){pointer=focus=false;Hide();}
}

public sealed class CalibrationSwitch:MonoBehaviour {
 public bool Value;public Graphic Track;public RectTransform Knob;
 public void Set(bool value){Value=value;Track.color=value?CalibrationWidgets.Blue:new Color32(165,174,187,255);Knob.anchoredPosition=new Vector2(value?29:3,-3);}
}
