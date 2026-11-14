"""Summarize independent Player runs; never infer frame rate from video export."""
import csv
import hashlib
import json
import math
from pathlib import Path
import statistics
import subprocess

root = Path(__file__).resolve().parents[2]
output = root / 'Validation/Pool'
def quantile(values, p):
    a = sorted(values)
    i = (len(a) - 1) * p
    lo = int(i)
    return a[lo] + (a[min(lo + 1, len(a) - 1)] - a[lo]) * (i - lo)

report = {}
for folder in sorted(output.glob('bench-*')):
    if not (folder / 'frames.csv').exists():
        continue
    with (folder / 'frames.csv').open() as handle:
        rows = list(csv.DictReader(handle))
    phases = {}
    for phase, start, end in [('stationary', 12, 70), ('moving', 72, 130)]:
        rr = [r for r in rows if start <= float(r['seconds']) < end]
        if len(rr) < 2 or float(rr[-1]['seconds']) - float(rr[0]['seconds']) < 55:
            raise ValueError(f'Incomplete measurement: {folder.name}/{phase}')
        seen = set()
        timing = []
        for r in rr:
            if r['new_timing'] == '1' and r['frame_timestamp'] not in seen:
                seen.add(r['frame_timestamp'])
                timing.append(r)
        stats = {}
        fields = ['cpu_frame_ms', 'cpu_main_ms', 'present_wait_ms', 'cpu_render_ms', 'gpu_ms',
                  'working_set_bytes', 'private_bytes', 'process_cpu_percent', 'managed_bytes']
        for field in fields:
            records = timing if field.endswith('_ms') else rr
            values = [float(r[field]) for r in records if math.isfinite(float(r[field])) and float(r[field]) >= 0]
            if field == 'gpu_ms':
                values = [v for v in values if v > 0]
            stats[field] = dict(median=statistics.median(values), p95=quantile(values, .95), p99=quantile(values, .99)) if values else None
        stats['render_hz'] = (int(rr[-1]['rendered_count']) - int(rr[0]['rendered_count'])) / (float(rr[-1]['seconds']) - float(rr[0]['seconds']))
        stats['timing_samples'] = len(timing)
        phases[phase] = stats
    provenance=json.loads((folder/'run-manifest.json').read_text())
    report[folder.name] = dict(hardware=(folder / 'hardware.txt').read_text(), phases=phases,provenance=provenance)

manifest={name:data['provenance'] for name,data in report.items()}
builds=[data['provenance']['build_files'] for data in report.values()]
if builds and any(b!=builds[0] for b in builds):raise ValueError('Performance runs use different builds; rerun before comparing')
(output / 'performance.json').write_text(json.dumps(report, indent=2))
(output / 'build-manifest.json').write_text(json.dumps(manifest, indent=2))
def fmt(value):
    return f'{value:.2f}' if value is not None else 'N/A'
lines = ['# Pool 独立 Player 性能实测', '', '每轮预热 10 秒，静止眼位和移动眼位各 60 秒；统计时排除每阶段前 2 秒。帧时按时间戳去重，视频导出不参与性能统计。', '',
         '| 档位/分辨率 | 眼位 | 渲染 Hz | GPU 中位/P95/P99 ms | CPU 主线程中位 ms | 进程 CPU % | 工作集 MiB |', '|---|---|---:|---|---:|---:|---:|']
for mode, data in report.items():
    for phase, stats in data['phases'].items():
        gpu = stats['gpu_ms']
        gpu_text = '/'.join(fmt(gpu[k]) for k in ['median', 'p95', 'p99']) if gpu else '不可用'
        main = stats['cpu_main_ms']['median'] if stats['cpu_main_ms'] else None
        cpu = stats['process_cpu_percent']['median'] if stats['process_cpu_percent'] else None
        memory = stats['working_set_bytes']['median'] / 1048576 if stats['working_set_bytes'] else None
        lines.append(f'| {mode} | {phase} | {fmt(stats["render_hz"])} | {gpu_text} | {fmt(main)} | {fmt(cpu)} | {fmt(memory)} |')
lines += ['', 'high：半分辨率平面倒影、连续动画；low：四分之一分辨率倒影，静止眼位约 30 Hz，移动时唤醒至 60 Hz；static：冻结水纹/焦散/泳圈并关闭平面倒影，使用 --always-render 维持相同刷新上限，作为动态效果的成本基线。', '',
          '进程 CPU 占比按全部逻辑处理器归一化。CPU 主线程帧时可能包含等待，不能等同于纯计算耗时。硬件与窗口状态见各测量目录；本机数据不代表核显、多屏、真实追踪或功耗结果。', '',
          '构建及源文件哈希见 build-manifest.json，逐帧数据见各目录 frames.csv。']
(output / 'performance.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
print('\n'.join(lines))
