"""Copy only the approved baked display cabinet into an isolated SDK project."""
import shutil

def prepare(source,common,root):
    from PIL import Image
    original=common/'Prototype/Assets/Showcase/BakedStudy'
    for pattern in ('*.fbx','*_occupied.exr','AcceptedAgX.png','EmptyReflection.exr'):
        for path in original.glob(pattern):shutil.copyfile(path,source/path.name)
    # Preserve the accepted display transform, resampled from 64^3 to 32^3.
    # Resizing the atlas directly would mix unrelated adjacent blue slices.
    with Image.open(original/'AcceptedAgX.png') as image:
        output=Image.new('RGB',(1024,32))
        for b in range(32):
            at=b*63/31;a=int(at);c=min(63,a+1)
            tile=Image.blend(image.crop((a*64,0,(a+1)*64,64)),image.crop((c*64,0,(c+1)*64,64)),at-a)
            output.paste(tile.resize((32,32),Image.Resampling.BILINEAR),(b*32,0))
        output.save(source/'AcceptedAgX.png')
    text=(root/'Prototype/Assets/Showcase/Shaders/BakedDisplaySurface.shader').read_text(encoding='utf-8-sig')
    text=text.replace('SpatialWallpaper/BakedDisplaySurface','ShowcaseSdk/BakedDisplaySurface')
    text=text.replace(' Properties {',' Properties {\n  [NoScaleOffset] _ReflectionCube("Static room reflection",Cube)=""{}')
    text=text.replace('   #pragma multi_compile _ _REFLECTION_PROBE_BOX_PROJECTION','')
    text=text.replace('   float _RadianceCapture;','   TEXTURECUBE(_ReflectionCube);SAMPLER(sampler_ReflectionCube);')
    text=text.replace('    if(_RadianceCapture>.5)return half4(diffuse,1);','')
    text=text.replace('GlossyEnvironmentReflection(reflect(-v,n),i.positionWS,_Roughness,1)','SAMPLE_TEXTURECUBE_LOD(_ReflectionCube,sampler_ReflectionCube,reflect(-v,n),_Roughness*6).rgb')
    text=text.replace('*63;','*31;').replace('(q.r+slice*64+.5)/4096,(q.g+.5)/64','(q.r+slice*32+.5)/1024,(q.g+.5)/32').replace('64.0/4096','32.0/1024')
    (source/'BakedDisplaySurface.shader').write_text(text,encoding='utf-8')
