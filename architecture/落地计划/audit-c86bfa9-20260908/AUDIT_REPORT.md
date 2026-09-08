# GameFoundation 1.4.0 文档—代码深度审计报告

## 审计基线与范围

本报告针对 `VERSION=1.4.0`、Git `c86bfa98475dbf348f129c0fa507d117d238914c` 的初始 clean 快照。生产源码链接全部指向本目录的 [`source_snapshot`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot)，生产文件字节与归档相同；本轮只在隔离副本改动两处原测试 fixture，并添加审计探针及必要测试引用，这些不是生产改动。`git_snapshot_final` 是补 git-aware gate 的独立 checkout。审计没有编辑 `D:\workespace\ws-game`，也没有查询 GitHub 或声称远端已经发生事故。

本轮相对 1.3.0 的基线变化为 124 个文件、6486 行新增、448 行删除；快照盘点为 1103 个 C#、188 个 Markdown、15 个 HTML tracked 文件。旧 1.3.0 的 17 项逐条复核后，当前锁定 **9 项：1 P1、8 P2**。报告只把有源码触发链的现状列为发现；“未实现”和“默认未接线”另列，不并入 9 项。

相关材料：[`doc-code-matrix.md`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/doc-code-matrix.md)、[`check-git-aware-skipunity.log`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/check-git-aware-skipunity.log)、[`pytest-git-aware.log`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/pytest-git-aware.log)。

## 证据等级

- **E1 机制探针**：用固定快照生产 DLL/真实宿主接线运行，输出可重复的触发结果。
- **E2 静态实现**：固定源码、文档、构建脚本或包清单直接推导触发条件和影响。
- **E3 Unity/消费者运行**：真实 Unity 场景、导入器、PlayMode 或消费方 smoke。
- **E4 文档或契约漂移**：文档、版本契约、默认装配与代码不一致。

本轮已有 E1 机制、E3 定向 Unity 机制结果和离线包校验；完整 Unity/consumer smoke 未执行。已有测试文件不等于完整 Unity 路线验收。Consumer13 的独立编译通过用于兼容基准，Consumer14 的失败用于证实 1.4 兼容性破坏。

## 当前发现索引

| ID | 等级 | 证据 | 当前结论 |
|---|---|---|---|
| CR140-01 | P1 | E1+E2 | 一次性宝箱先置 opened，再逐项直接入包；真实满包时奖励丢失且不能重试。 |
| CR140-02 | P2 | E1机制+E2静态链 | 跨图清场后 EnterMap 没有装备/套装光环重建步骤；持久装备集合与运行期 Aura 可能脱节。见 [`equipment_aura_mapclear.log`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes/equipment_aura_mapclear.log)。 |
| CR140-03 | P2 | E1+E2 | 自定义跨图 resolver 已在 gobj 路径给出结果，但事件只带 ref，监听又用内置 resolver 重解析并覆盖结果。 |
| PR140-01 | P2 | E2+E3 | Blob Quad 固定按 X 轴旋转 90°；在当前 XY 地面约定下几何法线/显示平面不符合影子基准。见 [`presentation-validation.md`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes-presentation/presentation-validation.md) 与最终 [`presentation-unity-results-3.xml`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes-presentation/unity_workspace/adapters/unity/presentation-unity-results-3.xml)。 |
| PR140-02 | P2 | E2+E3 | 条件异步模型替换销毁旧 VisualRoot，却不恢复 socket 子模型和新可见根的投影阴影状态。见 [`presentation-validation.md`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes-presentation/presentation-validation.md) 与最终 [`presentation-unity-results-3.xml`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes-presentation/unity_workspace/adapters/unity/presentation-unity-results-3.xml)。 |
| PR140-03 | P2 | E2+E3 | Animator 自动 Attack→Idle 过渡依赖状态结束检测；状态切换时完成事件可能漏发。见 [`presentation-validation.md`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes-presentation/presentation-validation.md) 与最终 [`presentation-unity-results-3.xml`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes-presentation/unity_workspace/adapters/unity/presentation-unity-results-3.xml)。 |
| PR140-04 | P2 | E1+E2 | 同一攻击批次多个目标仍按攻击者 FIFO 每次只释放一条，剩余反馈可进入下一攻击窗口。见 [`aoe-raw.log`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes-presentation/aoe/aoe-raw.log)。 |
| PJ140-01 | P2 | E1+E2+E4 | 删除 ICharacterRig.HitFrameReached 与重命名 ViewKind.GameObject 破坏 1.3 消费方源码兼容。 |
| PJ140-02 | P2 | E2 | zip 缺失时按 zip 名触发全量重建，但已有 lock/tgz 仍被保留并只上传缺 zip，存在跨批次混装路径。 |

