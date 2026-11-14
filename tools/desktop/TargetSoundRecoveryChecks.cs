using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Content;
using SpatialWallpaper.Library;
using SpatialWallpaper.Sdk.Capabilities;
static class TargetSoundRecoveryChecks {
 static int checks;
 static void Yes(bool value,string label){if(!value)throw new Exception(label);checks++;}
 static void No(Action action,string label){try{action();}catch(Exception e)when(e is InvalidDataException||e is ArgumentException||e is IOException){checks++;return;}throw new Exception("accepted:"+label);}
 static CalibrationProfile Profile(string display){
  var source=JObject.FromObject(new{camera_id="test",head_model_id="head",estimator_id="estimator",intrinsics_id="K",geometry_id="G",actual_mode=new{mode_id=(object)null,source_width_px=640,source_height_px=480,width_px=640,height_px=480,mirrored=false,rotation_deg=0},optional_features=new[]{BinocularObservation.Feature}});
  var p=new CalibrationProfile{revision=7};p.manual.Bind(source,display);return p;
 }
 static int Main(string[] args){try{return Run(args);}catch(Exception error){Console.Error.WriteLine("after-checks="+checks+" "+error.GetType().FullName+":"+error.Message);if(error.InnerException!=null)Console.Error.WriteLine(error.InnerException.GetType().FullName+":"+error.InnerException.Message);return 1;}}
 static int Run(string[] args){
  Directory.CreateDirectory(args[0]);string file=Path.Combine(args[0],"applied.json");
  var left=new DesktopAppliedSelection("showcase",ArtworkSource.BuiltinNative,null,null,Profile("left"),new SoundMix(.2f,.4f,false,true));
  string work=Guid.NewGuid().ToString("D"),revision=Guid.NewGuid().ToString("D"),build=new string('a',64);var right=new DesktopAppliedSelection(work,ArtworkSource.SdkNative,revision,build,Profile("right"),new SoundMix(.7f,.8f,true,false),true);
  var state=new DesktopAppliedState("settings-only",new[]{left,right});Yes(state.Targets.Count==2&&state.SelectedDisplayId=="settings-only","UI selection independent of applied targets");
  var parsed=DesktopAppliedState.Parse(state.Json);var exact=parsed.Targets["right"];Yes(exact.ArtworkId==work&&exact.RevisionId==revision&&exact.BuildId==build&&exact.Source==ArtworkSource.SdkNative&&exact.Paused,"exact SDK identity/revision/source and target pause preserved");Yes(exact.Sound.Music==.7f&&exact.Sound.MusicMuted&&!exact.Sound.EffectsMuted&&parsed.Targets["left"].Sound.EffectsMuted,"per-work sound/mute snapshots stay independent");
  var mutable=exact.Profile;mutable.manual.scaleX=20;Yes(exact.Profile.manual.scaleX!=20,"applied calibration snapshot immutable");
  No(()=>new DesktopAppliedState(null,new[]{left,left}),"duplicate display rejected");No(()=>new DesktopAppliedState(null,new[]{left,right,new DesktopAppliedSelection("pool",ArtworkSource.BuiltinNative,null,null,Profile("third"),new SoundMix())}),"target cap enforced");
  No(()=>new DesktopAppliedSelection(work,ArtworkSource.SdkNative,revision,null,Profile("right"),new SoundMix()),"native exact build required");
  No(()=>new DesktopAppliedSelection(work,ArtworkSource.BuiltinNative,null,null,Profile("right"),new SoundMix()),"SDK identity cannot bypass exact revision as builtin");No(()=>new DesktopAppliedSelection(work,ArtworkSource.SdkNative,revision,new string('A',64),Profile("right"),new SoundMix()),"build identity remains canonical");
  var malformed=state.Json;malformed["targets"][0]["source"]="0";No(()=>DesktopAppliedState.Parse(malformed),"numeric source alias rejected");malformed=state.Json;malformed["targets"][0]["preset_revision"]=8;No(()=>DesktopAppliedState.Parse(malformed),"preset snapshot mismatch rejected");
  var changed=parsed.Without("left");Yes(changed.Targets.Count==1&&parsed.Targets.Count==2&&changed.Targets.ContainsKey("right"),"single target removal leaves peer intact and previous state immutable");
  var pausedAll=parsed.WithPause(null,true);var resumeOne=pausedAll.WithPause("left",false);Yes(pausedAll.AllPaused&&!resumeOne.AllPaused&&!resumeOne.Targets["left"].Paused&&resumeOne.Targets["right"].Paused,"global pause then single target resume keeps peer paused and releases aggregate manual policy");No(()=>parsed.WithPause("missing",false),"pause cannot create phantom target");
  var single=new DesktopAppliedState("left",new[]{left});single.RequireRouting("right",true,s=>s.ArtworkId=="showcase");Yes(single.Targets.Count==1,"native peers allowed without mutating applied state");
  No(()=>single.RequireRouting("right",false,s=>true),"offline retained native peer still blocks legacy second target");
  var oldPeer=new DesktopAppliedSelection(work,ArtworkSource.SdkLegacy,revision,build,Profile("left"),new SoundMix());var oldState=new DesktopAppliedState("left",new[]{oldPeer});
  No(()=>oldState.RequireRouting("right",false,s=>false),"legacy to other legacy target rejected before global scene close");No(()=>oldState.RequireRouting("right",true,s=>false),"legacy peer blocks competing native target even without live handle");oldState.RequireRouting("left",true,s=>false);Yes(oldState.Targets["left"].RevisionId==revision,"same target source switch preserves prior exact selection until commit");
  var store=new DesktopAppliedStore(file);Yes(store.Snapshot.Targets.Count==0,"new store starts explicitly empty");store.Save(state);var loaded=new DesktopAppliedStore(file);Yes(JToken.DeepEquals(loaded.Snapshot.Json,state.Json),"format2 atomic file roundtrip");store.Save(changed);Yes(File.Exists(file+".bak")&&new DesktopAppliedStore(file).Snapshot.Targets.Count==1,"write retains prior complete backup");
  byte[] before=File.ReadAllBytes(file);using(var locked=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)){No(()=>store.Save(state),"replace failure propagates");}Yes(File.ReadAllBytes(file).SequenceEqual(before)&&store.Snapshot.Targets.Count==1&&!Directory.EnumerateFiles(args[0],"*.tmp").Any(),"failed save leaves old state/file intact and removes own temp");
  File.WriteAllText(file,"{corrupt");var recovered=new DesktopAppliedStore(file);Yes(recovered.RecoveredFromBackup&&recovered.Snapshot.Targets.Count==2,"corrupt primary recovers previous complete backup without silently clearing");recovered.Save(changed);Yes(new DesktopAppliedStore(file).Snapshot.Targets.Count==1&&Directory.EnumerateFiles(args[0],"applied.json.corrupt.*").Any(),"recovered publish preserves bad primary evidence and healthy backup");
  string old=Path.Combine(args[0],"format1.json");var legacy=new JObject{{"format",1},{"selection",new JObject{{"artwork_id","showcase"},{"display_id","left"},{"preset_id",left.Profile.id},{"preset_revision",left.Profile.revision},{"profile",JObject.FromObject(left.Profile)},{"content_revision",null}}}};File.WriteAllText(old,legacy.ToString());byte[] original=File.ReadAllBytes(old);
  var migrate=new DesktopAppliedStore(old);Yes(migrate.NeedsMigration&&migrate.Snapshot.Targets.Count==1&&File.ReadAllBytes(old).SequenceEqual(original),"format1 read is non-mutating");migrate.Save(migrate.Snapshot);Yes(File.ReadAllBytes(old+".format1.bak").SequenceEqual(original)&&!new DesktopAppliedStore(old).NeedsMigration,"format1 original preserved before atomic upgrade");
  legacy["selection"]["artwork_id"]=work;legacy["selection"]["content_revision"]=revision;File.WriteAllText(old,legacy.ToString());No(()=>new DesktopAppliedStore(old),"legacy SDK migration cannot guess exact build");var sdk=new DesktopAppliedStore(old,(w,r)=>w==work&&r==revision?build:null,w=>new SoundMix(.3f,.9f));Yes(sdk.Snapshot.Targets["left"].Source==ArtworkSource.SdkLegacy&&sdk.Snapshot.Targets["left"].BuildId==build&&sdk.Snapshot.Targets["left"].Sound.Music==.3f,"legacy revision and sound resolved without applying latest");
  No(()=>new DesktopAppliedStore(old,(w,r)=>throw new IOException("missing exact local revision")),"revision resolver IO failure cannot fallback to another backup selection");No(()=>new DesktopAppliedStore(old,(w,r)=>build,w=>throw new InvalidDataException("missing exact sound")),"sound resolver failure cannot fallback to another backup selection");
  var invalidLegacy=(JObject)legacy.DeepClone();invalidLegacy["selection"]["profile"]["schema_version"]=1;invalidLegacy["selection"]["profile"]["manual"]=null;File.WriteAllText(old,invalidLegacy.ToString());var oldRecovery=new DesktopAppliedStore(old);Yes(oldRecovery.RecoveredFromBackup&&oldRecovery.Snapshot.Targets["left"].ArtworkId=="showcase","invalid old schema/null manual recovers complete backup without null reference");
  string bad=Path.Combine(args[0],"both-bad.json");File.WriteAllText(bad,"{bad");File.WriteAllText(bad+".bak","{bad");No(()=>new DesktopAppliedStore(bad),"two corrupt files never become an empty applied table");
  var sameLeft=new DesktopAppliedSelection(work,ArtworkSource.SdkNative,revision,build,Profile("left"),new SoundMix(.2f,.3f));
  var sameState=new DesktopAppliedState("right",new[]{sameLeft,right});var scoped=sameState.WithSound(work,"left",new SoundMix(.1f,.4f,true,true));
  Yes(scoped.Targets["left"].Sound.MusicMuted&&scoped.Targets["right"].Sound.Music==.7f&&!scoped.Targets["right"].Sound.EffectsMuted,"target mute cannot change same-work peer");
  Yes(sameState.Targets["left"].Sound.Music==.2f&&scoped.SelectedDisplayId=="right","target change preserves prior immutable table and UI selection");
  var global=scoped.WithSound(work,null,new SoundMix(.6f,.5f));Yes(global.Targets.Values.All(s=>s.Sound.Music==.6f&&!s.Sound.MusicMuted),"old work-only call still applies globally");
  No(()=>sameState.WithSound(work,"bad\n",new SoundMix()),"control target rejected");
  string targetPath=Path.Combine(args[0],"target-mixes.json");var targets=new ContentTargetSoundStore(targetPath);targets.Set(work,"left",scoped.Targets["left"].Sound);targets.Set(work,"right",right.Sound);
  var reopened=new ContentTargetSoundStore(targetPath);Yes(reopened.Find(work,"left").MusicMuted&&!reopened.Find(work,"right").EffectsMuted,"target mute persistence survives actual file reopen");
  Yes(reopened.Find(work,"missing")==null,"unconfigured target uses caller work default");Yes(reopened.Page(new[]{work}).Count==1&&reopened.Page(new[]{Guid.NewGuid().ToString("D")}).Count==0,"pagination exposes only selected works");
  var checkpoint=reopened.Snapshot;reopened.SetGlobal(work,new SoundMix(.9f,.8f));Yes(reopened.Find(work,"left").Music==.9f&&reopened.Find(work,"right").Music==.9f,"global compatibility updates remembered targets");reopened.Restore(checkpoint);Yes(reopened.Find(work,"left").MusicMuted,"rollback restores exact target preferences");
  byte[] targetBefore=File.ReadAllBytes(targetPath);using(var locked=new FileStream(targetPath,FileMode.Open,FileAccess.Read,FileShare.Read)){No(()=>reopened.Set(work,"left",new SoundMix()),"failed target replace propagates");}Yes(File.ReadAllBytes(targetPath).SequenceEqual(targetBefore)&&reopened.Find(work,"left").MusicMuted,"failed target write leaves bytes and snapshot intact");
  No(()=>reopened.Set(work,"\n",new SoundMix()),"invalid display rejected before IO");No(()=>reopened.Set("../escape","left",new SoundMix()),"invalid work cannot become path");
  string offline=Path.Combine(args[0],"offline-legacy.json");var offlineStore=new DesktopAppliedStore(offline);offlineStore.Save(oldState.WithSound(work,"left",new SoundMix(.13f,.27f,true,false)));var offlineBytes=File.ReadAllBytes(offline);var offlineRestart=new DesktopAppliedStore(offline);
  Yes(offlineRestart.Snapshot.Targets["left"].RevisionId==revision&&offlineRestart.Snapshot.Targets["left"].BuildId==build&&offlineRestart.Snapshot.Targets["left"].Profile.revision==7&&offlineRestart.Snapshot.Targets["left"].Sound.Music==.13f&&File.ReadAllBytes(offline).SequenceEqual(offlineBytes),"offline record restart retains exact legacy revision calibration and sound without applying latest");
  Yes(!DesktopPolicy.UseLegacyContent(true,false)&&!DesktopPolicy.UseLegacyContent(true,true),"native pause and resume never enter legacy content loader");Yes(!DesktopPolicy.UseLegacyContent(false,true)&&DesktopPolicy.UseLegacyContent(false,false),"offline target suppresses presentation until connected");
  var pausePolicy=new DesktopPolicy{Locked=true};Yes(!pausePolicy.CaptureNeeded(true,false,true),"locked native capture stops");pausePolicy.Locked=false;Yes(pausePolicy.CaptureNeeded(true,false,true),"unlocked native policy permits tracker resume");
  reopened.Remove(work);Yes(new ContentTargetSoundStore(targetPath).Find(work,"left")==null,"uninstall clears only owned target preferences");
  Console.WriteLine("target sound recovery checks passed: "+checks);return 0;
 }
}
