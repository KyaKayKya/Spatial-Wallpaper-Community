using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public sealed class LibraryCardClick:MonoBehaviour,IPointerClickHandler {
 public Action Apply;
 public void OnPointerClick(PointerEventData e){if(e.button==PointerEventData.InputButton.Left&&e.clickCount==2)Apply?.Invoke();}
}
public sealed class LibraryCardVisual:MonoBehaviour,IPointerEnterHandler,IPointerExitHandler {
 public Func<bool> Selected,Active;public Evo.UI.ProceduralRect Border;public GameObject Check,Monitor;bool hovered;
 void LateUpdate(){bool selected=Selected();Color color=selected?CalibrationWidgets.Blue:new Color32(174,196,222,255);float width=selected?1.5f:hovered?1:0;if(Border.outlineColor!=color||Border.outlineWidth!=width){Border.outlineColor=color;Border.outlineWidth=width;Border.SetVerticesDirty();Border.SetMaterialDirty();}Check.SetActive(selected);Monitor.SetActive(Active());}
 public void OnPointerEnter(PointerEventData e){hovered=true;}
 public void OnPointerExit(PointerEventData e){hovered=false;}
}
public enum LibrarySymbol {Check,Monitor,Left,Right,Down,Eye,Target,Search}
