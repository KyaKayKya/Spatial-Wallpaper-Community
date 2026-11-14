# Cilbox 转换依赖

固定源与逐文件摘要见SOURCE.json。保留上游MIT LICENSE。这里只提取五个互相依赖的C#文件，没有Basis设备、网络、音频、用户同意或ContentPolice系统。Editor程序集用于受支持C#的转换，Player不包含此程序集；运行时使用SDK的规范化指令和精确桥接。编译依赖代码并不代表其原生解析/执行路径获准处理陌生包。

Cilbox.cs的三个修改是分离公共标记属性、关闭自动场景处理钩子、在转换结果生成后提前返回。compileOnly不执行proxy生命周期或写入场景。来源和修改不能代替转换等价性验收。
