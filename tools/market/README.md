# GitHub 社区市场工具

公开仓库为 [Spatial-Wallpaper-Community](https://github.com/KyaKayKya/Spatial-Wallpaper-Community)。作者流程见[创作者入门](../../docs/guides/壁纸市场创作者入门.md)，发布与治理见[维护指南](../../docs/guides/壁纸市场维护.md)。

当前发布器读取 GitHub API 元数据、重新下载精确 Release 字节，并调用固定摘要的 NativeValidator 检查 schema5、预算、许可声明与版本；它不执行投稿者代码。审核后的受保护工作流签名发布索引。客户端仍执行自己的资源准入和脚本隔离。

本目录同时保留早期 dry-run 工具及合成夹具。它们不是实际 SDK 准入或已上线状态的证据。真实工作流、在线签名下载和离线缓存结果记录在开发仓库的 native-market-live-20261114 证据中。

维护工具使用 Python 3.10+ 和 [requirements.txt](../../market/requirements.txt) 中的创作/发布依赖；播放器不需要安装这些工具。离线回归入口为 python -B -m unittest discover -s tests/market。
