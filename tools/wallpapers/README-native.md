# 导出原生作品包

面向维护原生发行物和编写隔离脚本的作者。原生作品使用 SDK 0.4.0-preview.1、schema5 和 scene-command ABI1；预览版本仍需人工体验验收。

## 准备依赖

普通用户运行发行包不需要 Unity 或 Python。作者使用 Unity 2022.3.62f3 和匹配 SDK；从源码构建验证器还需要本机 Unity 编译工具与 .NET Framework 4.8 参考程序集。发行物中的 NativeValidator.exe 只需要 Windows x64 和 .NET Framework 4.8。可选的 Python 打包命令需要 Python 3.9 或更高版本。

## 导出五件内置原生作品

1. 在包含原质量资产的 Prototype 工程中生成平台库。设置环境变量 SPATIAL_NATIVE_LIBRARY_OUTPUT 为新的证据目录，执行 SpatialWallpaper.Desktop.Editor.NativeSceneLibraryBuild.Generate。
2. 设置 SPATIAL_NATIVE_ARTWORK_OUTPUT 为新的输出目录，执行 SpatialWallpaper.Desktop.Editor.NativeArtworkExport.Run。
3. 核对 native-artworks.json 列出的五个 .spatial 文件、稳定 GUID、包摘要和场景资源数。
4. 使用同一发行物的可信 native-platform-library.json 检查每个包：

~~~powershell
.\NativeValidator.exe --package .\minishotgame.spatial --platform-index .\native-platform-library.json
~~~

成功时命令输出 valid:true、build_id、work_id、revision、schema、sdk、command_abi 和 sha256。退出码非零时停止发布。平台索引必须来自可信发行物；验证器不会把作者自报的索引当作信任根。

导出器只读取原场景，临时编译对象在导出后销毁。包内包含 manifest.json 与真正的隔离脚本 script.json；渲染资源由匹配的可信原生运行库提供。不要分发单独五包却遗漏匹配运行库。

## 修改脚本与重新打包

[异步视角和存档示例](../../Packages/com.spatialwallpaper.sdk/Samples~/NativeScripts/AsyncViewStorage.cs)展示 View、输入授权和真实异步存档调用。五件产品脚本位于 Prototype/Assets/NativeArtworks/Runtime；编译和资源绑定源位于 NativeArtworkExport.cs。作者程序集仅用于本地编译，不进入可信宿主。

从真实原生导出得到 manifest 与 script 后，可以执行以下命令。它调用发行验证器，通过后才以独占创建方式写出包，不覆盖已有文件：

~~~powershell
python .\tools\wallpapers\package_native.py --manifest .\manifest.json --script .\script.json --validator .\NativeValidator.exe --platform-index .\native-platform-library.json --output .\revised.spatial
~~~

保持 work_id 不变并修改 revision。包 build_id 是完整清单身份，管理器安装修订 ID 是独立 GUID。schema4 历史包不能通过改版本号迁移。

## 核对玩法和声音

MiniShot 使用原目标中心、半径和固定窗口坐标进行拾取；命中隐藏原靶盘及对应轨迹，Space 恢复全部目标，命中与重置分别播放原效果音。逐靶状态保存在稳定键 minishot.v1.target-01 至 target-10，异步失败可按 Space 重新读取，不伪造成功。撤权后输入不生效，存档取消由会话能力边界处理。

RainyCafe 的 rain_drops_2 音乐和 rain_on_window_loop 雨声分别循环；最终用户音量及静音覆盖脚本增益。模型、材质、贴图、透明、灯光、烘焙和可信内置动画沿用原场景。

导出与准入检查不证明画面或听感通过。人工检查五件画面、咖啡馆音乐雨声层次、MiniShot 不同眼位命中及重启存档，并核对用户静音后脚本不能恢复声音。

## 许可与投稿

导出器保留原资产的本地验证许可，未核实公开再分发权时 license_share 为 false。不要公开上传这些原资源，也不要将其标成已获许可的社区作品。社区作者应使用自己有分发权的资产，完整填写来源、许可与署名，再按发行包的市场投稿文档提交 PR。社区外来资源仍须经过相同准入和脚本隔离；这个内置平台库导出器不为任意外来 Scene/Prefab 提供准入证明。

## 使用动态资源入口

SceneResources 通过隔离原生命令桥异步上传；普通 Unity Play 未绑定时明确报错。BeginMesh(node, vertices, submeshIndexCounts) 生成请求；依次 WriteFloats、WriteIndices 填充完整数据，再 Seal。每个顶点包含 position(3)、normal(3)、tangent(4)、UV0(2)、UV1(2)、color(4)，共 18 个 float32；后接全部 submesh 的 uint32 三角索引。byteOffset 使用字节位置。BeginTexture(material, property, width, height) 接受 RGBA32 线性单 mip，以 WriteBytes 的 0–255 整数填充。原有其他格式路径继续保留。

每个 Tick 只执行一条资源操作并独占该批。float/index 单块至多 4096 元素，byte 单块至多 16384 元素，单资源至多 1 MiB。上传结束后空 Tick 自动查询真实进度；State 在收到 GPU fence 完成的 ACK 前保持 Pending。Ready 后调用 Apply 才替换 MeshFilter 或材质贴图；Applied 后 Release 恢复确切原绑定，再等待 GPU 退役释放预算。取消、撤权和旧回包不能把未发布资源设为 Ready 或重新应用。

如果硬件不支持 GraphicsFence，运行器禁用该动态入口并保留普通原生场景；不等待永远不会出现的完成状态。分阶段填充有字节上限，单次原生驱动调用尚无实测毫秒保证。
