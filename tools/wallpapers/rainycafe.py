"""Prepare original cafe surfaces and bounded geometry without plugin restoration."""
from pathlib import Path
import hashlib
import json
import shutil
import struct

GROUPS = {'Floor': 1280, 'Walls': 640, 'Ceiling': 512, 'Counter': 640,
          'Furniture': 640, 'Photographs': 768, 'StreetBulb': 512,
          'Props': 768, 'Lamps': 512, 'Plants': 512, 'Foliage': 512, 'Exterior': 512}


def fbx_vertices(filename):
    """Read source control point counts from Blender's binary FBX, without Blender."""
    data = filename.read_bytes()
    if not data.startswith(b'Kaydara FBX Binary'):
        raise ValueError('Expected binary FBX: ' + str(filename))
    wide = struct.unpack_from('<I', data, 23)[0] >= 7500
    header = '<QQQB' if wide else '<IIIB'
    header_size = struct.calcsize(header)
    total = 0

    def node(offset):
        nonlocal total
        end, count, size, length = struct.unpack_from(header, data, offset)
        if end == 0:
            return len(data)
        start = offset + header_size
        name = data[start:start + length]
        props = start + length
        if name == b'Vertices' and data[props:props + 1] in (b'd', b'f'):
            total += struct.unpack_from('<I', data, props + 1)[0] // 3
        child = props + size
        while child + header_size < end:
            following = node(child)
            if following >= end:
                break
            child = following
        return end

    offset = 27
    while offset + header_size < len(data):
        offset = node(offset)
    return total


