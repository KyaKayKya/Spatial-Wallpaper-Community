"""Build and view the local original-material Mita author project."""
from pathlib import Path
import argparse, os, subprocess, sys, hashlib, json
from prepare import ROOT, PROJECT, COMMON, prepare, restore_tracking_models

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('mode',choices=['prepare','build','preview','capture','validate','desktop','desktop-test','record','benchmark','lifecycle','gestures','gaze','interactions'])
    parser.add_argument('--display',type=int,default=0)
    parser.add_argument('--tracking',choices=['simulate','camera'])
    parser.add_argument('--calibration',type=Path)
    parser.add_argument('--camera')
    parser.add_argument('--audio',type=Path)
    parser.add_argument('--keep-width',action='store_true')
    parser.add_argument('--demo',action='store_true')
    parser.add_argument('--interactions',action='store_true')
    args=parser.parse_args()
    if args.interactions and (args.mode!='record' or args.demo):parser.error('--interactions requires record without --demo')
    if args.audio and args.mode not in ['prepare','build']:parser.error('--audio requires prepare or build')
    if args.mode=='lifecycle' and not args.tracking:parser.error('lifecycle requires --tracking simulate or camera')
    output=ROOT/'Validation/Mita/capture';output.mkdir(parents=True,exist_ok=True)
    env=dict(os.environ,ALLUSERSPROFILE=os.environ.get('ProgramData',r'C:\ProgramData'),MITA_OUTPUT=str(output))
    if args.mode in ['prepare','build']:
        prepare(args.audio.resolve() if args.audio else None)
    if args.mode=='build':
        command=[r'D:\Unity\Editor\2022.3.62f3\Editor\Unity.exe','-batchmode','-quit','-projectPath',str(PROJECT),'-executeMethod','MitaBuilder.Build','-logFile',str(ROOT/'Validation/Mita/build.log')]
        subprocess.run(command,env=env,check=True)
        receipt={'unity':'2022.3.62f3','repository_head_at_build':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'compiled_sources':{},'products':{}}
        for path in sorted((PROJECT/'Assets').rglob('*.cs')):receipt['compiled_sources'][str(path.relative_to(PROJECT))]=hashlib.sha256(path.read_bytes()).hexdigest()
        for relative in ['Builds/Mita/Mita.exe','Builds/Mita/Mita_Data/Managed/Assembly-CSharp.dll','Assets/Generated/Mita.unity']:
            receipt['products'][relative]=hashlib.sha256((PROJECT/relative).read_bytes()).hexdigest()
        (ROOT/'Validation/Mita/build-receipt.json').write_text(json.dumps(receipt,indent=2),encoding='utf8')
    elif args.mode in ['preview','capture','validate','desktop','desktop-test','record','benchmark','lifecycle','gestures','gaze','interactions']:
        command=[str(PROJECT/'Builds/Mita/Mita.exe'),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',str(output/'player.log')]
        if args.mode=='capture':command+=['--capture']
        if args.mode=='validate':command+=['--validate']
        if args.mode=='gaze':command+=['--gaze']
        if args.mode=='interactions':command+=['--interactions-v2']
        if args.mode=='record':command+=['--record']
        if args.mode=='benchmark':command+=['--benchmark']
        if args.mode=='lifecycle':command+=['--lifecycle']
        if args.mode=='gestures':command+=['--gestures']
        if args.mode in ['desktop','desktop-test']:command+=['--desktop','--display',str(args.display)]
        if args.mode=='desktop-test':command+=['--desktop-test']
        if args.keep_width:command+=['--keep-width']
        if args.demo:command+=['--demo']
        if args.interactions:command+=['--interactions']
        if args.tracking:
            calibration=args.calibration or (PROJECT/'tracking-calibration-synthetic.json' if args.tracking=='simulate' else None)
            if calibration is None or (args.tracking=='camera' and not args.camera):parser.error('camera tracking requires --camera and --calibration')
            if args.tracking=='camera':restore_tracking_models()
            command+=['--tracking','--tracking-mode',args.tracking,'--tracking-calibration',str(calibration.resolve()),'--tracking-python',str(COMMON/'tracker/.venv/Scripts/python.exe'),'--tracking-root',str(ROOT/'tracker')]
            if args.camera:command+=['--tracking-camera',args.camera]
        startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=1
        subprocess.run(command,env=env,check=True,startupinfo=startup)
        if args.mode=='gaze':
            frames=len((output/'gaze.csv').read_text(encoding='utf-8-sig').splitlines())-1
            subprocess.run(['ffmpeg','-y','-hide_banner','-loglevel','error','-framerate','30','-i',str(output/'frames-gaze/%05d.png'),'-frames:v',str(frames),'-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-movflags','+faststart',str(output/'mita-gaze.mp4')],check=True)
        if args.mode=='record':
            suffix='-interactions' if args.interactions else '-parallax' if args.demo else ''
            encode=['ffmpeg','-y','-hide_banner','-loglevel','error','-framerate','30','-i',str(output/('frames'+suffix+'/%05d.png'))]
            audio=list((PROJECT/'Assets/Audio').glob('Bgm.*')) if (PROJECT/'Assets/Audio').exists() else []
            audio=[p for p in audio if not p.name.endswith('.meta')]
            filters=[];labels=[];index=1
            if audio:
                encode+=['-i',str(audio[0])];filters.append('[1:a]volume=0.25[bg]');labels.append('[bg]');index+=1
            if args.interactions:
                events=json.loads((output/'interaction-audio.json').read_text())['clicks']
                for i,time in enumerate(events):
                    encode+=['-i',str(PROJECT/'Assets/Xlmy/Audio/MitaMouseClick.wav')]
                    filters.append(f'[{index}:a]adelay={round(time*1000)}:all=1[click{i}]');labels.append(f'[click{i}]');index+=1
            if labels:
                filters.append(''.join(labels)+f'amix=inputs={len(labels)}:normalize=0[mixed]')
                encode+=['-filter_complex',';'.join(filters),'-map','0:v:0','-map','[mixed]','-c:a','aac','-b:a','192k']
            encode+=['-c:v','libx264','-crf','18','-pix_fmt','yuv420p','-frames:v','480','-t','16','-movflags','+faststart',str(output/('mita-opening'+suffix+'.mp4'))]
            subprocess.run(encode,check=True)

if __name__=='__main__':main()
