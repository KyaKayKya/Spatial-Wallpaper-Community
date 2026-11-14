// Isolated experimental harness. Original EyeMU and UnitEye Web remain distinct.
import {MOTION_PHASES,TraceRecorder,eyeAspect,imageStats} from './diagnostics.mjs';
const $=id=>document.getElementById(id), stage=$('stage');
const profile=new URLSearchParams(location.search).get('profile')||'original';
if(!['original','uniteye'].includes(profile))throw Error('Unknown protocol');
const protocol=profile==='original'?'eyemu-original-tfjs4-cal255-live256-ridge26-v1':'uniteye-web-0cbbc9b-ridge36-v1';
const recorder=new TraceRecorder(protocol),captureRevision='fixation-motion-quality-v2';
let motionIndex=-1,motionCapture=null,displayMode='both',cameraMetadata=null,unitQuality={};
$('profile').textContent=protocol;
if(profile==='uniteye')$('eyes').hidden=true;
let baseModel,embedding,face,engine,video,stream,loaded=false,running=false,lastVideo=-1;
let busy=false,epoch=0,sessionViewport=null,frameCount=0,rx,ry,samples=[],results=[],fitRows=[],targetRows=[],targetIndex=-1,capture=null,locked=null;
const points=[];for(const y of [.15,.5,.85])for(const x of [.15,.5,.85])points.push([x,y]);
points.push([.3,.3],[.7,.3],[.3,.7],[.7,.7]);
const frame=document.createElement('canvas'),small=document.createElement('canvas');
const status=text=>$('status').textContent=text;
const json=value=>$('detail').textContent=JSON.stringify(value,null,2);
function dot(id,xy){const el=$(id);el.style.display=xy?'block':'none';if(xy){el.style.left=xy[0]*stage.clientWidth+'px';el.style.top=xy[1]*stage.clientHeight+'px';}}
function stop(message='已停止相机。'){
  recorder.finishPhase(performance.now(),true);motionCapture=null;motionIndex=-1;
  running=false;epoch++;capture=null;engine?.stop();stream?.getTracks().forEach(t=>t.stop());
  rx=ry=null;targetIndex=-1;locked=null;dot('gaze',null);dot('target',null);
  dot('smooth',null);$('calibrate').disabled=true;$('start').disabled=!loaded;status(message);
}
function fail(e){stop('错误：'+e.message);console.error(e);window.testError=String(e);}
window.addEventListener('pagehide',()=>stop());
document.addEventListener('fullscreenchange',()=>{if(locked&&!document.fullscreenElement)stop('已退出全屏并停止相机；本次校准不再使用。');});
function validFrame(features,raw,blink=false,quality={}){
  if(!running)return;
  if(locked&&(stage.clientWidth!==locked[0]||stage.clientHeight!==locked[1]||devicePixelRatio!==locked[2])){stop('窗口或 DPI 已改变，请重新校准。');return;}
  const now=performance.now();frameCount++;
  const featureFinite=!!features&&features.length===(profile==='original'?26:36)&&features.every(Number.isFinite);
  const accepted=featureFinite&&!(profile==='uniteye'&&blink)&&!quality.reason;
  let prediction=null;
  if(rx&&features?.every(Number.isFinite)){
    const x=profile==='original'?rx.predict(Float64Array.from(features)):rx.predict(features);
    const y=profile==='original'?ry.predict(Float64Array.from(features)):ry.predict(features);
    if(Number.isFinite(x)&&Number.isFinite(y)){
      prediction=[x,y];
      if(accepted&&capture&&now>=capture.begin&&now<=capture.end&&targetIndex>=9)capture.predictions.push(prediction);
    }
  }
  if(locked){
    if(recorder.frames.length>=30000){recorder.truncated=true;stop('记录达到 30000 帧上限，已停止相机。请导出数值。');return;}
    const q={...quality,blink:quality.blink??(profile==='uniteye'?blink:null),feature_finite:featureFinite,prediction_finite:!!prediction};
    const phase=motionCapture&&now>=motionCapture.begin_ms&&now<=motionCapture.end_ms?motionCapture:null;
    const row=recorder.record({time_ms:now,video_time_ms:quality.video_time_ms??null,
      stage:targetIndex>=0?(targetIndex<9?'fit':'held_out'):phase?'motion':motionIndex>=0?'motion_ready':'free_preview',
      target:targetIndex>=0?[...points[targetIndex]]:motionIndex>=0?[...MOTION_PHASES[motionIndex].target]:null,
      index:targetIndex>=0?targetIndex:null,phase_id:phase?.id??null,phase_attempt:phase?.attempt??null,
      collection_active:!!phase||!!(capture&&now>=capture.begin&&now<=capture.end),accepted,quality:q,
      features:features?[...features]:null,network:raw?[...raw]:null,prediction,
      display_mode:targetIndex>=0||motionIndex>=0?'hidden':displayMode});
    if(phase){phase.attempted++;phase.accepted+=accepted&&!!prediction;phase.blink+=q.blink===true;}
    $('quality').textContent=`${accepted?'有效输入':'无效输入'} · ${q.blink===true?'疑似眨眼':q.blink===false?'未标记眨眼':'眨眼未知'} · 记录 ${recorder.frames.length} 帧`;
    if(targetIndex<0&&motionIndex<0){
      dot('gaze',accepted&&displayMode!=='smooth'&&displayMode!=='hidden'?row.prediction_raw:null);
      dot('smooth',displayMode!=='raw'&&displayMode!=='hidden'?row.prediction_smooth:null);
    }
  }
  if(!accepted)return;
  if(capture&&now>=capture.begin&&now<=capture.end){
    const row={protocol,target:points[targetIndex],index:targetIndex,split:targetIndex<9?'fit':'held_out',
      time_ms:now,features:[...features],raw:[...raw]};
    targetRows.push(row);
  }
  if(capture&&now>capture.end)completeTarget();
}
function fit(){
  const X=fitRows.map(r=>r.features),ys=fitRows.map(r=>r.target);
  if(profile==='original'){
    // Original mainTesting.js newModel(): ridge lambda .1, no feature standardization.
    rx=new Regression(RidgeRegression,{lambda:.1});ry=new Regression(RidgeRegression,{lambda:.1});
    rx.train(mat(X,true),Float64Array.from(ys.map(p=>p[0])));ry.train(mat(X,true),Float64Array.from(ys.map(p=>p[1])));
  }else{
    rx=UnitEyeCore.trainRidge(X,ys.map(p=>p[0]),.1);ry=UnitEyeCore.trainRidge(X,ys.map(p=>p[1]),.1);
  }
}
function completeTarget(){
  const predictions=capture.predictions;capture=null;
  if(targetRows.length<5){targetRows=[];status('有效帧不足，继续看红点，按空格重试。');return;}
  samples.push(...targetRows);
  if(targetIndex<9)fitRows.push(...targetRows);
  else{
    const p=points[targetIndex];
    const errors=predictions.map(q=>Math.hypot((q[0]-p[0])*locked[0],(q[1]-p[1])*locked[1]));
    if(!errors.length){status('预测无效，请停止后重新校准。');return;}
    results.push({index:targetIndex,errors_css_px:errors});
  }
  targetRows=[];targetIndex++;
  if(targetIndex===9)fit();
  if(targetIndex===points.length){
    targetIndex=-1;dot('target',null);
    const es=results.flatMap(r=>r.errors_css_px).sort((a,b)=>a-b);
    const summary={protocol,held_out_frames:es.length,median_css_px:es[Math.floor(es.length*.5)],p95_css_px:es[Math.floor((es.length-1)*.95)]};
    json(summary);motionIndex=0;showMotion();
  }else showTarget();
}
function showTarget(){dot('gaze',null);dot('target',points[targetIndex]);status(`${targetIndex+1}/13 ${targetIndex<9?'校准':'独立验证'}：看红点，按空格。`);}
function showMotion(){
  dot('gaze',null);dot('smooth',null);const p=MOTION_PHASES[motionIndex];dot('target',p.target);
  status(`头动 ${motionIndex+1}/${MOTION_PHASES.length}：${p.instruction}。\n始终注视红点，按空格准备 1 秒后记录 ${p.seconds} 秒。测量期间隐藏预测点。`);
}
function completeMotion(){
  const phase=motionCapture;recorder.finishPhase(performance.now());motionCapture=null;
  if(phase.accepted<5){status('本段有效帧不足，请看红点按空格重试。');return;}
  motionIndex++;
  if(motionIndex===MOTION_PHASES.length){motionIndex=-1;dot('target',null);status('记录完成。蓝点=原始预测，绿点=100 ms 平滑；V 切换显示。Esc 停止后导出数值。');}
  else showMotion();
}
window.addEventListener('keydown',e=>{
  if(e.code==='Escape'){stop();return;}
  if(e.code==='KeyV'&&targetIndex<0&&motionIndex<0){displayMode=({both:'raw',raw:'smooth',smooth:'hidden',hidden:'both'})[displayMode];dot('gaze',null);dot('smooth',null);status(`显示：${displayMode}；蓝=原始，绿=平滑。Esc 停止并导出。`);}
  if(e.code==='Space'&&running&&motionIndex>=0&&!motionCapture){
    e.preventDefault();const p=MOTION_PHASES[motionIndex];motionCapture=recorder.beginPhase(p.id,p.target,performance.now()+1000,p.seconds*1000);
    status(`${p.instruction}，始终注视红点。1 秒后开始记录 ${p.seconds} 秒……`);return;
  }
  if(e.code==='Space'&&running&&targetIndex>=0&&!capture){e.preventDefault();targetRows=[];capture={begin:performance.now()+1000,end:performance.now()+3000,predictions:[]};status('保持看红点，正在采集……');}
});
setInterval(()=>{try{
  if(running&&capture&&performance.now()>capture.end)completeTarget();
  if(running&&motionCapture&&performance.now()>motionCapture.end_ms)completeMotion();
}catch(e){fail(e);}},100);

