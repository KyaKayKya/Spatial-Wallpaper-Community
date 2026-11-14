# 壁纸市场先行工具

这些 Python 工具供市场维护者验证候选索引，运行入口见[市场维护指南](../../docs/guides/壁纸市场维护.md)。它们只读 GitHub 元数据、计算附件摘要及生成 dry-run 计划；不执行壁纸、不接入 SDK 或播放器。

在仓库根目录运行离线回归与双作者夹具：

```cmd
python -m unittest discover -s tests/market
python -m tools.market dry-run --now 2026-10-26T00:00:00Z --output Validation/Market/dry-run.json
```

所需现有依赖及许可证见 [requirements.txt](../../market/requirements.txt)；本任务没有安装依赖。数据契约以 [schemas](../../market/schemas/) 和[正式设计](../../docs/design/content/GitHub壁纸市场.md)为准。测试夹具是 CC0 合成元数据和四字节文本，不是真实 SDK 包。
