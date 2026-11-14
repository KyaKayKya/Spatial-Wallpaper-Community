# 制作你的第一件原生壁纸

本指南面向使用 Unity 的作者，帮助你安装 SDK 0.4.0-preview.1，导出一个可准入的 schema5 原生作品。scene-command ABI 为 1。普通用户只需要完整客户端，无需 Unity 或单独安装 Python。

## 准备作者工程

使用 Unity **2022.3.62f3**、URP **14.0.12**，目标 Windows x86-64／D3D11，Color Space 选择 **Linear**。准备你有权使用和分发的原创素材。

在 Package Manager 中选择 **Add package from tarball**，安装匹配发行的 com.spatialwallpaper.sdk-0.4.0-preview.1.tgz。若使用源码目录，将本包放入作者工程的 Packages/com.spatialwallpaper.sdk，并在 Packages/manifest.json 添加：

~~~json
"com.spatialwallpaper.sdk": "file:com.spatialwallpaper.sdk"
~~~

不要混用旧版运行库、验证器或 schema4 导出步骤。[旧版创作路径](LegacyGettingStarted.md)仅用于维护既有作品，不能把其包修改版本号作为 schema5 包。

## 先理解可以导出什么

外来原生包支持规范静态场景数据和隔离作者程序。内置五件原作品有独立可信原资产库，支持范围不能直接套用到外来包。

先阅读[完整导出边界](../../../docs/guides/外来原生作品导出.md)。骨骼、Animator、探针，以及脚本中的非空 Material/Mesh/Texture/AudioClip/Prefab 字段引用等尚不支持的内容会明确拒绝。不要删掉原效果后声称完整迁移，也不要把任意作者 DLL 或 MonoBehaviour 加入可信宿主。

## 设置作品身份与根节点

1. 新建一个 GameObject，添加 SceneDefinition。
2. 将 contentRoot 指向自己的场景根节点；设置 displayName、authorFrame 和许可来源。
3. 为 templateId 设置一个稳定的小写 GUID。更新作品时保留这个 GUID，使用新 revision。
4. 作者脚本标记 WallpaperScript，使用准入支持的成员。每个节点最多一个启用的作者组件。

屏幕是一扇固定窗口：默认保上下、左右裁剪或扩展，模型统一缩放。眼位只驱动离轴投影，不通过平移场景或旋转相机伪造视差。

View.GetEye() 和 View.GetSize() 读取最近可信快照，不授予作者相机或设备访问权。Storage 和查询使用异步结果；取消、撤权和旧回包不能当作本 Tick 的同步成功。原生准入规则保存在源码仓库根目录 tools/sdk/rules/native-scene-v1.json，最终以验证器诊断为准；[旧作者 API 表](AuthorApi.md)的版本为0.3，不是完整原生 API 清单。

## 导出并验证

在自己的 Editor 工具中调用真实入口，definition 为上述 SceneDefinition：

~~~csharp
SpatialWallpaper.Sdk.Editor.ExternalNativeExporter.Export(
    definition,
    "D:/MyWallpaper/work-1.0.0.spatial",
    "1.0.0");
~~~

选择尚不存在的输出路径。导出拒绝覆盖已有文件，成功产物仍是 local-validation，不能直接作为可投稿包。

使用同版本可信验证器检查实际字节：

~~~powershell
.\NativeValidator.exe --package D:/MyWallpaper/work-1.0.0.spatial --platform-index unused
~~~

外来包不读取平台库；unused 是为了兼容验证器 CLI 的占位参数。确认退出码为0，JSON中 valid 为true、schema为5、sdk为0.4.0-preview.1。准入通过不代表已实际看过画面或听过声音。

## 导入客户端体验

在匹配完整发行目录运行 Launch.exe，将 .spatial 拖入原 Library，选择作品并预览，再选择目标显示器显式应用。确认场景正常显示，交互与异步存档符合设计；关闭管理器后继续播放，从托盘重开。

更新安装不会自动切换正在播放的修订。需要体验新版时再次显式应用。完整操作、每目标声音与恢复见[发行与体验指南](../../../docs/guides/SDK0.4发行与体验.md)。公开下载中的 NativeValidator 不是播放器，不能独立播放作品。

## 分享和投稿

1. 用 prepare_shared_external.py --list-resources 生成实际资源与SHA清单。
2. 逐项准备真实分发权声明，再按[外来原生作品导出](../../../docs/guides/外来原生作品导出.md)生成并复验新的可分享包。
3. 将不可变产物放入自己的公开 GitHub Release，并在 wallpaper.json 声明准确版本和 capabilities。
4. 按[投稿指南](../../../docs/guides/壁纸市场创作者入门.md)提交一个绑定仓库/GUID/revision/packageSha256 的元数据 PR。

确认PR自动检查通过并经维护者合并后，才会发布到签名索引。来源登记或作者清单更新不会自动批准修订。

## 遇到问题

| 现象 | 下一步 |
| --- | --- |
| 不支持的组件或资源引用 | 对照外来导出边界，保留原作者工程，不静默降质 |
| 版本/ABI/摘要不一致 | 使用同一发行的 SDK、验证器与运行库重新导出 |
| 准入通过但没有播放器 | 取得完整客户端；验证器和源码快照不是安装包 |
| 旧包可用、新包被拒绝 | 核对 schema，勿用0.3的Editor快照流程冒充0.4 |
| 投稿后索引仍是旧版 | 检查确切修订投稿是否合并、实际包摘要及发布任务结果 |

报告问题时附 SDK/Unity/URP 版本、稳定GUID、修订、包摘要、错误文本及最小复现；不要上传凭据、私人存档或未获授权素材。
