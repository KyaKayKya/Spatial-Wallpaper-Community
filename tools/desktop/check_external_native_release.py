"""Real final-release external CC0 example import/render/GPU retirement smoke.

Uses the frozen product Store/configuration and final Player under the same owned
Job as check_native_release. Never starts DesktopRuntime/Tracker, attaches a
desktop window, installs an input collector, injects input or captures pixels.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

import check_native_release as native

EXAMPLE = "Authoring/Examples/OriginalExternal/original-cc0-triangle.spatial"
ADMISSION = "Authoring/Examples/OriginalExternal/admission.json"


def external_harness():
    """Reuse protocol/ownership checks, replacing only the selected package case."""
    code = native.HARNESS

    def replace(before, after):
        nonlocal code
        native.require(code.count(before) == 1, "base harness contract changed: " + before[:70])
        code = code.replace(before, after)

    replace("real final five packages admitted/imported and exact release native render role; no input/gameplay/visual/audio/multidisplay claim",
            "real final original CC0 external package admission/import/render and orderly GPU retirement; no input/gameplay/visual/audio claim")
    replace('foreach(JObject entry in (JArray)manifest["packages"]){',
            'foreach(JObject entry in new JArray(Read(Path.Combine(release,"' + ADMISSION + '")))){')
    replace('string file=Path.Combine(release,"native-content",(string)entry["file"]);var package=NativePackage.Validate(file);',
            'string file=Path.Combine(release,"' + EXAMPLE + '");var package=NativeInstalledPackage.Validate(file,platform);Need(package.IsExternal,"example-must-use-external-admission");')
    replace('Need(config["input"]==null,"unexpected-input-collector");',
            '''Need(config["input"]==null,"unexpected-input-collector");
    Need((string)config["artwork"]==NativeInstalledPackage.ExternalSceneId&&config["external_package"] is JObject external&&
      (string)external["build_id"]==installed.BuildId&&(string)external["sha256"]==installed.ArchiveSha256&&
      Path.GetFullPath((string)external["path"])==installed.ArchivePath,"external-configuration-route-mismatch");
    Need(((JArray)config["script"]["node_paths"]).Count==0&&((JArray)config["script"]["resources"]).Count==0,"external-cannot-resolve-platform-assets");''')
    replace('NativeRenderProcess renderer=null;', 'NativeRenderProcess renderer=null;FileStream retainedLog=null;')
    replace('var first=Observe(renderer,',
            'retainedLog=new FileStream(renderer.DiagnosticLog,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);\n     var first=Observe(renderer,')
    replace('renderer.Dispose();}', '''renderer.Dispose();
      if(retainedLog!=null){using(var saved=File.Create(Path.Combine(Path.GetDirectoryName(result),"external-player.log"))){retainedLog.Position=0;retainedLog.CopyTo(saved);}retainedLog.Dispose();retainedLog=null;}
      item["gpu_retirement_scope"]="Final frozen NativeRenderInstance.Close awaits ExternalNativeScene.CloseAsync and CanDisposeOwner fence/object retirement before successful process exit; final log and exit status retained.";
     }''')
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="New short Validation directory, for example Validation/ne1.")
    parser.add_argument("--source-root", type=Path, default=native.ROOT)
    parser.add_argument("--compiler-runtime", type=Path, required=True)
    parser.add_argument("--compiler", type=Path, required=True)
    parser.add_argument("--framework", type=Path, default=Path(os.environ.get("ProgramFiles(x86)", "C:/Program Files (x86)")) / "Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8")
    parser.add_argument("--startup-seconds", type=int, choices=range(15, 46), default=35)
    args = parser.parse_args()
    native.no_links(args.release.absolute())
    args.release = args.release.resolve()
    output = args.output.absolute()
    native.no_links(output.parent)
    native.require(not output.exists(), "output already exists; never overwrite state")
    native.require(not output.resolve().is_relative_to(args.release), "output cannot be in release")
    native.require(len(str(output / "data/library/builds" / ("f" * 64 + ".spatial." + "0" * 32 + ".tmp"))) < 260,
                   "use a shorter Validation output directory")
    output.mkdir(parents=True, exist_ok=False)
    report = {"passed": False, "release": str(args.release), "example": EXAMPLE,
              "tool_sha256": native.digest(Path(__file__)), "base_tool_sha256": native.digest(Path(native.__file__)),
              "not_verified": ["image quality", "physical displays", "audio experience", "input/gameplay", "normal Launch/DesktopRuntime lifecycle"],
              "input": "absent-unfocused; no collector, hooks or injected events"}
    try:
        native.require(os.name == "nt", "Windows is required for actual native render")
        manifest, verified = native.verify_release(args.release)
        report["source_commit"] = manifest["source_commit"]
        report["release_manifest_sha256"] = native.digest(args.release / "release-manifest.json")
        report["verified_files"] = len(verified)
        native.require(EXAMPLE in verified and ADMISSION in verified, "external example missing from final inventory")
        expected = native.read(args.release / ADMISSION)
        command = [str(args.release / "Authoring/NativeValidator/NativeValidator.exe"), "--package", str(args.release / EXAMPLE),
                   "--platform-index", str(args.release / "native-runtime/native-platform-library.json")]
        done = subprocess.run(command, capture_output=True, timeout=20)
        (output / "validator.log").write_bytes(done.stdout + done.stderr)
        done.check_returncode()
        admitted = json.loads(done.stdout.decode("utf-8-sig"))
        native.require(admitted == expected and admitted["valid"] is True and admitted["schema"] == 5
                       and admitted["sdk"] == manifest["version"] and admitted["command_abi"] == 1
                       and admitted["policy"] == "native-schema5-external-data-v1"
                       and admitted["license_share"] is True and admitted["sha256"] == verified[EXAMPLE],
                       "final external validator/admission/manifest identity differs")
        report["final_validator"] = admitted
        native.HARNESS = external_harness()
        report["compile"] = native.compile_harness(args, manifest, output)
        retirement_sources = ("Prototype/Assets/Desktop/NativeRenderInstance.cs", "Prototype/Assets/NativeArtworks/Runtime/ExternalNativeScene.cs")
        report["retirement_source_sha256"] = {name: hashlib.sha256(native.source_bytes(args.source_root, manifest["source_commit"], name)).hexdigest() for name in retirement_sources}
        report["job"] = native.run_owned(output / "ReleaseSmoke.exe", [args.release, output / "data", output / "native-result.json", args.startup_seconds * 1000], output, args.startup_seconds + 65)
        result = native.read(output / "native-result.json")
        native.require(result["passed"] and len(result["cases"]) == 1, "external harness did not complete")
        report["native"] = result
        log = (output / "external-player.log").read_text(encoding="utf-8-sig", errors="replace")
        native.require("Native renderer shutdown: script completed=True released=True error=" in log
                       and "Native renderer shutdown: input completed=True released=True error=" in log
                       and "Native renderer shutdown: quit error=\n" in log.replace("\r\n", "\n")
                       and "retained unknown native state" not in log,
                       "external orderly retirement/clean quit not present in final Player log")
        report["gpu_retirement"] = {"passed": True, "proof": "real final Player orderly exit through frozen fence/CanDisposeOwner gate, no forced Job termination", "log_sha256": native.digest(output / "external-player.log")}
        critical = [name for name in verified if name in {"SpatialWallpaper.exe", "UnityPlayer.dll", EXAMPLE, ADMISSION}
                    or name.startswith(("native-runtime/", "Authoring/NativeValidator/"))
                    or name.startswith("SpatialWallpaper_Data/Managed/") and name.endswith(".dll")]
        native.require(all(native.digest(args.release.joinpath(*native.relative(name).parts)) == verified[name] for name in critical), "tested final inputs changed")
        native.require(native.digest(args.release / "release-manifest.json") == report["release_manifest_sha256"], "final manifest changed")
        report["tested_critical_inputs_unchanged"] = True
        report["passed"] = True
        report["status"] = "passed-final-external-native-import-render-retirement"
        return 0
    except Exception as error:
        report["status"] = "failed"
        report["error"] = repr(error)
        print("External native release smoke failed:", error, file=sys.stderr)
        return 1
    finally:
        (output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(report.get("status", "failed"), output / "report.json", flush=True)


if __name__ == "__main__":
    raise SystemExit(main())
