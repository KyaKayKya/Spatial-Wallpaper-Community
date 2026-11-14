"""Check composited desktop scene features against observed native references.

This is a diagnostic image check, not a quality or frame-time certification.
References must be visually reviewed at the same display, eye and resolution.
"""
from pathlib import Path
import argparse
import json
import math
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Validation/Dependencies/python'))
from PIL import Image


def signature(image):
    """Keep spatial colour structure; exclude icons, taskbar and right overlays."""
    width, height = image.size
    region = image.convert('RGB').crop((round(width * .32), round(height * .10),
                                        round(width * .80), round(height * .90)))
    return list(region.resize((16, 12), Image.Resampling.BOX).tobytes())


def compare(reference, actual):
    if reference.size != actual.size:
        raise AssertionError('desktop-reference-resolution-mismatch')
    expected, observed = signature(reference), signature(actual)
    return compare_signatures(expected, observed)


def compare_signatures(expected, observed):
    assert len(expected) == len(observed) == 16*12*3
    # Desktop notifications can cover a small part of the scene. Require the
    # remaining 80% of spatial cells to match, rather than judging whole-image
    # brightness. This deliberately cannot certify artwork quality.
    def error_at(cell):
        offset=cell*3
        return sum((a-b)**2 for a,b in zip(expected[offset:offset+3],observed[offset:offset+3]))/max(1,sum(a*a for a in expected[offset:offset+3]))
    cells=sorted(range(16*12),key=error_at)[:154]
    indices=[cell*3+channel for cell in cells for channel in range(3)]
    expected, observed = [expected[i] for i in indices], [observed[i] for i in indices]
    energy = sum(value * value for value in expected)
    # A black reference cannot establish scene visibility, including night scenes.
    if energy == 0 or max(expected) - min(expected) < 20:
        raise AssertionError('reference-has-no-scene-features')
    error = math.sqrt(sum((a - b) ** 2 for a, b in zip(expected, observed)) / energy)
    if error > .38:
        raise AssertionError('builtin-frame-visible-after-sdk-switch: spatial error %.4f' % error)
    return error


def observation(state, artwork):
    assert state['active_artwork'] == artwork and state['attached'] and state['scene_loaded'], state
    assert not state['paused'] and not state['preview'] and state['error'] is None, state
    target = next(display for display in state['displays'] if display['id'] == state['display_id'])
    assert [state['render_width'], state['render_height']] == [target['width'], target['height']], \
        'native-window-and-Unity-buffer-size-differ'
    details = state.get('native_presentation')
    if details is not None:
        camera, = [camera for camera in details['cameras'] if camera['selected']]
        assert camera['enabled'] and camera['active'] and camera['target_texture'] is None, camera
        assert not details['sdk_loaded'] and not details['sdk_static_held'], details
        assert details['rendered_artwork'] == artwork and details['rendered_camera'] == camera['id'], details
        assert 0 <= details['unity_frame'] - details['rendered_frame'] <= 2, details


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference-directory', type=Path, required=True)
    parser.add_argument('--run-directory', type=Path, required=True)
    args = parser.parse_args()
    cases = []
    for artwork in ['pool', 'limpool', 'mita']:
        reference = Image.open(args.reference_directory / ('direct-' + artwork + '.png'))
        # Prove darkness and a different work cannot pass just because IPC succeeded.
        for name, negative in [('black', Image.new('RGB', reference.size)),
                               ('different-work', Image.open(args.reference_directory /
                                ('direct-' + ('limpool' if artwork == 'pool' else 'pool') + '.png')))]:
            try:
                compare(reference, negative)
            except AssertionError:
                cases.append({'artwork': artwork, 'negative': name, 'rejected': True})
            else:
                raise AssertionError('visibility checker accepted ' + name + ' for ' + artwork)
        for prefix in ['direct-', 'after-sdk-', 'after-preview-', 'after-resume-']:
            path = args.run_directory / (prefix + artwork + '.png')
            if not path.exists():
                cases.append({'image': path.name, 'passed': False, 'error': 'missing-required-image'})
                continue
            case = {'image': path.name, 'passed': False}
            try:
                state = json.loads(path.with_suffix('.json').read_text(encoding='utf-8'))
                case['spatial_error'] = compare(reference, Image.open(path))
                observation(state, artwork)
                case['passed'] = True
            except AssertionError as failure:
                case['error'] = str(failure)
            cases.append(case)
    images = [case for case in cases if 'image' in case]
    report = {'passed': len(images) == 12 and all(case['passed'] for case in images),
              'scope': 'native scene visibility only; no RC, quality or performance claim',
              'cases': cases}
    output = args.run_directory / 'visibility-check.json'
    output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
