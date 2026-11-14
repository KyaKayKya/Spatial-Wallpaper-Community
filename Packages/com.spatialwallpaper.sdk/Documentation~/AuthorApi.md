# 作者 API 参考

由 `tools/sdk/generate_rules.py` 从发行规则源生成。修改规则源后重新生成，不手工编辑本页。

SDK `0.3.0-preview.3`；Unity `2022.3.62f3`；URP `14.0.12`；规则 SHA256 `cbe53b53dce9e0047354c164a7ab0d175d5e08e1f7dcf00a8c36d2081fa1f028`。

只准入下列成员和规则支持的数据操作。普通 Unity Play 中可以调用的其他成员，不代表能够进入沙箱。

作者回调：`Start`, `Update`。

Unity 对象和资源句柄属于当前实例及当前代。不能保存到别的实例、访问宿主相机或主投影、采集设备，或调用任意文件和网络 API。

## 成员

| 成员标识 | 所需能力 | 参数约束 |
| --- | --- | --- |
| `UnityEngine.CoreModule::UnityEngine.Transform::Rotate(System.Single,System.Single,System.Single)->System.Void` | — | f32 -100000…100000; f32 -100000…100000; f32 -100000…100000 |
| `UnityEngine.CoreModule::UnityEngine.Transform::get_localPosition()->UnityEngine.Vector3` | — | 无参数 |
| `UnityEngine.CoreModule::UnityEngine.Transform::set_localPosition(UnityEngine.Vector3)->System.Void` | — | vector3 -100000…100000 |
| `UnityEngine.CoreModule::UnityEngine.Light::get_intensity()->System.Single` | — | 无参数 |
| `UnityEngine.CoreModule::UnityEngine.Light::set_intensity(System.Single)->System.Void` | — | f32 -100000…100000 |
| `UnityEngine.CoreModule::UnityEngine.Material::SetFloat(System.String,System.Single)->System.Void` | — | string; f32 -100000…100000 |
| `UnityEngine.CoreModule::UnityEngine.Material::SetColor(System.String,UnityEngine.Color)->System.Void` | — | string; color -100000…100000 |
| `UnityEngine.CoreModule::UnityEngine.Mesh::Clear()->System.Void` | — | 无参数 |
| `UnityEngine.CoreModule::UnityEngine.Texture2D::SetPixel(System.Int32,System.Int32,UnityEngine.Color)->System.Void` | — | i32 0…8191; i32 0…8191; color -100000…100000 |
| `UnityEngine.CoreModule::UnityEngine.Texture2D::Apply()->System.Void` | — | 无参数 |
| `SpatialWallpaper.Sdk::Input::GetButtonDown(System.String)->System.Boolean` | `input` | string 可空 |
| `SpatialWallpaper.Sdk::Objects::Instantiate(Prefab)->Node` | — | prefab 可空 instance-and-generation |
| `SpatialWallpaper.Sdk::Objects::Destroy(Node)->System.Void` | — | node 可空 instance-and-generation |
| `SpatialWallpaper.Sdk::Resources::CreateMesh()->Mesh` | — | 无参数 |
| `SpatialWallpaper.Sdk::Resources::CreateTexture(System.Int32,System.Int32)->Texture` | — | i32 1…8192; i32 1…8192 |
| `SpatialWallpaper.Sdk::Resources::CreateMaterial(Material)->Material` | — | material 可空 instance-and-generation |
| `SpatialWallpaper.Sdk::Audio::GetBand(System.Int32)->System.Single` | `audio-features` | i32 -2147483648…2147483647 |
| `mscorlib::System.Collections.Generic.List<System.Int32>::Add(System.Int32)->System.Void` | — | i32 -2147483648…2147483647 |
| `mscorlib::System.Collections.Generic.List<System.Int32>::get_Count()->System.Int32` | — | 无参数 |
| `mscorlib::System.Collections.Generic.List<System.Int32>::get_Item(System.Int32)->System.Int32` | — | i32 -2147483648…2147483647 |
| `mscorlib::System.Collections.Generic.List<System.Int32>::set_Item(System.Int32,System.Int32)->System.Void` | — | i32 -2147483648…2147483647; i32 -2147483648…2147483647 |
| `mscorlib::System.Collections.Generic.List<System.Int32>::Clear()->System.Void` | — | 无参数 |
| `mscorlib::System.Collections.Generic.List<System.Single>::Add(System.Single)->System.Void` | — | f32 -100000…100000 |
| `mscorlib::System.Collections.Generic.List<System.Single>::get_Count()->System.Int32` | — | 无参数 |
| `mscorlib::System.Collections.Generic.List<System.Single>::get_Item(System.Int32)->System.Single` | — | i32 -2147483648…2147483647 |
| `mscorlib::System.Collections.Generic.List<System.Single>::set_Item(System.Int32,System.Single)->System.Void` | — | i32 -2147483648…2147483647; f32 -100000…100000 |
| `mscorlib::System.Collections.Generic.List<System.Single>::Clear()->System.Void` | — | 无参数 |
| `SpatialWallpaper.Sdk::Resources::Release(Mesh)->System.Void` | — | mesh instance-and-generation |
| `SpatialWallpaper.Sdk::Resources::Release(Texture)->System.Void` | — | texture instance-and-generation |
| `SpatialWallpaper.Sdk::Resources::Release(Material)->System.Void` | — | material instance-and-generation |
| `UnityEngine.CoreModule::UnityEngine.Mesh::set_vertices(UnityEngine.Vector3[])->System.Void` | — | vector3[] recursive-instance-and-generation |
| `UnityEngine.CoreModule::UnityEngine.Mesh::set_triangles(System.Int32[])->System.Void` | — | i32[] recursive-instance-and-generation |
| `UnityEngine.CoreModule::UnityEngine.Mesh::RecalculateNormals()->System.Void` | — | 无参数 |
| `UnityEngine.CoreModule::UnityEngine.Mesh::RecalculateBounds()->System.Void` | — | 无参数 |
| `SpatialWallpaper.Sdk::Resources::BindMesh(Node,Mesh)->System.Void` | — | node instance-and-generation; mesh 可空 instance-and-generation |
| `SpatialWallpaper.Sdk::Storage::BeginRead(System.String)->System.Int32` | `private-storage` | string 可空 |
| `SpatialWallpaper.Sdk::Storage::BeginWriteText(System.String,System.String,System.String)->System.Int32` | `private-storage` | string 可空; string 可空; string 可空 |
| `SpatialWallpaper.Sdk::Storage::State(System.Int32)->System.Int32` | `private-storage` | i32 -2147483648…2147483647 |
| `SpatialWallpaper.Sdk::Storage::Version(System.Int32)->System.String` | `private-storage` | i32 -2147483648…2147483647 |
| `SpatialWallpaper.Sdk::Storage::Error(System.Int32)->System.String` | `private-storage` | i32 -2147483648…2147483647 |
| `SpatialWallpaper.Sdk::Storage::TakeText(System.Int32)->System.String` | `private-storage` | i32 -2147483648…2147483647 |
| `SpatialWallpaper.Sdk::Storage::Release(System.Int32)->System.Boolean` | `private-storage` | i32 -2147483648…2147483647 |
| `SpatialWallpaper.Sdk::Llm::BeginPrompt(System.String)->System.Int32` | `llm-proxy` | string 可空 |
| `SpatialWallpaper.Sdk::Llm::BeginCancel(System.Int32)->System.Int32` | `llm-proxy` | i32 -2147483648…2147483647 |
| `SpatialWallpaper.Sdk::Llm::State(System.Int32)->System.Int32` | `llm-proxy` | i32 -2147483648…2147483647 |
| `SpatialWallpaper.Sdk::Llm::Error(System.Int32)->System.String` | `llm-proxy` | i32 -2147483648…2147483647 |
| `SpatialWallpaper.Sdk::Llm::TakeText(System.Int32)->System.String` | `llm-proxy` | i32 -2147483648…2147483647 |
| `SpatialWallpaper.Sdk::Llm::Release(System.Int32)->System.Boolean` | `llm-proxy` | i32 -2147483648…2147483647 |
| `SpatialWallpaper.Sdk::Input::GetButton(System.String)->System.Boolean` | `input` | string 可空 |
| `SpatialWallpaper.Sdk::Input::GetButtonUp(System.String)->System.Boolean` | `input` | string 可空 |
| `SpatialWallpaper.Sdk::Input::IsFocused()->System.Boolean` | `input` | 无参数 |
| `SpatialWallpaper.Sdk::Input::GetPointerX()->System.Single` | `input` | 无参数 |
| `SpatialWallpaper.Sdk::Input::GetPointerY()->System.Single` | `input` | 无参数 |
| `SpatialWallpaper.Sdk::Input::GetWheel()->System.Single` | `input` | 无参数 |
| `SpatialWallpaper.Sdk::View::GetEye()->UnityEngine.Vector3` | `view-data` | 无参数 |
| `SpatialWallpaper.Sdk::View::GetSize()->UnityEngine.Vector3` | `view-data` | 无参数 |
| `SpatialWallpaper.Sdk::Sound::PlayMusic(UnityEngine.AudioClip,System.Single)->System.Void` | `sound-playback` | sound instance-and-generation; f32 0…1 |
| `SpatialWallpaper.Sdk::Sound::PlayEffect(UnityEngine.AudioClip,System.Single)->System.Void` | `sound-playback` | sound instance-and-generation; f32 0…1 |
| `SpatialWallpaper.Sdk::Sound::StopMusic()->System.Void` | `sound-playback` | 无参数 |
| `SpatialWallpaper.Sdk::Sound::StopEffects()->System.Void` | `sound-playback` | 无参数 |

