using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using SpatialWallpaper.Mita;

public static class MitaInteractionBuilder {
 public static void Apply(GameObject actor,AnimatorController controller,MitaBehaviour behaviour){
  var look=behaviour.GetComponent<MitaLook>();var gestures=behaviour.GetComponent<MitaGestures>();
  var source=MitaXlmyBuilder.Clip("鼠标点击互动骨骼动作");
  string path=AnimationUtility.CalculateTransformPath(look.head,actor.transform);
  var bindings=AnimationUtility.GetCurveBindings(source).Where(b=>b.path==path&&b.propertyName.StartsWith("m_LocalRotation.")).OrderBy(b=>b.propertyName).ToArray();
  if(bindings.Length!=4)throw new InvalidOperationException("Original click head quaternion bindings missing");
  var curves="xyzw".Select(axis=>AnimationUtility.GetEditorCurve(source,bindings.Single(b=>b.propertyName.EndsWith("."+axis)))).ToArray();
  Func<float,Quaternion> sample=t=>new Quaternion(curves[0].Evaluate(t),curves[1].Evaluate(t),curves[2].Evaluate(t),curves[3].Evaluate(t)).normalized;
  var origin=sample(0);var basis=look.head.localRotation;
  var machine=new AnimatorStateMachine{name="HeadResponse"};AssetDatabase.AddObjectToAsset(machine,controller);
  var rest=Make("Rest",path,basis,t=>Quaternion.identity,.15f);var none=machine.AddState("None");none.motion=rest;none.writeDefaultValues=false;machine.defaultState=none;
  foreach(var name in new[]{"Nod","Shake"}){
   float duration=name=="Nod"?1.8f:2.4f;
   var clip=Make(name,path,basis,u=>{
    float phase=name=="Nod"?u:u<.5f?u*2:(u-.5f)*2;
    var delta=Quaternion.Inverse(origin)*sample(phase*source.length);
    float angle=Mathf.DeltaAngle(0,name=="Nod"?delta.eulerAngles.x:delta.eulerAngles.y);
    if(name=="Shake")angle*=u<.5f?.45f:-.45f;
    return Quaternion.AngleAxis(angle,name=="Nod"?Vector3.right:Vector3.up);
   },duration);
   var state=machine.AddState(name);state.motion=clip;state.writeDefaultValues=false;
  }
  controller.AddLayer(new AnimatorControllerLayer{name="HeadResponse",stateMachine=machine,defaultWeight=0,blendingMode=AnimatorLayerBlendingMode.Override});
  var interaction=behaviour.gameObject.AddComponent<MitaInteractions>();interaction.actor=actor.GetComponent<Animator>();interaction.headLayer=controller.layers.Length-1;
  interaction.nodDuration=1.8f;interaction.shakeDuration=2.4f;
  // Reuse the original welcome smile and blink on the existing face layer.
  var face=controller.layers[1].stateMachine;var reset=(AnimationClip)face.defaultState.motion;
  reset.SetCurve("Head",typeof(SkinnedMeshRenderer),"blendShape.SmileSmall",AnimationCurve.Constant(0,1,0));
  var pet=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AnimationClip/Blink.anim"));pet.name="Original smile and blink";
  var welcome=MitaXlmyBuilder.Clip("1合手动画");var smileBinding=AnimationUtility.GetCurveBindings(welcome).Single(b=>b.propertyName=="blendShape.SmileSmall");var smile=AnimationUtility.GetEditorCurve(welcome,smileBinding);
  var keys=Enumerable.Range(0,31).Select(i=>new Keyframe(i/30f*1.8f,Mathf.Clamp(smile.Evaluate(i/30f*welcome.length)-smile.Evaluate(0),0,35))).ToArray();keys[0].value=keys[keys.Length-1].value=0;
  pet.SetCurve("Head",typeof(SkinnedMeshRenderer),"blendShape.SmileSmall",new AnimationCurve(keys));
  const string petPath="Assets/Generated/Pet.anim";AssetDatabase.DeleteAsset(petPath);AssetDatabase.CreateAsset(pet,petPath);
  var statePet=face.AddState("Pet");statePet.motion=pet;statePet.writeDefaultValues=false;
  controller.AddParameter("Pet",AnimatorControllerParameterType.Trigger);
  var enter=face.defaultState.AddTransition(statePet);enter.hasExitTime=false;enter.duration=.08f;enter.AddCondition(AnimatorConditionMode.If,0,"Pet");
  var exit=statePet.AddTransition(face.defaultState);exit.hasExitTime=true;exit.exitTime=1;exit.duration=.15f;
  behaviour.gameObject.AddComponent<MitaInteractionAudit>();
 }
 static AnimationClip Make(string name,string path,Quaternion basis,Func<float,Quaternion> delta,float length){
  var clip=new AnimationClip{name="Original click head - "+name,frameRate=60};var curves=new[]{new AnimationCurve(),new AnimationCurve(),new AnimationCurve(),new AnimationCurve()};
  for(int i=0;i<=90;i++){float u=i/90f;var q=basis*delta(u);for(int a=0;a<4;a++)curves[a].AddKey(new Keyframe(u*length,q[a]));}
  for(int a=0;a<4;a++)clip.SetCurve(path,typeof(Transform),"localRotation."+"xyzw"[a],curves[a]);
  clip.EnsureQuaternionContinuity();string dest="Assets/Generated/Mita-"+name+".anim";AssetDatabase.DeleteAsset(dest);AssetDatabase.CreateAsset(clip,dest);return clip;
 }
}
