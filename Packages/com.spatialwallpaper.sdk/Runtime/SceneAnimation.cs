using System;
using UnityEngine;

namespace SpatialWallpaper.Sdk {
 public static class SceneAnimation {
  static Animator Target(GameObject node){if(!node)throw new ArgumentNullException(nameof(node));var animator=node.GetComponent<Animator>();return animator?animator:throw new InvalidOperationException("missing-owned-Animator");}
  public static void SetFloat(GameObject node,string parameter,float value)=>Target(node).SetFloat(parameter,value);
  public static void SetBool(GameObject node,string parameter,bool value)=>Target(node).SetBool(parameter,value);
  public static void Trigger(GameObject node,string parameter)=>Target(node).SetTrigger(parameter);
 }
}