成员标识保留程序集、类型与完整签名，用于对应准入错误。`Node` 是有归属的 GameObject，`Texture` 是有归属的 Texture2D；这些名称不开放额外 Unity 成员。

## 内置材质属性

已准入的自定义 Shader 由包内 Shader 描述验证属性类型；它不能替换可信渲染器或主投影。以下内置属性来自相同规则源。

### Universal Render Pipeline/Lit

| 属性 | 类型 | 纹理维度 |
| --- | --- | --- |
| `_WorkflowMode` | Float | 0 |
| `_BaseMap` | Texture | 2 |
| `_BaseColor` | Color | 0 |
| `_Cutoff` | Range | 0 |
| `_Smoothness` | Range | 0 |
| `_SmoothnessTextureChannel` | Float | 0 |
| `_Metallic` | Range | 0 |
| `_MetallicGlossMap` | Texture | 2 |
| `_SpecColor` | Color | 0 |
| `_SpecGlossMap` | Texture | 2 |
| `_SpecularHighlights` | Float | 0 |
| `_EnvironmentReflections` | Float | 0 |
| `_BumpScale` | Float | 0 |
| `_BumpMap` | Texture | 2 |
| `_Parallax` | Range | 0 |
| `_ParallaxMap` | Texture | 2 |
| `_OcclusionStrength` | Range | 0 |
| `_OcclusionMap` | Texture | 2 |
| `_EmissionColor` | Color | 0 |
| `_EmissionMap` | Texture | 2 |
| `_DetailMask` | Texture | 2 |
| `_DetailAlbedoMapScale` | Range | 0 |
| `_DetailAlbedoMap` | Texture | 2 |
| `_DetailNormalMapScale` | Range | 0 |
| `_DetailNormalMap` | Texture | 2 |
| `_ClearCoatMask` | Float | 0 |
| `_ClearCoatSmoothness` | Float | 0 |
| `_Surface` | Float | 0 |
| `_Blend` | Float | 0 |
| `_Cull` | Float | 0 |
| `_AlphaClip` | Float | 0 |
| `_SrcBlend` | Float | 0 |
| `_DstBlend` | Float | 0 |
| `_SrcBlendAlpha` | Float | 0 |
| `_DstBlendAlpha` | Float | 0 |
| `_ZWrite` | Float | 0 |
| `_BlendModePreserveSpecular` | Float | 0 |
| `_AlphaToMask` | Float | 0 |
| `_ReceiveShadows` | Float | 0 |
| `_QueueOffset` | Float | 0 |
| `_MainTex` | Texture | 2 |
| `_Color` | Color | 0 |
| `_GlossMapScale` | Float | 0 |
| `_Glossiness` | Float | 0 |
| `_GlossyReflections` | Float | 0 |
| `unity_Lightmaps` | Texture | 5 |
| `unity_LightmapsInd` | Texture | 5 |
| `unity_ShadowMasks` | Texture | 5 |