## 逐项发现

### CR140-01（P1）一次性宝箱在发奖前被永久标记已开

`OpenChest` 先把 `open_state` 写为 true，再调用 `RollLootInto`；后者直接对每个 stack 调 `Inventory.AddItem`，没有 `BeginBatch`、Partial 结果或失败回滚，见 [`GameObjectHost.cs:296`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/carriers/gobj/core/GameObjectHost.cs:296) 与 [`GameObjectHost.cs:328`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/carriers/gobj/core/GameObjectHost.cs:328)。该 chest 路径绕过了已有 `LootHost.PickUp` 的 Reject/Partial 事务处理，后者见 [`LootHost.cs:390`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/gameplay/loot/core/LootHost.cs:390)。

满包时可能产生部分交付或零奖励，随后再次交互因 `open_state` 直接返回。探针使用真实 `InventoryHost` + `GameObjectHost` + stub loot，日志见 [`inventory_reject_chest.log`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes/inventory_reject_chest.log)，证明当前状态副作用；尚无磁盘存档证据，不把它表述为已写入存档的数据损失。

建议验收：采用 `LootHost.PickUp` 同样的 batch/Partial 协议；只有整批交付成功才提交 `open_state=true`，部分交付保留剩余奖励并可重试，掉落器或库存失败不得吞掉奖励。

### CR140-02（P2）跨图清场后未重建持久装备与套装光环

跨图由 `SceneRouter.FinishLoading` 执行 `World.ClearAll` 并派发 entity.destroyed，见 [`SceneRouter.cs:265`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/foundation/scene_router/core/SceneRouter.cs:265) 与 [`SceneRouter.cs:276`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/foundation/scene_router/core/SceneRouter.cs:276)。AuraHost 在 [`AuraHost.cs:136`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/rules/skill/core/AuraHost.cs:136)–[`AuraHost.cs:155`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/rules/skill/core/AuraHost.cs:155) 响应销毁事件移除目标名下运行 Aura，但 EquipmentHost 的 `_equipped` ledger（[`EquipmentHost.cs:68`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/carriers/item/core/EquipmentHost.cs:68)）仍保留 equipped 记录。Unity 常驻宿主的 [`FrameworkResidentHost.cs:639`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Shell/FrameworkResidentHost.cs:639)–[`FrameworkResidentHost.cs:658`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Shell/FrameworkResidentHost.cs:658) 只在玩家缺失时把同一玩家实体加回并调用 `Gameplay.EnterMap`；其中 [`GameplayAssembly.cs:827`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/gameplay/assembly/GameplayAssembly.cs:827) 的 post-load 只执行掉落、区域触发、刷新和经济补货，没有按持久装备重放 grants/Aura 的步骤。装备持久化注册在 [`GameplayAssembly.cs:1150`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/gameplay/assembly/GameplayAssembly.cs:1150)。独立真实机制日志 [`equipment_aura_mapclear.log`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes/equipment_aura_mapclear.log) 观察到 `afterAura=False`、`afterEquipped=True`；这支持 E1 机制与 E2 静态链，不伪称完整 router E2E。

建议验收：跨图前后 equipped instance 集合和 Aura 记录分别一致；新图实体创建后按 instance→definition→grants 重建装备与套装授予的 Aura；重复 post-load 幂等，不重复叠加 Aura。当前 `equipment_aura` 是真实 Carriers/`ClearAll` 机制证据，完整 router E2E 仍未执行。

### CR140-03（P2）自定义跨图 resolver 结果被内置路径覆盖

事件监听在 [`GameplayAssembly.cs:758`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/gameplay/assembly/GameplayAssembly.cs:758) 只接收 ref，随后 `TeleportUnit` 又使用装配根 `_teleportTargetResolver` 重解析，见 [`GameplayAssembly.cs:1231`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/gameplay/assembly/GameplayAssembly.cs:1231)。相邻注释宣称“原样使用 ref”不能改变代码没有携带已解析 `(MapId, Position)` 的事实。

复现日志 [`crossmap_custom_resolver.log`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes/crossmap_custom_resolver.log) 显示期望自定义位置 `(99,88)`、实际 `(30,40)`。sameMap/null 的早退路径已有修复；建议让第一次 crossMap resolver 判定成为唯一权威：事件携带解析结果或判定句柄，同图位置只能落地一次。

