using System;
using System.IO;
using UnityEngine;
using SpatialTracking;

namespace SpatialWallpaper.Mita {
 [DefaultExecutionOrder(-100)]
 public sealed class MitaTracking : MonoBehaviour {
  RuntimeTrackingHost host;TrackingPipeline pipeline;EyeSmoother filter;
  WindowProjection projection;Vector3 viewCenter;string stream;
  MitaCalibration binding;
  Vector3 displayEye=new Vector3(0,0,-.6f);
  public string CalibrationStatus {get;private set;}="OFF";
  public long Accepted=>pipeline==null?0:pipeline.Accepted;
  public bool Fresh=>filter!=null&&filter.Fresh;
  public void SetPaused(bool paused){
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--tracking")<0)return;
   if(paused){host?.Dispose();host=null;pipeline=null;filter=null;projection.ClearExternalFreshness();}
   else Start();
  }
  static string Arg(string name,string fallback=""){
   var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);return index>=0&&index+1<args.Length?args[index+1]:fallback;
  }
  void Start(){
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--tracking")<0){enabled=false;return;}
   projection=GetComponent<MitaBehaviour>().projection;
   binding=new MitaCalibration(TrackingWire.Decode(File.ReadAllBytes(Arg("--tracking-calibration"))));
   if(binding.NeedsBinocular){pipeline=new TrackingPipeline();filter=new EyeSmoother(new[]{0.0,0,-.6});viewCenter=Vector3.zero;}
   else{
    var calibration=binding.Legacy;pipeline=new TrackingPipeline(calibration);filter=new EyeSmoother(calibration.e0);
    viewCenter=new Vector3((float)calibration.view_center_m[0],(float)calibration.view_center_m[1],0);
    projection.referenceEye=new Vector3((float)calibration.e0[0],(float)calibration.e0[1],(float)calibration.e0[2])-viewCenter;
   }
   projection.ResetTrackingClock();
   string mode=Arg("--tracking-mode","simulate");
   string camera=mode=="simulate"?"simulation":Arg("--tracking-camera");
   if(binding.NeedsBinocular&&camera!=(string)binding.Profile.manual.source["camera_id"])throw new InvalidDataException("CALIBRATION_CAMERA_MISMATCH");
   host=new RuntimeTrackingHost(pipeline,Arg("--tracking-python"),Arg("--tracking-root"),mode,camera);
   if(binding.NeedsBinocular)host.CameraModeId=(string)binding.Profile.manual.source["actual_mode"]["mode_id"];
   host.Start();
  }
  void Update(){
   if(binding==null)return;
   GetComponent<MitaInteractions>().Receive(pipeline?.RawLatest);
   bool viewportValid=true;string display=null;
   if(binding.NeedsBinocular){
    viewportValid=DisplayViewport.TryMeasure(new Rect(0,0,Screen.width,Screen.height),binding.Profile.manual.display_width,out var viewport,out display)&&display==binding.Profile.manual.display_id;
    GetComponent<MitaDisplayRig>().physicalViewport=viewportValid?viewport:null;
    viewCenter=viewportValid?new Vector3((float)viewport.CenterX,(float)viewport.CenterY,0):Vector3.zero;
    projection.referenceEye=new Vector3(0,0,-.6f)-viewCenter;
   }
   // Physical window geometry must remain current even while capture is paused.
   if(pipeline==null){if(binding.NeedsBinocular)projection.SetEye(displayEye-viewCenter);return;}
   var sample=pipeline.Latest;
   if(binding.NeedsBinocular){
    sample=viewportValid?binding.Map(pipeline.RawLatest,QpcClock.Now,display):new TrackingSnapshot(0,false);
    CalibrationStatus=!viewportValid?"DISPLAY_OR_VIEWPORT_MISMATCH":sample.Valid?"MAPPED":"WAITING_FOR_MATCHING_BINOCULAR_SAMPLE";
   }else CalibrationStatus=pipeline.Status;
   filter.Advance(sample,QpcClock.Now,Time.unscaledDeltaTime);
   displayEye=new Vector3((float)filter.X,(float)filter.Y,(float)filter.Z);
   var eye=displayEye-viewCenter;
   if(filter.Fresh){
    if(stream!=sample.Stream){stream=sample.Stream;projection.ResetTrackingClock();}
    projection.ApplyScreenEyeSample(eye,sample.Captured/1000000.0);
   }
   projection.SetEye(eye);projection.SetExternalFreshness(filter.Fresh);
  }
  void OnDestroy(){
   host?.Dispose();
   if(pipeline!=null&&host!=null){string output=Environment.GetEnvironmentVariable("MITA_OUTPUT");File.WriteAllText(Path.Combine(output,"tracking.txt"),"accepted="+pipeline.Accepted+" rejected="+pipeline.Rejected+" connections="+host.Connections+" diagnostic="+host.Diagnostic+" calibration="+CalibrationStatus);}
   if(projection)projection.ClearExternalFreshness();
  }
 }
}
