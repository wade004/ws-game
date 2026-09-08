# ws-game 1.6.0 表现/引擎有界审计采集

审计基线为 `85f1f4f`，版本文件为 `1.6.0`。本记录只覆盖 model 表现、装备槽位、动画事件及其 Unity 适配邻接路径；原仓库 `D:\workespace\ws-game` 保持只读。Unity 探针使用独立副本，未修改产品源码，也未运行全量门禁。

## 结论

本轮确认 1 个可复核的 P2 表现缺陷：样例 model `slot_mesh` 装备引用了 prefab 型 `model_ref`，实际调用 `ModelCharacterRig.ApplyEquipVisual` 后，Unity 渲染器把同一 id 当作独立 `Mesh` 读取，得到 `null` 并清空原有槽位网格。该结论只覆盖当前可达的 `display.equip_visual.sample_model_helmet` 配置路径；样例没有 `item.template` 行，因此没有把它扩张为完整 inventory→View 生产入口已验证。

另有一处静态契约差异列为待处理项，不计入确认缺陷：model 动画关键帧登记直接在 `UnityViewFactory` 调用 `Resources.Load<AnimationClip>` 并改写共享 clip 的 `events`，没有经 `IResourceLoader`。本轮没有把这条差异冒充成新的 Runtime 影响。

## PRES-85-01 · P2 · 样例 model 槽位换装把 prefab 引用按 Mesh 读取，槽位可见网格变为 null

### 精确定位

- `presentation/render/core/ModelCharacterRig.cs:183-201` 的 `ApplyEquipVisual` 对 `slot_mesh` 无条件调用 `IRenderer3D.SetSlotMesh`。
- `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:929-967` 的 `SetSlotMesh`/`ApplySlotMesh` 查找 `slot.head` 后，将 `ResolveMesh(meshId)` 结果写入 `MeshFilter.sharedMesh`。
- `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:1320` 的 `ResolveMesh` 直接执行 `Resources.Load<Mesh>(UnityResourceLoader.ResolveModelResourcesPath(meshId))`。
- `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityResourceLoader.cs:571-590` 的 model loader 实际缓存的是 `GameObject` prefab；`ResolveModelResourcesPath` 位于 `:594-595`。
- `data/_sample/display/display.equip_visual.json:13-17` 明确给 `sample_model_helmet` 配置 `mode=slot_mesh`、`slot_id=slot.head`、`mesh_ref=model.placeholder_biped`。
- `adapters/unity/Assets/Resources/GameFoundation/models/placeholder_biped.prefab:15,37-43` 是原生 `slot.head` `MeshFilter` 及其 authored mesh；生成器对应 `adapters/unity/Assets/Editor/GeneratePlaceholderModelAssets.cs:10,214`。

`placeholder_biped.prefab` 本身带有原生 `slot.head` 子对象及其 `MeshFilter`，但 `Resources/GameFoundation/models/placeholder_biped.prefab` 没有一个同路径的独立 `Mesh` 资产。因此这里不是“资源不存在时的理论旁路”：loader 成功得到 prefab，renderer 随后以 `Mesh` 类型读取 prefab 路径，Unity 返回 null。

### 触发条件与实际结果

触发条件是：创建 `model.placeholder_biped` 实例；通过 `IResourceLoader.LoadAsync(model.placeholder_biped, ResourceKind.Model)` 并 `Tick` 完成加载；对真实 `slot.head` 调用 `ModelCharacterRig.ApplyEquipVisual`，传入与样例相同的 `EquipVisualDef`。初始 `slot.head.MeshFilter.sharedMesh` 非空，且 `loader.IsLoaded(modelId)=true`、`TryGetModelPrefab=true`；Apply 后 `sharedMesh=null`。

预期是 mesh 引用按明确的资源契约解析成功，或在资源不可用时保留可见占位/原槽位并留下诊断；实际实现静默将槽位写成 null。因为 `ModelCharacterRig` 已把装备记入表现路径，后续不会自动重新申请一个可解析的独立 mesh。

### 真实 Unity 定向证据

探针源码为 [probe-slotmesh.cs](probe-slotmesh.cs)。它使用 prefab 自带的 `slot.head`，没有添加或替换 `MeshFilter`，并实际走 `ModelCharacterRig.ApplyEquipVisual`。Unity `6000.3.23f1` PlayMode 结果：退出码 `0`，XML `total=1 passed=1 failed=0`；日志标记如下：