### PR140-01（P2）Blob Quad 的显示平面与当前地面约定不一致

`SetShadow` 创建 `PrimitiveType.Quad`，固定 `localRotation = Quaternion.Euler(90,0,0)`，见 [`UnityRenderer3D.cs:750`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:750) 与 [`UnityRenderer3D.cs:774`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:774)。包 README 顶部仍写旧 `(X,height,Y)` 映射及 XZ 平面约定，见 [`README.md:23`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md:23)；当前相机实现使用 XY 平面，其平面/投影锚点见 [`UnityCamera.cs:67`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityCamera.cs:67)。Blob 与角色显示基准不一致，出现侧立/法线方向错误。

建议验收以同一 planePos、height=0、相机设置同时检查角色、sprite 与 Blob 的显示平面和法线；再用 height>0 检查影子贴地。定向 Unity 机制结果 dot≈1.19e-7，说明该 fixture 的法线断言通过；仍需产品相机/地面消费 smoke。

### PR140-02（P2）异步模型替换没有恢复 socket 与投影阴影状态

异步成功回调在 [`UnityRenderer3D.cs:295`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:295) 调用 `AttachVisual`；该方法销毁旧 `VisualRoot`，只重放 SlotMeshes、MaterialParams 和最后一次动画，见 [`UnityRenderer3D.cs:333`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:333) 与 [`UnityRenderer3D.cs:358`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:358)。挂在旧 VisualRoot 下的 socket 子实例随旧树销毁；此前 `None`/`Blob` 的关闭投影设置未重放，新 renderer 回到 `On`，尽管 Blob 对象本身在 Root 下可保留。

建议验收仅覆盖“先缺资源、后动态可用才替换”的条件路径，须恢复 socket child、shadow mode（尤其 None/Blob 设置）、slot/material/animation 全部状态。定向 Unity 结果观察到 parentReal=true、childDestroyed=true、句柄残留、后续 Detach 的 `MissingReferenceException` 和新 renderer 投影状态回 On；该结果只适用于该异步 fixture，不泛化为所有冷启动。

### PR140-03（P2）Animator 自动过渡的完成事件存在漏发窗口

模型完成事件依赖宿主每帧调用 `Tick`，再由 `IsAnimatorStateFinished` 在 [`UnityRenderer3D.cs:565`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:565)–[`UnityRenderer3D.cs:571`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:571) 判断当前状态。自动 Attack→Idle 过渡若在检测帧已改变当前状态，完成判断以 Idle 取代 Attack，`finished` 事件不会发给状态机；状态机的瞬态锁因此可能残留。回调接线见 [`UnityViewFactory.cs:841`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:841)。定向测试观察到 Attack 已回 Idle、`finished=0`，详见 [`presentation-validation.md`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes-presentation/presentation-validation.md)。

建议以原始播放 token/state 发出一次且仅一次 finished，再允许 Idle 过渡；分别检查重播、自动过渡、Animator 与 Legacy 路线。定向 Animator fixture 观察 Idle=true、finished=0；这只是机制窗口结果，不是完整游戏路线验收。

### PR140-04（P2）同一次攻击多目标反馈仍按 FIFO 单条释放

`HitFrameSyncPolicy` 把等待项逐条加入 `_pending`，见 [`HitFrameSyncPolicy.cs:91`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/presentation/feedback_binder/core/HitFrameSyncPolicy.cs:91)；命中事件只查找同一攻击者最早一项、移除并 `return`，见 [`HitFrameSyncPolicy.cs:141`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/presentation/feedback_binder/core/HitFrameSyncPolicy.cs:141)。同一攻击批次多个目标并不作为一个不可拆分的 release batch 出队，后续目标会等下一命中帧或超时。真实 `aoe-raw.log` 记录了同一命中帧只释放 A、下一帧才释放 B、超时释放 C 的 A/B/C 顺序。

建议把一次逻辑攻击的所有规则封装成带 attack token 的批次；一个命中帧一次释放该 token 全部目标，下一攻击使用新 token；超时也按 token 原子释放。

### PJ140-01（P2）1.4 公共契约破坏 1.3 消费方源码

