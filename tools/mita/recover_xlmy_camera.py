"""Extract the original Xlmy camera track without importing Timeline or game scripts."""
import json, re, shutil
import yaml
from prepare import COMMON

def extract(assets=None,target=None):
    cache=COMMON/'.local-assets/mita-xlmy'
    assets=assets or cache/'recovered-project/ExportedProject/Assets'
    text=(assets/'MonoBehaviour/Camera.playable').read_text(encoding='utf8')
    tracks=[yaml.safe_load(m[1])['MonoBehaviour'] for m in re.finditer(r'--- !u!114 &\d+\n(.*?)(?=\n--- !u!|\Z)',text,re.S)]
    motion=next(t for t in tracks if t.get('m_Name')=='Animation Track')
    def clip(name):return yaml.safe_load('\n'.join((assets/'AnimationClip'/name).read_text(encoding='utf8').splitlines()[3:]))['AnimationClip']
    exposure=clip('Recorded (1).anim')['m_FloatCurves'][0]['curve']['m_Curve']
    motion_clip=clip('Recorded.anim')
    target=target or cache/'minimal/Camera';target.mkdir(exist_ok=True)
    for suffix in ['', '.meta']:
        shutil.copy2(assets/('AnimationClip/Recorded.anim'+suffix),target/('Recorded.anim'+suffix))
    data={'offset_position':motion['m_InfiniteClipOffsetPosition'],'offset_euler':motion['m_InfiniteClipOffsetEulerAngles'],
          'field_of_view':7.9597573,'exposure':[{k:v[k] for k in ['time','value','inSlope','outSlope']} for v in exposure],
          'position':motion_clip['m_PositionCurves'][0]['curve']['m_Curve'],
          'euler':motion_clip['m_EulerCurves'][0]['curve']['m_Curve'],
          'source':'https://www.bilibili.com/video/BV1XZcNeaEsd/'}
    (target/'camera.json').write_text(json.dumps(data,indent=2),encoding='utf8')
    print('Recovered original camera track, offsets and exposure:',target)

if __name__=='__main__':extract()
