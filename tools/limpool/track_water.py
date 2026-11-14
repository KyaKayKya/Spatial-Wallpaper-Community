"""Track fixed water-plane points instead of confusing camera motion with flicker."""
from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np,csv,json,sys
r=Path(__file__).resolve().parents[2]/'Validation/Limpool'
labels=sys.argv[1:] or ['isolate-'+x for x in ['composite','reflection','transmission','specular','normal','fresnel']]
result={};canvas=Image.new('RGB',(1200,len(labels)*180),'#182128');draw=ImageDraw.Draw(canvas)
for row,label in enumerate(labels):
    traces=[];times=[]
    pts=np.array([(x,z) for x in np.linspace(-.23,.23,80) for z in np.linspace(-.26,.03,40)])
    for i,eye in enumerate(csv.DictReader((r/label/'capture.csv').open())):
        times.append(float(eye['time']))
        ex,ey,ez=[float(eye['eye_'+a]) for a in 'xyz']
        factor=-ez/(pts[:,1]-ez)
        px=(ex+(pts[:,0]-ex)*factor)/.53*1600+800
        py=450-(ey+(-.0761875-ey)*factor)/.298125*900
        valid=(px>2)&(px<1597)&(py>550)&(py<895)
        a=np.asarray(Image.open(r/label/f'{i:04}.png'),dtype=np.float32)
        x=np.clip(px,0,1598);y=np.clip(py,0,898);xi=x.astype(int);yi=y.astype(int);fx=(x-xi)[:,None];fy=(y-yi)[:,None]
        v=(a[yi,xi]*(1-fx)+a[yi,xi+1]*fx)*(1-fy)+(a[yi+1,xi]*(1-fx)+a[yi+1,xi+1]*fx)*fy
        v=v.mean(axis=1);v[~valid]=np.nan;traces.append(v)
        if i in [40,41,42,43,44,45]:
            col=i-40;crop=Image.fromarray(a.astype('uint8')).crop((100,650,1500,895)).resize((200,140));canvas.paste(crop,(col*200,row*180+30));draw.text((col*200+5,row*180+8),f'{label} {i}',fill='white')
    v=np.array(traces);d=np.abs(v[2:]-2*v[1:-1]+v[:-2])
    summary={}
    for side,mask in [('left',pts[:,0]<0),('right',pts[:,0]>0)]:
        q=d[:,mask];summary[side]={'mean_second_difference':float(np.nanmean(q)),'p95':float(np.nanquantile(q,.95)),'p99':float(np.nanquantile(q,.99)),'fraction_over_10':float(np.count_nonzero(q>10)/np.count_nonzero(np.isfinite(q)))}
    phases={}
    mid_times=np.array(times[1:-1])
    for name,start,end in [('left_right',0,10),('up_down',10,20),('near_far',20,30)]:
        phase_mask=(mid_times>=start)&(mid_times<end)
        if not phase_mask.any():continue
        phases[name]={}
        for side,mask in [('left',pts[:,0]<0),('right',pts[:,0]>0)]:
            q=d[phase_mask][:,mask];phases[name][side]={'p99':float(np.nanquantile(q,.99)),'fraction_over_10':float(np.count_nonzero(q>10)/np.count_nonzero(np.isfinite(q)))}
    summary['phases']=phases;result[label]=summary
canvas.save(r/'tracked-water-contact.png');(r/'tracked-water.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
