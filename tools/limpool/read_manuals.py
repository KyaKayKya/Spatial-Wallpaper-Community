from pathlib import Path
from pypdf import PdfReader
r=Path(__file__).resolve().parents[2]
for base in [r/'Prototype/Assets/VolumetricLights',r/'Prototype/Assets/Bakery/docs']:
    for p in base.rglob('*.pdf'):
        text='\n'.join(page.extract_text() for page in PdfReader(p).pages)
        out=r/'Validation/Limpool'/ (p.stem+'.txt');out.write_text(text,encoding='utf-8')
        print(p, len(text))
        for line in text.splitlines():
            if any(s in line.lower() for s in ['2022','urp','directional','depth','orthographic','shadow','2021']):print(line)
