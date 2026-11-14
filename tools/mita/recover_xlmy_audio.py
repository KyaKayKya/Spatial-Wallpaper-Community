"""Decode the verified original click offline; FMOD is never shipped in the wallpaper."""
import ctypes as c
import subprocess,zipfile
from prepare import COMMON

def extract(archive,target):
    tools=COMMON/'.local-assets/mita-xlmy/audio-tool';tools.mkdir(exist_ok=True)
    with zipfile.ZipFile(archive) as source:
        for suffix,name in [('/Plugins/x86_64/fmodstudio.dll','fmodstudio.dll'),('/StreamingAssets/Sound.bank','Sound.bank')]:
            matches=[entry for entry in source.namelist() if entry.endswith(suffix)]
            if len(matches)!=1:raise ValueError('Original audio dependency not found: '+name)
            (tools/name).write_bytes(source.read(matches[0]))
    path=tools/'fmodstudio.dll'
    version=c.WinDLL('version');size=version.GetFileVersionInfoSizeW(str(path),None);buffer=c.create_string_buffer(size)
    if not version.GetFileVersionInfoW(str(path),0,size,buffer):raise c.WinError()
    pointer=c.c_void_p();length=c.c_uint()
    if not version.VerQueryValueW(buffer,'\\',c.byref(pointer),c.byref(length)):raise c.WinError()
    info=c.cast(pointer,c.POINTER(c.c_uint32));parts=[info[2]>>16,info[2]&65535,info[3]>>16,info[3]&65535]
    major,minor,patch=parts[1:] if parts[0]==0 else parts[:3]
    dll=c.WinDLL(str(path))
    def call(name,*args):
        result=getattr(dll,name)(*args)
        if result!=0:raise RuntimeError(name+' failed with FMOD result '+str(result))
    data=(tools/'Sound.bank').read_bytes();offset=data.find(b'FSB5')
    if offset<0:raise ValueError('Original sample bank not found')
    fsb=tools/'Sound.fsb';fsb.write_bytes(data[offset:])
    system=c.c_void_p();call('FMOD_System_Create',c.byref(system),(major<<16)|(minor<<8)|patch)
    try:
        # NOSOUND_NRT: no audio device, capture, or playback.
        call('FMOD_System_SetOutput',system,4);call('FMOD_System_Init',system,32,0,None)
        sound=c.c_void_p();call('FMOD_System_CreateSound',system,str(fsb).encode('utf8'),0x100,None,c.byref(sound))
        try:
            count=c.c_int();call('FMOD_Sound_GetNumSubSounds',sound,c.byref(count))
            if count.value!=1:raise ValueError('Unexpected sample count')
            sub=c.c_void_p();call('FMOD_Sound_GetSubSound',sound,0,c.byref(sub));name=c.create_string_buffer(512);call('FMOD_Sound_GetName',sub,name,512)
            if name.value!=b'MitaMouseClick':raise ValueError('Unexpected original click sample')
            sound_type=c.c_int();fmt=c.c_int();channels=c.c_int();bits=c.c_int()
            call('FMOD_Sound_GetFormat',sub,c.byref(sound_type),c.byref(fmt),c.byref(channels),c.byref(bits))
            frequency=c.c_float();priority=c.c_int();call('FMOD_Sound_GetDefaults',sub,c.byref(frequency),c.byref(priority))
            byte_count=c.c_uint();call('FMOD_Sound_GetLength',sub,c.byref(byte_count),4)
            first=c.c_void_p();second=c.c_void_p();first_size=c.c_uint();second_size=c.c_uint()
            call('FMOD_Sound_Lock',sub,0,byte_count.value,c.byref(first),c.byref(second),c.byref(first_size),c.byref(second_size))
            try:pcm=c.string_at(first,first_size.value)+(c.string_at(second,second_size.value) if second_size.value else b'')
            finally:call('FMOD_Sound_Unlock',sub,first,second,first_size.value,second_size.value)
            formats={1:'u8',2:'s16le',3:'s24le',4:'s32le',5:'f32le'}
            output=target/'Audio';output.mkdir(exist_ok=True)
            subprocess.run(['ffmpeg','-y','-hide_banner','-loglevel','error','-f',formats[fmt.value],'-ar',str(int(frequency.value)),'-ac',str(channels.value),'-i','pipe:0','-c:a','pcm_s16le',str(output/'MitaMouseClick.wav')],input=pcm,check=True)
        finally:call('FMOD_Sound_Release',sound)
    finally:call('FMOD_System_Release',system)
    print('Decoded original MitaMouseClick to PCM WAV; no FMOD runtime dependency')
