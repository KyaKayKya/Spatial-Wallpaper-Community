"""Finalize and inspect a Universal .app without macOS-only host tools."""
from pathlib import Path
import plistlib
import shutil
from native_inputs import mach_o_architectures

def finalize_app(root,app):
 root=Path(root);app=Path(app);contents=app/'Contents';plist=contents/'Info.plist'
 info=plistlib.loads(plist.read_bytes())
 executable=contents/'MacOS'/info['CFBundleExecutable']
 bridge=list(contents.rglob('libSpatialMac.dylib'))
 if len(bridge)!=1:raise ValueError('Expected exactly one Unity-imported libSpatialMac.dylib')
 camera=root/'Validation/Desktop/native/libSpatialCamera.dylib'
 architectures={}
 for path in [executable,bridge[0],camera]:
  actual=mach_o_architectures(path)
  if set(actual)!={'arm64','x86_64'}:raise ValueError('Player/native architectures must be Universal: '+str(path))
  relative=path.relative_to(app).as_posix() if path!=camera else 'Contents/Frameworks/libSpatialCamera.dylib'
  architectures[relative]=actual
 destination=contents/'Frameworks/libSpatialCamera.dylib';destination.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(camera,destination)
 resources=contents/'Resources';resources.mkdir(exist_ok=True)
 shutil.copyfile(root/'tools/macos/PrivacyInfo.xcprivacy',resources/'PrivacyInfo.xcprivacy')
 info['NSCameraUsageDescription']='为当前明确启用的空间壁纸追踪观察位置。'
 plist.write_bytes(plistlib.dumps(info,fmt=plistlib.FMT_XML,sort_keys=False))
 # Windows cannot produce a final macOS signature. Updating Info.plist also
 # invalidates any Unity development signature; the recipient must re-sign.
 return {'architectures':architectures,'requires_macos_signing':True,'executable':executable.relative_to(app).as_posix()}
