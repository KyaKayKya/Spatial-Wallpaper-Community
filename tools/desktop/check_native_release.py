"""Bounded five-package smoke against one complete, frozen Windows release.

No DesktopRuntime, tray, Tracker, desktop attachment, screenshots, raw input or
input injection. Script input stays absent/unfocused. Real gameplay, sound and
physical display experience remain manual acceptance items.

Developer dependencies: Git, Python 3.10+, an explicitly selected C# compiler
and .NET Framework 4.8 reference assemblies. Runtime dependencies and tested
executables come exclusively from --release, never adjacent development caches.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
SCENES = {"showcase", "parking", "train", "rainycafe", "minishotgame"}
SOURCE_FILES = (
    "Prototype/Assets/Desktop/Windows/NativeRenderProcess.cs",
    "Prototype/Assets/Desktop/Windows/WindowsDesktopWindow.cs",
    "Prototype/Assets/Desktop/Core/DesktopDisplay.cs",
    "Prototype/Assets/ContentRuntime/Core/ContentStore.cs",
    "Prototype/Assets/ContentRuntime/Core/ContentStore.Native.cs",
    "Prototype/Assets/ContentRuntime/Core/SandboxContentBuild.cs",
    "Prototype/Assets/ContentRuntime/Core/ContentArguments.cs",
    "Prototype/Assets/ContentRuntime/Runtime/NativeContentConfiguration.cs",
)

HARNESS = r'''
using System;using System.IO;using System.Linq;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;
using Newtonsoft.Json.Linq;using SpatialWallpaper.Content;using SpatialWallpaper.Desktop;using SpatialWallpaper.Sdk.Native;

// Supply only the final release directory and its actual platform index to the
// real, frozen configuration builder. No Unity object/render API is faked.
namespace UnityEngine {
 public static class Application {public static string dataPath;}
 public sealed class TextAsset {public string text;public static bool operator !(TextAsset value)=>value==null;}
 public static class Resources {
  public static string Platform;
  public static T Load<T>(string name) where T:class {if(name!="SpatialWallpaper/NativePlatformIndex")throw new InvalidOperationException("unexpected-smoke-resource");return new TextAsset{text=File.ReadAllText(Platform)} as T;}
 }
}
namespace SpatialWallpaper.Content {
 public static class NativeBrokerLease {
  public static Task<string> EnsureAsync(string path){throw new InvalidOperationException("smoke-input-collector-disabled");}
 }
}
static class ReleaseSmoke {
 static int checks;
 static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);checks++;}
 static JObject Read(string path)=>JObject.Parse(File.ReadAllText(path));
 static string Describe(Exception error){var lines=new System.Collections.Generic.List<string>();for(int i=0;error!=null&&i<12;i++,error=error.InnerException){string message;try{message=error.Message;}catch(Exception formatting){message="message-unavailable:"+formatting.GetType().FullName;}lines.Add(error.GetType().FullName+":"+message);}return string.Join("\n",lines);}
 static JObject Observe(NativeRenderProcess renderer,Func<JObject,bool> condition,int milliseconds,string why){
  var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<milliseconds){
   if(renderer.Error!=null)throw new InvalidOperationException(why+":"+renderer.Error);
   if(renderer.Completion.IsCompleted){renderer.Completion.GetAwaiter().GetResult();throw new InvalidOperationException(why+":renderer-ended");}
   var state=renderer.State;
   if(state["script_error"]?.Type==JTokenType.String||state["input_error"]?.Type==JTokenType.String)throw new InvalidOperationException(why+":"+state.ToString());
   if(condition(state))return state;Thread.Sleep(15);
  }throw new TimeoutException(why);
 }
 static int Main(string[] args){
  if(Console.ReadLine()!="RUN")return 2;
  string release=Path.GetFullPath(args[0]),data=Path.GetFullPath(args[1]),result=args[2];int deadline=int.Parse(args[3]);
  var report=new JObject{{"passed",false},{"scope","real final five packages admitted/imported and exact release native render role; no input/gameplay/visual/audio/multidisplay claim"},{"cases",new JArray()}};
  try{
   UnityEngine.Application.dataPath=Path.Combine(release,"SpatialWallpaper_Data");UnityEngine.Resources.Platform=Path.Combine(release,"native-runtime","native-platform-library.json");
   var manifest=Read(Path.Combine(release,"release-manifest.json"));var platform=new NativePlatformCatalog(File.ReadAllText(UnityEngine.Resources.Platform));
   string staging=Path.Combine(data,"staging");Directory.CreateDirectory(staging);var store=new ContentStore(Path.Combine(data,"library"),staging);
   foreach(JObject entry in (JArray)manifest["packages"]){
    string file=Path.Combine(release,"native-content",(string)entry["file"]);var package=NativePackage.Validate(file);
    string staged=Guid.NewGuid().ToString("D");File.Copy(file,Path.Combine(staging,staged+".spatial"));
    var imported=store.ImportNative(staged,platform);string work=(string)imported["work_id"],revision=(string)imported["revision_id"];
    Need(work==package.WorkId&&work==(string)entry["work_id"],"stable-work-GUID-not-preserved");
    var installed=store.OpenNativeBuild(work,revision,platform);Need(installed.BuildId==package.BuildId&&package.BuildId==(string)entry["build_id"],"installed-native-build-mismatch");
    var pose=new JObject{{"display_id","release-smoke-no-desktop-target"},{"width",.53},{"height",.298125},{"keep_height",true},{"eye",new JArray(0,0,-.67)},{"fresh",false},{"paused",false},{"sound",new JObject{{"music",0},{"effects",0},{"music_muted",true},{"effects_muted",true}}}};
    var config=new JObject{{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"desktop",false},{"diagnostics",true},{"pose",pose}};
    // Use the actual frozen management contract; this does not call its separate
    // PrepareInputAsync path and therefore cannot install a collector/hook.
    NativeContentConfiguration.Configure(config,store,work,revision,data,true);
    Need(config["input"]==null,"unexpected-input-collector");
    var item=new JObject{{"scene",package.SceneId},{"work_id",work},{"revision_id",revision},{"build_id",installed.BuildId},{"store_imported",true},{"preview_storage",true},{"input","absent-unfocused-no-collector"}};
    ((JArray)report["cases"]).Add(item);NativeRenderProcess renderer=null;
    try{
     renderer=new NativeRenderProcess(Path.Combine(release,"SpatialWallpaper.exe"),Path.Combine(data,"renders",package.SceneId),config);
     Need(renderer.Ready.Wait(deadline),"native-ready-deadline");var surface=renderer.Ready.GetAwaiter().GetResult();
     var first=Observe(renderer,s=>(bool?)s["ready"]==true&&(bool?)s["script_running"]==true&&(long?)s["script_batches"]>=2&&(int?)s["rendered_frame"]>=1,deadline,"native-first-render-and-ack");
     Need((int)first["pid"]==surface.ProcessId&&(long)first["creation_filetime"]==surface.CreationFileTime,"native-surface-kernel-identity");
     Need((int)first["render_errors"]==0&&(string)first["artwork"]==package.SceneId,"native-scene-render-error");
     Need((int)first["script_pid"]>0&&(long)first["script_creation"]>0,"pure-script-kernel-identity-missing");
     Need((int?)first["input_pid"]==0&&((JArray)first["input_helpers"]).Count==0,"unexpected-input-process");
     long batches=(long)first["script_batches"],frame=(long)first["frame"],rendered=(long)first["rendered_frame"];
     var moved=(JObject)pose.DeepClone();moved["eye"]=new JArray(.015,-.008,-.69);renderer.PublishPose(moved);
     var after=Observe(renderer,s=>(bool?)s["ready"]==true&&(long?)s["script_batches"]>=batches+4&&(long?)s["frame"]>=frame+4&&(long?)s["rendered_frame"]>rendered&&s["eye"] is JArray eye&&Math.Abs((double)eye[0]-.015)<.0001&&Math.Abs((double)eye[1]+.008)<.0001&&Math.Abs((double)eye[2]+.69)<.0001,deadline,"native-view-ack-and-render-progress");
     Need((int)after["render_errors"]==0&&(bool?)after["script_running"]==true&&after["script_error"]?.Type==JTokenType.Null,"native-script-not-healthy");
     item["first"]=first;item["after"]=after;item["actual_ack_growth"]=(long)after["script_batches"]-batches;item["actual_rendered_frame_growth"]=(long)after["rendered_frame"]-rendered;
     item["view_scope"]="actual projection eye changed and real script ACKs continued; source publishes initial/current view; author click/hit use is not exercised";
    }finally{
     if(renderer!=null){renderer.Stop();Need(renderer.Completion.Wait(20000),"native-renderer-close-deadline");renderer.Completion.GetAwaiter().GetResult();Need(renderer.ResourcesReleased&&renderer.CleanupError==null,"native-renderer-cleanup-unproven");renderer.Dispose();}
    }
    item["cleanly_closed"]=true;File.WriteAllText(result,report.ToString());Console.WriteLine("native smoke passed: "+package.SceneId);
   }
   var reopened=new ContentStore(Path.Combine(data,"library"),staging);Need(JToken.DeepEquals(reopened.Snapshot,store.Snapshot),"native-store-reopen-differs");Need(((JObject)store.Snapshot["applied"]).Count==0,"smoke-applied-to-desktop");
   report["passed"]=true;report["checks"]=checks;report["durable_store_reopen"]=true;return 0;
  }catch(Exception error){string description=Describe(error);report["error"]=description;Console.Error.WriteLine(description);return 1;}
  finally{File.WriteAllText(result,report.ToString());}
 }
}
'''


def require(ok, message):
    if not ok:
        raise ValueError(message)


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def read(path):
    def unique(pairs):
        value = {}
        for key, item in pairs:
            require(key not in value, "duplicate JSON key")
            value[key] = item
        return value
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=unique)


def relative(value):
    require(isinstance(value, str) and value and "\\" not in value and ":" not in value,
            "invalid release relative path")
    path = PurePosixPath(value)
    require(not path.is_absolute() and all(p not in {"", ".", ".."} for p in value.split("/")),
            "non-canonical release relative path")
    require(all(ord(c) >= 32 and c not in '<>"|?*' for c in value), "invalid character in release path")
    return path


def no_links(path):
    for ancestor in (path, *path.parents):
        if ancestor.exists():
            require(not ancestor.is_symlink() and not getattr(ancestor.lstat(), "st_file_attributes", 0) & 0x400,
                    "release/output link or reparse point")


def verify_release(release):
    no_links(release)
    manifest_path = release / "release-manifest.json"
    manifest = read(manifest_path)
    require(manifest.get("format") == "spatial-wallpaper-portable-release", "wrong release manifest format")
    require(manifest.get("version") == "0.4.0-preview.1", "wrong native release version")
    commit = manifest.get("source_commit")
    require(isinstance(commit, str) and re.fullmatch(r"[0-9a-f]{40}", commit), "frozen source commit missing")
    native = manifest["native"]
    require(native["sdk"] == manifest["version"] and native["schema"] == 5 and native["scene_command_abi"] == 1,
            "native release identity differs")
    inventory = manifest["files"]
    require(isinstance(inventory, dict) and inventory, "release file inventory missing")
    actual = set()
    for path in release.rglob("*"):
        no_links(path)
        if path.is_file():
            actual.add(path.relative_to(release).as_posix())
    require(actual == set(inventory) | {"release-manifest.json", "SHA256SUMS.txt"},
            "release contains unlisted or missing files")
    verified = {}
    last_progress = time.monotonic()
    for name, expected in inventory.items():
        target = release.joinpath(*relative(name).parts)
        require(isinstance(expected, str) and re.fullmatch(r"[0-9a-f]{64}", expected), "invalid release file digest")
        require(target.is_file() and digest(target) == expected, "release file digest differs: " + name)
        verified[name] = expected
        if time.monotonic() - last_progress > 10:
            print("verified release files:", len(verified), flush=True)
            last_progress = time.monotonic()
    lines = (release / "SHA256SUMS.txt").read_text(encoding="utf-8-sig").splitlines()
    sums = {}
    for line in lines:
        value, name = line.split("  ", 1)
        relative(name)
        require(name not in sums, "duplicate checksum path")
        sums[name] = value
    require(sums == dict(verified, **{"release-manifest.json": digest(manifest_path)}),
            "SHA256SUMS does not match the release inventory and manifest")
    required = {"SpatialWallpaper.exe", "native-runtime/native-runtime.json",
                "native-runtime/native-platform-library.json", "native-runtime/worker/ScriptWorker.exe",
                "native-runtime/supervisor/Supervisor.exe", "Authoring/NativeValidator/NativeValidator.exe",
                "native-content/catalog.json", "SpatialWallpaper_Data/Managed/Assembly-CSharp.dll"}
    require(required <= set(verified), "required final runtime/validator/catalog files missing")
    packages = manifest["packages"]
    require(isinstance(packages, list) and len(packages) == 5, "exactly five release packages required")
    require({p["cover_id"] for p in packages} == SCENES and len({p["work_id"] for p in packages}) == 5,
            "wrong native scene set or duplicate stable work GUID")
    require(read(release / "native-content/catalog.json") == packages, "native content catalog differs from release manifest")
    for package in packages:
        name = package["file"]
        require(relative(name).name == name and name.endswith(".spatial"), "package path is not a basename")
        require(verified.get("native-content/" + name) == package["sha256"], "package not bound to release inventory")
    return manifest, verified


def source_bytes(root, commit, name):
    return subprocess.check_output(["git", "-C", str(root), "show", commit + ":" + name], stderr=subprocess.PIPE)


def compile_harness(args, manifest, output):
    source = output / "frozen-sources"
    files = []
    inventory = {}
    paths = subprocess.check_output(["git", "-C", str(args.source_root), "ls-tree", "-r", "--name-only", manifest["source_commit"]], text=True).splitlines()
    names = list(SOURCE_FILES)
    # New package wrappers belong to the same frozen product contract; never
    # substitute an old working-tree implementation to make compilation pass.
    names += [p for p in paths if p.endswith("/NativeInstalledPackage.cs")]
    for name in dict.fromkeys(names):
        data = source_bytes(args.source_root, manifest["source_commit"], name)
        target = source.joinpath(*relative(name).parts)
        target.parent.mkdir(parents=True, exist_ok=True)
        # The two production executables each define their own catalog class.
        # Keep validator package types global and alias only Supervisor's three
        # hosting types; prepend aliases without changing any product behavior.
        aliases = ("extern alias SupervisorHost;\n"
                   "using PinnedProcessOwner=SupervisorHost::SpatialWallpaper.Sdk.Hosting.PinnedProcessOwner;\n"
                   "using NativeScriptControl=SupervisorHost::SpatialWallpaper.Sdk.Hosting.NativeScriptControl;\n"
                   "using RuntimePackageInspection=SupervisorHost::SpatialWallpaper.Sdk.Hosting.RuntimePackageInspection;\n")
        target.write_bytes(aliases.encode("utf-8") + data.removeprefix(b"\xef\xbb\xbf"))
        files.append(target)
        inventory[name] = hashlib.sha256(data).hexdigest()
    harness = output / "ReleaseSmoke.cs"
    harness.write_text(HARNESS, encoding="utf-8")
    files.append(harness)
    supervisor = args.release / "native-runtime/supervisor/Supervisor.exe"
    validator = args.release / "Authoring/NativeValidator/NativeValidator.exe"
    # Keep assembly loading local to the owned test directory. These are small
    # executables, not a copy of the Unity/runtime build or any assets/cache.
    for origin in (supervisor, validator, supervisor.parent / "Newtonsoft.Json.dll"):
        shutil.copyfile(origin, output / origin.name)
    require(digest(supervisor.parent / "Newtonsoft.Json.dll") == digest(validator.parent / "Newtonsoft.Json.dll"),
            "final component JSON dependencies differ")
    shutil.copyfile(supervisor.with_suffix(".exe.config"), output / "ReleaseSmoke.exe.config")
    framework = args.framework
    required = ("mscorlib.dll", "System.dll", "System.Core.dll", "Facades/netstandard.dll",
                "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll")
    references = [framework / name for name in required]
    require(all(p.is_file() for p in references), ".NET Framework 4.8 reference assemblies unavailable")
    command = [str(args.compiler_runtime), str(args.compiler), "-nologo", "-langversion:latest", "-platform:x64", "-nostdlib+",
               *("-r:" + str(p) for p in references), "-r:" + str(output / "Newtonsoft.Json.dll"),
               "-r:SupervisorHost=" + str(output / "Supervisor.exe"), "-r:" + str(output / "NativeValidator.exe"),
               "-out:" + str(output / "ReleaseSmoke.exe"), *map(str, files)]
    result = subprocess.run(command, capture_output=True, timeout=60)
    (output / "compile.log").write_bytes(result.stdout + result.stderr)
    result.check_returncode()
    return {"command": command, "source_sha256": inventory,
            "compiler_sha256": digest(args.compiler), "compiler_runtime_sha256": digest(args.compiler_runtime),
            "framework_sha256": {p.name: digest(p) for p in references}, "exit_code": result.returncode}


def run_owned(executable, arguments, output, timeout):
    sys.path.insert(0, str(ROOT / "tools/sdk"))
    from case_process import Job
    job = Job()
    process = None
    evidence = {"assigned_before_release": False, "forced_termination": False}
    failure = None
    try:
        with (output / "harness.log").open("wb") as log:
            process = subprocess.Popen([str(executable), *map(str, arguments)], stdin=subprocess.PIPE,
                                       stdout=log, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW)
            job.assign(process)
            evidence["assigned_before_release"] = True
            evidence["root_pid"] = process.pid
            process.stdin.write(b"RUN\n")
            process.stdin.flush()
            process.stdin.close()
            process.wait(timeout=timeout)
            evidence["exit_code"] = process.returncode
    except BaseException as error:
        failure = error
    finally:
        try:
            active = job.active()
            grace = time.monotonic() + .5
            while active and process is not None and process.poll() is not None and time.monotonic() < grace:
                time.sleep(.01)
                active = job.active()
            evidence["active_before_fallback"] = active
            if active:
                evidence["remaining_members"] = job.members()
                evidence["forced_termination"] = True
                job.terminate()
            deadline = time.monotonic() + 5
            while job.active() and time.monotonic() < deadline:
                time.sleep(.01)
            evidence["active_after_cleanup"] = job.active()
            job.wait_members()
        finally:
            job.close()
            if process is not None:
                # Assignment failure can leave only the RUN-gated root outside
                # our Job. Kill its retained process handle, never a PID search.
                if not evidence["assigned_before_release"] and process.poll() is None:
                    process.kill()
                evidence["root_exit_code"] = process.wait(timeout=5)
            (output / "job-cleanup.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    if failure is not None:
        raise failure
    require(evidence.get("exit_code") == 0 and not evidence["forced_termination"]
            and evidence["active_after_cleanup"] == 0, "harness failed or owned Job required fallback cleanup")
    return evidence


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release", type=Path, required=True, help="Final portable release directory containing release-manifest.json.")
    parser.add_argument("--output", type=Path, required=True, help="New task Validation directory outside the release.")
    parser.add_argument("--source-root", type=Path, default=ROOT, help="Git object source; only release-manifest.source_commit is read.")
    parser.add_argument("--compiler-runtime", type=Path, required=True, help="Developer compiler runtime, for example the explicit mono.exe path.")
    parser.add_argument("--compiler", type=Path, required=True, help="Developer csc.exe path; no compiler is required by runtime users.")
    parser.add_argument("--framework", type=Path, default=Path(os.environ.get("ProgramFiles(x86)", "C:/Program Files (x86)")) / "Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8")
    parser.add_argument("--startup-seconds", type=int, choices=range(15, 46), default=35, metavar="15..45")
    parser.add_argument("--structure-only", action="store_true", help="Verify the release inventory without compiling or starting any process; never counts as a native smoke pass.")
    args = parser.parse_args()
    no_links(args.release.absolute())
    args.release = args.release.resolve()
    output = args.output.absolute()
    no_links(output.parent)
    require(not output.exists(), "output directory already exists; never overwrite test/user state")
    require(not output.resolve().is_relative_to(args.release), "test output must not be inside the release")
    require(len(str(output / "data/library/builds" / (("f" * 64) + ".spatial." + ("0" * 32) + ".tmp"))) < 260,
            "output path is too long for the real .NET Framework store; use a short new Validation/nr1 directory")
    output.mkdir(parents=True, exist_ok=False)
    report = {"passed": False, "scope": __doc__, "release": str(args.release), "tool_sha256": digest(Path(__file__)),
              "not_verified": ["MiniShot aiming/hit feedback and gameplay saves", "visible image quality", "real layered music/effects", "physical multi-display", "long-run performance", "normal Launch manager/tray lifecycle"],
              "input": "no collector, hooks or injected input; intentionally absent/unfocused"}
    try:
        manifest, verified = verify_release(args.release)
        report["source_commit"] = manifest["source_commit"]
        report["release_manifest_sha256"] = digest(args.release / "release-manifest.json")
        report["verified_files"] = len(verified)
        if args.structure_only:
            report["status"] = "passed-release-structure-only-not-native-smoke"
            return 0
        require(os.name == "nt", "actual native smoke requires Windows")
        validator = args.release / "Authoring/NativeValidator/NativeValidator.exe"
        validations = []
        for package in manifest["packages"]:
            command = [str(validator), "--package", str(args.release / "native-content" / package["file"]),
                       "--platform-index", str(args.release / "native-runtime/native-platform-library.json")]
            done = subprocess.run(command, capture_output=True, timeout=20)
            (output / ("validator-" + package["cover_id"] + ".log")).write_bytes(done.stdout + done.stderr)
            done.check_returncode()
            result = json.loads(done.stdout.decode("utf-8-sig"))
            require(result.get("valid") is True and result["sha256"] == package["sha256"]
                    and result["build_id"] == package["build_id"] and result["work_id"] == package["work_id"]
                    and result["sdk"] == manifest["version"] and result["schema"] == 5 and result["command_abi"] == 1,
                    "final validator package identity differs")
            validations.append(result)
        report["final_validator"] = validations
        report["compile"] = compile_harness(args, manifest, output)
        report["job"] = run_owned(output / "ReleaseSmoke.exe", [args.release, output / "data", output / "native-result.json", args.startup_seconds * 1000],
                                  output, 5 * (args.startup_seconds + 25) + 30)
        result = read(output / "native-result.json")
        require(result["passed"] and len(result["cases"]) == 5, "native harness did not complete all packages")
        report["native"] = result
        # No broad second hash pass: the same frozen files are bound above;
        # recheck the manifest and executable/component/package inputs used by
        # this run, rather than repeating a full assets/cache inventory scan.
        critical = [name for name in verified if name in {"SpatialWallpaper.exe", "UnityPlayer.dll"}
                    or name.startswith(("native-runtime/", "Authoring/NativeValidator/", "native-content/"))
                    or name.startswith("SpatialWallpaper_Data/Managed/") and name.endswith(".dll")]
        require(all(digest(args.release.joinpath(*relative(name).parts)) == verified[name] for name in critical), "tested release input changed during smoke")
        require(digest(args.release / "release-manifest.json") == report["release_manifest_sha256"], "release manifest changed during smoke")
        report["tested_critical_inputs_unchanged"] = True
        report["passed"] = True
        report["status"] = "passed-five-final-native-packages-minimal-render-smoke"
        return 0
    except Exception as error:
        report["status"] = "failed"
        report["error"] = repr(error)
        print("Native release smoke failed:", error, file=sys.stderr)
        return 1
    finally:
        (output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(report.get("status", "failed"), output / "report.json", flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
