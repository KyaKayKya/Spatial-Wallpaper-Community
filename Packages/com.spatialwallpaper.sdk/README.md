# Spatial Wallpaper SDK

本目录是 `0.3.0-preview.2` 开发候选，与规则目录的 SDK 版本一致。提供五个程序化样例；本次增量通过 MiniShotGame 验证只读视图、作者脚本和真实沙箱预览。完整发行与 RC 总验收尚未完成。

作者工程固定使用 Unity **2022.3.62f3**、URP **14.0.12**、Linear 色彩空间，目标为 Windows x86-64／D3D11。SDK 不依赖 Tracker 或摄像头服务。作者只使用宿主提供的视口和眼位；设备采集与校准由可信客户端负责。

## 在隔离工程中安装

1. 使用上述 Unity 版本创建工程，关闭 Unity Editor。
2. 把本目录完整复制到工程的 `Packages/com.spatialwallpaper.sdk`。
3. 在工程的 `Packages/manifest.json` 的 `dependencies` 中加入 `"com.spatialwallpaper.sdk": "file:com.spatialwallpaper.sdk"`。
4. 打开工程，等待 Package Manager 解析包中固定的 URP 依赖。在 Player 设置中把 Color Space 设为 **Linear**。

已有工程请先备份。不要覆盖其他 SDK 版本目录，也不要把仓库的 `Prototype` 工程当成空工程安装验证。

## 制作、预览和导出

1. 在作者场景中设置 `SceneDefinition`，指定 `contentRoot`、作者参考框和模板 UUID。内容应位于 `contentRoot` 下。
2. 打开 **Spatial Wallpaper > Sandbox Preview**，选择场景定义。
3. 点击 **检查并制作预览快照**。遇到不支持的组件、脚本成员或渲染状态时，修正诊断后重新制作快照。
4. 选择同版本的完整**运行时发行目录**。它必须包含经过清单校验的 Native 预览程序、Supervisor、Validator 和 Worker；单独一个 EXE 不够。运行时发行物仍在准备，仓库的历史验证目录不能作为正式安装源。
5. 确认虚拟屏幕分辨率和物理尺寸，点击 **沙箱预览**。画布在独立可信窗口中，作者逻辑在受限 Worker 中执行。普通 Play 不能代替沙箱验证。
6. 取得真实呈现收据后，点击 **导出已验证快照**。导出保留已预览的同一份字节；修改场景后需重新制作并验证快照。
7. 关闭预览或点击 **停止**。若旧预览资源尚未证明释放，先使用 **重试恢复并确认旧预览退出**，不要启动替代实例。

完整操作步骤见[入门指南](Documentation~/GettingStarted.md)，成员和参数限制见同一规则源生成的[作者 API](Documentation~/AuthorApi.md)。虚拟屏幕选项不等于相应配置已获正式支持。

## 当前边界

- 固定窗口投影使用一份参考框和统一 XYZ 缩放；宿主负责物理尺寸、视口与眼位。
- 包携带检查后的数据、资源和受限程序表示，不携带可直接加载的作者程序集、设备设置或任意 Renderer Feature。支持目录以同版本规则与可信渲染配置为准。
- 桌面原生输入、真实客户端导入/应用和托盘生命周期已有开发验证；完整长测、性能和支持配置验收仍待 RC。
- LLM 默认使用确定性本机模拟回复；外部提供方、TLS 与模型效果未验证。
- 预览和客户端提供音源应用选择，普通 Play 与沙箱共用工程选择；默认不采集，来源退出不换源。操作见[选择音频来源](Documentation~/GettingStarted.md#选择音频来源)。
- NPR 在兼容与许可验证完成前不属于正式支持。
- 在 Package Manager 导入 **Starter Kit** 后，通过 **Spatial Wallpaper > Samples** 创建新场景；五个样例说明见[入门指南](Documentation~/GettingStarted.md)。`StaticWindow` 保留为历史静态样例。素材的本地验证不构成再分发授权。

渲染扩展与素材边界见[渲染与资源边界](Documentation~/Rendering.md)。main 合并仍等待用户对最终版本的明确验收。
