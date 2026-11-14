"""Build the native probe without modifying the machine or global environment."""
from pathlib import Path
import subprocess
def compile_probe(source,output,library=False):
 vc=sorted(Path('C:/Program Files/Microsoft Visual Studio/2022/Community/VC/Tools/MSVC').iterdir())[-1]
 sdk=Path('C:/Program Files (x86)/Windows Kits/10')
 ver=sorted(p.name for p in (sdk/'Include').iterdir() if (p/'um/windows.h').exists())[-1]
 args=[str(vc/'bin/Hostx64/x64/cl.exe'),'/nologo','/EHsc','/MT','/Fe:'+str(output),'/Fo:'+str(output.with_suffix('.obj'))]
 for path in [vc/'include',*[sdk/'Include'/ver/n for n in ['ucrt','shared','um','winrt']]]:args+=['/I'+str(path)]
 args+=(['/LD'] if library else [])
 args+=[str(source),'/link','/LIBPATH:'+str(vc/'lib/x64'),'/LIBPATH:'+str(sdk/'Lib'/ver/'um/x64'),'/LIBPATH:'+str(sdk/'Lib'/ver/'ucrt/x64'),'user32.lib','advapi32.lib','ws2_32.lib']
 subprocess.run(args,check=True)
