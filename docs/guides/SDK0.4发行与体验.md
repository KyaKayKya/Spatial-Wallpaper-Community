# SDK 0.4 Windows 发行与体验

此版身份为 SDK 0.4.0-preview.1、schema5、scene-command ABI1。preview 表示接口和外来资源覆盖仍有边界，不代表稳定版。以发行目录 release-manifest.json 中的来源提交、完整 SHA256SUMS.txt 和外部自动验证报告为准。

## 安装与运行

采用仓库现有 Launch 的便携交付方式。将整个 ZIP 解压到新的空目录，双击 Launch.exe；保留同目录 SpatialWallpaper.exe、数据目录、sdk-runtime、native-runtime、Python 和 Tracker。普通用户无需安装 Unity 或 Python，系统需要 Windows x64 和 .NET Framework 4.8。不要覆盖正在运行的旧目录。

用户库和每作品声音/存档保存在原管理器用户数据位置。升级安装不会把正在播放的旧修订自动切成刚下载的新修订。在 Library 详情中明确应用新修订。应用失败应恢复该目标原作品的确切修订及声音设置。

关闭管理器后继续播放，从原托盘重开；完全退出后再删除便携目录完成卸载。完全退出应恢复系统壁纸。删除程序目录不等于删除用户作品或私人存档。

## 新旧作品

native-content 包含 Showcase、Parking、Train、RainyCafe、MiniShot 的新原生包；旧作品、Pool、Limpool、Mita 和蓝图保留。新五包使用独立目录别名，封面继续使用原作品封面。五包直接引用可信原资产库，不能脱离匹配 runtime 只靠两个 JSON 展示原模型。其资源不经降采样替代。

原始付费/混合来源素材的公开再分发许可尚未逐项证明；本地个人发行和公开代码/原创示例分开记录。不要把 local-validation 包提交为 share 包。参见原作品许可证据清单。

旧版先行者已有存档时，保留旧库项，在原生先行者详情选择“导入旧版先行者存档”，先停止该原生作品的桌面播放。入口核对官方旧包的确切构建和档案摘要，仅迁移十个合法命中键；已有原生存档时拒绝覆盖，原存档和旧修订保留。迁移成功后再显式应用原生先行者。

## 创作、导出与投稿

在 Authoring 中安装匹配 SDK tgz；Unity 创作依赖为 2022.3.62f3。导出工具、匹配 NativeValidator 和示例随发行提供。随包 CC0 原创示例使用修订 1.0.1；公开市场的既有 1.0.0 字节和历史保留，不把本轮重新导出的档案复用为同一旧修订。隔离进程只执行准入字节码，宿主不加载作者 DLL/MonoBehaviour。View 读取最近可信快照；存档和查询通过真正异步结果返回，不能假设本 Tick 即命中。

参见《外来原生作品导出》了解当前完整静态数据子集与隔离作者脚本的支持边界。骨骼、Animator、Probe 及作者外来资源引用尚不支持的情况会精确拒绝，不能以静默删减代替完整导出；原五包的可信平台资源路径保留原有这些效果。

在公开仓库 KyaKayKya/Spatial-Wallpaper-Community 按市场投稿指南提交注册/修订元数据 PR。二进制放作者公开 GitHub Release，稳定 GUID、修订、大小和摘要须与真实包一致。自动检查不执行投稿者代码，审核后才发布客户端签名索引。稳定索引为 https://github.com/KyaKayKya/Spatial-Wallpaper-Community/releases/download/market-index/published.json ，同目录 published.sig.json 绑定精确字节；不可变 market-published-N 发布保留审核历史。项目自有代码按 MIT 提供，范围见 LICENSE 与 PROJECT_LICENSE_SCOPE.md；第三方代码、原作品资产及新 CC0 示例保留各自许可。

## 评论与排行

公开首个示例的评论入口为 https://github.com/KyaKayKya/Spatial-Wallpaper-Community/issues/3 。详情页跳转对应 GitHub Issue，匿名可以浏览，参与需要 GitHub 登录；无需手工高权限 PAT。GitHub 隐藏或删除的内容不在客户端伪造保留。网络失败或限流会显示错误，已有索引可离线浏览；过期数据不能冒充新数据。

热度为 GitHub Release 资产在有足够连续采样时的最近七日下载增量，不是评分或安装人数。首次采样/缺失数据不伪造热度；此规则不能彻底防刷。下架不会静默删除本地包或改变正在播放的确切修订。

## 手动体验清单

以下需要用户实际体验，未执行不能标为通过：

- 五件作品的画面、透明、阴影、灯光、动画和原素材品质。
- RainyCafe 音乐与雨声独立循环、各目标音量及静音始终压过脚本。
- MiniShot 视角、真实点击命中反馈/重置/声音，以及退出重启后的存档。
- 原 Library 导入、预览、应用、更新不自动切换、卸载；坏包或应用失败恢复原确切修订。
- 关闭管理器继续播放、托盘重开、完全退出恢复系统壁纸、重启恢复确切项和声音。
- 两物理显示目标独立选择、离线目标、校准、切换与恢复；长时间使用体验。

开发自动检查不替代这些画面、听感和多屏验收。
