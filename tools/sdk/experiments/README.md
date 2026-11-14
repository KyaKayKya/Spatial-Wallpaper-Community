# SDK 阶段 A 实验

供SDK开发者重现实验，不是可安装SDK或生产沙箱。统一入口为上级verify.py的阶段A。实现与限制见仓库docs/reference/evidence/content/sdk-runtime/阶段A可行性.md。

- build_unity.py：新建独立Unity工程，固定源码校验、作者C#转换、移除原作者程序集并构建Player。
- windows_sandbox.py：实验性AppContainer/Job/非交互站启动器；只对生成运行目录设置ACL，采用显式环境和继承句柄列表。
- run_permissions.py：原生直接OS探针，普通进程作正对照。短暂注入并释放F24合成标记，不读取其他按键。
- run_render.py：普通进程、AppContainer及私有Desktop真实D3D帧对照。
- run_frame_ipc.py：继承管道的固定大小帧协议；解析头后才读负载。
- run_presenter.py：仅消费像素的可信预览/桌面呈现，退出时解除自己的挂载。
- LibraryProbe.cpp：保留LPAC动态加载失败的诊断源码，不参与通过方案。

原作者样例StageAAuthor.cs及本目录新建程序使用LICENSE的MIT许可。生成工程复制的Cilbox源码保留上游独立MIT许可证与版权；来源与逐文件摘要保存在每次source-receipt.json。