`ViewKind.GameObject` 已改为 `Gobj`，见 [`ViewKind.cs:17`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/presentation/common/contracts/ViewKind.cs:17)；旧 `ICharacterRig.HitFrameReached` 从接口移除，改由新增 `IHitFrameEmitter` 承载，见 [`ICharacterRig.cs:25`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/presentation/render/contracts/ICharacterRig.cs:25) 与 [`IHitFrameEmitter.cs:23`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/presentation/render/contracts/IHitFrameEmitter.cs:23)。Consumer13 独立编译通过，Consumer14 使用真实 1.4 DLL 时出现 `ViewKind.GameObject` CS0117 与 `ICharacterRig.HitFrameReached` CS1061，日志见 [`consumer14_build_final.log`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes/consumer14_build_final.log)。

CHANGELOG 迁移说明位于 [`CHANGELOG.md:89`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/CHANGELOG.md:89)，但同文件 [`CHANGELOG.md:113`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/CHANGELOG.md:113) 仍宣称不破坏既有调用方；工程规范把不兼容变更归为 MAJOR，见 [`architecture/11_工程规范与测试.md:156`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/architecture/11_工程规范与测试.md:156)。建议保留旧别名/兼容层，或在下一个 major 发布；不得覆盖已发布 1.3/1.4 包。

### PJ140-02（P2）zip 缺失分支可能拼接不同批次的发布材料

工作流 [`release.yml:275`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/.github/workflows/release.yml:275) 只以 zip 文件名判断 `zipMissing`；[`release.yml:380`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/.github/workflows/release.yml:380) 在 zip 缺失时执行全量构建；[`release.yml:286`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/.github/workflows/release.yml:286) 的 `missing_files` 仍只记录缺附件，上传见 [`release.yml:398`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/.github/workflows/release.yml:398)。旧 lock/tgz 仍在而 zip 丢失时，会形成新 zip + 旧附件，只上传缺失文件。

当前 1.4 正常离线包的 6/6 DLL hash、模型/动画占位资源和 tgz 清单见 [`release_inventory_raw.log`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes/release_inventory_raw.log)，这证明正常批次可消费，不能证明缺 zip 分支安全。相同 HEAD 的隔离 Release 构建与 live lock 的 Core.Foundation 字节不同，见 [`rebuild-hash-comparison.log`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes/rebuild-hash-comparison.log)；这只支持“zip 缺失而旧 lock/tgz 仍在时不可假定同批”，未模拟远端上传，也不声称现有正常包错误。建议先从既有 tgz/lock 复原同批 zip 并逐 DLL 校验；无法证明同批时阻断发布。

## 旧 1.3.0 17 项复核

各项详细当前状态见 [`doc-code-matrix.md`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/doc-code-matrix.md) 的对应能力行；下表只保留与 1.4 新发现的关系，避免把局部修复泛化为全链路通过。

| 旧项 | 1.4 复核结论 |
|---|---|
| CR130-01 | 购买/任务事务按矩阵已有 batch/Partial 路径；CR140-01 的 GameObject chest 直发绕过该协议，不能把旧项的局部修复扩展到 chest。 |
| CR130-02 | RewardDispatcher/技能来源的局部保存路径已有实现；完整游戏磁盘存档纵切未由本轮证明。 |
| CR130-03/04 | charge、Proc ICD、school lock 与 channel 时间族按矩阵复核，未把局部时间族结论泛化为所有装配边界。 |
| CR130-05 | **部分修复**：sameMap/null 的二次传送路径已修，crossMap 仍把 ref 交回内置 resolver，形成 CR140-03。 |
| PR130-01～08 | 坐标、完成事件、动画登记、命中帧、资源降级、Vfx、装备反查和影子贴地均有局部实现；1.4 新路径差异分别形成 PR140-01～04，默认接线与 Unity 范围按矩阵保留。 |
| PJ130-01～04 | 附件检测、占位资源交付、注册表 PID 防护和版本契约按矩阵复核；新 zip 混批与 1.4 公共契约破坏分别形成 PJ140-02/PJ140-01，正常包检查不替代发布分支验证。 |

## 已修、仍漂移、未实现、默认接线与明确不支持

### 已修并有当前源码依据

3D 模型已经有 `CreateModelInstance`、Animator/Legacy 播放、完成事件、slot/socket、材质和 Blob/Projected shadow 的真实实现，不能再写成“3D 整体未实现”。Target chain 的 shape 查询可用：`TargetHost` 调用 [`TargetHost.cs:139`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/rules/targeting/core/TargetHost.cs:139) 到 [`BuiltinTargetStrategies.cs:102`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/rules/targeting/core/BuiltinTargetStrategies.cs:102)，由 [`RulesAssembly.cs:260`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/rules/assembly/RulesAssembly.cs:260) 默认注册；未实现的是 `ISkillHost.FindUnits` 便利 API。落地计划能力索引 [`落地方案与分阶段计划.md:1174`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/architecture/落地计划/落地方案与分阶段计划.md:1174) 的“范围整体默认不可用”属于过度泛化，应改为“FindUnits 便利 API 未实现；target.chain 形状查询已默认接通”。