### Universal Render Pipeline/Unlit

| 属性 | 类型 | 纹理维度 |
| --- | --- | --- |
| `_BaseMap` | Texture | 2 |
| `_BaseColor` | Color | 0 |
| `_Cutoff` | Range | 0 |
| `_Surface` | Float | 0 |
| `_Blend` | Float | 0 |
| `_Cull` | Float | 0 |
| `_AlphaClip` | Float | 0 |
| `_BlendOp` | Float | 0 |
| `_SrcBlend` | Float | 0 |
| `_DstBlend` | Float | 0 |
| `_SrcBlendAlpha` | Float | 0 |
| `_DstBlendAlpha` | Float | 0 |
| `_ZWrite` | Float | 0 |
| `_AlphaToMask` | Float | 0 |
| `_QueueOffset` | Float | 0 |
| `_MainTex` | Texture | 2 |
| `_Color` | Color | 0 |
| `_SampleGI` | Float | 0 |

## 静态限额

以下限额来自准入规则；实际性能另行测量，不能用调用成本常数代替 CPU/GPU 帧时。

| 规则 | 值 |
| --- | --- |
| `archive` | `67108864` |
| `expanded` | `134217728` |
| `entries` | `4096` |
| `file_bytes` | `33554432` |
| `classes` | `128` |
| `fields` | `128` |
| `methods` | `256` |
| `parameters` | `16` |
| `locals` | `128` |
| `method_instructions` | `16384` |
| `program_instructions` | `131072` |
| `stack` | `128` |
| `resources` | `4096` |
| `nodes` | `16384` |
| `prefabs` | `256` |
| `prefab_nodes` | `4096` |
| `components` | `32` |

