# ws-game 1.5.0 文档与项目深度复审

审计基线：3224ca1247b119ef656bf928a024a1e0a7701fcc（VERSION 1.5.0），2026-09-08。此次重新核查当前实现，不沿用历史报告的问题状态。本报告的代码链接指向冻结源码；审计没有修改框架源码。

**结论：本轮确认 8 项可行动缺陷：1 项 P1、7 项 P2；其中 1 项 P2 仅在测试钩子构造的模型替换条件路径复现。原回归场景多数已修，但不能把“上轮九项已根治”作为本版整体验收结论。** 各项证据、默认路径和条件路径见下文。文档缺失、框架能力未实现、游戏接入责任分别整理于 [00–14 章文档—代码矩阵](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/doc-code-matrix.md)；补充核对记录见 [文档证据](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/doc-evidence.md)。


| 编号 | 优先级 | 问题 | 本轮证据 |
|---|---|---|---|
| PJ150-01 | P1 | lock.version 越界路径触发删除 | 自建哨兵实际被删除，脚本 exit 0 |
| CR150-01 | P2 | 共享光环重建句柄/引用计数错误 | 正确行为断言失败 |
| CR150-02 | P2 | 宝箱旧余量与重建实体失联 | 同图重建机制实测 + 生产链静态核对 |
| CR150-03 | P2 | 旧档缺 pending 段不清空当前台账 | 真实 SaveSystem 入口断言失败 |
| CR150-04 | P2 | 满包采集无奖励仍提交冷却 | 显式时钟机制实测 |
| PR150-01 | P2 | sprite 装备快照重放早于 Bind | 真实 Unity + 绑定后事件正对照 |
| PR150-02 | P2 | 首次 Tick 前动画已退出，漏 finished | 真实 Animator 状态转换 + renderer 检测 |
| PR150-03 | P2（条件） | 占位模型无 socket，替换后挂件丢失 | 真实 Unity + 模型替换测试钩子 |

## 基线、范围与证据

- 冻结归档：[source_snapshot.zip](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot.zip)；SHA256 为 22A8DFAA24A51056241AB0A1B0648F04762A3A9F8E614A55F286C59B0B1C3250。
- 源码阅读覆盖架构 00–14、ADR、当前能力索引、核心/表现模块契约及关键实现、三个生产装配根、存档/切图/奖励/表现生命周期、构建与交付流程。全套非 Unity 门禁与定向探针补充静态阅读。没有把目录扫描或测试总数称为全部路径已审核。
- 构建在独立 git_snapshot 中进行，新增测试也只写该副本；source_snapshot 保持原提交。Unity 验证使用另一独立副本。
- 开始时 live 工作树 clean。审计过程中外部工作新增命中帧同步、常驻宿主配置及相关测试修改，随后提交为 8927398714fb516121641f8f9a1f6ce0838a0db8。主审已阅读该提交的 7 文件差异；它未修改本轮 8 项缺陷的直接实现文件。冻结版测试不能作为后续提交或外部新增临时场景的验收证据。收尾状态单独记录。
- 审核由 Astra 完成；Luna 执行隔离验证和文档证据整理。主审重新读取了关键源码、测试夹具、日志和交付包，没有仅采信代理结论。

### 已完成的基线验证

| 验证 | 结果及限制 |
|---|---|
| check.ps1 -SkipUnity | 退出码 0；总计 20 步 = **14 PASS + 6 SKIP**，不是 20 步均 PASS。[完整日志](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/check.process.log) |
| .NET 六测试工程 | **2,413 通过，0 失败**：Foundation 655、Numbers 106、Carriers 305、Rules 401、Presentation 486、Gameplay 460；构建 0 警告。 |
| Python 工具链 | **48 通过**。框架独立数据校验有 1 条 l10n 表未加载的警告；合并数据校验 0 错误/0 警告。 |
| 本地 1.5.0 交付一致性 | ZIP 内三个 tgz 与现有 dist/1.5.0/packages 三包逐字节一致；adapter tgz 六 DLL 哈希与 lock 全匹配；版本均为 1.5.0、lock 提交为 3224ca1。[主审交付复核](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/package_coherence.log) |
| 正常离线下载 | 实际 get_framework 校验并落地成功，退出码 0。[完整输出](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/get_framework.stdout.log) |
| 旧 API 源码消费探针 | 使用实际 1.5.0 交付 DLL，ViewKind.GameObject 和 ICharacterRig.HitFrameReached 编译成功；2 条预期 Obsolete 警告。只证明这两个旧入口的源码兼容。[构建日志](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/compat_consumer_15/build.stdout.log) |
| 完整 Unity / 独立版 / 消费方 / IL2CPP | 非 Unity 门禁中的 6 项未执行；此次定向 Unity 探针另列，不能替代完整 PlayMode、独立版两种模式或 IL2CPP 验收。未发布包、未操作远端 Release 或私服服务。 |

