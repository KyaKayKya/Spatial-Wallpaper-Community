# Spatial Wallpaper SDK

为 Unity 场景编写受控交互，并将作者逻辑放入隔离进程执行。渲染由原生 Unity 负责，设备、桌面与托盘由原客户端管理。

**SDK 0.4.0-preview.1 · schema5 · scene-command ABI1**

[开始制作](Documentation~/GettingStarted.md) · [外来资源边界](../../docs/guides/外来原生作品导出.md) · [社区投稿](../../docs/guides/壁纸市场创作者入门.md)

## 安装

使用 Unity 2022.3.62f3 与 URP 14.0.12，Windows x86-64／D3D11／Linear。在 Package Manager 中安装同版本 tgz，或以本地目录包安装本目录。普通用户播放不需要 Unity Editor；SDK创作也不要求摄像头设备。

## 从原创场景开始

设置 SceneDefinition 的 contentRoot、稳定 GUID、构图参考框与许可来源，使用 ExternalNativeExporter 导出，再用匹配的 NativeValidator 校验实际文件。完整步骤与命令见[原生入门](Documentation~/GettingStarted.md)。

脚本使用真实 View 快照与异步存档/查询。受控资源命令、世代、撤权、预算和 GPU 退役分别约束作者行为；不能加载任意作者 DLL、插件或 Editor 代码进入可信宿主。

## 支持范围

内置原作品使用可信原资产目录，保留完整模型、材质、动画和声音。外来作者包接受严格静态数据/Shader子集；骨骼、Animator、探针及非空作者资源字段引用等未支持情况明确拒绝，不静默删减。

本包保留 [schema4历史入门](Documentation~/LegacyGettingStarted.md)、[0.3生成API表](Documentation~/AuthorApi.md)与[旧渲染说明](Documentation~/Rendering.md)，用于维护旧作品；它们不是0.4完整能力承诺。原生规则源位于 tools/sdk/rules/native-scene-v1.json（源码仓库），最终以匹配验证器和实际运行结果为准。

代码采用 [MIT](LICENSE.md)，第三方及素材许可独立；完整边界见[许可范围](PROJECT_LICENSE_SCOPE.md)。预览版本仍需真实画面、声音、交互、多屏和长期使用验收。
