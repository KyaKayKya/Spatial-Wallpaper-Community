"""Inspect the right border over consecutive Player frames (no image synthesis)."""
import sys,json
from pathlib import Path
import numpy as np
from PIL import Image
r=Path(__file__).resolve().parents[2]/'Validation/RainyCafe'
label=sys.argv[1]
frames=range(900)
contact=Image.new('RGB',(30*24,700))
values=[];previous=None;worst_line=0
for index in frames:
 im=Image.open(r/'video'/f'{index:04}.png').convert('RGB')
 crop=im.crop((1576,100,1600,800))
 if 145<=index<175:contact.paste(crop,((index-145)*24,0))
 a=np.asarray(crop,dtype=float).mean(axis=2)
 edge=a[:450,-6:];line=0
 if previous is not None:
  changed=np.abs(edge-previous)>12
  for column in changed.T:
   run=0
   for pixel in column:
    run=run+1 if pixel else 0;line=max(line,run)
 previous=edge;worst_line=max(worst_line,line)
 values.append({'frame':index,'dark_edge_pixels':int(np.count_nonzero(edge<8)),'mean_edge':float(edge.mean()),'abrupt_vertical_run':line})
contact.save(r/f'right-edge-{label}.png')
(r/f'right-edge-{label}.json').write_text(json.dumps(values,indent=2))
jump=max(abs(a['mean_edge']-b['mean_edge']) for a,b in zip(values,values[1:]))
print(label,'consecutive right border crops saved; maximum adjacent-frame edge luminance change',jump)
print('Longest abrupt vertical pixel run:',worst_line)
if label=='after':assert jump<5 and worst_line<64, 'Abrupt edge flicker remains in consecutive frames'
