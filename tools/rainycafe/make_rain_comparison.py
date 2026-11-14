"""Show unaltered same-eye, same-time window crops for rain review."""
from pathlib import Path
from PIL import Image,ImageDraw
r=Path(__file__).resolve().parents[2]/'Validation/RainyCafe'
box=(395,150,1090,570)
canvas=Image.new('RGB',(1390,448));draw=ImageDraw.Draw(canvas)
for i,(folder,label) in enumerate([('stills-legacy-rain','Legacy procedural rain'),('stills','COZY particles, adapted to the cafe')]):
 canvas.paste(Image.open(r/folder/'front.png').convert('RGB').crop(box),(i*695,28))
 draw.text((i*695+8,7),label+' | same eye, t=2s',fill='white')
canvas.save(r/'details/rain-comparison.png')
print('Saved same-eye rain comparison')