async function load(){
  if(loaded)return;
  status('加载模型并进行无相机自检……');
  if(profile==='original'){
    await tf.setBackend('webgl');await tf.ready();
    baseModel=await tf.loadLayersModel('model/my-model.json');
    const indices=[39,43,46];
    if(indices.map(i=>baseModel.layers[i].name).join()!=='dense_6,dense_7,dense_8')throw Error('模型层契约不匹配');
    embedding=tf.model({inputs:baseModel.inputs,outputs:indices.map(i=>baseModel.layers[i].output)});
    const probe=tf.tidy(()=>embedding.predict([tf.zeros([1,128,128,3]),tf.zeros([1,128,128,3]),tf.zeros([1,8]),tf.zeros([1,4])]).map(t=>Array.from(t.dataSync())));
    if(!probe.flat().every(Number.isFinite))throw Error('模型输出非有限数');
    face=await facemesh.load({maxFaces:1}); // Same legacy API: mesh versus scaledMesh stay separate.
    const blank=document.createElement('canvas');blank.width=320;blank.height=240;
    if((await face.estimateFaces(blank,false,false)).length)throw Error('空白图不应检测到人脸');
    // Check author's ridge runtime without fitting any participant data.
    const r=new Regression(RidgeRegression,{lambda:.1});r.train(mat([[0,0],[1,0],[0,1],[1,1]],true),new Float64Array([0,1,1,2]));
    if(!Number.isFinite(r.predict(new Float64Array([.5,.5]))))throw Error('Ridge 自检失败');
    window.smokeResult={profile,tfjs:tf.version.tfjs,model:probe,facemesh_loaded:true,blank_face_check:true,ridge:true,camera:false};
  }else{
    const {UnitEyeWeb}=await import('./uniteye-cv.js');
    engine=new UnitEyeWeb({onGaze:(_filtered,raw,blink,present,features)=>{
      validFrame(features,present?[raw.x/engine.screenW,raw.y/engine.screenH]:null,blink,
        {...unitQuality,face_present:present,blink,upstream_blink_gate:true,
          reason:!present?(unitQuality.crop_valid===false?'crop_out_of_bounds':'no_face'):null,
          pipeline_elapsed_ms:performance.now()-(unitQuality.started_ms??performance.now())});
    }});
    // No init() here: upstream init also requests camera. Delay it until explicit Start.
    const ort=await import('https://cdn.jsdelivr.net/npm/onnxruntime-web@1.20.1/dist/ort.mjs');
    ort.env.wasm.wasmPaths='https://cdn.jsdelivr.net/npm/onnxruntime-web@1.20.1/dist/';ort.env.wasm.numThreads=1;
    const s=await ort.InferenceSession.create('models/eyemu.onnx',{executionProviders:['wasm']});
    const feeds={};for(const [n,shape] of [['input_1:0',[1,128,128,3]],['input_2:0',[1,128,128,3]],['input_4',[1,8]],['input_5',[1,4]]])feeds[n]=new ort.Tensor('float32',new Float32Array(shape.reduce((a,b)=>a*b,1)),shape);
    const out=await s.run(feeds);if(!Array.from(out.dense_8.data).every(Number.isFinite))throw Error('UnitEye 模型输出无效');await s.release();
    window.smokeResult={profile,onnx:true,camera:false};
  }
  loaded=true;$('start').disabled=false;status('模型自检通过，未开启相机。等待你准备好后再点启动。');json(window.smokeResult);
}
async function start(){
  if(running)return;await load();$('start').disabled=true;
  if(profile==='uniteye'){
    engine.screenW=stage.clientWidth;engine.screenH=stage.clientHeight;
    try{await engine.init();}catch(e){engine.stop();throw e;}
    instrumentUnitEye();
    cameraMetadata=readCamera(engine.video);
    running=true;engine.start();
  }else{
    stream=await navigator.mediaDevices.getUserMedia({video:{facingMode:'user',aspectRatio:1.3333,width:{ideal:1920}},audio:false});
    video=document.createElement('video');video.muted=true;video.playsInline=true;video.srcObject=stream;await video.play();
    window.videoWidth=video.videoWidth;window.videoHeight=video.videoHeight;
    frame.width=video.videoWidth;frame.height=video.videoHeight;small.width=frame.width/4;small.height=frame.height/4;
    cameraMetadata=readCamera(video);
    running=true;lastVideo=-1;tick(++epoch);
  }
  $('calibrate').disabled=false;status(profile==='original'?'相机已开启，请检查底部两眼图；确认后点全屏校准。':'UnitEye Web 已开启；点击全屏校准，参数只属于本页协议。');
}
function readCamera(v){const s=v.srcObject?.getVideoTracks()[0]?.getSettings()||{};return {width:v.videoWidth,height:v.videoHeight,frame_rate:s.frameRate??null,aspect_ratio:s.aspectRatio??null};}
function instrumentUnitEye(){
  // Observe the original Web methods without changing their arguments or return values.
  if(!engine._diagnosticWrapped){
    engine._diagnosticWrapped=true;
    const process=engine._processFrame.bind(engine);
    engine._processFrame=async()=>{
      unitQuality={started_ms:performance.now(),video_time_ms:engine.video.currentTime*1000,face_present:null,blink_method:'upstream_blendshape_or_EAR'};
      try{return await process();}catch(e){validFrame(null,null,false,{...unitQuality,reason:'pipeline_error',error:String(e)});throw e;}
    };
    const eye=engine._eyeTensor.bind(engine);engine._eyeTensor=(...args)=>{
      const value=eye(...args);unitQuality.crop_valid=(unitQuality.crop_valid??true)&&!!value;
      if(value){unitQuality.eye_stats??=[];unitQuality.eye_stats.push(imageStats(engine._crop));}return value;
    };
    const pose=engine._headPose.bind(engine);engine._headPose=(...args)=>{const p=pose(...args);unitQuality.model_geometry=[...p];return p;};
  }
  const detector=engine.faceLandmarker.detectForVideo.bind(engine.faceLandmarker);
  engine.faceLandmarker.detectForVideo=(...args)=>{
    const res=detector(...args),lm=res.faceLandmarks?.[0];unitQuality.face_present=!!lm;
    if(lm){
      unitQuality.ear=eyeAspect(lm,engine.video.videoWidth,engine.video.videoHeight);
      const categories=res.faceBlendshapes?.[0]?.categories||[];
      unitQuality.blink_scores=['eyeBlinkLeft','eyeBlinkRight'].map(n=>categories.find(c=>c.categoryName===n)?.score??null);
      unitQuality.head_matrix=res.facialTransformationMatrixes?.[0]?.data?Array.from(res.facialTransformationMatrixes[0].data):null;
    }return res;
  };
}
async function tick(token){
  if(!running||token!==epoch)return;
  try{
    if(!busy&&video.currentTime!==lastVideo){
      busy=true;lastVideo=video.currentTime;
      const started=performance.now(),q={video_time_ms:video.currentTime*1000};
      frame.getContext('2d').drawImage(video,0,0);
      small.getContext('2d').drawImage(frame,0,0,small.width,small.height);
      const predictions=await face.estimateFaces(small,false,false);
      if(!running||token!==epoch){busy=false;return;}
      const p=predictions[0];
      q.face_present=!!p;q.face_confidence=p?.faceInViewConfidence??null;
      if(p&&p.faceInViewConfidence>.9){
        q.ear=eyeAspect(p.scaledMesh);q.blink=q.ear.blink;q.blink_method=q.ear.method;
        const corners=getEyeCorners(p,small.height,small.width),geom=[getFaceYaw(p.mesh),getFacePitch(p.mesh),getFaceRoll(p.mesh),getFaceSize(p.boundingBox)];
        const crops=[eyeBoundsFromCorners([corners[0]*small.width,corners[1]*small.height],[corners[2]*small.width,corners[3]*small.height]),
                     eyeBoundsFromCorners([corners[6]*small.width,corners[7]*small.height],[corners[4]*small.width,corners[5]*small.height])];
        q.crop_valid=crops.every(r=>r.every(Number.isFinite)&&r[0]>=0&&r[2]>=0&&r[1]<=frame.width&&r[3]<=frame.height&&r[4]>1&&r[5]>1);
        q.model_geometry=[...geom];q.crop_rects=crops.map(r=>[...r]);
        if(q.crop_valid){
          crops.forEach((r,i)=>$(i?'right':'left').getContext('2d').drawImage(frame,r[0],r[2],r[4],r[5],0,0,128,128));
          // Match author's calibration /255 and live svr.js /256 discrepancy explicitly.
          const divisor=targetIndex>=0&&targetIndex<9?255:256;
          q.normalization_divisor=divisor;q.eye_stats=[imageStats($('left')),imageStats($('right'))];
          const outs=tf.tidy(()=>embedding.predict([tf.browser.fromPixels($('left')).reverse(1).div(divisor).sub(.5).expandDims(),
            tf.browser.fromPixels($('right')).div(divisor).sub(.5).expandDims(),tf.tensor2d([corners]),tf.tensor2d([geom])]).map(t=>Array.from(t.dataSync())));
          const features=[...outs[0].map(v=>v/100),...outs[1].map(v=>v/10),...outs[2],...corners,...geom];
          q.pipeline_elapsed_ms=performance.now()-started;validFrame(features,outs[2],false,q);
        }else validFrame(null,null,false,{...q,reason:'crop_out_of_bounds',pipeline_elapsed_ms:performance.now()-started});
      }else validFrame(null,null,false,{...q,reason:p?'low_face_confidence':'no_face',pipeline_elapsed_ms:performance.now()-started});
      busy=false;
    }
    // End invalid captures too; don't hang indefinitely waiting for a face.
    if(capture&&performance.now()>capture.end)completeTarget();
    requestAnimationFrame(()=>tick(token));
  }catch(e){busy=false;if(running&&!recorder.truncated)validFrame(null,null,false,{reason:'pipeline_error',error:String(e)});fail(e);}
}
$('load').onclick=()=>load().catch(fail);$('start').onclick=()=>start().catch(fail);$('stop').onclick=()=>stop();
$('calibrate').onclick=async()=>{try{
  await stage.requestFullscreen();await new Promise(r=>setTimeout(r,150));
  locked=[stage.clientWidth,stage.clientHeight,devicePixelRatio];sessionViewport=[...locked];samples=[];results=[];fitRows=[];rx=ry=null;
  recorder.reset();motionIndex=-1;motionCapture=null;frameCount=0;dot('smooth',null);
  if(engine){engine.screenW=locked[0];engine.screenH=locked[1];}
  targetIndex=0;showTarget();
}catch(e){fail(e);}};
$('export').onclick=()=>{
  const data={protocol,capture_revision:captureRevision,viewport:sessionViewport,camera:cameraMetadata,
    source_versions:{eyemu:'7184a11584604337c882e4e038dc4321f3016c46',uniteye:'0cbbc9bfbb0d0b0b95b57deec3095e5d5ac81996'},
    results,samples,frame_count:frameCount,frames:recorder.frames,motion_phases:recorder.phases,truncated:recorder.truncated,
    display_filter:{type:'EMA',tau_ms:100,reset_gap_ms:250,blink_resets:true,affects_calibration:false},
    limitations:['CSS pixels, not measured centimetres','Targets assume instructed fixation; no independent gaze truth',
      'Original EAR blink flag is heuristic and does not reject original calibration frames',
      'UnitEye retains upstream blink gate; Web comparator uses fixed lambda .1, not full native training',
      'Motion phase labels are instructions, not measured physical head displacement',
      'Pipeline elapsed time excludes camera exposure/display latency','Harness target schedule differs from phone UI']};
  const a=document.createElement('a');a.href=URL.createObjectURL(new Blob([JSON.stringify(data)],{type:'application/json'}));a.download=protocol+'-'+Date.now()+'.json';a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000);
};
// Explicit no-camera test entry used only for preparation.
if(new URLSearchParams(location.search).get('smoke')==='1')load().catch(fail);