## 原生命令桥（开发中）

以下目录用于新原生命令桥，目标SDK `0.4.0-preview.1`、schema `5`，不改变上面的0.3包身份或准入规则。只有下表显式成员及镜像成员可进入新桥；其他旧渲染API必须在导出/执行预检失败，不能转发给旧Unity宿主。协议/镜像检查不代表沙箱与实际渲染已集成。

| 操作 | 目标 | 语义 | VM成员 |
| --- | --- | --- | --- |
| `SetPosition` | `node` | state | `UnityEngine.CoreModule::UnityEngine.Transform::set_localPosition(UnityEngine.Vector3)->System.Void` |
| `SetRotation` | `node` | state | 待作者API接入 |
| `SetScale` | `node` | state | 待作者API接入 |
| `SetActive` | `node` | state | `UnityEngine.CoreModule::UnityEngine.GameObject::SetActive(System.Boolean)->System.Void` |
| `SetLightIntensity` | `light` | state | `UnityEngine.CoreModule::UnityEngine.Light::set_intensity(System.Single)->System.Void` |
| `MaterialFloat` | `material` | state | `UnityEngine.CoreModule::UnityEngine.Material::SetFloat(System.String,System.Single)->System.Void` |
| `MaterialColor` | `material` | state | `UnityEngine.CoreModule::UnityEngine.Material::SetColor(System.String,UnityEngine.Color)->System.Void` |
| `AnimatorFloat` | `node` | state | `SpatialWallpaper.Sdk::SceneAnimation::SetFloat(Node,System.String,System.Single)->System.Void` |
| `AnimatorBool` | `node` | state | `SpatialWallpaper.Sdk::SceneAnimation::SetBool(Node,System.String,System.Boolean)->System.Void` |
| `AnimatorTrigger` | `node` | event | `SpatialWallpaper.Sdk::SceneAnimation::Trigger(Node,System.String)->System.Void` |
| `PlayEffect` | `sound` | event | `SpatialWallpaper.Sdk::Sound::PlayEffect(UnityEngine.AudioClip,System.Single)->System.Void` |
| `PlayMusic` | `sound` | event | `SpatialWallpaper.Sdk::Sound::PlayMusic(UnityEngine.AudioClip,System.Single)->System.Void` |
| `StopMusic` | `none` | event | `SpatialWallpaper.Sdk::Sound::StopMusic()->System.Void` |
| `StopEffects` | `none` | event | `SpatialWallpaper.Sdk::Sound::StopEffects()->System.Void` |
| `Instantiate` | `prefab` | resource | `SpatialWallpaper.Sdk::Objects::Instantiate(Prefab)->Node` |
| `Destroy` | `node` | resource | `SpatialWallpaper.Sdk::Objects::Destroy(Node)->System.Void` |
| `Query` | `node` | query | 见原生专用查询API |
| `Resource` | `none` | resource | 待作者API接入 |