def prepare(source, common, ROOT):
    from PIL import Image, ImageChops, ImageMath, ImageFilter
    source, common, ROOT = Path(source), Path(common), Path(ROOT)
    from cafe_quality_assets import restore
    quality = restore(common)
    original = common / '.local-assets/rainycafe/0f21d680fbdf5cd6/Prototype/Assets/RainyCafe/Generated'
    receipt = json.loads((ROOT / 'tools/rainycafe/local-assets.json').read_text(encoding='utf-8-sig'))
    hashes = receipt.get('sha256', receipt.get('files', {}))
    if not isinstance(hashes, dict):
        raise ValueError('Expected local-assets SHA256 mapping')
    source.mkdir(parents=True, exist_ok=True)
    records = []
    for name, size in GROUPS.items():
        coverage=None
        if name in ('Props','Plants','Lamps','Foliage'):
            with Image.open(original/(name+'Albedo.png')) as albedo,Image.open(original/(name+'Lighting.png')) as lighting:
                rgb=ImageChops.lighter(albedo.convert('RGB'),lighting.convert('RGB'))
                coverage=ImageChops.lighter(ImageChops.lighter(*rgb.split()[:2]),rgb.split()[2]).point(lambda v:255 if v else 0).convert('F')
        for filename in (name + '.fbx', *[name + kind + '.png' for kind in ['Lighting','Albedo','Normal','Roughness']]):
            incoming = original / filename
            if name in ('Counter','Furniture','Props','Lamps','Plants','Foliage','Exterior') and filename.endswith('.fbx'):
                incoming = quality / 'Reduced' / filename
                if not incoming.is_file():
                    raise FileNotFoundError('Generate reduced RainyCafe FBX preserving BakeUV first: ' + str(incoming))
            key = 'Prototype/Assets/RainyCafe/Generated/' + filename
            expected = hashes.get(key)
            actual = hashlib.sha256(incoming.read_bytes()).hexdigest()
            reduced = name in ('Counter','Furniture','Props','Lamps','Plants','Foliage','Exterior') and filename.endswith('.fbx')
            if not reduced and (not expected or expected != actual):
                raise ValueError('rainycafe-source-hash-mismatch:' + filename)
            if filename.endswith('.fbx'):
                shutil.copyfile(incoming, source / filename)
                records.append({'name': name, 'vertices': fbx_vertices(incoming), 'sha256': actual,
                                'source': str(incoming), 'reduced': reduced})
            else:
                # Lighting is the original COMBINED bake, including material color.
                with Image.open(incoming) as image:
                    target_size=size if filename.endswith('Lighting.png') else min(size,512)
                    if coverage is not None and filename.endswith(('Lighting.png','Albedo.png')):
                        # Tiny bake islands otherwise average with unoccupied black
                        # atlas space and lose their color at display-sized LOD.
                        mask=coverage.resize((target_size,target_size),Image.Resampling.BOX)
                        channels=[ImageMath.lambda_eval(lambda values:values['convert'](values['a']*255/(values['b']+.0001),'L'),a=c.convert('F').resize((target_size,target_size),Image.Resampling.BOX),b=mask) for c in image.convert('RGB').split()]
                        # One texel of bake padding keeps bilinear samples on
                        # these subpixel islands out of the unoccupied atlas.
                        Image.merge('RGB',channels).filter(ImageFilter.MaxFilter(3)).save(source/filename)
                    else:image.convert('RGB').resize((target_size,target_size),Image.Resampling.LANCZOS).save(source/filename)
        if name=='Foliage':shutil.copy2(original/'FoliageOpacity.png',source/'FoliageOpacity.png')
    shutil.copy2(original/'Glass.fbx',source/'Glass.fbx')
    detail_root=quality/'NativeDetails'
    details=json.loads((detail_root/'details.json').read_text())
    for detail in details:
        incoming=detail_root/(detail['name']+'.smesh')
        assert detail['source_sha256']==hashes['Prototype/Assets/RainyCafe/Generated/'+detail['group']+'.fbx']
        assert hashlib.sha256(incoming.read_bytes()).hexdigest()==detail['output_sha256']
        shutil.copy2(incoming,source/incoming.name)
    shutil.copy2(detail_root/'details.json',source/'details.json')
    (source/'CafeFlat.shader').write_text('''Shader "RainyCafeSdk/Flat" {
Properties {_BaseColor("Color",Color)=(1,1,1,1)}
SubShader {Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"} Pass {
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
float4 _BaseColor;
CBUFFER_END
float4 vert(float4 p:POSITION):SV_POSITION{return TransformObjectToHClip(p.xyz);}
half4 frag():SV_Target{return _BaseColor;}
ENDHLSL
}}}''',encoding='utf-8')
    for shader in ['CafeSurface.shader','CafeGlass.shader']:
        text=(ROOT/'Prototype/Assets/RainyCafe/Shaders'/shader).read_text(encoding='utf-8')
        text=text.replace('RainyCafe/','RainyCafeSdk/').replace('_Clock*(.009+seed*.008)','_Time.y*(.009+seed*.008)')
        if shader=='CafeSurface.shader':
            # This scene uses baked illumination and three unshadowed local lights.
            # Avoid compiling unused Lit passes and shadow variants into its bundle.
            text=text.replace('#pragma multi_compile _ _ADDITIONAL_LIGHTS','#define _ADDITIONAL_LIGHTS')
            # The trusted SDK profile uses Forward+. Its clustered light loop
            # must be compiled; the original scene used per-object Forward.
            text=text.replace('#pragma vertex vert','#pragma vertex vert\n   #pragma multi_compile _ _FORWARD_PLUS')
            text=text.replace('#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS','').replace('#pragma multi_compile_fragment _ _SHADOWS_SOFT','')
            text=text.replace('UsePass "Universal Render Pipeline/Lit/ShadowCaster"','').replace('UsePass "Universal Render Pipeline/Lit/DepthOnly"','')
            text=text.replace('UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,v.uv))','normalize(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,v.uv).xyz*2-1)')
        (source/shader).write_text(text,encoding='utf-8')
    (source / 'geometry-source.json').write_text(json.dumps({
        'objects': records, 'source_vertices': sum(item['vertices'] for item in records),
        'omitted': ['SmallGlass', 'StreetGlass'],
        'replacement': 'Original UVs, window geometry, props, lamps and foliage retained with bounded mesh LOD; original surface and condensation shaders; GPU weather',
        'rights': 'User local assets; original redistribution rights remain unverified'
    }, indent=2), encoding='utf-8')


if __name__ == '__main__':
    import sys
    for filename in sorted(Path(sys.argv[1]).glob('*.fbx')):
        print(filename.name, fbx_vertices(filename), filename.stat().st_size)
