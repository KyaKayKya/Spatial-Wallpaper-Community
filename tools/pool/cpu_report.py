"""Summarize completed WARP trials separately from hardware GPU benchmarks."""
import csv
import json
import statistics
from pathlib import Path

root = Path(__file__).resolve().parents[2] / 'Validation/Pool'
hardware = json.loads((root / 'performance.json').read_text())['bench-high-1080']
reports = {}
def stats(values):
    ordered = sorted(values)
    def q(p):
        x = (len(ordered)-1)*p
        lo = int(x)
        return ordered[lo] + (ordered[min(lo+1,len(ordered)-1)]-ordered[lo])*(x-lo)
    return {'median': statistics.median(ordered), 'p95': q(.95)}

for name in ['cpu-warp-high-1080','cpu-warp-low-720']:
    folder = root/name
    if not (folder/'frames.csv').exists():
        continue
    info = (folder/'hardware.txt').read_text()
    assert 'GPU=Microsoft Basic Render Driver' in info, info
    manifest = json.loads((folder/'run-manifest.json').read_text())
    assert manifest['build_files']==hardware['provenance']['build_files'], 'Different build'
    rows = list(csv.DictReader((folder/'frames.csv').open()))
    phases = {}
    for phase,start,end in [('stationary',12,70),('moving',72,130)]:
        selected = [r for r in rows if start <= float(r['seconds']) < end]
        assert selected and float(selected[-1]['seconds'])-float(selected[0]['seconds'])>55, 'Incomplete run'
        data = {}
        data['render_hz']=(int(selected[-1]['rendered_count'])-int(selected[0]['rendered_count']))/(float(selected[-1]['seconds'])-float(selected[0]['seconds']))
        # Weight periodic process counters by wall time: slow frames must not be underrepresented.
        for field in ['process_cpu_percent','working_set_bytes','private_bytes']:
            seconds = {}
            for r in selected:
                seconds[int(float(r['seconds']))]=float(r[field])
            data[field]=stats(list(seconds.values()))
        seen=set()
        timings=[]
        for r in selected:
            if r['new_timing']=='1' and r['frame_timestamp'] not in seen:
                seen.add(r['frame_timestamp']);timings.append(float(r['cpu_frame_ms']))
        data['frame_ms']=stats(timings)
        phases[phase]=data
    reports[name]={'hardware':info,'phases':phases,'arguments':manifest['arguments'],
                   'same_build_as_gpu':True,'window':json.loads((folder/'window-state.json').read_text())}

(root/'cpu-performance.json').write_text(json.dumps(reports,indent=2))
lines=['# CPU 软件渲染实测','',
       '同一 Pool 独立构建，i7-14700KF。通过 `-force-d3d11 -force-driver-type-warp` 强制软件光栅化；两轮均核实 Microsoft Basic Render Driver。预热 10 秒，静止/移动各 60 秒，统计排除阶段前 2 秒。真实眼动追踪未启用。','',
       '| 配置 | 眼位 | 实际 FPS | CPU 中位/P95 % | 驻留内存 MiB | 私有提交 MiB | 帧时中位/P95 ms |',
       '|---|---|---:|---:|---:|---:|---:|']
for name,run in reports.items():
    for phase,s in run['phases'].items():
        lines.append(f'| {name} | {phase} | {s["render_hz"]:.2f} | {s["process_cpu_percent"]["median"]:.2f}/{s["process_cpu_percent"]["p95"]:.2f} | {s["working_set_bytes"]["median"]/1048576:.1f} | {s["private_bytes"]["median"]/1048576:.1f} | {s["frame_ms"]["median"]:.2f}/{s["frame_ms"]["p95"]:.2f} |')
lines += ['', 'CPU 百分比按全部逻辑处理器归一化；没有满载不代表能达到 60 FPS。WARP 的 FrameTimingManager GPU 时间对应软件后端工作，不能作为物理 GPU 利用率。场景渲染使用 CPU，Windows 桌面合成和屏幕显示仍可能使用 GPU。内存两种口径不能相加。低档同时降低分辨率与反射成本，不能把差异单独归因于一个参数。', '',
          '同构建 GPU 高档 1080p 移动眼位参考：约 59.94 FPS、CPU 0.45%、驻留内存 191.82 MiB；详见 performance.json。', '',
          '启动参数依据：[Unity 2022.3 Player 参数](https://docs.unity.cn/Documentation/Manual/PlayerCommandLineArguments.html)；软件后端依据：[Microsoft WARP 指南](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/directx-warp)。原始记录保留在各运行目录。']
(root/'cpu-performance.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('\n'.join(lines))
