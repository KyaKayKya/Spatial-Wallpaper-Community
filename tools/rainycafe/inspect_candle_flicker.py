"""Inspect time-separated, unmodified Player candle crops."""
from pathlib import Path
import csv,json
import numpy as np
from PIL import Image,ImageDraw
import sys
r=Path(__file__).resolve().parents[2]/'Validation/RainyCafe'
folder=sys.argv[1] if len(sys.argv)>1 else 'hold'
rows=list(csv.DictReader((r/folder/'capture.csv').open()))
print('CSV fields',list(rows[0]))
for row in rows:
 if row['frame'] in ('0','450','900','1050','1200'):print(row['time'],row['candle2_intensity'])
canvas=Image.new('RGB',(720,600));draw=ImageDraw.Draw(canvas)
for i,frame in enumerate(range(0,1350,150)):
 im=Image.open(r/f'{folder}/{frame:04}.png').convert('RGB').crop((120,90,360,270))
 x=(i%3)*240;y=(i//3)*200;canvas.paste(im,(x,y+20));draw.text((x+4,y+3),f't={frame/30:.1f}s',fill='white')
canvas.save(r/('details/candle-flicker-'+folder+'.png'))
for key in rows[0]:
 if 'intensity' in key:
  values=[float(v[key]) for v in rows];print(key,min(values),max(values))
