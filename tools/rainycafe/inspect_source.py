"""Inspect supplied archive metadata without executing embedded scripts."""
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT.parents[1] / 'art/RainyCafe'
with zipfile.ZipFile(SOURCE / 'RainyCafe_Start_Here.zip') as archive:
    for name in ('README_Chinese.txt', 'RainyCafe_Complete_Project.manifest.json'):
        print(name)
        print(archive.read(name).decode('utf-8-sig'))