```text
PRESENTATION85 slot_mesh rig_apply=true loader_loaded=True loader_prefab=true expected_mesh_ref_applied=true actual_mesh_null=True
```

原始结果见 [probe-slotmesh-unity.xml](probe-slotmesh-unity.xml) 与 [probe-slotmesh-unity.log](probe-slotmesh-unity.log)。测试中的 `Assert.IsNull` 是故障现状断言，所以“测试通过”表示现状被稳定复现，不表示产品行为正确。

### 默认/配置/测试钩子路径

默认配置入口是 `data/_sample/display/display.equip_visual.json:13-17`；`display.map.sample_model_hero` 在 `data/_sample/display/display.map.json:12` 声明 `model.placeholder_biped` 与 `slot.head`。三个 Unity 生产装配根均把 `UnityResourceLoader`、`UnityViewFactory` 和 `IRenderer3D` 接在一起，但本轮没有声称完整 inventory→View 入口会产生该样例装备事件，因为当前样例缺少 `item.template` 行。

现有正向模型测试未覆盖“非空 mesh 成功替换”：`adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/UnityRenderer3DTests.cs:183-199` 只覆盖未知槽位和显式清空 null；`presentation/render/tests/ModelCharacterRigTests.cs:148-158` 使用 StubRenderer3D，只证明 id 被转发，不能证明 Unity 资源解析。

### 修复与验收建议

先确定 `mesh_ref` 的资源合同：建议为独立网格增加明确的 `Mesh` 资源种类与 loader 取回接口，或把数据字段改为真正由 `ResourceKind.Model` 表示的可提取槽位资产；两者都应在 02/04/09/14 和 ADR 中统一。随后 `UnityRenderer3D` 只从注入的 loader 取已加载 mesh，不直接读 `Resources`；缺失时应保留可见占位/原槽位并记录诊断。

验收至少包括：

1. 冷加载样例/真实独立 Mesh 后，`ApplyEquipVisual` 使 `slot.head` 的 `MeshFilter` 或 `SkinnedMeshRenderer` 保持非空。
2. 自定义 `IResourceLoader` 记录首次请求，确认 renderer 不自行读取 Unity 资源路径；异步完成后只替换同一存活实例。
3. 缺失 mesh、模型替换、销毁重建和卸装均不把装备意图静默变成不可见；重复装备/卸装结果幂等。

## 静态契约差异（未计入确认缺陷）

`adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:966-990` 的 `RegisterModelClipEvents` 直接按 `ResolveAnimClipResourcesPath` 调用 `Resources.Load<AnimationClip>`，随后将 `clip.events` 整体替换。当前 `IResourceLoader` 的 `ResourceKind` 没有 `AnimationClip`，而 02 第 1.7 节要求消费方经 loader 取资源；这至少是实现与契约的静态差异。共享 `AnimationClip` 时整体赋值还可能丢失美术自带事件或让首个 `display.anim_set` 的事件列表决定其它使用方的共享结果。

本轮没有找到一条比槽位问题更具体且已由真实生产配置触发的 Runtime 影响，故不将它列为确认缺陷。修复槽位合同时应一并决定：动画 clip 是否纳入 loader，事件登记是否由 loader/适配器维护，如何合并而不是覆盖 authored events，以及不同 `display.anim_set` 是否允许共享同一 `resource_ref`。

## 定向回归记录

以下均为 Unity `6000.3.23f1`、同一独立副本，测试前将只读 `data/` 样本复制到该副本预期的 fixture 根；不是全量 check：

| 测试类 | XML 结果 | 退出码 |
|---|---:|---:|
| `UnityRenderer3DTests` | 19/19 passed | 0 |
| `ModelViewTests` | 13/13 passed | 0 |
| `EquipmentVisualReplayTests` | 3/3 passed | 0 |
| `HitFrameSyncEndToEndTests` | 2/2 passed | 0 |
| `AnimReplayAndFinishEndToEndTests` | 6/6 passed | 0 |

对应原始 XML/日志已归档为 `regression-*.xml` / `regression-*.log`。这些回归支持 PR150 已有的绑定、完成事件、命中帧与装备重放路径当前通过；它们不覆盖 mesh_ref 的正向非空 Unity 解析，因此不能抵消 PRES-85-01。

## Runtime 边界

已执行真实 Unity 定向探针和以上五个相关 PlayMode 回归。未执行全量门禁、独立版、IL2CPP、完整 inventory→View 装备入口或性能测试；静态契约差异仅作为复核项记录。原仓库未写入，报告与证据仅落在本审计目录。