### 仍漂移的文档与实现

- 包 README 顶部 [`README.md:23`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md:23) 仍写旧 `(X,height,Y)`（XZ 平面）/缺资源 throw；同文件 [`README.md:603`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md:603) 又写已降级，需统一。
- ADR0017 决策 1 规定 renderer 不隐式加载；但 [`UnityRenderer3D.cs:265`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:265) 直接 `Resources.Load`，[`UnityRenderer3D.cs:295`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:295) 又发起 `LoadAsync`，需统一责任边界。
- CHANGELOG [`:18`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/CHANGELOG.md:18) “17 项全部根治”与 [`:113`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/CHANGELOG.md:113) “不破坏既有编译”需收窄为已验证范围。

- 传送文档的“唯一权威结果／不再独立解析”仍超出实现：见 [gobj README](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/carriers/gobj/README.md:157) 与 [assembly README](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/gameplay/assembly/README.md:245)。同图和 null 分支已修；跨图只传原始引用，上层仍用内置 resolver 再解析，应明确 CR140-03 尚未闭合。

### 未实现或当前明确边界

GF 内容编辑器未实现（与已存在的 toolchain 校验器、生成脚本分开）；talent 查询的激活存档、`ISkillHost.FindUnits` 便利 API、TargetPoint 便利入口、采集时钟、`dayProvider`/`ownerResolver`/`QuestUpdate`/vendor 回调、完整新局重置、生产回放装配、`day_cycle` 与 ATB 全链路、孤儿检查/校验器默认接线、DisplayCoverage、FeedbackRuleValidator 全默认接线仍需按能力表处理。离散模式下召唤 duration 不推进，掉落自动清理未在离散 tick 中调用；LootHost 的绝对 `ExpireAt` 时钟仍推进。以上条目分别属于未实现、未默认接线或本版明确排除，详见矩阵，不自动计入 9 项缺陷。

武器 Swing/Impact 能力的定义在 [`WeaponStyleResolver.cs:20`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/presentation/vfx_sfx/core/WeaponStyleResolver.cs:20) 与 [`WeaponStyleResolver.cs:30`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/presentation/vfx_sfx/core/WeaponStyleResolver.cs:30)；当前 core/presentation/games 只有定义和测试消费者，没有生产调用。默认 factory 的 `equipVisual` 只进入 model：[`UnityViewFactory.cs:287`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:287) 传给 `UnityModelView`，[`UnityViewFactory.cs:310`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:310) 的 `UnitySpriteView` 没有该入口。`EquipmentVisualSource` 声明初始库存限制，见 [`EquipmentVisualSource.cs:26`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/presentation/render/core/EquipmentVisualSource.cs:26)，而 `InventoryHost.InjectInstance` 不发 `item.added`，见 [`InventoryHost.cs:275`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/source_snapshot/core/carriers/item/core/InventoryHost.cs:275)；跨图新 View 也不重放既有装备事件。

## 修复依赖顺序与验收边界

先修数据损失：CR140-01 的箱子事务与 CR140-02 的装备/Aura 重建；同时可并行修 3D 平面/异步状态恢复。其次修传送权威性，再修动画完成、命中帧批次与默认装配，最后处理发布包分支和文档/CHANGELOG 契约。每项验收必须同时保留静态源码断言和可执行探针；E1 通过不表示 Unity、导入器、性能或用户流程通过。

当前已验证：git-aware gate 2394 tests 全过、Python 48、14 PASS/6 SKIP、exit 0；合并根 60 表 284 记录 0 错误/0 警告，框架根 5 表 124 记录 0 错误/1 警告；正常 1.4 zip DLL hash 为 6/6，模型/动画占位资源和包清单检查通过。Unity 定向机制结果覆盖 Blob、Animator 和异步 swap，共 3 项缺陷机制复现，不是完整 Unity 测试套件；完整 Unity 编译、独立版 smoke、消费方演练仍是 SKIP，见 [`check-git-aware-skipunity.log`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/check-git-aware-skipunity.log) 与 [`presentation-validation.md`](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-c86bfa9-20260908/probes-presentation/presentation-validation.md)。
