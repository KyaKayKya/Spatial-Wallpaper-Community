# 摄像头识别 Tracker

Windows/macOS 共用的 Python 工作进程，使用 uv 管理。负责设备选择、采集、外部使用让出、MediaPipe 单脸识别及 TCP 测量输出。Runtime 的设置/需求汇总、Unity 校准、画面回退和平滑由对应模块实现。

公共消息定义见[接口文档](../docs/design/contracts/进程通信与追踪接口.md)，职责见[架构文档](../docs/design/tracking/摄像头识别架构.md)。Unity 独立宿主已接通 Tracker v1 的合成/录制链路并监督子进程；Manager 与安装包自动启动未实现。完整运行见[联调指南](../docs/guides/展示柜追踪联调.md)，验证边界见[联调证据](../docs/reference/evidence/integration/验证说明.md)。

## 开发环境与运行

在本目录执行，使用 Windows x64 或 macOS、Python 3.11。uv 会按 `.python-version` 和 `uv.lock` 建立本地 `.venv`：

```powershell
uv sync --locked
uv run spatial-tracker fetch-models
uv run spatial-tracker devices
```

`devices` 仅枚举，不打开相机。没有设备时正常返回 `{"catalog_ready": true, "devices": []}`。选择返回的 `camera_id`；不使用索引或名称匹配。

模型仅在显式执行 `fetch-models` 时下载到 `.cache/models`；正常运行不联网下载。每次加载校验固定 SHA-256，损坏文件报错，不静默覆盖。可用全局参数 `--model-dir PATH` 指向部署时预置的模型目录。

macOS 先在仓库根运行 `python3 tools/macos/build_native.py`，再执行相同 Tracker 命令。AVFoundation 动态库 的位置可用 `SPATIAL_MAC_CAMERA_LIBRARY` 显式配置；Player 内由适配器指定随包动态库。Mac 不启用 MF sensor 监视，外部活动保持 unknown/冲突检测；支持设备目录中的显式格式选择，实际生效参数需实机回归。设备与帧、权限和 Metal 完整验收见[双端指南](../docs/guides/双端开发与macOS运行.md)，不将 Windows 结果当 Mac 通过。

## 不接摄像头的验证

```powershell
uv run python tools/fetch_test_fixture.py
uv run spatial-tracker analyze-image .cache/fixtures/portrait.jpg
uv run pytest -q
uv run ruff check .
uv run ruff format --check src tests tools
```

图片分析输出流描述、头姿、眼位、有效性与质量；不修改用户设置，不发起采集。测试图片来自 MediaPipe 官方测试资源，下载后校验哈希。没有模型或图片时，相关真实推理/子进程测试明确 skip，其余测试仍可运行；完整验收要求先下载两类资源，不能把 skip 当通过。

测试分为：

- 严格帧解析、半包/粘包、非法输入和有界队列。
- 受控硬件适配器驱动真实会话：指定相机、暂停、让出与条件恢复、拔插、打开期间新意图、迟到回调、释放失败。
- Windows 原生无设备枚举、设备通知、活动监视启动/停止、真实 SoftwareBitmap 转 RGB。
- 独立合成投影检查坐标轴、四元数、鼻尖与眼位；真实 MediaPipe 人像识别和空白图丢失；换流重建模型跟踪状态。
- 真实 loopback TCP：握手、重复请求、覆盖旧意图、流描述先于样本、查询、失联释放和序号拒绝；真实子进程 stdin 启动与正常退出。

这些验证不能证明具体摄像头驱动、实际画面质量或与会议软件的兼容性。接入硬件后应验证 RGB 格式、旋转/镜像、实际帧率、长时间开关、双相机拔插，以及 Windows 相机/会议软件开始和结束使用时的句柄释放与恢复。本版能力的 `compatibility_status` 保持 `unverified`。

## 模拟与录制源

受管启动时可用 python -m spatial_tracker simulate，参数为 --scenario motion 或 faults，以及可选 --recording FILE。它仍读取相同 stdin 引导，复用 TrackerWorker 和正式消息；不打开真实相机。录制格式与 Unity 宿主启动命令见联调指南。

## Runtime 对接

Runtime 监听 `127.0.0.1:0`，启动工作进程，将以下结构作为 **stdin 的一行 UTF-8 JSON** 传入；令牌由 Runtime 随机生成，不放命令行、日志或场景包。下例只展示字段，不能把示例令牌用于产品：

```json
{"host":"127.0.0.1","port":54321,"token":"replace-with-runtime-generated-secret","runtime_instance_id":"11111111-1111-4111-8111-111111111111"}
```

启动入口为 `uv run spatial-tracker run`（开发）或虚拟环境内 `python -m spatial_tracker run`。工作目录和模型目录由启动者明确指定。Runtime 负责校验 Hello 中的角色令牌及受管进程身份，然后回 Welcome。完整公共信封不可省略；所有帧遵循 4 字节大端长度＋UTF-8 JSON。

Tracker 实现的消息：

| 方向 | 消息 | 用途 |
| --- | --- | --- |
| 发送/接收 | Hello / Welcome | 协议 1、连接身份与本机原生时钟域协商 |
| 接收 | SetTrackerIntent | 完整期望状态，统一启停、选设备、推理频率和恢复条件 |
| 接收 | GetState | 查询 cameras / tracking；不触发额外采集 |
| 发送 | OperationResult | accepted 后 completed / failed；重复请求返回记录 |
| 发送 | CameraCatalogChanged | 完整设备目录及版本 |
| 发送 | TrackerStateReport | 相机、识别及外部活动事实 |
| 发送 | TrackerStreamChanged | 新流描述或 null |
| 发送 | TrackingSample | 同帧头姿与眼位；只保留待发的最新一份 |
| 双向 | Ping / Pong | nonce 回显；6 秒无入站消息退出并释放 |