新增 .NET 机制探针 4 项：共享光环、缺失存档段按正确行为断言均失败；宝箱实体重建、满包采集按缺陷现状断言均通过。脚本路径探针退出 0，但确认越界删除。Unity 6000.3.23f1 定向执行 5 个测试均 PASS：其中 3 个确认 sprite 重放、首次 Tick 前动画退出、条件 socket 缺陷，2 个支持正常自动退出回归。见 [核心与交付验证明细](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/validation.md)、[Unity 验证明细](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/unity-validation.md)。新增探针与以上原生回归套件分开统计。以正确行为断言时失败，或以缺陷现状断言时通过，都必须结合断言内容解释；“探针通过”不表示产品正确。

## 当前可行动发现

### PJ150-01 · P1 · 锁文件版本可绕过目标目录边界并触发递归删除

定位：[get_framework.ps1](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/toolchain/get_framework.ps1:314)，删除点 [380–383](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/toolchain/get_framework.ps1:380)。

前置条件是使用 -AllowVersionMismatch，并读取带路径片段的 lock.version。脚本仅校验调用参数 -Version 的 X.Y.Z 格式；放行后把未经同等校验的锁文件 version 赋给 EffectiveVersion，拼接路径，随后删除已有目录。六个 DLL 的哈希校验不保护该元数据字段。

安全复现只使用审计新建目录及复制的有效 1.5.0 ZIP/lock。将副本 lock.version 设为 x/../../outside_sentinel，实际规范化落点位于 Target/packages 之外、仍在审计夹具内。**六 DLL 哈希通过，脚本退出 0，哨兵 keep.txt 从存在变为不存在，目标外目录被替换为框架内容。**

证据：[预检绝对路径与哨兵前后值](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/path_probe/path_probe_paths.txt)、[实际脚本输出](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/security_path_probe.log)。

修复要求：锁文件实际版本同样校验格式；规范化落点后验证为 Target 的严格子目录；所有递归替换前完成边界校验。验收至少包含正常版本、合法版本不一致、路径分隔符、..、绝对路径等输入；无效输入不得删除或写入目标与邻居目录。无需构造或删除用户文件。

### CR150-01 · P2 · 跨图恢复共享光环后，卸下一件装备会误删另一件的效果

定位：[EquipmentHost.ReapplyAuraGrants](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/carriers/item/core/EquipmentHost.cs:706)；套装同类逻辑 [ReapplySetBonuses](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/carriers/item/core/EquipmentHost.cs:798)。

两件装备授予同一 aura_def，默认 AllowMultiSourceTiming=false 即可触发。World.ClearAll 清除真实光环；重放第一件产生新句柄，第二件仅因 HasAura(def)=true 就复用上一张地图已经失效的 previous 句柄。新实例的引用计数只有第一件；卸第一件时会移除仍应由第二件维持的效果。

定向探针经真实 World.ClearAll → DispatchPending → AddEntity → Gameplay.EnterMap → Unequip，**恢复后光环存在，但卸第一件后实际 False，预期仍为 True**。没有遗漏事件队列清理。[失败日志](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/probe_a_shared_aura.log)；[复现夹具](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/git_snapshot/core/gameplay/assembly/tests/CR140_02_EquipmentAuraMapClearTests.cs)。

修复要求：重建来源到当前实例句柄的映射与引用计数，不能以“同定义存在”替代“该来源句柄存活”。验收覆盖两件装备、装备+套装、多来源计时、重复重放幂等及逐件卸下。现有单件装备、单独套装测试不能覆盖这一组合。

### CR150-02 · P2 · 宝箱余量按瞬态实体 ID 保存，实体重建后旧账无法关联

定位：[GameObjectHost pending 写入](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/carriers/gobj/core/GameObjectHost.cs:510)、[pending 查询](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/carriers/gobj/core/GameObjectHost.cs:518)、[GobjPendingLootPersistable 保存 ID](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/carriers/gobj/core/GobjPendingLootPersistable.cs:62)。

Partial 台账以 gobj.inst_N 为键。生产 SpawnHost 卸图会清空刷新点的 EntityId，重入生成实体时经 GameObjectFactory/WorldSim 分配新 ID；ClearAll 不重置序列。旧余量仍在台账，却不能通过新实体领取。当前持久化测试更换的是 GameObjectHost，复用了旧 World/实体，并未证明重新创建实体后的关联。

