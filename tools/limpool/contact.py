from pathlib import Path
from PIL import Image,ImageDraw
r=Path(__file__).resolve().parents[2]/'Validation/Limpool'
names=['front','left','right','up','down','near','far','near-left-down','near-left-up','near-right-down','near-right-up','far-left-down','far-left-up','far-right-down','far-right-up']
canvas=Image.new('RGB',(2000,750),'#111820');draw=ImageDraw.Draw(canvas)
for k,name in enumerate(names):
    im=Image.open(r/'stills-default'/(name+'.png')).resize((400,225))
    x=k%5*400;y=k//5*250;canvas.paste(im,(x,y));draw.text((x+8,y+229),name,fill='white')
canvas.save(r/'boundary-contact.png')
im=Image.open(r/'stills-default/front.png')
for name,box in [('water-detail',(100,620,1500,900)),('wall-light-detail',(1230,360,1600,680)),('beam-detail',(690,175,1240,540))]:im.crop(box).save(r/(name+'.png'))
