"""Refresh the existing evidence report from this build's measured results."""
from pathlib import Path
import json,datetime
r=Path(__file__).resolve().parents[2]
p=r/'docs/reference/evidence/limpool/验证说明.md';s=p.read_text(encoding='utf-8')
data=json.loads((r/'Validation/Limpool/performance.json').read_text())
labels={'default':'完整','no-volume':'无体积光','no-waterlight':'无动态水光','reflection-low':'四分之一反射','no-reflection':'无实时反射','low':'低品质'}
table='| 品质 | 眼位 | CPU 总帧 ms | GPU ms | 实际 FPS | 工作集 MiB |\n| --- | --- | --- | --- | --- | --- |\n'
def pair(x):return f"{x['median']:.3f} / {x['p95']:.3f}"
for q,name in labels.items():
    for phase,label in [('fixed','固定'),('moving','移动')]:
        d=next(x for x in data if x['quality']==q and x['phase']==phase)
        table+=f"| {name} | {label} | {pair(d['cpu_ms'])} | {pair(d['gpu_ms'])} | {d['fps']:.2f} | {d['working_set_mib']['median']:.1f} |\n"
f=next(x for x in data if x['quality']=='default' and x['phase']=='fixed')
m=next(x for x in data if x['quality']=='default' and x['phase']=='moving')
l=next(x for x in data if x['quality']=='low' and x['phase']=='moving')
table+=f"\n默认固定眼位主线程为 {pair(f['cpu_main_ms'])} ms，Present 等待为 {pair(f['present_wait_ms'])} ms；默认移动主线程为 {pair(m['cpu_main_ms'])} ms。默认移动 private memory 中位数 {m['private_mib']['median']:.1f} MiB，低品质为 {l['private_mib']['median']:.1f} MiB。\n\n"
a=s.index('| 品质 | 眼位 |');b=s.index('**分项成本的解释限制：**',a);s=s[:a]+table+s[b:]
s=s.replace('原模型后方开放空间使用简化蓝色环境及连续水面，没有增加新建筑布局。默认视图仍比参考图更规整，光束和水光较克制；交付图不是对参考图的逐像素复刻。','原模型后方开放空间使用 CC0 HDR 云天空、连续海面及三束近似透云光；保留原建筑布局。外海光束与太阳方向一致，缓慢改变亮度，但没有真实云体遮挡模拟。交付图不是对参考图的逐像素复刻。')
s=s.replace('审查修复了反射体积光深度来源、反射失效仍通过检查、组合边界覆盖不足、墙面水光缺少遮挡和恢复路径依赖 worktree 层级等问题。最终静态代码复查未发现本次 D3D11 固定配置的阻塞缺陷。','本轮修复透明后深度复制导致的水深归零、左墙 18 cm 缝隙、插件最小采样步长与显示空间尺度不匹配，以及天窗倒影尖锐亮点的采样问题。独立静态代码复查未发现新增阻塞缺陷。')
stability=json.loads((r/'Validation/Limpool/reflection-stability.json').read_text())
section='''## 水面与光照复核

- 左栏杆下墙体延伸到左墙内 2 cm，并在制作副本和 Unity 网格保持一致；原件未改。恢复原粗糙度/法线，重烘焙稳定间接光。
- 采用 HDR 云天空、蓝青色水体、局部翡翠色受光区域、柔和天窗与外海光束。波纹比上一轮疏，运动速度提高，保持已认可的波幅与建筑布局。
- 上一轮最小 mip 过滤仍有左右下方亮点突变。使用同一冻结水波时间 12 秒、300 帧左右眼位路径隔离反射、透射、太阳高光、法线及菲涅耳，再以 900 帧完整左右/上下/近远路径对照。主要突变位于高对比反射；固定 LOD 未改善，整片宽过滤则损失建筑细节。
- 当前把同一次反射结果分为受限细节与高亮残差，分别按较窄足迹和实际离轴投影下的 0.06 弧度角度足迹过滤。未过滤两层相加还原源 HDR；GPU 检查在绝对 0.002 + 源值 0.1% 容差内通过。保留天窗、亮墙及建筑倒影，不用历史混帧。
- 完整路径另发现上下段的细法线菲涅耳权重突变。最终采用 0.264 的细法线权重强度，倒影扭曲仍使用原法线幅度；同构建 A/B 的原实现组同时恢复强度 1，以覆盖这两个修正。该权重处理是平均法线分布的近似。
- 下表跟踪固定水面点，统计 8-bit RGB 均值的帧间二阶差。它仍包含正常视角相关反射运动，不能单独认证“无频闪”；本次以两侧明显尖锐突变减少作为回归阈值。数据见 [reflection-stability.json](../../../../Validation/Limpool/reflection-stability.json)，连续帧对照见交付 `Evidence/tracked-water-contact.png`。

| 下方水域 | 二阶差 P99：原实现 → 当前 | 二阶差 >10 的比例：原实现 → 当前 |
| --- | --- | --- |
'''
for side,label in [('left','左侧'),('right','右侧')]:
    before=stability['metrics']['prefilter-before'][side];after=stability['metrics']['prefilter-after'][side]
    section+=f"| {label} | {before['p99']:.3f} → {after['p99']:.3f} | {before['fraction_over_10']*100:.3f}% → {after['fraction_over_10']*100:.3f}% |\n"
section+='''
- 实际 GPU 水深及冻结效果眼位往返回归见 [visual-regression.json](../../../../Validation/Limpool/visual-regression.json)。往返检查只排查历史滞后，不能替代移动中的闪烁检查。固定眼位水面、水光与光束持续变化的像素证据另存 `audit.json`。

'''
a=s.index('## 本轮水面与光照复核') if '## 本轮水面与光照复核' in s else s.index('## 水面与光照复核')
b=s.index('## 性能口径',a);s=s[:a]+section+s[b:]
s=s.replace('测试配置：2026-10-22','测试配置：'+str(datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=8))).date()))
p.write_text(s,encoding='utf-8')
(r/'Validation/Limpool/performance.md').write_text('# 独立 Player 性能\n\n'+table,encoding='utf-8')
