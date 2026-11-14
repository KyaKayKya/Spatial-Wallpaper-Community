# SDK 入门样例

这些样例使用项目自制的几何、纹理和 Shader，供内部 SDK RC 制作与验证。它们不包含角色、NPR 或第三方美术素材，也不扩大项目的公开发行授权。

## 创建场景

1. 在 Unity **2022.3.62f3** 中安装同版本 SDK，使用 URP **14.0.12**。
2. 在 **Project Settings > Player > Other Settings** 中将 **Color Space** 设为 **Linear**。
3. 在 Package Manager 的 SDK 页面导入 **Starter Kit**。
4. 从 **Spatial Wallpaper > Samples** 选择一个样例。

命令会提示保存已修改的场景，并在 `Assets/SpatialWallpaperSamples` 创建新的资源目录和场景。重复执行会使用新目录。样例不会改写已有场景；空工程缺少 URP 配置时会创建基本配置。

## 使用样例

| 样例 | 可观察行为 | 接入点 |
| --- | --- | --- |
| Interaction and storage | 获得输入焦点后按空格切换颜色；同一安装的存档保留颜色 | `Input`、带版本的异步 `Storage` |
| Audio response | 随宿主所选应用的低频特征改变颜色；没有有效音频时保持基础颜色 | `Audio.GetBand`，不采集设备或选择进程 |
| Dynamic mesh and texture | 三角形改变形状，小纹理持续更新 | 有归属的网格创建、绑定和纹理更新 |
| Shader and Prefab | 自制 Shader 明暗循环；Prefab 周期性生成和销毁 | 材质参数、经过检查的 Prefab |
| LLM reply and cancellation | 模拟回复到达后变绿并存入私有存档；空格取消待处理请求或重新发起 | 异步 `Llm`、取消和 `Storage` |

脚本只使用 SDK 已准入的成员和 `Start`、`Update` 回调。示范动画按作者更新帧推进，用于说明数据通路。复杂动画、`Animator` 和蒙皮角色不属于这些样例的支持声明。

## 沙箱预览和导出

1. 保持样例场景打开，在 **Spatial Wallpaper > Sandbox Preview** 选择场景的 `SceneDefinition`。
2. 制作预览快照，选择同版本完整运行时目录，再启动沙箱预览。
3. 验证画面和行为后，导出已验证快照。
4. 在独立客户端管理器中拖入导出包，点击 **应用**。

沙箱窗口和桌面输入遵循宿主的命中与焦点规则。普通窗口覆盖壁纸时不会向作者继续发送桌面输入。音频只由可信宿主配置，作者不能选择进程或回退到全局回环。LLM 使用确定性本机模拟时返回 `reply-as-data`；这不代表外部模型、TLS 或服务商已经验证。

普通 Play 使用安装内的能力代理和单独的预览存档。它保留 Unity 原始脚本行为，不能替代沙箱准入、实际预览与无 Editor 客户端验收。资源由沙箱实例统一回收；在同一长会话中替换动态资源时，应显式释放不再使用的资源。
