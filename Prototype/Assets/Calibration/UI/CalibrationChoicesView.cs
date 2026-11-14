using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public sealed partial class CalibrationDemoView {
 RectTransform choicePopup;GameObject choiceOwner;
 sealed class Choice {public string Label;public Action Select;public Choice(string label,Action select){Label=label;Select=select;}}
 Evo.UI.Button Dropdown(Transform parent,string name,float x,float y,float width,Func<List<Choice>> options,string caption=null){
  Evo.UI.Button button=null;button=ui.Button(parent,"",x,y,width,()=>ShowChoices(button,options()));button.name=name;
  var label=button.GetComponentInChildren<TMP_Text>();label.text=caption??name;label.alignment=TextAlignmentOptions.MidlineLeft;label.rectTransform.sizeDelta=new Vector2(width-48,40);
  ui.Icon(button.transform,"Down",width-30,10,20,CalibrationWidgets.Ink);
  return button;
 }
 void ShowChoices(Evo.UI.Button owner,List<Choice> options){
  CloseChoices();if(options.Count==0)return;choiceOwner=owner.gameObject;
  float w=canvas.rect.width,h=canvas.rect.height;
  choicePopup=ui.Panel(canvas,"Dropdown overlay",0,0,w,h,Color.clear);
  var dismiss=choicePopup.gameObject.AddComponent<UnityEngine.UI.Button>();dismiss.transition=Selectable.Transition.None;dismiss.onClick.AddListener(CloseChoices);
  var corners=new Vector3[4];((RectTransform)owner.transform).GetWorldCorners(corners);var p=canvas.InverseTransformPoint(corners[0]);
  float width=((RectTransform)owner.transform).rect.width,height=Mathf.Min(240,options.Count*44);
  float x=Mathf.Clamp(p.x-canvas.rect.xMin,0,w-width),y=canvas.rect.yMax-p.y+4;
  if(y+height>h-8)y=Mathf.Max(8,y-height-48);
  var viewport=ui.Panel(choicePopup,"Dropdown list",x,y,width,height,Color.white);viewport.gameObject.AddComponent<RectMask2D>();
  var content=ui.Rect(viewport,"Options",0,0,width,options.Count*44);var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=24;
  for(int i=0;i<options.Count;i++){
   var item=options[i];var button=ui.Button(content,item.Label,0,i*44,width,()=>{CloseChoices();item.Select();});
   button.GetComponentInChildren<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
   if(i==0)EventSystem.current.SetSelectedGameObject(button.gameObject);
  }
 }
 void CloseChoices(){if(choicePopup){choicePopup.gameObject.SetActive(false);Destroy(choicePopup.gameObject);choicePopup=null;}if(choiceOwner&&choiceOwner.activeInHierarchy)EventSystem.current.SetSelectedGameObject(choiceOwner);choiceOwner=null;}
}