机制探针在同一地图、同一位置、同一模板下，经真实 World.ClearAll/DispatchPending/GameObjectFactory 重建实体：oldId=gobj.inst_1 的余量仍在，新实体 gobj.inst_2 无对应余量。[复现输出](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/probe_c_partial_cross_map.log)。该探针没有直接运行完整 SpawnHost/SceneRouter 场景流程；对应生产链由 [SpawnHost.UnloadMap](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/gameplay/spawn/core/SpawnHost.cs:358)、[OnMapEnter 生成](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/gameplay/spawn/core/SpawnHost.cs:148) 和 [GameObjectFactory.Spawn](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/carriers/gobj/core/GameObjectFactory.cs:32) 交叉核实。

**on_map_enter 本来允许生成新的实体；重新 roll 本身不是这里的错误。** 问题是已记账的旧余量成为孤儿，单纯序列化它不等于完成跨生命周期恢复。新进程若生成顺序不同，还存在瞬态 ID 错配风险；该跨进程风险不能冒充已实测事实。

修复要求：定义稳定的地图/刷新点/奖励归属身份并恢复关联，或把待领奖励转为独立于原箱子的领取载体。若产品明确选择离图放弃余量，则须清理台账并修改能力承诺，不能继续保存无法再访问的记录。

### CR150-03 · P2 · 真正加载旧档时，没有清空缺失的新 pending 段

定位：[SaveSystem.ComputeReadOrder](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/foundation/save_system/core/SaveSystem.cs:632)、[实际 Load 循环](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/foundation/save_system/core/SaveSystem.cs:314)；兼容承诺 [GobjPendingLootPersistable](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/carriers/gobj/core/GobjPendingLootPersistable.cs:25)。

SaveSystem 只对存档中已经存在的段安排 Load。旧档完全没有 world.gobj_pending_loot 时，新的 persistable.Load(JsonNull) 根本不会调用。同一宿主先产生余量再读旧档，读档成功后旧余量仍留存，会污染恢复后的状态及下一次保存。

探针使用真实 SaveSystem.Save 生成合法档案，仅移除新段后经真实 SaveSystem.Load 读取：**LoadStatus.Loaded，但 pending 数量从 1 保持为 1，预期为 0**。[失败日志](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/probe_b_old_save_missing_pending.log)、[复现夹具](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/git_snapshot/core/carriers/gobj/tests/GobjPendingLootPersistenceTests.cs)。

修复要求：在实际恢复入口定义缺失可选段的替换/清空语义；单测直接调用 Load(JsonNull) 不够。补空段、缺段、非空段及同宿主跨槽读取的组合，避免顺带改变其它持久段的既有兼容规则。

### CR150-04 · P2 · 采集物仍先提交冷却，再忽略入包失败

定位：[GameObjectHost.GatherNode](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/carriers/gobj/core/GameObjectHost.cs:585)、[RollLootInto](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/core/carriers/gobj/core/GameObjectHost.cs:598)。

显式注入前进的 SimTime、配置 respawn_after_use>0，在背包满时采集，代码先写 used_at，再忽略 Inventory.AddItem 的失败/部分交付结果。腾位后立即重试仍被冷却挡住，没有余量补领。此问题独立于文档已披露的“默认 SimTime 恒 0”；修复采集时钟并不能解决交付事务。

机制探针显式设 t=100、冷却=60，满包采集后移除填充物，再在 t=101 交互：used_at 前后均为 100，奖励数量均为 0，掉落仅 roll 1 次。[复现输出](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/probe_gather_full_cooldown.log)。PASS 表示确认缺陷现状。

修复要求：成功交付与冷却提交保持一致，或保留此次已抽取的未交付奖励。验收覆盖完全失败、部分成功、成功和同一冷却内重试，不能只测试宝箱分支。

### PR150-01 · P2 · sprite 初始装备重放早于 Bind，装备事件被过滤

定位：[UnityViewFactory.CreateView](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:350)、[SpriteViewBase.Bind](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/presentation/render/core/SpriteViewBase.cs:137)、[装备事件过滤](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/presentation/render/core/SpriteViewBase.cs:185)。

工厂创建 sprite 后立即回放已有装备，但 EntityId 此时仍为默认值，直到工厂返回、ViewBinder 调用 Bind 才设置。重放事件的 unitId 与默认 ID 不相等，被过滤。model 构造期已设置实体 ID，因此仅测 model 重放或 Bind 后手工装备事件会漏过此问题。

