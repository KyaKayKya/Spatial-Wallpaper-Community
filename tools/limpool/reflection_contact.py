"""Actual Player water crops: consecutive frames from all three eye sweeps."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
r=Path(__file__).resolve().parents[2]/'Validation/Limpool'
canvas=Image.new('RGB',(2100,1020),'#182128');draw=ImageDraw.Draw(canvas)
try:font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',20)
except OSError:font=ImageFont.load_default()
for axis,(name,start) in enumerate([('LEFT / RIGHT',40),('UP / DOWN',340),('NEAR / FAR',640)]):
    for version,label in enumerate(['prefilter-before','prefilter-after']):
        y=(axis*2+version)*170
        for col in range(3):
            frame=start+col
            im=Image.open(r/label/f'{frame:04}.png').crop((100,650,1500,895)).resize((700,123))
            canvas.paste(im,(col*700,y+35))
            draw.text((col*700+8,y+5),f'{name} / {"BEFORE" if version==0 else "AFTER"} / frame {frame}',font=font,fill='white')
canvas.save(r/'tracked-water-contact.png')
