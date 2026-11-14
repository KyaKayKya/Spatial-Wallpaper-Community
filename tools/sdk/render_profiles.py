"""One trusted profile catalog. Its presence is not a release support claim."""
from pathlib import Path
import hashlib
import json
import re
SOURCE=Path(__file__).with_name('render-profiles.json')

def canonical(value):
    return json.dumps(value,ensure_ascii=True,sort_keys=True,separators=(',',':'))

def catalog():
    value=json.loads(SOURCE.read_text(encoding='utf-8'))
    if value['schema']!=1:raise ValueError('render-catalog-schema')
    ids=[row['id'] for row in value['profiles']]
    if len(ids)!=len(set(ids)) or value['default'] not in ids:raise ValueError('render-catalog-identity')
    for row in value['profiles']:
        if (set(row)!={'id','version','format_version','engine','pipeline','platform','color_space','graphics_api','settings','renderer_features','camera','capture'} or
            type(row['format_version']) is not int or row['format_version']!=1 or
            row['engine']!='2022.3.62f3' or row['pipeline']!='14.0.12' or row['platform']!='windows-x86_64'):
            raise ValueError('render-catalog-descriptor')
        if (not isinstance(row['id'],str) or not re.fullmatch(r'[a-z][a-z0-9.-]{0,95}',row['id']) or
            not isinstance(row['version'],str) or not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)',row['version']) or
            row['color_space']!='Linear' or row['graphics_api']!='Direct3D11' or row['renderer_features']!=[]):
            raise ValueError('render-catalog-unsupported')
        settings=row['settings']
        boolean_keys={'hdr','depth_texture','opaque_texture','main_light_shadows','additional_light_shadows','soft_shadows'}
        if (not isinstance(settings,dict) or set(settings)!=boolean_keys|{'rendering_mode','msaa','shadow_cascades','shadow_distance','additional_lights'} or
            any(type(settings[key]) is not bool for key in boolean_keys) or
            settings['rendering_mode']!='ForwardPlus' or settings['additional_lights']!='PerPixel' or
            type(settings['msaa']) is not int or settings['msaa'] not in (1,2,4,8) or
            type(settings['shadow_cascades']) is not int or settings['shadow_cascades'] not in (1,2,3,4) or
            type(settings['shadow_distance']) is not int or not 1<=settings['shadow_distance']<=1000):
            raise ValueError('render-catalog-settings')
        # These are the current capture contract, not claims that pipeline HDR
        # or MSAA are preserved in the transmitted SDR frame.
        if canonical(row['camera'])!=canonical({'allow_hdr':True,'allow_msaa':True,'post_processing':True,'renderer_index':-1,'resolved_renderer_index':0,'render_type':'Base','camera_stack_count':0}):
            raise ValueError('render-catalog-camera')
        if canonical(row['capture'])!=canonical({'format':'ARGB32','graphics_format':'R8G8B8A8_SRGB','depth_stencil_format':'D24_UNorm_S8_UInt','srgb':True,'samples':1,'depth_bits':24,'readback':'RGBA32'}):
            raise ValueError('render-catalog-capture')
    return value

def identity(descriptor):
    return {key:descriptor[key] for key in ('id','version','format_version')}|{'descriptor_sha256':hashlib.sha256(canonical(descriptor).encode()).hexdigest()}

def default_identity():
    value=catalog();return identity(next(row for row in value['profiles'] if row['id']==value['default']))

def require_runtime_receipt(log,expected,width,height):
    """Recheck a Worker-reported observation, not proof of visual fidelity/presentation."""
    prefix='SDK_RENDER_PROFILE '
    rows=[line[len(prefix):] for line in log.splitlines() if line.startswith(prefix)]
    if len(rows)!=1 or len(rows[0])>16384:raise ValueError('render-receipt-count')
    def unique(pairs):
        value={}
        for key,item in pairs:
            if key in value:raise ValueError('render-receipt-duplicate-field')
            value[key]=item
        return value
    value=json.loads(rows[0],object_pairs_hook=unique)
    if not isinstance(value,dict) or set(value)!=set(expected)|{'profile','observed','hardware'} or set(expected)!={'instance','attempt','generation','build_id'}:
        raise ValueError('render-receipt-fields')
    for key,item in expected.items():
        if type(value[key]) is not type(item) or value[key]!=item:raise ValueError('render-receipt-identity')
    profile=next((row for row in catalog()['profiles'] if canonical(identity(row))==canonical(value['profile'])),None)
    if profile is None:raise ValueError('render-receipt-profile')
    observed={key:profile[key] for key in ('engine','color_space','graphics_api','settings','renderer_features','camera','capture')}
    observed.update(pipeline_type='UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset',renderer_type='UnityEngine.Rendering.Universal.UniversalRenderer',post_process_data=True)
    if canonical(value['observed'])!=canonical(observed):raise ValueError('render-receipt-observation')
    hardware=value['hardware']
    strings={'graphics_device_name','graphics_device_vendor','graphics_device_version','graphics_format','depth_stencil_format'}
    integers={'graphics_device_id','graphics_device_vendor_id','width','height'}
    if (not isinstance(hardware,dict) or set(hardware)!=strings|integers or
        any(not isinstance(hardware[key],str) or not hardware[key].strip() or len(hardware[key])>1024 for key in strings) or
        any(type(hardware[key]) is not int or hardware[key]<0 for key in integers) or
        hardware['width']!=width or hardware['height']!=height):raise ValueError('render-receipt-hardware')
    if any(hardware[key]!=value['observed']['capture'][key] for key in ('graphics_format','depth_stencil_format')):
        raise ValueError('render-receipt-hardware-format')
    return value