影响已装备玩家创建新 sprite View 的场景，例如跨图或恢复后重建外观。三个默认装配根已接入装备快照，所以它不是只存在于自定义构造方式中的缺口。

真实 Unity 探针使用 sample hero/hat：CreateView → Bind → SyncPose 后装备资源加载进度为 **0**，同一装备事件在 Bind 后发送，进度为 **0.5**；装备映射已存在，排除了缺配置导致的假阳性。[Unity 日志与复现代码索引](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/unity-validation.md)。

修复要求：在绑定身份后执行快照恢复，或明确创建/绑定/首次同步的顺序契约；保证 sprite/model 两条路径一致。验收必须通过 CreateView → Bind → SyncPose 的真实顺序，并用 Bind 后同一装备事件作正对照。

### PR150-02 · P2 · 动画在首次检测前已经自动退出时，仍漏发 finished

定位：[UnityRenderer3D 完成检测](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:720)，关键状态记录 [everEnteredTarget](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:729)。

当前修复只有在 renderer.Tick 观察过目标状态后，才允许“已回 idle”表示完成。短剪辑或检测间隔较长时，进入和自动退出都可能在首次观察前完成；此后 everEnteredTarget 一直为 false，依赖 finished 的上层收不到完成通知。

真实 Unity 同步探针未 yield、未提前调用 renderer.Tick：PlayAnim(blend=0)，手动推进真实 Animator 0、0.25、0.10 秒，状态 **idle → test_autoexit → idle → idle**；随后两次 renderer.Tick 的 finished 均为 **0**。前置状态转换由真实控制器完成，没有篡改内部标记。正常自动退出的现有用例和对照仍通过，两者不矛盾。[边界测试 XML](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/unity_probe/adapters/unity/presentation150-animator-before-first-tick-results.xml)。

修复要求：完成通知不能依赖采样恰好命中过短暂状态；建立能识别本次播放进入/退出的可靠生命周期。验收覆盖首次检测前退出、过渡中采样、正常时长、重复 Tick、替换/取消播放，完成事件至多一次。

### PR150-03 · P2 · 条件模型替换时，缺失 socket 的装备意图没有保留

定位：[UnityRenderer3D.AttachToSocket](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/source_snapshot/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:851)。当当前 placeholder 没有目标 socket，方法直接 return，后面的 SocketChildren 登记不执行；ModelCharacterRig 却已记录为装备完成。后续 AttachVisual 只重挂登记表中的 child，无法补上这件装备。

真实 Unity + CompleteAsyncModelSwapForTest 探针先向无 socket.custom 的占位模型挂 child，再替换为存在 socket.custom 的模型：新挂点子节点数 **0**，child 仍留在 GameFoundation.EngineHost 根节点。[条件复现记录](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/unity-validation.md)。

**此项是条件机制缺陷。** 当前默认同步 Resources 解析下，缺失资源未必会自然变为可用；测试钩子绕过了真实异步资源时序。它不证明默认异步链必现，更不等于完整异步加载已实现或通过。

修复要求：挂点暂不可用时也保留挂接意图，待视觉资源就绪后重放；或明确返回失败并允许 rig 重试。验收同时覆盖原模型有/无最终挂点、替换成功/失败以及卸装取消。

## 上轮九项复核

| 上轮项 | 当前结论 |
|---|---|
| CR140-01 满包开箱 | 同一实体生命周期内 Reject/Partial 已替换旧逻辑；新 pending 的跨生命周期及旧档清理不能视为已根治，见 CR150-02/03。 |
| CR140-02 装备光环跨图 | 单件/单套装恢复已接；共享多来源恢复仍有缺陷，见 CR150-01。 |
| CR140-03 自定义跨图 resolver | 已解析的 MapId/Position 随 GobjInteractedEvent 交给 ApplyResolvedTeleport；旧的二次默认解析路径已修。 |
| PR140-01 Blob Quad 朝向 | 当前固定世界朝向和深度偏移已覆盖原问题。 |
| PR140-02 模型替换丢挂件/阴影 | 已登记挂点的 child 先摘后挂、阴影重放已实现；placeholder 缺最终 socket 的条件缺口见 PR150-03。 |
| PR140-03 Animator 自动退出 | 已观察到目标状态后的退出已处理；首次观察前已退出仍漏事件，见 PR150-02。 |
| PR140-04 AoE 命中批次 | 同 attacker 未释放窗口内已合批；没有真实 attack/cast 实例 ID，同一窗口内不同攻击仍可能混批，代码注释已披露，不能称按攻击实例完全闭合。 |
| PJ140-01 旧 API 源码兼容 | 两个旧入口已恢复，实际 1.5 DLL 消费探针编译通过；不扩张为所有二进制/引擎版本兼容。 |
| PJ140-02 缺 ZIP 自动混批 | workflow 已在其它资产仍存在时阻止缺 ZIP 重建；本地当前五件套也一致。人工补包指导仍需修订，见文档矩阵。 |

