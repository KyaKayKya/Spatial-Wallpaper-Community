using System;
using System.IO;
using SpatialWallpaper.Blueprint;
class WorkspaceChecks {
 static void Require(bool v,string name){if(!v)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static int Main(){string root=Path.Combine(Path.GetTempPath(),"blueprint-check-"+Guid.NewGuid().ToString("N"));try{
  var store=new BlueprintWorkspace(root);var source=new byte[]{1,2,3};string hash=new string('a',64);var settings=new BlueprintSettings();var first=store.Create("木屋",hash,source,settings);
  Require(store.List().Length==1,"create-work");Require(File.ReadAllBytes(store.SourcePath(first.SourceHash)).Length==3,"source-preserved");
  settings.Yaw=25;var saved=store.Save(first.WorkId,first.RevisionId,settings);Require(store.Revision(first.WorkId,first.RevisionId).Settings.Yaw==0,"immutable-old-revision");Require(store.Revision(saved.WorkId,saved.RevisionId).Settings.Yaw==25,"new-revision-persists");
  bool conflict=false;try{store.Save(first.WorkId,first.RevisionId,settings);}catch(BlueprintImportException){conflict=true;}Require(conflict,"stale-revision-conflict");
  var copy=store.SaveAs(saved,"另一个构图",settings);Require(copy.WorkId!=saved.WorkId&&copy.SourceHash==saved.SourceHash,"save-as-shares-source");Require(Directory.GetFiles(Path.Combine(root,"sources")).Length==1,"deduplicated-source-storage");
  var reopened=new BlueprintWorkspace(root);Require(reopened.List().Length==2&&reopened.Revision(saved.WorkId,saved.RevisionId).Settings.Yaw==25,"reopen-restores-works");
  bool bad=false;try{store.SourcePath("../escape");}catch(BlueprintImportException){bad=true;}Require(bad,"source-path-is-bounded");Console.WriteLine("Blueprint workspace: 9 passed");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}finally{if(Directory.Exists(root))Directory.Delete(root,true);}}
}
