# 1.8.0 表现专项审计记录

## 范围与基线

本记录对应冻结仓 `D:\workespace\ws-game-review-e070e3f` 的 `HEAD e070e3fc3b8ec992183590aab773d346fe9ab211`、版本 `1.8.0`。

原仓 `D:\workespace\ws-game` 未写入；本轮产品代码、原测试和提交均未修改。

Unity 验证使用 `C:\Users\1\.codex\visualizations\2026\09\08\01a0801a-5646-78c2-a68c-99bbc742bed9\audit-e070e3f-20260908\unity_probe_2\unity`，版本为 `6000.3.23f1 (09d2ecc7fb28)`。

该副本重建同步了冻结仓 `adapters/unity/Assets`、`Packages`、`ProjectSettings`、`data`、`adapters/conformance`、`games/_template` 和 Unity 插件 DLL。

同步前后 `UnityViewFactory.cs` SHA256 都是 `A96260DD9069BBEA6FA7B5796E342008639F410DD92172493C1CF07B4656357B`；关键 data fixture 与六个 Core DLL 的完整记录见 [animation-baseline-hashes.log](animation-baseline-hashes.log)。

此前 `unity_probe_1` 的 `UnityViewFactory.cs` 是旧 1.7 实现，SHA256 为 `B40B1C664AF23E28CD8A0266D22C486B2F359FE27BE977E4F55E9C15CD9237E5`，没有 `s_modelClipOverrides`；基于该混版副本产生的结果全部排除，不进入本结论。

## 总体结论

本次覆盖的上一轮 `PRES-170-01` 共享 `AnimationClip.events` 污染路径已修复并通过定向验证：本类型不再写回共享 base clip，空事件不会继承另一配置的事件，跨 factory 和实体销毁重建的定向 Unity 正确性验证通过。

本轮干净副本的动画隔离组 8/8 通过，默认动画生产入口组 7/7 通过；去重后的相关表现回归共 58 个 testcase，58/58 通过。

这些结果证明了当前剪辑隔离、默认状态登记、Animator 实例播放和既有表现回归的边界行为，不能替代完整 Unity 门禁、真实游戏运行、用户验收或最终画面检查。

本轮没有确认新的 P1/P2 产品缺陷；下面的存档恢复表现问题是有真实 .NET 证据的交互候选，按未评级候选保留，不与已确认缺陷数混合。

## PRES-170-01 修复核对

