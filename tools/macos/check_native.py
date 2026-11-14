"""Compile the native bridge and exercise its self-owned AppKit fixture."""
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
if sys.platform != 'darwin':
    raise SystemExit('This check requires a macOS login session')
subprocess.run([sys.executable, str(root / 'tools/macos/build_native.py')], check=True)
output = root / 'Validation/Desktop/native/NativeChecks'
subprocess.run(['xcrun', 'clang', '-fobjc-arc', '-fmodules',
    '-fmodules-cache-path=/private/tmp/spatial-clang-modules',
    '-Wno-deprecated-declarations', '-framework', 'AppKit', '-framework', 'CoreGraphics',
    str(root / 'tools/macos/NativeChecks.m'), '-o', str(output)], check=True)
subprocess.run([str(output)], check=True)
