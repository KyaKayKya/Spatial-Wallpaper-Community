"""Stage only the verified original audio files for native Unity import."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parents[2]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    common = Path(subprocess.check_output(['git', 'rev-parse', '--path-format=absolute', '--git-common-dir'], cwd=ROOT, text=True).strip()).parent
    release = common / '.local-assets/releases/WallpaperSdk-20261110-preview3'
    originals = common / '.local-assets/sdk-audio-20261110'
    records = json.loads((release / 'music-sources.json').read_text(encoding='utf-8'))
    scenes = {'MiniShotGame': 'minishotgame', 'Train': 'train', 'Showcase': 'showcase', 'Parking': 'parking', 'RainyCafe': 'rainycafe', 'RainyCafe-rain': 'rainycafe'}
    target = (ROOT / 'Prototype/Assets/SdkNativeAudio').resolve()
    staged = []
    for record in records:
        source = originals / Path(urlparse(record['url']).path).name
        expected = record['sha256']
        if not source.is_file() or digest(source) != expected:
            raise ValueError('Original audio source missing or changed: ' + str(source))
        destination = target / scenes[record['scene']] / source.name
        staged.append((source, destination, {**record, 'file': destination.relative_to(ROOT).as_posix(), 'quality': 'unaltered original source bytes, not legacy mono 22050Hz SDK audio'}))
    for name in ('Hit.wav', 'Reset.wav'):
        source = release / 'AuthorProject/Assets/MiniShotGameSdk/Audio' / name
        if not source.is_file():
            raise FileNotFoundError(source)
        destination = target / 'minishotgame' / name
        staged.append((source, destination, {'scene': 'MiniShotGame-effect', 'file': destination.relative_to(ROOT).as_posix(), 'sha256': digest(source), 'bytes': source.stat().st_size, 'origin': 'retained original AuthorProject/Assets/MiniShotGameSdk/Audio/' + name, 'quality': 'unchanged original effect source bytes'}))
    for source, destination, record in staged:
        if not destination.resolve().is_relative_to(target):
            raise ValueError('Audio target outside owned native audio directory')
        if destination.exists() and digest(destination) != record['sha256']:
            raise FileExistsError('Refuse replacing existing native audio: ' + str(destination))
    for source, destination, record in staged:
        destination.parent.mkdir(parents=True, exist_ok=True)
        if not destination.exists():
            shutil.copyfile(source, destination)
        if digest(destination) != record['sha256']:
            raise ValueError('Audio copy digest mismatch')
    args.output.mkdir(parents=True, exist_ok=True)
    report = {'status': 'original-audio-staged-not-imported-or-audibly-validated', 'files': [record for _, _, record in staged], 'bytes': sum(source.stat().st_size for source, _, _ in staged)}
    (args.output / 'sources.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({'status': report['status'], 'files': len(staged), 'bytes': report['bytes'], 'output': str(args.output)}, indent=2))


if __name__ == '__main__':
    main()