[UnityViewFactory.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs#L136) 的进程级 authored 快照以 `resource_ref` 为键，覆盖剪辑以 `(resource_ref, event signature)` 为键；这两个表不再属于某个 factory 实例。

`RegisterModelClipEvents` 先经 `UnityResourceLoader.TryLoadAnimationClipSync` 取 base clip（[UnityViewFactory.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs#L1032-L1036)）。

空 `events` 在 [UnityViewFactory.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs#L1040-L1044) 直接保持 authored base clip；该分支没有把别的配置写入共享资源。

非空配置在 [UnityViewFactory.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs#L1047-L1067) 以 authored 快照合并到 `Instantiate(baseClip)` 的运行期副本，并缓存同签名副本。

运行期副本经 `UnityRenderer3D.ApplyAnimClipOverride` 写入该实例 Animator 的覆盖表（[UnityRenderer3D.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs#L1392-L1411)），没有改变共享 `AnimationClip.events`。

覆盖缓存命中 `overrideClip == null` 时会重新实例化，覆盖 Unity fake-null 的销毁后引用（[UnityViewFactory.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs#L1058-L1067)）。

Unity 定向组 [animation-isolation-targeted-clean-v2.xml](animation-isolation-targeted-clean-v2.xml) 为 8/8：`ModelClipEventIsolationTests` 3/3，`Pres170_01SharedClipEventIsolationTests` 5/5。

五个生命周期/顺序用例分别覆盖非空后空、空后非空、跨 factory、场景重建、实体销毁重建；这些用例还经 `PlayAnim` 和 `OnAnimEvent` 收集实际 Animator 播放中的事件集合。

这组测试的注册输入仍通过 `RegisterModelClipEventsForTest`（[UnityViewFactory.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs#L986-L993)），不是完整游戏装配入口，因此不能写成整条游戏播放链已经端到端验证。

## 生产入口与资源生命周期

model View 创建后调用 `RegisterDefaultModelClips`（[UnityViewFactory.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs#L930-L942)）。

该入口从 `display.anim_set` 查记录，逐条把状态到 `resource_ref` 登记并调用事件注册（[UnityViewFactory.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs#L962-L981)）。

默认动画生产入口 [animation-production-entry-clean-v1.xml](animation-production-entry-clean-v1.xml) 为 7/7 通过，包含冷启动请求、状态切换、受击完成、销毁重建和 model root 的基础表现行为。

相关测试直接构造 `UnityViewFactory`，未经过完整 `FrameworkResidentHost`、`GameFoundationBootstrap`、真实游戏场景和正式两套数据表的全链路装配；这项边界已保留。

`UnityResourceLoader.TryLoadAnimationClipSync` 命中自己的 `_animationClips`，未命中时按资源路径同步加载并写回（[UnityResourceLoader.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityResourceLoader.cs#L673-L687)）。

异步队列在 `FinishAnimClipLoad` 复用同一同步解析缓存（[UnityResourceLoader.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityResourceLoader.cs#L468-L475)）；本轮没有把异步动画加载误报为失败。

`Unload` 会移除 loader 的 animation clip 缓存（[UnityResourceLoader.cs](../../../../adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityResourceLoader.cs#L372-L384)），但不会清空 `UnityViewFactory` 的进程级 authored/override 表。

如果同一 `resource_ref` 在同一进程内被重新绑定到另一实际资源或另一 loader 身份，按 Id 的静态缓存可能需要进一步失效策略；本轮没有这样的资源身份变化运行证据，故不列为缺陷或候选结论。

同样没有执行独立 Mesh 冷加载的未确认合同验证，不作推断。

单个 model 的多个状态若在数据中共用一个 base clip，Animator 覆盖表按 original clip 建键；本轮没有完成正式数据装配下的多状态同 clip 播放判定，因此只作为后续验收边界，不计入缺陷。

## 存档抑制与掉落物 View 候选

生产读档逐段 Load 与失败回滚包在 `IEventBus.SuppressDispatch` 作用域（[SaveSystem.cs](../../../../core/foundation/save_system/core/SaveSystem.cs#L365-L389)）。

`GameplayAssembly.RestoreFromSlot` 在目标地图与当前地图相同的情况下不会调用场景路由（[GameplayAssembly.cs](../../../../core/gameplay/assembly/GameplayAssembly.cs#L932-L960)）。

地面掉落物恢复路径是 `DroppedLootPersistable.Load`（[DroppedLootPersistable.cs](../../../../core/gameplay/loot/core/DroppedLootPersistable.cs#L80-L101)）→ `LootHost.RestoreDropped`（[LootHost.cs](../../../../core/gameplay/loot/core/LootHost.cs#L578-L596)）；缺少实体时最终调用 `WorldSim.AddEntity`（[WorldSim.cs](../../../../core/foundation/sim_loop/core/WorldSim.cs#L342-L357)），它会排入 `EntityCreatedEvent`。

`ViewBinder` 只在构造时订阅 `entity.created` 来建立 View（[ViewBinder.cs](../../../../presentation/view_binding/core/ViewBinder.cs#L79-L101)），入口回调见 [ViewBinder.cs](../../../../presentation/view_binding/core/ViewBinder.cs#L133-L157)，没有 `SaveLoaded` 后的世界实体补扫。

独立探针使用真实 `SaveSystem`、`DroppedLootPersistable`、`LootHost`、`WorldSim` 和 `ViewBinder`，只把 `IViewFactory` 换成最小 Stub；实际顺序是 Drop 并建立 View、Save、调用 `LootHost.ClearDroppedExcept(empty)`（[LootHost.cs](../../../../core/gameplay/loot/core/LootHost.cs#L699-L716)）并 tick 模拟同图恢复前实体已从 WorldSim 消失、再 Load 并 tick。

探针结果为 `status=Loaded entity_present=True binder_views=0 created_views=1`；日志中的 `RESULT=PASS` 只表示该候选复现成功，不表示产品验收通过。完整记录见 [SaveLootViewBindingProbe.log](SaveLootViewBindingProbe.log)，源码见 [SaveLootViewBindingProbe.cs](SaveLootViewBindingProbe.cs)。

因此得到一个表现层交互候选：在同图、不切 Scene 的恢复路径里，逻辑掉落物可以恢复到 WorldSim，但 SuppressDispatch 丢弃 `EntityCreatedEvent`，而 ViewBinder 没有 SaveLoaded 补扫，可能留下“有逻辑实体、无绑定 View”。

该证据只到 View 绑定层，不能直接表述为 Unity 屏幕实际不可见；没有把它新增为 P1/P2，也没有扩大成 HUD 或装备外观问题。

建议验收动作是：同图 `RestoreFromSlot` 前清除掉落物实体但保留存档段，恢复后确认 WorldSim、LootHost、ViewBinder 三者都各有同一 id，且再次 tick 不依赖事件补发；修复后把该场景纳入真实 Shell/Presentation 装配测试。

## 去重验证与排除项

干净副本的 8 条隔离 XML、7 条生产入口 XML 和合并回归 XML 均在 presentation 目录留档；合并过滤器的 58 个唯一 testcase 全部通过，结果见 [animation-presentation-regression-clean-v1.xml](animation-presentation-regression-clean-v1.xml)（原件未归档，codex 产出目录已不存在，结论以本节引用的 58/58 数值为准）与 [animation-presentation-regression-clean-v1.log](animation-presentation-regression-clean-v1.log)（原件未归档，codex 产出目录已不存在，结论以本节引用的 58/58 数值为准）。

合并过滤器覆盖 `UnityRenderer3DTests`、`ModelViewTests`、`EquipmentVisualReplayTests`、`HitFrameSyncEndToEndTests`、`AnimReplayAndFinishEndToEndTests`、两组共享 clip 测试和 `UnityViewFactoryDefaultAnimationTests`；8 条隔离用例与 7 条生产入口用例包含在合并结果内，去重后仍为 58。

早先混版副本产生的 `animation-isolation-targeted-v3.xml`（3/8）、`animation-presentation-regression-v2.xml`（34/51）和 `animation-production-entry-v2.xml`（6/7）不属于冻结 1.8 证据，不能用于质量结论；失败原因为副本使用旧 1.7 `UnityViewFactory.cs`，不是当前源码回归。

本轮没有执行完整 Unity 门禁（编译/EditMode/PlayMode 全套）、全量游戏构建、独立版、consumer、IL2CPP、实际游戏压测或用户验收；定向 PlayMode 已包含其自身编译步骤。

本轮只做静态生产入口检查、有限 Unity 正确性运行和独立 .NET 探针，没有网络发布验证。

## 建议修复顺序

第一优先处理同图读档后的 View 重建合同：优先让 SaveLoaded 后的 Presentation 入口按 WorldSim 当前实体补建 View，或明确由恢复路径补发可观察的创建通知，并为掉落物建立完整装配验收。

第二优先为同一 `resource_ref` 多状态共享 base clip 增加正式 `display.anim_set` 数据和真实状态播放验收，确认覆盖键与状态语义一致；在证据出现前不把它列为缺陷。

第三优先明确资源身份变化时 authored/override 静态缓存的生命周期策略，配合 loader 卸载、资源重载和 domain reload 配置做 Runtime 证据；当前定向测试已覆盖实体/scene 生命周期的 fake-null 防护。

本记录只完成审计、日志和探针，没有改产品代码、没有提交；原仓最终状态由主审按冻结 HEAD 复核。
