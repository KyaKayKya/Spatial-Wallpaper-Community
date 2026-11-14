# 开发 Spatial Wallpaper

本页帮助你定位公开源码、准备工具和运行相关检查。公开仓库是一份可审查的源码快照，**不是完整原作品 Unity 工程或播放器安装包**。

## 准备工具

| 工作 | 依赖 |
| --- | --- |
| 阅读、修改文档 | Git；Markdown 编辑器 |
| Python 工具和市场测试 | Python 3.10+；market/requirements.txt |
| SDK 创作、Unity 工程 | Unity 2022.3.62f3、URP 14.0.12；Windows x64/D3D11 |
| Windows 原生运行库 | 工程构建脚本要求的 .NET Framework 4.8 工具链及 Windows C++ 编译工具 |
| 使用完整发行播放器 | Windows x64/.NET Framework 4.8；运行依赖随便携包附带 |

从 [OMITTED_INPUTS.json](../../OMITTED_INPUTS.json) 查看源码快照未包含的原资产和依赖。你必须分别取得这些输入的许可；导出器不会下载它们。公开源码中的完整原作品构建脚本不能在缺少素材时直接生成等价作品，不能把缺失资产的报错当作普通安装步骤。

## 找到你要修改的模块

| 路径 | 职责 |
| --- | --- |
| Packages/com.spatialwallpaper.sdk | 作者 API、脚本转换、原生命令、资源准入与 Editor 导出 |
| Prototype/Assets/Desktop | 原 DesktopRuntime、窗口及托盘生命周期 |
| Prototype/Assets/Library | 原作品库 UI（市场协议和客户端位于 ContentRuntime/Core） |
| Supervisor | 隔离启动与进程管理 |
| tracker | 摄像头识别与追踪服务 |
| tools/market | 只读投稿检查、审核后签名发布、公开源码同步 |
| tools/desktop、tools/sdk | 运行库、Player 和便携发行构建工具 |
| tests | 对应模块的离线或集成检查 |

## 运行相关检查

在仓库根目录使用独立 Python 环境：

~~~powershell
python -m venv .venv
.venv\Scripts\python.exe -m pip install -r market/requirements.txt
.venv\Scripts\python.exe -B -m unittest discover -s tests/market
~~~

成功时 unittest 汇总显示 OK。此命令检查市场工具，不会运行作者提交的代码，也不代表 Unity 场景、摄像头、声音或物理多屏已验收。修改其他模块时只运行相关编译和最小检查；在 PR 中写明命令和实际结果。

## 理解两条作品路径

Unity 原生负责渲染，作者逻辑进入隔离进程；渲染不等待逐属性 RPC，也不通过整帧像素 IPC 搬运。完整内置作品引用可信原资产库；社区外来包经静态数据/Shader 准入后构造已知类型，支持边界见[导出指南](../guides/外来原生作品导出.md)。不要向可信宿主加载任意作者 DLL、MonoBehaviour 或 Editor 代码。

## 维护与发布

维护者在私有开发源的唯一活动 dev 分支完成集成及验证，再用完整冻结 SHA 导出公开源码。公开 README 的源模板在 docs/public；公开市场运营状态不归源码同步所有。按[公开源码发布流程](../guides/公开源码快照.md)执行 dry run、检查差异、提交公开任务 PR，再通过 merge commit 合并。

main 保存用户验收后的版本；投稿和源码PR仍指向唯一活动dev，不能将默认分支名直接当作投稿目标。预览版本号不等于稳定承诺。完整公开下载还需要单独核实运行库、模型、音乐和第三方资产的分发权。源码 PR、市场发布和完整桌面发行是不同的产物，不能用其中一个代替另一个。