## 文档应怎样更新

1. **修正当前合同矛盾。** ADR0017/架构02禁止 renderer 隐式加载，但追记批准把同步加载移到 loader 并保持行为；当前缓存未命中仍同步 Resources.Load。须决定禁止的是直接调用，还是首次引用触发加载，随后统一 ADR、接口约束、包 README 和实现。
2. **补齐存档协议。** 架构10、assembly README、KnownOrder 决策须收录新 pending 段，明确缺段清空、加载顺序、稳定身份和领取生命周期。CHANGELOG 的“旧档缺段为空”目前有实测反例。
3. **把能力分为已实现、未默认装配、未实现、明确非目标。** 天赋运行时分配、TargetPoint 消费、FindUnits 便利 API、回放入口、GF 编辑器等不能由 schema/类存在推导为可用；target.chain、模型武器/动画/装备也不能沿用旧报告写成未实现。
4. **列出生产接入缺口。** Quest.Update、采集时钟、owner/day/vendor、武器 swing/impact 消费、VFX 持续跟随及完整新局重置分别注明调用方和验收条件。已披露的限制进入能力清单，不重复包装为新发现。
5. **修正交付与验收措辞。** same HEAD + SyncOnly 不能证明新 ZIP 与旧附件同批；恢复附件应核对整套字节/hash。历史 followup 的通过/跳过计数自相矛盾，应按原始日志更正并保留测试提交。能力索引的两处物理断行应修复表格格式。

每章对应的文件、代码锚点及现状见 [文档—代码矩阵](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/doc-code-matrix.md)。

## 整体项目质量判断与后续验收

项目的模块分层、数据校验、纯 .NET 测试和本地交付一致性已有扎实基础。本轮风险集中在跨模块状态转换，而非缺少类或基本实现：奖励与冷却/开箱提交、存档与真实实体身份、装备来源与光环句柄、View 创建与绑定、动画观察时序，以及发布元数据与文件系统边界。

建议按以下依赖顺序整改，每组可作为有界交付，完成后由独立审核按真实入口验收：

| 顺序 | 交付范围 | 验收条件 |
|---|---|---|
| 1 | 下载工具路径边界 | 无效元数据在任何写入/删除前拒绝；正常/合法 mismatch 保持可用；审计自有邻居哨兵完好。 |
| 2 | 奖励持久化与交付语义 | 先确定稳定身份及缺段恢复合同，再实现宝箱/采集；满包、部分交付、跨图、同宿主旧档、新进程恢复结果一致且不重复发放。 |
| 3 | 装备授予生命周期 | 来源与实例句柄重建正确；两件装备、装备+套装、多来源和最后一件卸下的结果均符合规则。 |
| 4 | 表现生命周期 | sprite/model 初始重放一致；短剪辑/低帧率退出可靠；占位替换保留挂件意图及阴影；命中帧与 timeout 有可区分证据。 |
| 5 | 文档和交付验收 | 合同、实现、能力矩阵、版本说明一致；运行完整目标 Unity/独立版/消费方链，归档真实日志；不能仅把新增单测转绿作为整体闭环。 |

这次交付是审核和复现，不包含框架修复、版本发布或运行验收批准。

## 收尾时的工作区变化

主审在 2026-09-08 15:30（Asia/Tokyo）再次核对：live HEAD 为 8927398714fb516121641f8f9a1f6ce0838a0db8，VERSION 仍为 1.5.0，工作树 clean。该提交在 CHANGELOG 新增 1.6.0 条目，但本轮没有把它认定为已完成的 1.6.0 发布。

已静态阅读 3224ca1 → 8927398 的全部 7 文件差异：常驻宿主开关改为序列化配置、反馈释放原因诊断及测试加固。此提交没有修改本报告 8 项发现的直接实现文件；其“区分真实命中帧与 timeout”的测试加固方向也与本报告验收要求一致。本轮的构建、包和 Runtime 证据仍严格属于冻结的 3224ca1，不转记为 8927398 全量通过。

证据：[收尾状态及缺陷文件差异检查](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/final_scope_check.log)、[后续提交完整差异](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-3224ca1-20260908/later_workspace_change.patch)。
