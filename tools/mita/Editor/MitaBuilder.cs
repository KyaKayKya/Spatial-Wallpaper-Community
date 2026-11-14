using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Animations;
using System.Linq;
using UnityEditor.Animations;
using SpatialWallpaper.Mita;

public static class MitaBuilder {
 [Serializable] sealed class CameraKey {public float time,value,inSlope,outSlope;}
 [Serializable] sealed class CameraVectorKey {public float time;public Vector3 value,inSlope,outSlope;}
 [Serializable] sealed class CameraData {public Vector3 offset_position,offset_euler;public float field_of_view;public CameraKey[] exposure;public CameraVectorKey[] position,euler;}
 static AnimationCurve[] Curves(CameraVectorKey[] keys){return Enumerable.Range(0,3).Select(axis=>new AnimationCurve(keys.Select(k=>new Keyframe(k.time,k.value[axis],k.inSlope[axis],k.outSlope[axis])).ToArray())).ToArray();}
 public static void Build(){
  GraphicsSettings.renderPipelineAsset=null;QualitySettings.renderPipeline=null;
  PlayerSettings.colorSpace=ColorSpace.Linear;
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var frame=new GameObject("ContentFrame").transform;
  var root=new GameObject("MitaPlacement").transform;
  root.SetParent(frame,false);
  root.SetPositionAndRotation(new Vector3(.075f,-.9f,.03f),Quaternion.Euler(0,180,0));
  root.localScale=Vector3.one*.6f;
  var textures=Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string,string>>(File.ReadAllText("Assets/Xlmy/texture-map.json"));
  var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Xlmy/XlmyMita.prefab");
  if(!source)throw new InvalidOperationException("Missing original character prefab");
  var actor=(GameObject)PrefabUtility.InstantiatePrefab(source);actor.SetActive(true);actor.transform.SetParent(root,false);
  var animator=actor.GetComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
  Directory.CreateDirectory("Assets/Generated");
  var controller=MitaXlmyBuilder.Create("Assets/Generated/Mita.controller");
  animator.runtimeAnimatorController=controller;
  foreach(var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>()){
   string meshName=renderer.name.Split('.')[0];
   string materialName=meshName=="Hair"?"Hair_0":meshName=="Head"?"Face_0":meshName=="Body"?"Body":"Cloth";
   var matched=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Material/"+materialName+".mat"));matched.name="Xlmy "+meshName;matched.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(textures[renderer.name]);
   if(!matched.mainTexture)throw new InvalidOperationException("Missing matching atlas: "+renderer.name);
   string materialPath="Assets/Generated/Xlmy-"+meshName+".mat";AssetDatabase.DeleteAsset(materialPath);AssetDatabase.CreateAsset(matched,materialPath);
   renderer.sharedMaterials=Enumerable.Repeat(matched,renderer.sharedMesh.subMeshCount).ToArray();
   if(meshName=="Head"){
    var mesh=UnityEngine.Object.Instantiate(renderer.sharedMesh);mesh.RecalculateNormals();mesh.RecalculateTangents();
    AssetDatabase.DeleteAsset("Assets/Generated/XlmyHead.asset");AssetDatabase.CreateAsset(mesh,"Assets/Generated/XlmyHead.asset");renderer.sharedMesh=mesh;
   }
   renderer.updateWhenOffscreen=true;
   foreach(var mat in renderer.sharedMaterials){
    if(!mat||!mat.shader||!mat.shader.isSupported||ShaderUtil.ShaderHasError(mat.shader))throw new InvalidOperationException("Invalid original shader on "+renderer.name);
   }
  }
  var camera=new GameObject("WindowCamera").AddComponent<Camera>();camera.gameObject.AddComponent<AudioListener>();
  camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.075f,.025f,.105f);camera.allowHDR=false;
  var projection=camera.gameObject.AddComponent<WindowProjection>();projection.contentDepth=3;
  projection.SetEye(projection.referenceEye);
  var volume=new GameObject("AuthorReference").AddComponent<DisplaySceneVolume>();volume.size=new Vector3(.53f,.298125f,3);volume.showGuide=false;
  volume.transform.SetParent(frame,false);
  RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.black;RenderSettings.fog=false;
  var light=new GameObject("OriginalMenuKey").AddComponent<Light>();light.type=LightType.Point;
  light.transform.SetParent(frame,false);
  light.color=new Color(1,.9198113f,.9482321f);light.intensity=2;light.range=2;
  light.transform.position=new Vector3(-.05f,.27f,-.45f);light.shadows=LightShadows.Soft;light.shadowStrength=.25f;
  var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Floor";floor.transform.SetParent(frame,false);floor.transform.localPosition=new Vector3(0,-.1490625f,5);floor.transform.localScale=new Vector3(2,1,1);
  UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
  var floorMat=new Material(Shader.Find("Sprites/Default"));floorMat.color=camera.backgroundColor;floorMat.renderQueue=1000;floor.GetComponent<Renderer>().sharedMaterial=floorMat;
  Directory.CreateDirectory("Assets/Generated");AssetDatabase.CreateAsset(floorMat,"Assets/Generated/Floor.mat");
  var backdrop=GameObject.CreatePrimitive(PrimitiveType.Quad);backdrop.name="OriginalMenuBackdrop";UnityEngine.Object.DestroyImmediate(backdrop.GetComponent<Collider>());
  backdrop.transform.position=new Vector3(0,0,2);backdrop.transform.localScale=Vector3.one*8;
  var backgroundTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Texture2D/Background Menu.png");
  if(!backgroundTexture)throw new InvalidOperationException("Missing original menu background");
  var backgroundImporter=(TextureImporter)AssetImporter.GetAtPath("Assets/Texture2D/Background Menu.png");backgroundImporter.textureType=TextureImporterType.Default;backgroundImporter.wrapMode=TextureWrapMode.Repeat;backgroundImporter.SaveAndReimport();
  backgroundTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Texture2D/Background Menu.png");
  var backdropMesh=UnityEngine.Object.Instantiate(backdrop.GetComponent<MeshFilter>().sharedMesh);backdropMesh.uv=backdropMesh.uv.Select(uv=>uv*16).ToArray();AssetDatabase.CreateAsset(backdropMesh,"Assets/Generated/BackdropMesh.asset");backdrop.GetComponent<MeshFilter>().sharedMesh=backdropMesh;
  var backdropMat=new Material(Shader.Find("Sprites/Default"));backdropMat.mainTexture=backgroundTexture;backdropMat.mainTextureScale=Vector2.one*16;backdropMat.color=new Color(.24f,.11f,.3f,.12f);backdropMat.renderQueue=1001;
  backdrop.GetComponent<Renderer>().sharedMaterial=backdropMat;AssetDatabase.CreateAsset(backdropMat,"Assets/Generated/Backdrop.mat");
  var runtime=new GameObject("MitaWallpaper").AddComponent<MitaBehaviour>();runtime.actor=animator;runtime.placement=root;runtime.projection=projection;runtime.view=camera;runtime.volume=volume;
  if(Directory.Exists("Assets/Audio")){
   var clips=AssetDatabase.FindAssets("t:AudioClip",new[]{"Assets/Audio"});
   if(clips.Length!=1)throw new InvalidOperationException("Expected one selected BGM");
   string audioPath=AssetDatabase.GUIDToAssetPath(clips[0]);var importer=(AudioImporter)AssetImporter.GetAtPath(audioPath);var settings=importer.defaultSampleSettings;settings.loadType=AudioClipLoadType.Streaming;settings.preloadAudioData=false;importer.defaultSampleSettings=settings;importer.SaveAndReimport();
   runtime.music=runtime.gameObject.AddComponent<AudioSource>();runtime.music.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(audioPath);runtime.music.volume=.25f;runtime.music.loop=true;
  }
  runtime.gameObject.AddComponent<MitaCapture>();
  runtime.gameObject.AddComponent<MitaGazeAudit>();
  runtime.gameObject.AddComponent<MitaDesktop>();
  runtime.gameObject.AddComponent<MitaTracking>();
  runtime.gameObject.AddComponent<MitaRecording>();
  runtime.gameObject.AddComponent<MitaBenchmark>();runtime.gameObject.AddComponent<MitaLifecycle>();
  var sampledCamera=new GameObject("OriginalCameraAnimationData").AddComponent<Camera>();sampledCamera.enabled=false;sampledCamera.orthographic=true;
  var cameraAnimator=sampledCamera.gameObject.AddComponent<Animator>();cameraAnimator.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/AnimatorController/CameraMenu.controller");cameraAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
  var sampledLight=new GameObject("Point Light").AddComponent<Light>();sampledLight.transform.SetParent(sampledCamera.transform,false);sampledLight.transform.localPosition=new Vector3(0,.134f,.35f);sampledLight.enabled=false;
  var shot=runtime.gameObject.AddComponent<MitaShot>();shot.originalOpening=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/AnimationClip/CameraMenu Start.anim");
  if(!shot.originalOpening)throw new InvalidOperationException("Missing original opening camera clip");
  var cameraData=JsonUtility.FromJson<CameraData>(File.ReadAllText("Assets/Xlmy/Camera/camera.json"));
  shot.cameraPosition=Curves(cameraData.position);shot.cameraEuler=Curves(cameraData.euler);
  shot.cameraOffsetPosition=cameraData.offset_position;shot.cameraOffsetEuler=cameraData.offset_euler;shot.sourceFieldOfView=cameraData.field_of_view;
  shot.exposure=new AnimationCurve(cameraData.exposure.Select(k=>new Keyframe(k.time,k.value,k.inSlope,k.outSlope)).ToArray());
  shot.sampledCamera=sampledCamera;shot.sampledLight=sampledLight;shot.key=light;shot.placement=root;shot.windowCamera=camera;shot.floor=floor.GetComponent<Renderer>();shot.characterSurfaces=actor.GetComponentsInChildren<SkinnedMeshRenderer>();shot.Sample(6.5f);
  shot.backdrop=backdrop.GetComponent<Renderer>();
  var rig=runtime.gameObject.AddComponent<MitaDisplayRig>();rig.projection=projection;rig.contentFrame=frame;rig.Fit(16f/9);
  var look=runtime.gameObject.AddComponent<MitaLook>();look.placement=root;look.shot=shot;
  var bones=actor.GetComponentsInChildren<Transform>();
  look.head=bones.Single(t=>t.name=="Head"&&!t.GetComponent<SkinnedMeshRenderer>());
  look.leftEye=bones.Single(t=>t.name=="Left Eye");look.rightEye=bones.Single(t=>t.name=="Right Eye");
  ((AnimationClip)controller.layers[0].stateMachine.states.Single(s=>s.state.name=="MitaIdle").state.motion).SampleAnimation(actor,0);
  var forward=actor.transform.InverseTransformDirection((look.leftEye.position+look.rightEye.position)*.5f-look.head.position);forward.y=0;forward.Normalize();
  look.neutralFacing=forward;
  var facingWorld=actor.transform.TransformDirection(forward);
  look.headTarget=new GameObject("HeadTarget").transform;look.eyesTarget=new GameObject("EyesTarget").transform;
  look.headAim=AddAim(look.head,look.headTarget,look.head.InverseTransformDirection(facingWorld),look.head.InverseTransformDirection(actor.transform.up));
  look.leftAim=AddAim(look.leftEye,look.eyesTarget,look.leftEye.InverseTransformDirection(facingWorld),look.leftEye.InverseTransformDirection(actor.transform.up));look.rightAim=AddAim(look.rightEye,look.eyesTarget,look.rightEye.InverseTransformDirection(facingWorld),look.rightEye.InverseTransformDirection(actor.transform.up));
  MitaXlmyBuilder.Runtime(actor,controller,runtime);
  MitaEffectsBuilder.Apply(actor,runtime);
  MitaInteractionBuilder.Apply(actor,controller,runtime);
  const string path="Assets/Generated/Mita.unity";EditorSceneManager.SaveScene(scene,path);AssetDatabase.SaveAssets();
  PlayerSettings.productName="Mita Spatial Wallpaper";PlayerSettings.runInBackground=true;PlayerSettings.resizableWindow=true;
  PlayerSettings.enableFrameTimingStats=true;
  PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
  PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
  var report=BuildPipeline.BuildPlayer(new[]{path},"Builds/Mita/Mita.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
  if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new InvalidOperationException("Mita build failed");
 }
 static AimConstraint AddAim(Transform bone,Transform target,Vector3 axis,Vector3 up){
  var aim=bone.gameObject.AddComponent<AimConstraint>();aim.aimVector=axis;aim.upVector=up;
  aim.worldUpType=AimConstraint.WorldUpType.ObjectRotationUp;aim.worldUpObject=bone.parent;aim.worldUpVector=Vector3.up;
  aim.AddSource(new ConstraintSource{sourceTransform=target,weight=1});aim.weight=0;aim.constraintActive=true;aim.locked=true;return aim;
 }
}