`SetTrackerIntent.payload` 的最小实例：

```json
{
  "intent_revision": "1",
  "camera_id": "use-camera-id-from-catalog",
  "capture_requested": true,
  "auto_resume_allowed": true,
  "capture_preference": {"mode_id": null, "max_inference_hz": 20},
  "required_features": ["head_pose", "eye_pose"],
  "retry_generation": "0"
}
```

关闭时发更高 `intent_revision` 且 `capture_requested=false`。恢复竞争后的采集由 Runtime 将 `auto_resume_allowed` 合成为“壁纸仍需要追踪且用户未暂停”；Tracker 不持有 UI 状态。主动重试递增 `retry_generation`，仍不覆盖已知外部占用。

Windows 默认 `mode_id=null` 使用 SharedReadOnly，不改相机全局格式；经`camera_modes_v1`能力协商后，用户可显式选择当前相机SupportedFormats中的模式ID，以ExclusiveControl配置原生分辨率和帧率，失败不会静默降级。启用设备后通过CameraModesChanged提供支持列表，未开启时modes=[]表示尚未查询。实际生效值在StreamDescriptor.actual_mode报告。推理限频1—60Hz与采集FPS、诊断预览≤8fps分别计算，不承诺达到上限。完整字段与控制权约束见[接口8.7](../docs/design/contracts/进程通信与追踪接口.md#87-显式采集模式2026-10-21)。

控制连接关闭立即开始释放并退出；不自行重连。Runtime 负责进程监督，必须确认进程退出才能把无法确认的释放判为完成。没有自动选择第一台相机、备用设备、人物 ID、视频传输或校准接口。

## 模块与数据含义

| 模块 | 职责 |
| --- | --- |
| camera.py / activity.py | WinRT 设备通知与共享采集；MF sensor 活动事件，排除自身 PID |
| session.py | 串行设备所有权、最新意图、让出/恢复及有界重试 |
| estimator.py / assets.py | 固定模型资源、单脸 LIVE_STREAM、PnP 几何和源描述 |
| protocol.py / worker.py | 帧校验、有界发送、异步操作、心跳与 Runtime 生命周期 |
| cli.py | 开发命令及受控启动入口 |

测量坐标为未镜像相机图像右方 +X、上方 +Y、朝被摄者 +Z。头姿原点是 canonical 模型索引 1；眼参考点取索引 33、133、263、362 的均值。模型先减鼻尖原点，再将厘米转为标称米；旋转也转换到同一轴约定，四元数顺序为 xyzw。

`model_m` 来自固定脸模型，不是个人真实尺度。当前 PnP 内参采用 `fx=fy=图像宽度`、主点为图像中心、零畸变的先验；流和样本均明确标记，Unity 校准适配层负责空间变换和尺度修正；真实校准求解与交互仍待实现和验收。重投影 RMS 超过 `max(8 px, 图宽×0.02)` 时标 degraded、数据无效；阈值仍需实机调优，不是精度承诺。

MediaPipe 自带连续跟踪/单脸平滑；不另做人物锁定。每次新采集流重建其内部跟踪历史，旧流回调丢弃。无脸时立即输出 searching/lost 与 null 位姿，画面短时保持、回默认和平滑恢复仍由 Unity 实现。

外部使用优先依靠 MF sensor 活动报告；监视不可用时降为共享控制变化/冲突检测。已让出但活动 unknown 时保持让出，不循环抢开；手动重试或可信 inactive 且允许恢复时再协调。原生事件的覆盖范围取决于驱动和使用应用，不宣称能预知所有应用未来的开机请求。

## 资源来源与验证记录

资源均不提交 Git；SHA-256 在 `assets.py` / `tools/fetch_test_fixture.py` 固定：

| 资源 | 官方来源 | SHA-256 |
| --- | --- | --- |
| Face Landmarker float16 v1 | [模型文件](https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task) | 64184e229b263107bc2b804c6625db1341ff2bb731874b0bcc2fe6544e0bc9ff |
| Canonical face model | [MediaPipe 源码资源](https://github.com/google-ai-edge/mediapipe/blob/master/mediapipe/modules/face_geometry/data/canonical_face_model.obj) | 8bac80443397e113f41a8b565ea72c59390bc031d9defab289dba7bc0c54e618 |
| Portrait 测试图 | [官方测试图片](https://storage.googleapis.com/mediapipe-assets/portrait.jpg) | a6f11efaa834706db23f275b6115058fa87fc7f14362681e6abe14e82749de3e |

MediaPipe 代码与 canonical 资源遵循其 [Apache-2.0 LICENSE](https://github.com/google-ai-edge/mediapipe/blob/master/LICENSE)；模型使用及发布依据见[官方 Face Landmarker 模型说明](https://ai.google.dev/edge/mediapipe/solutions/vision/face_landmarker#models)。本次开发缓存不等于完成安装包资源分发审查。

历史验证条件与结果见[验证说明](../docs/reference/evidence/tracker/验证说明.md)。这里保留资源来源和直接使用入口，不维护第二份审查过程。

Windows 适配依据：[共享帧读取](https://learn.microsoft.com/en-us/windows/apps/develop/camera/process-media-frames-with-mediaframereader)、[独占控制变化事件](https://learn.microsoft.com/en-us/uwp/api/windows.media.capture.mediacapture.capturedeviceexclusivecontrolstatuschanged)、[MF sensor 活动监视](https://learn.microsoft.com/en-us/windows/win32/api/mfidl/nf-mfidl-mfcreatesensoractivitymonitor)。
