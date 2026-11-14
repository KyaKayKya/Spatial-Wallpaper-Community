"""Prepare licensed, local audio for the isolated SDK author projects."""
from pathlib import Path
import hashlib,json,math,shutil,struct,subprocess,wave,array
ROOT=Path(__file__).resolve().parents[2]
ASSETS=ROOT.parents[1]/'.local-assets/sdk-audio-20261110'

def cafe_mix(music,item,catalog):
    rain=next(entry for entry in catalog if entry['scene']=='RainyCafe-rain')
    original=ASSETS/Path(rain['url']).name
    assert hashlib.sha256(original.read_bytes()).hexdigest()==rain['sha256']
    target=ASSETS/'RainyCafe-sdk-mixed.wav';receipt=target.with_suffix('.json')
    processing='music x0.8; rain loudnorm I=-30 TP=-6 LRA7, mono PCM16 22050Hz, 0.25s loop crossfade; repeat rain under music'
    if not target.is_file():
        raw=ASSETS/'RainyCafe-rain.normalizing.wav'
        subprocess.run(['ffmpeg','-v','error','-nostdin','-y','-i',str(original),'-af','loudnorm=I=-30:TP=-6:LRA=7','-ac','1','-ar','22050','-c:a','pcm_s16le',str(raw)],check=True,timeout=120)
        with wave.open(str(raw),'rb') as audio:rain_samples=array.array('h',audio.readframes(audio.getnframes()))
        seam=round(.25*22050);loop=rain_samples[seam:]
        for i in range(seam):loop[len(loop)-seam+i]=round(rain_samples[len(rain_samples)-seam+i]*(1-i/seam)+rain_samples[i]*i/seam)
        with wave.open(str(music),'rb') as audio:mix=array.array('h',audio.readframes(audio.getnframes()))
        for i in range(len(mix)):mix[i]=max(-32768,min(32767,round(mix[i]*.8+loop[i%len(loop)])))
        with wave.open(str(target),'wb') as audio:audio.setnchannels(1);audio.setsampwidth(2);audio.setframerate(22050);audio.writeframes(mix.tobytes())
        raw.unlink();receipt.write_text(json.dumps({'source':item['source'],'music_derived_sha256':item['output_sha256'],'ambience':rain,'processing':processing,'output_sha256':hashlib.sha256(target.read_bytes()).hexdigest()},ensure_ascii=False,indent=2),encoding='utf-8')
    result=json.loads(receipt.read_text(encoding='utf-8'));assert result['output_sha256']==hashlib.sha256(target.read_bytes()).hexdigest()
    assert result['music_derived_sha256']==item['output_sha256'] and result['ambience']['sha256']==rain['sha256']
    return target,result,processing

def prepare(project,scene,folder=None):
    catalog=json.loads((Path(__file__).parent/'music.json').read_text(encoding='utf-8'))
    source=next(item for item in catalog if item['scene']==scene)
    original=ASSETS/Path(source['url']).name
    if not original.is_file() or hashlib.sha256(original.read_bytes()).hexdigest()!=source['sha256']:
        raise ValueError('Missing licensed music source; see '+source['page'])
    target=ASSETS/(scene+'-sdk.wav');receipt=target.with_suffix('.json')
    duration=min(float(source['probe']['format']['duration']),90)
    fade=.5 if duration<85 else 2
    filters=f'loudnorm=I=-24:TP=-3:LRA=7; first {duration} seconds; mono PCM16 22050Hz; {fade}s linear loop crossfade'
    if target.is_file() and target.stat().st_size<100:
        target.rename(target.with_suffix('.empty-red.wav'))
        if receipt.is_file():receipt.rename(receipt.with_suffix('.empty-red.json'))
    if not target.is_file():
        raw=target.with_suffix('.normalizing.wav')
        subprocess.run(['ffmpeg','-v','error','-nostdin','-y','-i',str(original),'-t',str(duration),'-af','loudnorm=I=-24:TP=-3:LRA=7','-ac','1','-ar','22050','-c:a','pcm_s16le',str(raw)],check=True,timeout=120)
        with wave.open(str(raw),'rb') as audio:
            assert audio.getnchannels()==1 and audio.getframerate()==22050 and audio.getsampwidth()==2
            samples=array.array('h',audio.readframes(audio.getnframes()))
        seam=round(fade*22050);assert len(samples)>seam*2
        body=samples[seam:]
        for i in range(seam):body[len(body)-seam+i]=round(samples[len(samples)-seam+i]*(1-i/seam)+samples[i]*(i/seam))
        with wave.open(str(target),'wb') as output:output.setnchannels(1);output.setsampwidth(2);output.setframerate(22050);output.writeframes(body.tobytes())
        raw.unlink()
        receipt.write_text(json.dumps({'source':source,'output_sha256':hashlib.sha256(target.read_bytes()).hexdigest(),'processing':filters,'format':'mono PCM16 22050Hz; bounded local development preview'},ensure_ascii=False,indent=2),encoding='utf-8')
    item=json.loads(receipt.read_text(encoding='utf-8'));assert item['output_sha256']==hashlib.sha256(target.read_bytes()).hexdigest()
    if scene=='RainyCafe':target,item,filters=cafe_mix(target,item,catalog)
    destination=Path(project)/'Assets'/(folder or scene+'Sdk')/'Audio';destination.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(target,destination/'Music.wav');(destination/'Music.source.json').write_text(json.dumps(item,ensure_ascii=False,indent=2),encoding='utf-8')
    text=f"{source['title']} — {source['author']}\n{source['page']}\nCC0 1.0: {source['license_url']}\nOriginal SHA256: {source['sha256']}\nDerived SHA256: {item['output_sha256']}\nProcessing: {filters}\nOriginal scene art: local trial only; public redistribution unverified.\n"
    if scene=='RainyCafe':
        rain=item['ambience'];text+=f"\nAmbience: {rain['title']} — {rain['author']}\n{rain['page']}\nCC0 1.0: {rain['license_url']}\nOriginal SHA256: {rain['sha256']}\n"
    if scene=='MiniShotGame':text+='\nHit/Reset effects: original procedural sine tones generated by tools/wallpapers/audio.py.\n'
    (destination/'LICENSE.txt').write_text(text,encoding='utf-8')
    if scene=='MiniShotGame':
        for name,reset in [('Hit',False),('Reset',True)]:
            samples=[];rate=22050;length=.22 if reset else .12
            for i in range(int(length*rate)):
                t=i/rate;frequency=(440 if t<.11 else 660) if reset else 900-450*t/length
                sample=.28*math.sin(2*math.pi*frequency*t)*min(1,t/.005)*max(0,1-t/length)**2;samples.append(struct.pack('<h',round(sample*32767)))
            with wave.open(str(destination/(name+'.wav')),'wb') as output:output.setnchannels(1);output.setsampwidth(2);output.setframerate(rate);output.writeframes(b''.join(samples))
    return item

if __name__=='__main__':
    import argparse
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('project',type=Path);p.add_argument('scene');a=p.parse_args();print(json.dumps(prepare(a.project,a.scene),ensure_ascii=False,indent=2))
