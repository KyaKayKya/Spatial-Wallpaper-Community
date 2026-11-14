using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using SpatialWallpaper.Mita;

public static class MitaXlmyBuilder {
 static readonly System.Collections.Generic.Dictionary<string,AnimationClip> clips=new System.Collections.Generic.Dictionary<string,AnimationClip>();
 public static AnimationClip Clip(string suffix){
  if(clips.TryGetValue(suffix,out var cached))return cached;
  string guid=AssetDatabase.FindAssets("t:AnimationClip",new[]{"Assets/Xlmy/AnimationClip"}).Single(g=>AssetDatabase.GUIDToAssetPath(g).EndsWith(suffix+".anim"));
  var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
  var clip=UnityEngine.Object.Instantiate(source);
  var events=AnimationUtility.GetAnimationEvents(source).Where(e=>e.functionName=="Wink").ToArray();foreach(var e in events)e.functionName="PlayWink";AnimationUtility.SetAnimationEvents(clip,events);
  var settings=AnimationUtility.GetAnimationClipSettings(source);settings.loopTime=suffix=="待机";AnimationUtility.SetAnimationClipSettings(clip,settings);
  string path="Assets/Generated/Xlmy-"+suffix+".anim";AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(clip,path);clips[suffix]=clip;return clip;
 }
 public static AnimatorController Create(string path){
  clips.Clear();
  AssetDatabase.DeleteAsset(path);var controller=AnimatorController.CreateAnimatorControllerAtPath(path);
  var machine=controller.layers[0].stateMachine;var idle=machine.AddState("MitaIdle");idle.motion=Clip("待机");
  var opening=machine.AddState("MitaStartIdle");opening.motion=Clip("2眨眼动画");machine.defaultState=opening;
  var openingExit=opening.AddTransition(idle);openingExit.hasExitTime=true;openingExit.exitTime=1;openingExit.hasFixedDuration=true;openingExit.duration=.15f;
  string[] suffixes={"1合手动画","2眨眼动画","3耶动画","4闭眼挥手动画","鼠标点击互动骨骼动作","5邪恶动画"};
  string[] names={"Xlmy Welcome","Xlmy Wink","Xlmy Peace","Xlmy Wave","Xlmy Dismiss","Mita EndingFace"};
  for(int i=0;i<names.Length;i++){
   var state=machine.AddState(names[i]);state.motion=Clip(suffixes[i]);state.tag="Gesture";state.speed=.8f;
   var exit=state.AddTransition(idle);exit.hasExitTime=true;exit.exitTime=1;exit.hasFixedDuration=true;exit.duration=.25f;
  }
  controller.AddParameter("Blink",AnimatorControllerParameterType.Trigger);
  var face=new AnimatorStateMachine{name="Face"};AssetDatabase.AddObjectToAsset(face,controller);
  var reset=new AnimationClip{name="Reset Blink"};reset.SetCurve("Head",typeof(SkinnedMeshRenderer),"blendShape.Blink",AnimationCurve.Constant(0,1,0));AssetDatabase.DeleteAsset("Assets/Generated/ResetBlink.anim");AssetDatabase.CreateAsset(reset,"Assets/Generated/ResetBlink.anim");
  var none=face.AddState("None");none.motion=reset;none.writeDefaultValues=false;face.defaultState=none;
  var blink=face.AddState("Blink");blink.motion=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AnimationClip/Blink.anim");blink.writeDefaultValues=false;
  var enter=none.AddTransition(blink);enter.hasExitTime=false;enter.duration=0;enter.AddCondition(AnimatorConditionMode.If,0,"Blink");
  var back=blink.AddTransition(none);back.hasExitTime=true;back.exitTime=1;back.duration=0;
  controller.AddLayer(new AnimatorControllerLayer{name="Face",stateMachine=face,defaultWeight=1,blendingMode=AnimatorLayerBlendingMode.Additive});
  return controller;
 }
 public static void Runtime(GameObject actor,AnimatorController controller,MitaBehaviour behaviour){
  var gestures=behaviour.gameObject.AddComponent<MitaGestures>();gestures.actor=actor.GetComponent<Animator>();
  gestures.names=new[]{"Xlmy Welcome","Xlmy Wink","Xlmy Peace","Xlmy Wave","Xlmy Dismiss","Mita EndingFace"};
  gestures.durations=gestures.names.Select(n=>{var state=controller.layers[0].stateMachine.states.Single(s=>s.state.name==n).state;return ((AnimationClip)state.motion).length/state.speed;}).ToArray();
  behaviour.gameObject.AddComponent<MitaGestureAudit>();
 }
}
