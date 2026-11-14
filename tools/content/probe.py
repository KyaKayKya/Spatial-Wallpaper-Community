"""Build and run the P1 probes in two clean Unity projects, without Tracker.

Run author, boundary, player, run, error-gate in that order (or all).
The native type-table probe is an observation, never content admission.
"""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "Validation/Content/probes"
REPORTS = OUTPUT / "reports"
UNITY = Path("D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe")
SOURCE = ROOT / "Prototype/Assets/ContentValidation"


def sha256(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(chunk)
    return result.hexdigest()


def player_closure():
    directory = OUTPUT / "player"
    result = hashlib.sha256()
    for path in sorted(directory.rglob("*")):
        if path.is_file():
            result.update(path.relative_to(directory).as_posix().encode("utf-8") + b"\0")
            result.update(bytes.fromhex(sha256(path)))
    return result.hexdigest()


def build(role):
    project = OUTPUT / (role + "-project")
    assets = project / "Assets/ContentValidation"
    assets.mkdir(parents=True, exist_ok=True)
    (project / "Assets/csc.rsp").write_text("-define:CONTENT_PROBE\n", encoding="utf-8")
    # Obsolete file from an earlier local experiment, not user source.
    for name in ["Runtime/ProbeBundleAudit.cs", "Runtime/ProbeBundleAudit.cs.meta"]:
        obsolete = assets / name
        if obsolete.exists():
            obsolete.unlink()
    for source in SOURCE.rglob("*"):
        if not source.is_file():
            continue
        target = assets / source.relative_to(SOURCE)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
    (project / "Packages").mkdir(exist_ok=True)
    shutil.copy2(ROOT / "tools/content/probe-manifest.json", project / "Packages/manifest.json")
    shutil.copy2(ROOT / "tools/content/probe-packages-lock.json", project / "Packages/packages-lock.json")
    (project / "ProjectSettings").mkdir(exist_ok=True)
    shutil.copy2(ROOT / "Prototype/ProjectSettings/ProjectVersion.txt", project / "ProjectSettings/ProjectVersion.txt")
    inputs = project / "Assets/ProbeInputs"
    inputs.mkdir(exist_ok=True)
    for relative in ["Train/Shaders/WinterJourney.shader", "Pool/Shaders/PoolReflection.shader"]:
        source = ROOT / "Prototype/Assets" / relative
        shutil.copy2(source, inputs / source.name)
        shutil.copy2(Path(str(source) + ".meta"), inputs / (source.name + ".meta"))
    environment = os.environ.copy()
    environment.setdefault("ALLUSERSPROFILE", environment.get("ProgramData", "C:/ProgramData"))
    environment["CONTENT_PROBE_OUTPUT"] = str(OUTPUT)
    environment["CONTENT_PROBE_ROLE"] = role
    command = [str(UNITY), "-batchmode", "-quit", "-projectPath", str(project),
               "-executeMethod", "SpatialWallpaper.ContentValidation.ContentProbeBuild.Run",
               "-logFile", str(OUTPUT / (role + "-build.log"))]
    result = subprocess.run(command, env=environment, timeout=1200)
    print(role, "Unity exit", result.returncode, flush=True)
    if result.returncode:
        raise SystemExit(result.returncode)


def boundary():
    data = UNITY.parent / "Data"
    output = OUTPUT / "boundary"
    output.mkdir(parents=True, exist_ok=True)
    shutil.copy2(data / "Managed/Newtonsoft.Json.dll", output / "Newtonsoft.Json.dll")
    mono = data / "MonoBleedingEdge/bin/mono.exe"
    command = [str(mono), str(data / "MonoBleedingEdge/lib/mono/4.5/csc.exe"), "-nologo",
               "-langversion:latest", "-define:CONTENT_PROBE", "-r:" + str(output / "Newtonsoft.Json.dll"),
               "-r:" + str(data / "MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll"),
               "-out:" + str(output / "ProbeChecks.exe")]
    command += [str(SOURCE / p) for p in ["Runtime/ProbeData.cs", "Tests/ContentProbeChecks.cs", "Tests/NativeTypeTableProbe.cs"]]
    subprocess.run(command, check=True)
    result = subprocess.run([str(mono), str(output / "ProbeChecks.exe"), str(OUTPUT)], capture_output=True, text=True)
    (REPORTS / "boundary-checks.txt").write_text(result.stdout + result.stderr, encoding="utf-8")
    print(result.stdout, result.stderr)
    if result.returncode:
        raise SystemExit(result.returncode)


def run(error_gate=False):
    player = OUTPUT / "player/ContentProbe.exe"
    before = sha256(player)
    closure_before = player_closure()
    run_root = OUTPUT / "runs" / ("error-gate" if error_gate else "positive")
    (run_root / "bundles").mkdir(parents=True, exist_ok=True)
    shutil.copy2(OUTPUT / "safe.json", run_root / "safe.json")
    for name in ["clean", "callbacks", "resources", "hidden-resource"]:
        shutil.copy2(OUTPUT / "bundles" / name, run_root / "bundles" / name)
    report_path = run_root / "player-report.json"
    if report_path.exists():
        report_path.unlink()
    command = [str(player), "-screen-fullscreen", "0", "-screen-width", "960", "-screen-height", "540",
               "--probe-root", str(run_root), "-logFile", str(run_root / "player.log")]
    if error_gate:
        command.append("--inject-probe-error")
    result = subprocess.run(command, timeout=90)
    report = json.loads(report_path.read_text(encoding="utf-8"))
    shutil.copy2(report_path, REPORTS / ("error-gate-report.json" if error_gate else "player-report.json"))
    assert sha256(player) == before, "Player binary changed during validation"
    assert player_closure() == closure_before, "Player files changed during validation"
    if error_gate:
        assert result.returncode == 1 and not report["passed"], "Unity Error was incorrectly reported as success"
        assert all(case["passed"] for case in report["cases"]), "Error-gate run also had a non-injected failure"
        assert report["errors"] == ["Error: deliberate-probe-error"], report["errors"]
    else:
        assert result.returncode == 0 and report["passed"] and not report["errors"], report
        shutil.copy2(run_root / "player.png", REPORTS / "player.png")
    print(("error gate" if error_gate else "positive"), "verified; cases=", len(report["cases"]), "Player SHA256=", before, "closure=", closure_before)


def fingerprint():
    files = list(SOURCE.rglob("*.cs")) + [ROOT / "tools/content/probe.py", ROOT / "tools/content/probe-manifest.json", ROOT / "tools/content/probe-packages-lock.json", UNITY, OUTPUT / "player/ContentProbe.exe", OUTPUT / "player/UnityPlayer.dll",
        OUTPUT / "player/ContentProbe_Data/Managed/Assembly-CSharp.dll"]
    files += list((OUTPUT / "bundles").glob("*"))
    files += [ROOT / "Prototype/Assets/Train/Shaders/WinterJourney.shader", ROOT / "Prototype/Assets/Pool/Shaders/PoolReflection.shader"]
    for role in ["author", "player"]:
        files += [OUTPUT / (role + "-project/Packages/manifest.json"), OUTPUT / (role + "-project/Packages/packages-lock.json")]
    records = [{"path": str(p.relative_to(ROOT)) if p.is_relative_to(ROOT) else str(p), "size": p.stat().st_size, "sha256": sha256(p)} for p in files if p.is_file()]
    document = {"baseline": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(), "files": records, "playerClosureSha256": player_closure(),
                "scope": "P1 experiment only; accepted content is pure data; native type table is not admission; not four-scene or human acceptance"}
    (REPORTS / "fingerprint.json").write_text(json.dumps(document, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    REPORTS.mkdir(parents=True, exist_ok=True)
    role = sys.argv[1] if len(sys.argv) > 1 else "all"
    if role == "all":
        build("author")
        boundary()
        build("player")
        run()
        run(True)
        fingerprint()
    elif role in ["author", "player"]:
        build(role)
    elif role == "boundary":
        boundary()
    elif role == "run":
        run()
        fingerprint()
    elif role == "error-gate":
        run(True)
    else:
        raise SystemExit("Choose all, author, boundary, player, run or error-gate")
