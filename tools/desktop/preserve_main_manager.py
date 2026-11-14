"""Preserve the verified full original manager, SDK/runtime and existing author delivery."""
from pathlib import Path
import argparse,copy,hashlib,io,json,shutil,subprocess,sys,tarfile,zipfile
from verify_receipt import validate
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from release_identity import identity
from verify_delivery import verify_inventory
from assembly_inventory import identities
from package_author import package as package_author
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--evidence',type=Path,required=True);p.add_argument('--name',default='Desktop-SDK-Integrated-20261111');p.add_argument('--resume-incomplete',action='store_true');args=p.parse_args()
 assert args.name and all(c.isalnum() or c in '-_' for c in args.name)
 receipt=validate();evidence=args.evidence.resolve();report=json.loads((evidence/'report.json').read_text());assert report['status']=='passed-main-manager-development-combination' and report['leftover_owned_players']==[]
 product=ROOT/'Prototype/Builds/Library';assert report['launcher_sha256']==sha(product/'Launch.exe') and report['assembly_sha256']==sha(product/'SpatialWallpaper_Data/Managed/Assembly-CSharp.dll')
 common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent;target=common/'Prototype/Builds'/args.name;assert target.resolve().is_relative_to((common/'Prototype/Builds').resolve())
 if target.exists():
  assert args.resume_incomplete and not (target/'delivery.json').exists(),'completed/existing delivery is immutable'
  for name,digest in receipt['files'].items():assert sha(target/name)==digest,name
 previous=common/'.local-assets/releases/WallpaperSdk-20261110-preview3';expected=identity();expected.pop('package_sha256');runtime=json.loads((product/'sdk-runtime/runtime.json').read_text());assert {k:runtime[k] for k in expected}==expected;verify_inventory(product/'sdk-runtime',runtime['files'],exclude=['runtime.json'])
 versions=identities([product/'SpatialWallpaper_Data/Managed/SpatialWallpaper.Sdk.dll',product/'sdk-runtime/worker/player/Worker_Data/Managed/SpatialWallpaper.Sdk.dll',product/'sdk-runtime/SpatialWallpaper.Preview.exe',product/'sdk-runtime/supervisor/Supervisor.exe']);assert all(v==expected for v in versions.values())
 catalog=json.loads((product/'SpatialWallpaper_Data/StreamingAssets/sdk-content/catalog.json').read_text());assert len(catalog)==5
 for item in catalog:
  package=product/'SpatialWallpaper_Data/StreamingAssets/sdk-content'/item['file'];assert sha(package)==item['sha256']
  with zipfile.ZipFile(package) as archive:manifest=json.loads(archive.read('manifest.json'))
  assert manifest['build_id']==item['build_id'] and all(manifest[k]==expected[k] for k in ['sdk','engine','pipeline','vm','backend'])
 if not target.exists():shutil.copytree(product,target)
 for name,digest in receipt['files'].items():assert sha(target/name)==digest,name
 shutil.copytree(ROOT/'tracker/src',target/'Tracker/src',ignore=shutil.ignore_patterns('__pycache__','*.pyc'),dirs_exist_ok=args.resume_incomplete)
 for name in ['pyproject.toml','uv.lock']:shutil.copy2(ROOT/'tracker'/name,target/'Tracker'/name)
 models=next((p for p in [ROOT/'tracker/.cache/models',common/'tracker/.cache/models'] if p.is_dir()),None)
 if models:shutil.copytree(models,target/'Tracker/.cache/models',dirs_exist_ok=args.resume_incomplete)
 (target/'Launch.cmd').write_bytes(b'@echo off\r\nstart "" /d "%~dp0" "%~dp0Launch.exe" %*\r\nexit /b\r\n')
 docs=target/'Documentation';docs.mkdir(exist_ok=args.resume_incomplete);shutil.copytree(previous/'notices',target/'notices',dirs_exist_ok=args.resume_incomplete)
 shutil.copy2(previous/'music-sources.json',target/'music-sources.json')
 original_names={'minishotgame':'MiniShotGame','train':'Train','showcase':'Showcase','parking':'Parking','rainycafe':'RainyCafe'}
 records=[{'name':original_names[item['original_id']],'package':'SpatialWallpaper_Data/StreamingAssets/sdk-content/'+item['file'],'sha256':item['sha256'],'build_id':item['build_id']} for item in catalog]
 (target/'packages.json').write_text(json.dumps(records,indent=2))
 for name in ['Windows桌面运行.md','作品库运行.md','校准演示运行.md','Minecraft蓝图壁纸运行.md']:shutil.copy2(ROOT/'docs/guides'/name,docs/name)
 for name in ['GettingStarted.md','AuthorApi.md','Rendering.md']:shutil.copy2(ROOT/'Packages/com.spatialwallpaper.sdk/Documentation~'/name,docs/name)
 # Refresh SDK source and generated scene assets, retaining licensed broker tools.
 author_receipt=package_author(previous,target)
 archiveEvidence=target/'Evidence/MainManager';archiveEvidence.mkdir(parents=True,exist_ok=args.resume_incomplete)
 for run in (ROOT/'Validation/Content/sdk-runtime/main-manager/runs').iterdir():
  if not (run/'report.json').is_file():continue
  dest=archiveEvidence/run.name;dest.mkdir(exist_ok=args.resume_incomplete)
  for path in run.rglob('*'):
   if not path.is_file():continue
   relative=path.relative_to(run)
   if path.suffix.lower()=='.png' or path.name in ['report.json','result.json','progress.json','expected-restart.json','manager.log','runtime.log','parent-invalid.json','status.json'] or relative.as_posix().endswith('/journal/state.json'):
    saved=dest/relative;saved.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,saved)
 quality=ROOT/'Validation/Content/sdk-runtime/quality-repair';saved=target/'Evidence/Quality';saved.mkdir(parents=True,exist_ok=args.resume_incomplete)
 for path in quality.rglob('*'):
  if path.is_file() and (path.name in {'report.json','result.json','error.txt','details.json','group-colors.txt'} or path.name in {'original-group-mask.png'}):
   relative=path.relative_to(quality)
   if any(part in {'desktop','calibration','Tracker','worker','player','runtime','supervisor','validator'} for part in relative.parts):continue
   dest=saved/relative;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,dest)
 for scene in ['Train','Showcase','RainyCafe']:
  build=next(item['build_id'] for item in catalog if original_names[item['original_id']]==scene);matches=[]
  for run in (ROOT/'Validation/Content/sdk-runtime/G/scenes'/scene).iterdir():
   result=run/'result.json'
   if result.is_file() and json.loads(result.read_text(encoding='utf-8-sig')).get('receipt',{}).get('build')==build:matches.append(run)
  assert matches,'missing matching LPAC scene evidence: '+scene
  run=max(matches,key=lambda path:path.stat().st_mtime);dest=saved/'Scenes'/scene;dest.mkdir(parents=True,exist_ok=args.resume_incomplete)
  for name in ['worker-front.png','result.json','report.json']:shutil.copy2(run/name,dest/name)
 shutil.copy2(ROOT/'Validation/Desktop/build-receipt.json',target/'Evidence/build-receipt.json')
 (target/'README.md').write_text('# Spatial Wallpaper 主管理器整合开发预览\n\n双击 Launch.exe，使用原作品库的导入 SDK、预览、应用及右侧声音控制。五个既有 SDK 包已内置；Pool、Limpool、Mita 和蓝图继续可用。更新后仍须显式应用。升级前从旧托盘完全退出；首次接入备份索引和设置，保留已有安装身份和私有存档。\n\n关闭管理器继续播放；托盘可重开、暂停、恢复原壁纸和完全退出。重启恢复最后实际应用的确切修订及音量。使用步骤见 Documentation/作品库运行.md 与 Windows桌面运行.md；制作作品见 Documentation/GettingStarted.md。作者工程为 WallpaperSdk-AuthorProject.zip，运行时直接选择 sdk-runtime。\n\n固定 Unity 2022.3.62f3 / URP 14.0.12 / normalized-4，包与 runtime 身份已核验。SDK 图像按显示区域原生渲染，最大边 1920、总像素不超过 2073600；保持原内存预算。雨夜咖啡馆恢复原表面、窗框、花盆及灯具，Train/Showcase 提高材质贴图精度。参数编辑、历史修订选择和现代包再分享授权尚未提供。\n\n本机交付复用主项目已有 Tracker Python 环境，包含 Tracker 源码与模型；不是无开发环境安装包。当前不是 RC；完整 D—G、输入长测、性能与物理摄像头、混合 DPI、系统恢复和 macOS 仍需冻结候选验收；NPR 与外部 LLM 未验证。素材仅本机内部试用，公开分发许可未核实。\n',encoding='utf-8')
 with (target/'README.md').open('a',encoding='utf-8') as notes:notes.write('\n当前相关短例在1080p实际桌面提交约46–48 FPS，尚未达到稳定60 FPS；有界 LOD 与原场景仍非逐像素一致。具体前后画面、失败与版本摘要见 Evidence；本轮不作为 RC。\n')
 summary=ROOT/'docs/reference/evidence/desktop/sdk-quality-repair-20261111.json'
 if summary.is_file():shutil.copy2(summary,target/'Evidence/sdk-quality-repair-20261111.json')
 metadata={'status':'verified-main-manager-development-delivery','source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'identity':expected,'assembly_identities':versions,'main_manager_evidence':evidence.name,'build_receipt_sha256':sha(target/'Evidence/build-receipt.json'),'sdk_packages':catalog,'scope':'full original Launch.exe manager; not standalone ContentManager; development combination, not RC','tracker_environment':str(common/'tracker/.venv/Scripts/python.exe'),'not_verified':['frozen RC D-G','long input','performance','physical camera','mixed DPI and physical system events','macOS','NPR','external LLM']}
 metadata['author_receipt_sha256']=sha(target/'author-receipt.json')
 metadata['files']={p.relative_to(target).as_posix():sha(p) for p in target.rglob('*') if p.is_file()};(target/'delivery.json').write_text(json.dumps(metadata,indent=2,ensure_ascii=False),encoding='utf-8');print(target,flush=True)
if __name__=='__main__':main()
