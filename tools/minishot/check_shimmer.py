"""Measure high-frequency temporal changes in the same target-free ceiling crop."""
import argparse
import json
from pathlib import Path
import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Validation/MiniShotGame'

def measure(folder):
    files=sorted(folder.glob('[0-9][0-9][0-9][0-9].png'))
    assert len(files)==64, 'Expected the complete 64-frame micro-motion capture'
    images=np.stack([np.asarray(Image.open(f).convert('RGB'),dtype=np.float32)[45:110,100:700].mean(axis=2)/255 for f in files])
    # Smooth motion has low second temporal differences. A discrete coverage jump spikes them.
    differences=np.diff(images,n=2,axis=0)
    return {'temporalSecondDifferenceRms':float(np.sqrt(np.mean(differences**2))),
            'meanLuminance':float(images.mean()),'frames':len(files),'crop':[100,45,700,110]}

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline',action='store_true')
    parser.add_argument('--candidate',default='aliasing-after')
    args=parser.parse_args()
    before=measure(OUT/'aliasing-before')
    if args.baseline:
        print(json.dumps(before,indent=2))
    else:
        after=measure(OUT/args.candidate)
        report={'before':before,'after':after,'ratio':after['temporalSecondDifferenceRms']/before['temporalSecondDifferenceRms']}
        report['pass']=report['ratio']<.6 and after['meanLuminance']>before['meanLuminance']*.7
        (OUT/'shimmer-checks.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
        print(json.dumps(report,indent=2))
        assert report['pass'], 'White grid temporal instability has not improved sufficiently'