### 原生专用作者成员

这些成员只由原生规则目录准入，旧schema4规则与摘要保持不变。普通Play中的查询需要已绑定的原生查询会话；未绑定时明确报错，不返回默认命中。

| 成员 | 参数类型 | 返回类型 |
| --- | --- | --- |
| `SpatialWallpaper.Sdk::SceneAnimation::SetFloat(Node,System.String,System.Single)->System.Void` | `node, string, f32` | `void` |
| `SpatialWallpaper.Sdk::SceneAnimation::SetBool(Node,System.String,System.Boolean)->System.Void` | `node, string, bool` | `void` |
| `SpatialWallpaper.Sdk::SceneAnimation::Trigger(Node,System.String)->System.Void` | `node, string` | `void` |
| `SpatialWallpaper.Sdk::SceneQueries::BeginBounds(Node)->System.Int32` | `node` | `i32` |
| `SpatialWallpaper.Sdk::SceneQueries::BeginRaycast(Node,UnityEngine.Vector3,UnityEngine.Vector3,System.Single)->System.Int32` | `node, vector3, vector3, f32` | `i32` |
| `SpatialWallpaper.Sdk::SceneQueries::BeginAnimatorState(Node,System.Int32)->System.Int32` | `node, i32` | `i32` |
| `SpatialWallpaper.Sdk::SceneQueries::State(System.Int32)->System.Int32` | `i32` | `i32` |
| `SpatialWallpaper.Sdk::SceneQueries::Error(System.Int32)->System.String` | `i32` | `string` |
| `SpatialWallpaper.Sdk::SceneQueries::ReadVector(System.Int32,System.String)->UnityEngine.Vector3` | `i32, string` | `vector3` |
| `SpatialWallpaper.Sdk::SceneQueries::ReadFloat(System.Int32,System.String)->System.Single` | `i32, string` | `f32` |
| `SpatialWallpaper.Sdk::SceneQueries::ReadInt(System.Int32,System.String)->System.Int32` | `i32, string` | `i32` |
| `SpatialWallpaper.Sdk::SceneQueries::ReadBool(System.Int32,System.String)->System.Boolean` | `i32, string` | `bool` |
| `SpatialWallpaper.Sdk::SceneQueries::ReadNode(System.Int32)->Node` | `i32` | `node` |
| `SpatialWallpaper.Sdk::SceneQueries::Release(System.Int32)->System.Void` | `i32` | `void` |
| `SpatialWallpaper.Sdk::SceneResources::BeginMesh(Node,System.Int32,System.Int32[])->System.Int32` | `node, i32, i32[]` | `i32` |
| `SpatialWallpaper.Sdk::SceneResources::BeginTexture(UnityEngine.Material,System.String,System.Int32,System.Int32)->System.Int32` | `material, string, i32, i32` | `i32` |
| `SpatialWallpaper.Sdk::SceneResources::WriteFloats(System.Int32,System.Int32,System.Single[])->System.Void` | `i32, i32, f32[]` | `void` |
| `SpatialWallpaper.Sdk::SceneResources::WriteIndices(System.Int32,System.Int32,System.Int32[])->System.Void` | `i32, i32, i32[]` | `void` |
| `SpatialWallpaper.Sdk::SceneResources::WriteBytes(System.Int32,System.Int32,System.Int32[])->System.Void` | `i32, i32, i32[]` | `void` |
| `SpatialWallpaper.Sdk::SceneResources::Seal(System.Int32)->System.Void` | `i32` | `void` |
| `SpatialWallpaper.Sdk::SceneResources::State(System.Int32)->System.Int32` | `i32` | `i32` |
| `SpatialWallpaper.Sdk::SceneResources::Error(System.Int32)->System.String` | `i32` | `string` |
| `SpatialWallpaper.Sdk::SceneResources::Apply(System.Int32)->System.Void` | `i32` | `void` |
| `SpatialWallpaper.Sdk::SceneResources::Cancel(System.Int32)->System.Void` | `i32` | `void` |
| `SpatialWallpaper.Sdk::SceneResources::Release(System.Int32)->System.Void` | `i32` | `void` |
| `UnityEngine.CoreModule::UnityEngine.GameObject::SetActive(System.Boolean)->System.Void` | `bool` | `void` |

