# ws-game 1.7.0 表现 / Unity 深入审计

基线为冻结仓 `D:\workespace\ws-game-review-8160178` 的 `8160178b76fb51ae704a8f14b428decf228cc33e`（tag `v1.7.0`）。原仓 `D:\workespace\ws-game` 未写入、未修改产品源码或既有测试，未提交任何变更。

Unity 验证使用独立副本 `C:\Users\1\.codex\visualizations\2026\09\08\01a0801a-5646-78c2-a68c-99bbc742bed9\audit-8160178-20260908\unity_probe_1\unity`，同步了冻结仓当前 `adapters/unity`、`games/_template`、`data`、`core`、`presentation` 和当前测试 fixture，Unity 版本为 `6000.3.23f1`。第一次准备运行产生的 `PresentationClipSharedStateProbe.xml` 为 `testcasecount=0` 的旧空结果，没有计入结论；有效结果是 `PresentationClipSharedStateProbe2.xml`。

## 结论

本轮确认一项新的 P2 表现缺陷：`AnimationClip.events` 的数据驱动事件仍写入 Unity 全局共享剪辑；空 `events` 的 anim_set 直接返回，且每个 `UnityViewFactory` 独立保存 pristine 快照。由此，配置顺序和场景重进会让一个 anim_set 看到另一个 anim_set 的事件。

上轮 AUD-05 已独立复核为当前正确行为：`slot_mesh` prefab 型引用可提取真实槽位网格，缺失时保留当前网格并发起加载；本轮不能以旧的“槽位被清空”结论重报。owner/day/vendor 三处 Unity 组合根的新增透传源码存在，真实 Unity 入口的本轮证据覆盖 `GameFoundationBootstrap.QuestDayProvider`；owner resolver/vendor 回调的 Unity 生产触发未由本轮新增测试覆盖，不能扩张为三者均已 Runtime 验证。

## PRES-17-01 · P2 · 空事件配置与跨 UnityViewFactory 生命周期污染共享 AnimationClip

### 生产触发链与精确定位

1. `UnityViewFactory.RegisterModelClipEvents` 在 `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:1016-1021` 对 `clipDef.Events.Count == 0` 直接返回。该 anim_set 不登记自己的事件签名、不创建 override，也不保存 authored 基线。
2. 首个非空配置在 `UnityViewFactory.cs:1029-1036` 从当前共享 `AnimationClip.events` 捕获 `_modelClipPristineEvents`；随后 `:1042-1048` 用 `baseClip.events = MergeEvents(...)` 写回共享资产。
3. `_modelClipPristineEvents`、`_modelClipEventSignatures`、`_modelClipOverrides` 是 `UnityViewFactory` 实例字段（`UnityViewFactory.cs:124-144`）。Unity `Resources.Load<AnimationClip>` 对同一路径返回共享对象，因而新的 factory 会把前一 factory 已写入的数据事件当作“美术自带事件”基线。
4. 仅发生不同签名时才在 `:1057-1068` 创建并应用 `AnimatorOverrideController`；空配置不会进入该隔离分支。

### 真实 Unity 复现

独立 probe 源码为 [PresentationClipSharedStateProbe.cs](PresentationClipSharedStateProbe.cs)，使用真实 `UnityEngineHost`、`UnityResourceLoader.TryLoadAnimationClipSync`、`UnityViewFactory.RegisterModelClipEventsForTest` 和 `model.placeholder_biped`，不是 Stub renderer 或手工复制实现。结果文件：[PresentationClipSharedStateProbe2.xml](PresentationClipSharedStateProbe2.xml)、[PresentationClipSharedStateProbe2.log](PresentationClipSharedStateProbe2.log)。

Unity PlayMode XML：`total=3, passed=3, failed=0`；日志结束为 `Test run completed. Exiting with code 0 (Ok). Run completed.`。这三条是“故障现状断言”，所以测试通过表示现状被稳定复现，不表示产品行为正确。

| 顺序 | 预期 | 实际日志证据 |
| --- | --- | --- |
| A 非空 `probe_a` → B `events=[]` | B 只看到 authored 事件，`probe_a=false`；B 应有独立配置状态 | `PRESENTATION17 CLIP_EMPTY_AFTER_A expected_B_probe_a=false actual_shared_probe_a=True b_override=False` |
| B `events=[]` → A 非空 `probe_a` | B 已创建后仍不应被 A 的数据事件改变 | `PRESENTATION17 CLIP_EMPTY_BEFORE_A expected_B_probe_a=false actual_shared_probe_a=True b_override=False` |
| factory A 写 `probe_a` → 新 factory B 写 `probe_b` | B 的 pristine 基线不应包含 A 的数据事件，`probe_a=false` | `PRESENTATION17 CLIP_CROSS_FACTORY expected_factoryB_probe_a=false actual_shared_probe_a=True actual_shared_probe_b=True b_override=False` |

这不是只读 clip 数组的静态推测：日志同时证明 B 仍走共享控制器（`b_override=False`），而共享剪辑已经含有前一配置的 `probe_a`。完整端到端 `display.anim_set` 数据装配尚未用两条正式数据表复现；本结论限定为真实生产方法链可达的注册层，并由 Unity 内部 hook 触发私有生产方法。

修复方向是把 authored 基线和每个 anim_set 的事件结果从共享 `AnimationClip` 写回路径中移出：空配置也必须获得 authored-only 的实例/override；跨 factory 要共享稳定的 authored 快照或按实例克隆，不能以当前可变全局 clip 作为基线。验收应覆盖上述三种顺序、场景重建、实体销毁/重建，并用播放中的 Animator 剪辑验证事件集合。

## AUD-05 独立复核：已修项，不重报旧故障

