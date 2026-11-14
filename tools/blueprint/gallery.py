"""Bounded real Player gallery for light, quality and weather comparisons."""
from pathlib import Path
import subprocess,sys
from PIL import Image,ImageDraw
root=Path(__file__).resolve().parents[2]
cases=[('day-clear','clear',0,12,'clear','grass'),('dusk-natural','natural',1,16.8,'clear','waterfront'),('night-warm','warm',2,20,'clear','waterfront'),('cloudy','natural',1,15,'cloudy','grass'),('rain','warm',1,17,'rain','waterfront'),('snow','natural',1,15,'snow','snow'),('island','clear',1,16,'clear','island')]
for name,look,quality,hour,weather,terrain in cases:
 subprocess.run([sys.executable,str(root/'tools/blueprint/run.py'),'shot','--name',name,'--look',look,'--quality',str(quality),'--hour',str(hour),'--weather',weather,'--terrain',terrain,'--yaw','20'],check=True)
canvas=Image.new('RGB',(1280,3*385+385),(20,20,20));draw=ImageDraw.Draw(canvas)
for i,(name,*_) in enumerate(cases):
 picture=Image.open(root/'Validation/Blueprint'/name/'front.png');picture.thumbnail((640,360));x=i%2*640;y=i//2*385;canvas.paste(picture,(x,y));draw.text((x+10,y+365),name,fill='white')
canvas.save(root/'Validation/Blueprint/gallery.jpg')