查询状态：Unknown=0、Pending=1、Ready=2、Failed=3。Begin只排队，收到真实提交ACK后才Ready；未Ready读取结果会失败。ReadVector的字段为bounds的center/extents、raycast命中后的point/normal；ReadFloat为命中后的distance或Animator的normalized_time；ReadInt为state_hash；ReadBool为hit或in_transition。ReadNode在真实无命中时返回null；结果保留到Release，不能把Pending解释为无命中。


镜像读取及变换：`UnityEngine.CoreModule::UnityEngine.Transform::get_localPosition()->UnityEngine.Vector3`, `UnityEngine.CoreModule::UnityEngine.Transform::Rotate(System.Single,System.Single,System.Single)->System.Void`, `UnityEngine.CoreModule::UnityEngine.Light::get_intensity()->System.Single`。

每Tick最多一份未确认批；TickInput包含DeltaTime、原有派生输入快照和有编号的累计事件，成功确认后才消费事件水位。状态在事件/资源/查询屏障之间合并，事件保序。镜像写后读同步；动画/渲染帧只推进SnapshotSequence，不推进冲突SceneRevision。状态/资源原子提交推进一次修订，真实冲突拒绝整批并停止脚本会话，不重放事件。

异步查询使用原生专用成员，只有Pending、Ready、Failed三种有效生命周期状态；真实原生求值结果只在提交ACK之后可读，不得用默认值冒充已返回。产品接线与实际运行验收单独记录。

| 查询 | 参数形状 | 结果形状 |
| --- | --- | --- |
| `bounds` | `empty` | `bounds6` |
| `raycast` | `ray` | `hit` |
| `animator_state` | `layer` | `animator` |
