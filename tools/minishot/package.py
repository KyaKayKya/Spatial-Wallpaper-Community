"""Package the verified MiniShot author project without caches or other scenes."""
from pathlib import Path
import argparse,hashlib,json,os,shutil,subprocess,sys,zipfile
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'))
from verify_delivery import verify_candidate
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--delivery',type=Path,required=True);p.add_argument('--preview',type=Path,required=True);p.add_argument('--client-proof',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
 delivery=a.delivery.resolve();preview=a.preview.resolve();proof=a.client_proof.resolve();out=a.output.resolve();project=ROOT/'Validation/Sdk/v-minishot';identity=verify_candidate(delivery)['identity']
 assert json.loads((preview/'report.json').read_text())['status']=='passed-development-preview-not-RC'
 assert json.loads((proof/'report.json').read_text())['status']=='passed-installed-native-MiniShot-client-not-RC'
 assert not out.exists(),'refuse to overwrite an existing trial or author project';out.mkdir(parents=True)
 creator=out/'MiniShotGame-Author';creator.mkdir();shutil.copytree(project/'Assets/MiniShotGameSdk',creator/'Assets/MiniShotGameSdk');shutil.copyfile(project/'Assets/MiniShotGameSdk.meta',creator/'Assets/MiniShotGameSdk.meta')
 shutil.copytree(project/'ProjectSettings',creator/'ProjectSettings')
 packages=creator/'Packages';packages.mkdir()
 tarball='com.spatialwallpaper.sdk-'+identity['sdk']+'.tgz'
 for name in ['manifest.json','packages-lock.json',tarball]:shutil.copyfile(project/'Packages'/name,packages/name)
 manifest=json.loads((packages/'manifest.json').read_text());assert manifest['dependencies']['com.spatialwallpaper.sdk']=='file:'+tarball
 assert digest(packages/tarball)==digest(delivery/tarball),'author project must use this exact installed SDK'
 scene='Assets/MiniShotGameSdk/Generated/MiniShotGameSdk.unity';assert (creator/scene).is_file()
 package=out/'MiniShotGame.spatial';shutil.copyfile(preview/'MiniShotGame.spatial',package)
 assert digest(package)==json.loads((proof/'report.json').read_text())['package_sha256'],'client proof is for another package'
 for path in [creator/'README.md',out/'README.md']:
  path.write_text('''# MiniShotGame SDK 内部试用

这是 SDK 0.3.0-preview.2 的开发预览，不是 RC。原场景与原作品库保持不变。

本机：双击 Start-MiniShotGame.cmd 打开专用试用作品库，已预装并应用 SDK 版 MiniShot。关闭管理器后壁纸继续运行；从托盘停止或退出。此入口使用单独 TrialData，不修改原作品库。

其他 Windows 电脑：解压同一候选的完整 SpatialWallpaperClient-windows-x64.zip，运行 Start-ContentManager.cmd，把 MiniShotGame.spatial 拖入并应用。无需 Unity Editor。保留 player/sdk-runtime 完整目录。

点击桌面空白处取得焦点，再点击靶盘使其变绿；空格清空十块靶盘。失焦时不接收按键。右上小点绿色表示存档就绪、蓝色表示收到交互焦点、橙色表示读写中、红色表示错误。存档错误时按空格重新读取。重启后恢复命中状态。

制作：解压 MiniShotGame-Author.zip，用 Unity Hub 打开工程，固定 Unity 2022.3.62f3、URP 14.0.12、Linear、D3D11。打开 Assets/MiniShotGameSdk/Generated/MiniShotGameSdk.unity。SDK tarball 已在 Packages 内，以相对路径安装；首次打开需解析 Unity 依赖。修改脚本后退出并重新进入 Play。

退出 Play，选中 SceneDefinition，在 Spatial Wallpaper > Sandbox Preview 制作快照，选择配套候选 runtime 目录，沙箱预览后导出同一快照。管理器中用“更新”替换已有试用作品并再应用，可以保留存档身份。

普通 Play 是制作反馈，正式客户端的作者脚本在 LPAC Worker 沙箱执行，经 Input、View、Storage 接口与宿主交互。没有相机控制、原始设备或任意文件接口。当前管理器导入后的名称仍显示 Imported sandbox content。

已验证真实预览/导出、桌面点击与存档、失焦隔离、正常重启恢复和清空；原 RC 长测、性能及完整 D—G 仍待完成。活跃 Play 重编译恢复未验证，极近眼位进入靶盘 near 裁剪范围时的拾取存在边界限制。NPR、真实外部 LLM 服务与 macOS 未验证或不受支持。

原素材和派生网格仅供用户内部试用；公开分发许可未核实，不上传或转售素材。SDK 版不要求与原画面逐帧一致，原版继续保留作回退。
''',encoding='utf-8')
 # Copy only the already stopped, dedicated test library. No user's library is read.
 trial=out/'TrialData';shutil.copytree(proof/'data/library',trial/'library');shutil.copytree(proof/'data/sdk-runtime/backend',trial/'sdk-runtime/backend')
 manager=os.path.relpath(delivery/'client/manager/ContentManager.exe',out)
 common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent
 attempts=os.path.relpath(common/'.local-assets/sdk-attempts/minishot/a',out)
 launcher='@echo off\r\nstart "" "%~dp0'+manager+'" --content-data-root "%~dp0TrialData" --sdk-attempts "%~dp0'+attempts+'"\r\n'
 (out/'Start-MiniShotGame.cmd').write_bytes(launcher.encode('ascii'))
 (creator/'README.md').write_text('''# MiniShotGame SDK 作者工程

用 Unity Hub 打开本工程，固定 Unity 2022.3.62f3、URP 14.0.12、Linear、D3D11。SDK 0.3.0-preview.2 的 tarball 位于 Packages，依赖为相对路径。首次打开需解析 Unity 包；无需原仓库、其他场景或商业插件。

打开 Assets/MiniShotGameSdk/Generated/MiniShotGameSdk.unity。普通 Play 用于制作反馈；修改脚本后退出并重新进入 Play。制作菜单 Spatial Wallpaper > MiniShotGame SDK > Create author scene 可以从随附 Source 重新生成场景，会先提示保存当前场景。

正式导出：退出 Play，选中 SceneDefinition，打开 Spatial Wallpaper > Sandbox Preview，制作快照并选择配套发行物的完整 runtime 目录，沙箱预览后导出同一快照。最终用户把 .spatial 拖入配套客户端并应用，无需 Unity Editor。

点击桌面空白处授焦，再点击靶盘变绿；空格重置。脚本通过 Input、View、Storage 接口工作，右上小点蓝色表示收到焦点，绿色表示存档就绪，橙色表示读写中，红色表示错误。

此工程与素材仅供用户内部试用，公开分发许可尚未核实。开发预览不是 RC；活跃 Play 重编译恢复与极近裁剪下的拾取存在已说明限制，完整长测和性能验收仍待完成。
''',encoding='utf-8')
 files={p.relative_to(creator).as_posix():digest(p) for p in creator.rglob('*') if p.is_file()}
 with zipfile.ZipFile(out/'MiniShotGame-Author.zip','w',zipfile.ZIP_DEFLATED) as archive:
  for name in sorted(files):archive.write(creator/name,'MiniShotGame-Author/'+name)
 report={'status':'packaged-internal-MiniShot-development-preview-not-RC','identity':identity,'delivery':str(delivery),'preview':str(preview),'client_proof':str(proof),'package_sha256':digest(package),'author_files':files,'author_zip_sha256':digest(out/'MiniShotGame-Author.zip'),'local_trial_data':'dedicated stopped test library; original user library untouched'}
 (out/'package-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out,flush=True)
if __name__=='__main__':main()
