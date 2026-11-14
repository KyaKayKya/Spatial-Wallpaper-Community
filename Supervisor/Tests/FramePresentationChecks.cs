using System;using System.IO;using System.Drawing;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Supervision.Windows;
static class FramePresentationChecks {
 public static Action<string> InputOpenProbe;
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static void Until(Func<bool> condition,int timeout){var clock=Stopwatch.StartNew();while(!condition()){if(clock.ElapsedMilliseconds>timeout)throw new TimeoutException("frame-presentation-deadline");Thread.Sleep(10);}}
 static double RedCenter(RuntimeFrame frame){var pixels=new byte[frame.Width*frame.Height*4];frame.CopyPixels(pixels);long sum=0,count=0;for(int y=0;y<frame.Height;y++)for(int x=0;x<frame.Width;x++){int at=(y*frame.Width+x)*4;if(pixels[at]>150&&pixels[at+1]<100&&pixels[at+2]<100){sum+=x;count++;}}Need(count>100,"projection-author-red-pixels-missing");return (double)sum/count;}
 static int RedWidth(RuntimeFrame frame){var pixels=new byte[frame.Width*frame.Height*4];frame.CopyPixels(pixels);int left=frame.Width,right=-1;for(int y=0;y<frame.Height;y++)for(int x=0;x<frame.Width;x++){int at=(y*frame.Width+x)*4;if(pixels[at]>150&&pixels[at+1]<100&&pixels[at+2]<100){left=Math.Min(left,x);right=Math.Max(right,x);}}Need(right>=left,"projection-author-red-width-missing");return right-left+1;}
 [STAThread]static int Main(string[] args){PresentedInputChecks.ManualFocusWait=args.Length>6&&args[6]=="manual-focus";Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Process host=null,worker=null,foreignWorker=null;SupervisorManagerConnection connection=null;RuntimeSession session=null,foreignSession=null;Exception failure=null;JObject result=new JObject();try{
  string output=args[0],state=Path.Combine(output,"state");
  var start=new ProcessStartInfo(args[1],"\""+Path.Combine(output,"config.json")+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};host=Process.Start(start);
  Until(()=>File.Exists(Path.Combine(state,"endpoint.json"))||host.HasExited,15000);Need(!host.HasExited,"frame-host-start-failed");connection=new SupervisorManagerConnection(Path.Combine(state,"endpoint.json"));
  session=SupervisorSessionClient.Start(connection,Guid.NewGuid(),args[3],Guid.NewGuid(),1,args[2],!(args.Length>4&&args[4].StartsWith("desktop-channel",StringComparison.Ordinal)));Need(session.Running.Wait(30000),"frame-worker-running-deadline");session.Running.GetAwaiter().GetResult();
  var journal=JObject.Parse(File.ReadAllText(Path.Combine(state,"journal/state.json")));int pid=(int)journal["active"][session.Identity.Attempt.ToString("N")]["process"];worker=Process.GetProcessById(pid);long creation=RestrictedProcess.ReadCreationFileTime(worker.Handle);
  var pixels=new byte[640*360*4];RuntimeFrame frame=null;long sequence=0;Until(()=>{frame=connection.ReadVerifiedFrame(session,640,360,sequence).GetAwaiter().GetResult();if(frame==null)return false;sequence=frame.Sequence;frame.CopyPixels(pixels);return true;},5000);
  bool captureOnly=args.Length>4&&args[4]=="content-capture";
  int center=(180*640+320)*4;if(!captureOnly)Need(pixels[center]>150&&pixels[center+1]<100&&pixels[center+2]<100,"frame-author-geometry-missing");File.WriteAllBytes(Path.Combine(output,"frame.rgba"),pixels);
  byte originalFirstPixel=pixels[0];pixels[0]=(byte)(originalFirstPixel^255);frame.CopyPixels(pixels);Need(pixels[0]==originalFirstPixel,"verified-frame-pixels-mutable-through-copy");
  string mode=args.Length>4?args[4]:"normal";
  if(mode=="inspection"){
   var inspect=typeof(SupervisorManagerConnection).GetMethod("InspectPackage");Need(inspect!=null,"shared-restricted-package-inspection-unavailable");
   var inspected=(Task)inspect.Invoke(connection,new object[]{args[2]});Need(inspected.Wait(15000),"package-inspection-deadline");inspected.GetAwaiter().GetResult();var value=inspected.GetType().GetProperty("Result").GetValue(inspected);var valueType=value.GetType();Need((string)valueType.GetProperty("BuildId").GetValue(value)==args[3],"inspection-exact-build");
   using(var hash=System.Security.Cryptography.SHA256.Create())Need((string)valueType.GetProperty("ArchiveSha256").GetValue(value)==BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(args[2]))).Replace("-","").ToLowerInvariant(),"inspection-exact-archive");result["restricted_inspection_verified"]=true;
   string broken=Path.Combine(output,"broken.spatial");File.WriteAllText(broken,"invalid-package");bool rejected=false;try{((Task)inspect.Invoke(connection,new object[]{broken})).GetAwaiter().GetResult();}catch(InvalidOperationException error){rejected=error.Message.StartsWith("restricted-package-inspection:restricted-Validator-rejected:",StringComparison.Ordinal);}Need(rejected,"restricted-inspection-accepted-broken-package");result["broken_package_rejected"]=true;
   long beforeInspection=frame.Sequence;Until(()=>{var newer=connection.ReadVerifiedFrame(session,640,360,beforeInspection).GetAwaiter().GetResult();if(newer==null)return false;frame=newer;return true;},5000);Need(frame.Sequence>beforeInspection,"inspection-stalled-existing-worker");result["existing_worker_continues_after_inspection_failure"]=true;
   var store=new SpatialWallpaper.Content.ContentStore(Path.Combine(output,"library"),Path.Combine(output,"staging"));string staged=Guid.NewGuid().ToString("D");File.Copy(args[2],Path.Combine(output,"staging",staged+".spatial"));var import=store.GetType().GetMethod("ImportSandbox");Need(import!=null,"restricted-package-library-import-unavailable");
   var imported=(JObject)import.Invoke(store,new object[]{staged,value,"SDK runtime scene"});Need((string)imported["build_id"]==args[3]&&((JObject)store.Snapshot["applied"]).Count==0&&((JObject)store.Snapshot["works"]).Count==1,"sandbox-import-recorded-applied-prematurely");result["sandbox_import_without_apply"]=true;
   var duplicate=(JObject)import.Invoke(store,new object[]{staged,value,"same build"});Need((bool)duplicate["duplicate"]&&(string)duplicate["work_id"]==(string)imported["work_id"],"sandbox-import-created-duplicate-work");
   string changed=Guid.NewGuid().ToString("D"),changedPath=Path.Combine(output,"staging",changed+".spatial");File.Copy(args[2],changedPath);File.AppendAllText(changedPath,"changed-after-inspection");string before=store.Snapshot.ToString();bool changedRejected=false;try{import.Invoke(store,new object[]{changed,value,"changed"});}catch(System.Reflection.TargetInvocationException error){changedRejected=error.InnerException is InvalidDataException&&error.InnerException.Message=="sandbox-inspection-archive-changed";}Need(changedRejected&&store.Snapshot.ToString()==before,"changed-staging-archive-accepted-or-published");
   var reopened=new SpatialWallpaper.Content.ContentStore(Path.Combine(output,"library"),Path.Combine(output,"staging"));Need(reopened.Snapshot.ToString()==before,"sandbox-library-import-not-durable");result["sandbox_import_digest_replay_and_restart_verified"]=true;
  }
  if(mode=="cross-session"||mode=="input"){
   foreignSession=SupervisorSessionClient.Start(connection,Guid.NewGuid(),args[3],Guid.NewGuid(),2,args[2],true);Need(foreignSession.Running.Wait(30000),"foreign-frame-worker-running-deadline");foreignSession.Running.GetAwaiter().GetResult();
   journal=JObject.Parse(File.ReadAllText(Path.Combine(state,"journal/state.json")));foreignWorker=Process.GetProcessById((int)journal["active"][foreignSession.Identity.Attempt.ToString("N")]["process"]);Need(RestrictedProcess.ReadCreationFileTime(foreignWorker.Handle)>0,"foreign-frame-worker-retained");
  }
  if(captureOnly){var diagnostics=connection.Call("ReadWorkerDiagnostics",new JObject{{"host",connection.Host.ToString("N")},{"attempt",session.Identity.Attempt.ToString("N")},{"instance",session.Identity.Instance.ToString("N")},{"generation",session.Identity.Generation}}).GetAwaiter().GetResult();File.WriteAllBytes(Path.Combine(output,"worker.log"),Convert.FromBase64String((string)diagnostics["log_base64"]));result["captured_content"]=new JObject{{"width",640},{"height",360},{"sequence",frame.Sequence},{"build_id",args[3]},{"scope","actual LPAC Worker capture through production Host; no window presentation or fidelity claim"}};}
  else if(mode.StartsWith("desktop-channel",StringComparison.Ordinal)){
   var open=typeof(SupervisorManagerConnection).GetMethod("OpenDesktopInputStream");Need(open!=null,"shared-desktop-input-stream-unavailable");
   Func<CancellationToken,Task<Stream>> connect=token=>(Task<Stream>)open.Invoke(connection,new object[]{session,token});
   if(mode=="desktop-channel-races"){
    using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var cancel=new CancellationTokenSource()){
     Task<Stream> pending=null;try{InputOpenProbe=point=>{if(point=="connected"){entered.Set();Need(release.Wait(5000),"shared-desktop-connect-probe-deadline");}};pending=connect(cancel.Token);Need(entered.Wait(3000),"shared-desktop-connect-probe-not-reached");cancel.Cancel();release.Set();bool denied=false;try{using(var unexpected=pending.GetAwaiter().GetResult()){}}catch(OperationCanceledException){denied=true;}Need(denied,"shared-desktop-cancel-after-connect-returned-stream");result["cancel_after_connect_rejected"]=true;}finally{release.Set();InputOpenProbe=null;if(pending!=null)try{using(var late=pending.GetAwaiter().GetResult()){}}catch{}}
    }
   }
   using(var channel=new SpatialWallpaper.Supervision.Broker.DesktopInputChannel(connect(CancellationToken.None).GetAwaiter().GetResult())){
    var initial=channel.GetFocusConfirmation();Need(initial.Ready&&!initial.Focused&&initial.Epoch>0,"shared-desktop-channel-initial-authority");channel.Revoke();Until(()=>channel.GetFocusConfirmation().Ready,2000);Need(!channel.GetFocusConfirmation().Focused,"shared-desktop-revoke-retained-focus");channel.Dispose();Need(channel.Completion.Wait(3000),"shared-desktop-channel-not-drained");channel.Completion.GetAwaiter().GetResult();
   }
   bool previewRejected=false;try{using(var wrong=new SpatialWallpaper.Supervision.Broker.DesktopInputChannel(connection.OpenPreviewInputStream(session).GetAwaiter().GetResult())){}}catch(InvalidDataException){previewRejected=true;}Need(previewRejected,"formal-session-accepted-preview-input-source");
   bool canceled=false;using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{using(var stream=connect(cancel.Token).GetAwaiter().GetResult()){}}catch(OperationCanceledException){canceled=true;}}Need(canceled,"shared-desktop-canceled-open-returned-stream");
   if(mode=="desktop-channel-races"){
    using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
     Task<Stream> pending=null;try{InputOpenProbe=point=>{if(point=="request-written"){entered.Set();Need(release.Wait(5000),"shared-desktop-write-probe-deadline");}};pending=connect(CancellationToken.None);Need(entered.Wait(3000),"shared-desktop-write-probe-not-reached");session.Stop();release.Set();bool denied=false;try{using(var unexpected=pending.GetAwaiter().GetResult()){}}catch(InvalidOperationException){denied=true;}Need(denied,"shared-desktop-stop-after-request-returned-stream");result["stop_after_request_rejected"]=true;}finally{release.Set();InputOpenProbe=null;if(pending!=null)try{using(var late=pending.GetAwaiter().GetResult()){}}catch{}}
    }
   }
   session.Stop();bool stopped=false;try{using(var stream=connect(CancellationToken.None).GetAwaiter().GetResult()){}}catch(InvalidOperationException){stopped=true;}Need(stopped,"shared-desktop-stopped-session-opened-stream");
   result["desktop_channel"]=new JObject{{"same_formal_session",true},{"initial_focus",false},{"preview_route_rejected",previewRejected},{"canceled_open_rejected",canceled},{"stopped_session_rejected",stopped},{"scope","actual authenticated LPAC formal-session input handshake and revocation; no native desktop source, Explorer attachment, accepted clicks or presentation claim"}};
  }else
  using(var form=new Form{Text="Spatial Wallpaper SDK frame verification",ClientSize=new Size(640,360),StartPosition=FormStartPosition.CenterScreen,ShowInTaskbar=false}){
   form.Show();Application.DoEvents();
   var presenterType=typeof(FramePresentationChecks).Assembly.GetType("SpatialWallpaper.Supervision.Presentation.WindowsFramePresenter");Need(presenterType!=null,"actual-window-presenter-available");
   using(var presenter=(IDisposable)Activator.CreateInstance(presenterType,new object[]{form,mode=="cross-session"?foreignSession:session,args[3],640,360})){
    var stamp=presenterType.GetMethod("Bind").Invoke(presenter,null);
    if(mode=="application"||mode=="application-projection"||mode=="application-preview"||mode=="application-preview-input"||mode=="application-desktop-input")result["application"]=ContentApplicationChecks.Run(form,connection,args[2],args[3],output,mode=="application-projection",mode=="application-preview"||mode=="application-preview-input",mode=="application-preview-input",mode=="application-desktop-input");
    else if(mode=="input"){
     int presented=0;Func<RuntimeFrame> next=()=>{Until(()=>{Application.DoEvents();var n=connection.ReadVerifiedFrame(session,640,360,frame.Sequence).GetAwaiter().GetResult();if(n==null)return false;frame=n;return true;},5000);return frame;};
     result["presented_input"]=PresentedInputChecks.Run(form,presenter,session,foreignSession,connection,next,f=>{result["presentation"]=(JObject)ScreenFrameValidation.Present(presenterType,presenter,new object[]{stamp,f,Path.Combine(output,"input-presented-"+(++presented)+".png")});},args.Length>5&&args[5]=="no-input-adapter");
    }else{
    var regionProof=presenterType.GetMethod("TryGetPresentedRegion");Func<bool> hasRegion=()=>{var values=new object[]{Rectangle.Empty};bool valid=(bool)regionProof.Invoke(presenter,values);if(valid)Need((Rectangle)values[0]==new Rectangle(form.PointToScreen(Point.Empty),form.ClientSize),"input-region-differs-from-presented-target");return valid;};
    if(mode=="input-region"){Need(regionProof!=null,"presented-input-region-unavailable");Need(!hasRegion(),"unpresented-target-enabled-input");}
    Func<long,RuntimeFrame> revisionFrame=revision=>{var current=frame;Until(()=>{var next=connection.ReadVerifiedFrame(session,640,360,current.Sequence).GetAwaiter().GetResult();if(next!=null)current=next;return current.ProjectionRevision==revision;},8000);frame=current;return current;};
    if(mode.StartsWith("projection",StringComparison.Ordinal)){
     var original=frame;var configuration=mode=="projection"?new ProjectionConfiguration(1,640,360,.6f,.3375f,-.12f,0,-.6f):mode=="projection-fit"?new ProjectionConfiguration(1,640,360,.6f,.3375f,0,0,-.6f):new ProjectionConfiguration(1,640,360,1.7777778f,1f,0,0,-.1f);
     connection.SetProjection(session,configuration).GetAwaiter().GetResult();stamp=presenterType.GetMethod("BindProjection").Invoke(presenter,new object[]{1L});
     bool refused=false;try{ScreenFrameValidation.Present(presenterType,presenter,new object[]{stamp,original,null});}catch(System.Reflection.TargetInvocationException error){refused=error.InnerException.Message=="presenter-projection-revision";}Need(refused,"old-projection-frame-accepted-for-new-target");revisionFrame(1);
    }
    bool stopped=false,blocked=false;
    if(mode=="reentrant-stop")form.Paint+=(sender,evt)=>{session.Stop();stopped=true;};
    if(mode=="stop-blocking")form.Paint+=(sender,evt)=>{var stopping=Task.Run(()=>session.Stop());blocked=!stopping.Wait(500);stopped=!blocked;};
    try{var receipt=(JObject)ScreenFrameValidation.Present(presenterType,presenter,new object[]{stamp,frame,Path.Combine(output,"presented.png")});Need(!blocked,"presenter-stop-blocked-by-paint");Need(mode=="normal"||mode=="input-region"||mode=="inspection"||mode.StartsWith("projection",StringComparison.Ordinal),mode=="cross-session"?"presenter-accepted-foreign-session-frame":"presenter-accepted-after-paint-stop");result["presentation"]=receipt;}
    catch(System.Reflection.TargetInvocationException error){if(mode=="cross-session"&&error.InnerException is InvalidDataException&&error.InnerException.Message=="presenter-frame-identity")result["cross_session_rejected"]=true;else if(mode!="normal"&&stopped&&error.InnerException is InvalidOperationException&&error.InnerException.Message=="presenter-session-ended")result["paint_stop_rejected"]=true;else throw error.InnerException;}
    if(mode.StartsWith("projection",StringComparison.Ordinal)){
     double first=RedCenter(frame);int firstWidth=RedWidth(frame);long previous=frame.Sequence;Until(()=>{var next=connection.ReadVerifiedFrame(session,640,360,previous).GetAwaiter().GetResult();if(next==null)return false;frame=next;return true;},5000);
     var desired=presenterType.GetMethod("BindProjection").Invoke(presenter,new object[]{2L});bool stale=false;try{ScreenFrameValidation.Present(presenterType,presenter,new object[]{stamp,frame,null});}catch(System.Reflection.TargetInvocationException error){stale=error.InnerException.Message=="presenter-stale-target";}Need(stale,"superseded-projection-stamp-accepted");
     var configuration=mode=="projection"?new ProjectionConfiguration(2,640,360,.6f,.3375f,.12f,0,-.6f):mode=="projection-fit"?new ProjectionConfiguration(2,640,360,.6f,.3375f,0,0,-.6f,true):new ProjectionConfiguration(2,640,360,.017777778f,.01f,0,0,-.6f);
     connection.SetProjection(session,configuration).GetAwaiter().GetResult();revisionFrame(2);result["second_projection"]=(JObject)ScreenFrameValidation.Present(presenterType,presenter,new object[]{desired,frame,Path.Combine(output,"projection-second.png")});double second=RedCenter(frame);
     if(mode=="projection")Need(Math.Abs(first-301.21)<3&&Math.Abs(second-337.79)<3&&second-first>30,"actual-projection-eye-parallax-wrong");
     else Need(Math.Abs(first-319.5)<3&&Math.Abs(second-319.5)<3,"physical-scale-eye-normalization-wrong");
     int secondWidth=RedWidth(frame);result["red_widths"]=new JArray(firstWidth,secondWidth);
     if(mode=="projection-edge")Need(Math.Abs(firstWidth-53.83)<4&&Math.Abs(secondWidth-212.28)<4,"physical-scale-projected-width-wrong");
     if(mode=="projection-fit")Need(Math.Abs(firstWidth-109.71)<4&&Math.Abs(secondWidth-182.86)<4,"fixed-window-fit-width-wrong");
     result["red_centers"]=new JArray(first,second);bool replay=false;try{connection.SetProjection(session,configuration).GetAwaiter().GetResult();}catch(InvalidDataException){replay=true;}Need(replay,"projection-revision-replay-accepted");
     bool rollback=false;try{presenterType.GetMethod("BindProjection").Invoke(presenter,new object[]{1L});}catch(System.Reflection.TargetInvocationException error){rollback=error.InnerException.Message=="presenter-projection-rollback";}Need(rollback,"projection-target-revision-rollback-accepted");result["projection_verified"]=true;
    }
    if(mode=="normal"){
     Action<object,string> reject=(oldStamp,reason)=>{bool refused=false;try{ScreenFrameValidation.Present(presenterType,presenter,new object[]{oldStamp,frame,null});}catch(System.Reflection.TargetInvocationException error){refused=error.InnerException.Message==reason;}Need(refused,"presentation-boundary-not-rejected:"+reason);};
     reject(stamp,"presenter-frame-sequence-or-size");result["replayed_frame_rejected"]=true;
     int left=form.Left;form.Left=left+1;Application.DoEvents();reject(stamp,"presenter-stale-target");form.Left=left;Application.DoEvents();reject(stamp,"presenter-stale-target");result["moved_and_returned_target_rejected"]=true;
     var prior=presenterType.GetMethod("Bind").Invoke(presenter,null);typeof(Control).GetMethod("RecreateHandle",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(form,null);Application.DoEvents();reject(prior,"presenter-stale-target");result["recreated_handle_rejected"]=true;
     Until(()=>{var next=connection.ReadVerifiedFrame(session,640,360,frame.Sequence).GetAwaiter().GetResult();if(next==null)return false;frame=next;return true;},5000);
     var rebound=presenterType.GetMethod("Bind").Invoke(presenter,null);result["rebound_presentation"]=(JObject)ScreenFrameValidation.Present(presenterType,presenter,new object[]{rebound,frame,Path.Combine(output,"rebound.png")});
     session.Stop();form.Refresh();using(var captured=new Bitmap(640,360)){using(var graphics=Graphics.FromImage(captured))graphics.CopyFromScreen(form.PointToScreen(Point.Empty),Point.Empty,new Size(640,360));var color=captured.GetPixel(320,180);Need(!(color.R>150&&color.G<100&&color.B<100),"stopped-session-repainted-old-frame");}result["stopped_paint_rejected"]=true;
     form.Close();bool closed=false;try{presenterType.GetMethod("Bind").Invoke(presenter,null);}catch(System.Reflection.TargetInvocationException error){closed=error.InnerException.Message=="presenter-target-unavailable";}Need(closed,"destroyed-target-rebound");result["destroyed_target_rejected"]=true;
    }
    if(mode=="input-region"){
     Need(hasRegion(),"verified-presentation-missing-input-region");PaintEventHandler erase=(sender,evt)=>evt.Graphics.Clear(Color.Blue);form.Paint+=erase;form.Refresh();using(var screen=new Bitmap(1,1)){using(var graphics=Graphics.FromImage(screen))graphics.CopyFromScreen(form.PointToScreen(new Point(320,180)),Point.Empty,new Size(1,1));Need(screen.GetPixel(0,0).B>200&&screen.GetPixel(0,0).R<30,"overpaint-negative-did-not-erase-visible-frame");}Need(hasRegion(),"ordinary-repaint-revoked-live-input-region");form.Paint-=erase;
     Until(()=>{var fresh=connection.ReadVerifiedFrame(session,640,360,frame.Sequence).GetAwaiter().GetResult();if(fresh==null)return false;frame=fresh;return true;},5000);ScreenFrameValidation.Present(presenterType,presenter,new object[]{stamp,frame,null});Need(hasRegion(),"new-frame-did-not-restore-region-proof");
     presenterType.GetMethod("BindProjection").Invoke(presenter,new object[]{1L});Need(hasRegion(),"pending-eye-update-revoked-unchanged-visible-region");form.Refresh();Need(hasRegion(),"ordinary-refresh-revoked-live-input-region");
     connection.SetProjection(session,new ProjectionConfiguration(1,640,360,.6f,.3375f,0,0,-.6f)).GetAwaiter().GetResult();revisionFrame(1);var rebound=presenterType.GetMethod("BindProjection").Invoke(presenter,new object[]{1L});ScreenFrameValidation.Present(presenterType,presenter,new object[]{rebound,frame,null});Need(hasRegion(),"reverified-presentation-missing-input-region");
     form.Left++;Need(!hasRegion(),"moved-presentation-retained-input-region");session.Stop();Need(!hasRegion(),"stopped-session-retained-input-region");result["presented_input_region_verified"]=true;
    }
    }
   }
   form.Close();Application.DoEvents();
  }
  result["worker_identity"]=new JObject{{"attempt",session.Identity.Attempt.ToString("N")},{"instance",session.Identity.Instance.ToString("N")},{"generation",session.Identity.Generation},{"pid",pid},{"creation_filetime",creation}};
 }catch(Exception error){failure=error;result["primary_error"]=error.ToString();Console.Error.WriteLine(error);}
 finally{
  try{if(foreignSession!=null){foreignSession.Stop();if(!foreignSession.Completion.Wait(10000))throw new TimeoutException("foreign-frame-session-stop-deadline");foreignSession.Completion.GetAwaiter().GetResult();Need(foreignSession.ResourcesReleased&&foreignWorker.WaitForExit(5000),"foreign-frame-session-cleanup-unproven");result["foreign_session_kernel_exit_and_cleanup"]=true;}
   if(session!=null){session.Stop();if(!session.Completion.Wait(10000))throw new TimeoutException("frame-session-stop-deadline");session.Completion.GetAwaiter().GetResult();Need(session.ResourcesReleased,"frame-session-resources-unproven");result["session_resources_released"]=true;}
   if(worker!=null){Need(worker.WaitForExit(5000),"frame-worker-kernel-exit-unproven");result["retained_worker_kernel_exited"]=true;}
   if(connection!=null){Need((bool?)connection.Call("Exit",new JObject()).GetAwaiter().GetResult()["ok"]==true,"frame-host-exit-ack");}
   if(host!=null){Need(host.WaitForExit(10000)&&host.ExitCode==0,"frame-host-kernel-exit-unproven");result["retained_host_kernel_exited"]=true;}
  }catch(Exception cleanup){result["cleanup_error"]=cleanup.ToString();Console.Error.WriteLine(cleanup);if(failure==null)failure=cleanup;}
  finally{foreignWorker?.Dispose();worker?.Dispose();host?.Dispose();File.WriteAllText(Path.Combine(args[0],"result.json"),result.ToString());}
 }
 return failure==null?0:1;
 }
}
