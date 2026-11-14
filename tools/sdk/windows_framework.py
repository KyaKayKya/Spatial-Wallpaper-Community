"""Explicit reference assemblies for trusted standalone .NET Framework 4.8 executables."""
from pathlib import Path
import os,hashlib
FRAMEWORK=Path(os.environ.get('ProgramFiles(x86)','C:/Program Files (x86)'))/'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
NAMES=('mscorlib.dll','System.dll','System.Core.dll','Facades/netstandard.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')
def references():
 files=[FRAMEWORK/name for name in NAMES]
 if not all(path.is_file() for path in files):raise RuntimeError('.NET Framework 4.8 reference assemblies unavailable')
 return ['-nostdlib+',*('-r:'+str(path) for path in files)]
def identity():
 references()
 return {name:hashlib.sha256((FRAMEWORK/name).read_bytes()).hexdigest() for name in NAMES}
