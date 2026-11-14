# SDK 验证工具

当前产品入口是原作品库的 `Launch.exe`。构建使用 `py -3 tools/desktop/build.py`，主入口有关组合使用 `py -3 tools/desktop/check_main_manager.py`，以通过的报告调用 `tools/desktop/preserve_main_manager.py --evidence <报告目录>` 保存完整本机交付。`build_client.py` 与旧 `verify_delivery.py assemble` 已退役；历史独立客户端报告只保留原范围，不能代替主管理器验证。`verify_delivery.py` 的库存函数和历史候选核验仍供现有检查使用。

本轮主管理器整合不是 RC；完整 D—G、输入长测、性能及原事故关闭标准仍适用。当前进度与证据见[同一恢复点](../../docs/work/sdk-runtime/README.md)。

运行 py -3 tools/sdk/verify.py --stage A --platform windows 验证Windows机制可行性；运行 py -3 tools/sdk/verify.py --stage B --platform windows 验证内容契约。C执行机制和D资源/监督统一验收已实现；E—G及macOS尚未实现总验收，--all不会因跳过而成功。

B命令检测生成规则漂移，在干净Unity工程编译SDK并构建原创Shader夹具，编译同一C#准入源码执行攻击用例，再在AppContainer内运行正负包。首次需要固定Unity、UPM依赖缓存及Mono工具链；缺环境返回失败。generate_rules.py从rules/v1.json生成运行时目录。

正式字段和当前限制见[SDK内容契约](../../docs/design/contracts/SDK内容契约.md)。目录是执行绑定前的候选契约；通过B不能对陌生包开放生产运行。报告绑定本次源码摘要；完整SDK还需E—G及平台适用验收；C/D机制通过不等于产品交付完成。