当前 `UnityRenderer3D.SetSlotMesh` / `ApplySlotMesh` 位于 `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:970-1007`：`meshId` 非空时经注入的 `IResourceLoader.TryGetOrLoadSlotMesh`（`:991-994`）解析；缺失时 `:997-1007` 保留当前网格、诊断并发起 `LoadAsync`。`UnityResourceLoader.TryGetOrLoadSlotMesh` 在 `:713-760` 先从已加载模型 prefab 按槽位提取网格，再处理独立 Mesh；提取结果缓存按 `(resourceId, slotId)` 复用。

当前冻结源码的正式 Unity 测试 [SlotMeshResourceContractTests.xml](SlotMeshResourceContractTests.xml) / [SlotMeshResourceContractTests.log](SlotMeshResourceContractTests.log) 为 `6/6 passed`、退出码 0，覆盖：

- prefab 型 `model.placeholder_biped` 经 `ResourceKind.Model` 加载后，`slot.head` 网格保持非空；
- 已缓存命中、缺失保留旧网格、缺失确实发起 `LoadAsync`；
- 显式卸装和重复卸装幂等；
- 模型实例销毁重建后仍能应用提取网格。

这些是正确性断言，不是旧报告的 `Assert.IsNull` 故障现状断言。当前样例仍没有完整 `item.template` → inventory → equipment → View 的生产数据入口，因此不把这 6 条扩大为完整背包装备流程已验证。

## 动画事件修复回归边界

[ModelClipEventIsolationTests.xml](ModelClipEventIsolationTests.xml) 为 `3/3 passed`、退出码 0，真实验证了：经 loader 获取 AnimationClip、保留 authored events、不同签名使用 override 隔离、相同签名复用共享 clip。PRES-17-01 说明这套修复没有覆盖空 `events` 和跨 factory 的共享生命周期；两者是同一共享源污染根因的遗漏分支。

## owner/day/vendor 真实 Unity 入口核对

三处入口的源码透传如下：

- `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Bootstrap/GameFoundationBootstrap.cs:199-205` 暴露 `QuestOwnerResolver`、`QuestDayProvider`、`VendorOpenRequested`，`:330-341` 传入真实 `GameplayAssembly`；
- `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Shell/FrameworkResidentHost.cs:169-175` 暴露三者，`:337-348` 传入 `GameplayAssembly`；
- `games/_template/Runtime/GameOptions.cs:210-212` 与 `games/_template/Runtime/GameBootstrap.cs:217-236` 提供模板组合根字段与透传。

[GameFoundationBootstrapQuestDayProviderTests.xml](GameFoundationBootstrapQuestDayProviderTests.xml) 为真实 `GameFoundationBootstrap` 组件构造、真实 `Awake → BuildWorld → GameplayAssembly → QuestHost` 路径的 `1/1 passed`、退出码 0：第 1 天每日任务交付后为 `Unavailable`，切换 provider 到第 2 天后恢复可接。这证明 day provider 已穿过 Unity 组合根进入任务宿主。owner resolver/vendor 回调只完成了源码接线核对；本轮没有把它们写成 Unity Runtime 已通过。

## 相关定向回归

以下均为当前冻结源码同步后的 Unity 6000.3.23f1 PlayMode，XML 与 log 已归档在本目录，均退出码 0：

| 测试 | 结果 |
| --- | ---: |
| `SlotMeshResourceContractTests` | 6/6 |
| `ModelClipEventIsolationTests` | 3/3 |
| `GameFoundationBootstrapQuestDayProviderTests` | 1/1 |
| `UnityRenderer3DTests` | 19/19 |
| `ModelViewTests` | 13/13 |
| `EquipmentVisualReplayTests` | 3/3 |
| `HitFrameSyncEndToEndTests` | 2/2 |
| `AnimReplayAndFinishEndToEndTests` | 6/6 |
| 共享剪辑污染 probe（现状断言） | 3/3，故障现状复现 |

这些定向结果不代表全量 Unity、Standalone、IL2CPP、性能、资源泄漏、完整游戏流程或用户验收已通过；本轮也未以旧 DLL 代替当前源码基线。

## 静态候选边界：独立 Mesh 的异步 Model 合同

当前没有可用于 Runtime 复现的独立 Mesh 资产，`adapters/unity/Assets/Resources/GameFoundation/models/` 只有 prefab 型占位模型。因此以下只列为静态候选，不计入确认缺陷：

- `UnityResourceLoader.FinishModelLoad`（`UnityResourceLoader.cs:459-464`）对 `ResourceKind.Model` 只调用 `TryLoadModelSync`；该同步方法 `:852-870` 只执行 `Resources.Load<GameObject>`。
- 同一文件 `:746-760` 的 `TryGetOrLoadSlotMesh` 另有 `Resources.Load<Mesh>` 独立网格分支，并把结果加入 `_standaloneMeshes` 与 `_loaded`。
- `UnityRenderer3D.ApplySlotMesh` 的同步捷径 `UnityRenderer3D.cs:991-994` 通常会先走独立 Mesh 分支，因此当前标准 `SetSlotMesh` 调用不能据此报告“所有独立 Mesh 换装失败”。但若合法独立 Mesh 只能在 `LoadAsync(ResourceKind.Model)` 冷路径消费，异步回调会先收到 `success=false`；`UnityRenderer3D.cs:1069-1073` 的 `!success || ...` 短路会保留旧网格，无法把后续可解析的独立 Mesh 回填。

需要独立 Mesh 资产或可控异步资源出现时序，才能把该候选升级为 Runtime 缺陷；本轮未伪造资产、未将同步捷径的可用性扩张为异步合同通过。

