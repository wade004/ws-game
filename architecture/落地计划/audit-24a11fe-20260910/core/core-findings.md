# Core deep review findings — 1.16.1 / 24a11fe

审计对象是冻结 checkout `D:\workespace\ws-game-artifacts\audit-24a11fe-frozen`，HEAD `24a11fe28f9647cd532c41f56f7ab18c00fb8516`，VERSION `1.16.1`。产品 checkout `D:\workespace\ws-game` 保持 clean；本报告、探针 runner、日志和编译镜像只写入本审计根。

本轮没有把旧版注释或 exit 0 当作功能通过。隔离 runner [run-core-bounded.ps1](repro/run-core-bounded.ps1) 对当前冻结源码运行了仓内既有的 92 个有界回归测试：Gameplay 32、Numbers 14、Equipment/Immunity 46，三项目均 exit 0 且测试全部通过。这些是当前回归测试证据，不称为独立断言。另有独立最小 oracle [CoreBoundaryProbe.cs](repro/CoreBoundaryProbe.cs) 直接驱动三个 resident reload 场景（Quest/Economy/Stat）及一个非法 Id 校验场景，输出见 [CoreBoundaryProbe.current.log](logs/CoreBoundaryProbe.current.log)。回归覆盖 resident 状态在 `reload -> 后续操作` 的连续性，合法输入均先过 registry validation；存档/任务/资源的跨模块状态也纳入了选定的持久化和装配回归子集。

归档重建入口是 [prepare-and-run.ps1](repro/prepare-and-run.ps1)：它接收 `-FrozenRoot`/`-OutputRoot`，先校验冻结 HEAD/VERSION，再从冻结树恢复最小 build 镜像，复制 repro 并通过 `FrameworkRoot` 参数化 probe 项目引用，最后调用两个 runner。入口拒绝已有 build image/raw log，不递归删除；本轮只对入口和路径做静态校验，未重跑已有 raw。

## 已修复并由本版独立验收

### CORE114-01 QuestHost reload 目标数组迁移 — 当前未复现

- 契约：活跃任务在同 id 定义 reload 后，后续合法进度更新不得因旧 `ObjectiveCounts` 形状越界；定义变化必须保持可读状态。
- 触发：先接取一目标 kill 任务并写入进度，再 resident reload 为两目标、减少目标、重排目标、改变 count，以及加入 collect 目标；随后读写新索引。
- 源码：`core/gameplay/quest/core/QuestHost.cs:134-159` 捕获旧定义并调用 `MigrateProgressAfterReload`；`232-270` 按 `(Type, TargetRef)` 配对、clamp、重算状态；`280-290` 对无匹配非消耗 collect 从当前库存重算，其余从 0 起算。
- 期望：目标增加/减少/重排均有与新定义相符的数组，后续更新成功；目标减少或 count 变化时状态按新定义重新判定。
- 实际：`runner-current.log` 的 Gameplay 项 26 个 CORE114/正式校验断言全部通过；其中目标增加、减少、重排、collect、状态升降、定义移除和 Failed 状态均通过。未观察 `IndexOutOfRangeException`。
- 严重度：历史 P2 已修复；1.16.1 当前无 P2 证据。
- 修复验收：保持迁移匹配的一对一语义；未来变更若调整重复 `(Type, TargetRef)` 的配对策略，需同步更新本版 oracle。

### CORE114-02 Economy none/timer/on_map_enter 转换 — 当前未复现

- 契约：同 `(vendor,item)` 的库存消费状态跨定义 reload 保留；策略转换必须使 `TimerRemaining` 与新策略一致，库存上限变化不得凭空返还已消费库存。
- 触发：库存耗尽后 `none -> timer`，`timer -> none`，timer 周期缩短，以及 stock limit 缩小/增大；每次 reload 后调用 resident `Update`。
- 源码：`core/gameplay/economy/core/EconomyHost.cs:104-156` 重建定义并对账；`192-221` 在 timer 转换时初始化/夹取计时器、非 timer 清空计时器，并按上限夹取剩余库存。
- 期望：`none -> timer` 从完整新周期开始并到期补货；反向转换不再补货；周期和上限变化保持自洽与消费守恒。
- 实际：Gameplay 项中的 5 个 Economy 迁移断言全部通过；none→timer 到期补至 2，timer→none 保持 0，周期夹取至 3，上限夹取/扩容均符合断言。未观察 resident 永久卡 0 或 null timer。
- 严重度：历史 P2 已修复；1.16.1 当前无 P2 证据。
- 修复验收：策略表继续只对当前定义有效，余额 `_balances` 不因定义 reload 被重建；下架旧 key 的惰性保留是当前明确边界。

### CORE114-03 StatHost derived cache — 当前未复现

- 契约：reload 后，未显式 `SetBase` 的已缓存 stat 必须使用新 definition；显式 runtime base 必须保留。
- 触发：两个 unit 仅查询 A 以写入旧 cache，reload `default_base`，发布 `DataLoadCompleted`，再查询 A/B；另测显式 base 与 StatChanged 事件。
- 源码：`core/numbers/stat_block/core/StatHost.cs:82-93` reload 后调用重算；`123-150` 遍历既有 cache，用新 definition 重算、删除已删除定义并发布变化事件；注释明确不触碰 `UnitStats.Base`。
- 期望：A/B 同值见新默认值；显式 base 不被覆盖；值变化发一次事件，值不变不发。
- 实际：Numbers 项 4 个 CORE114-03 断言全部通过，包括 query-order、显式 base、事件变化和无变化四项。未观察 A 旧值/B 新值分叉。
- 严重度：历史 P2 已修复；1.16.1 当前无 P2 证据。
- 修复验收：只重算已有 cache 是当前约定；未查询过的 stat 仍在首次查询时使用新定义，不应因审计扩展而提前广播事件。

### CORE114-04 ExprValueJson 非法 Id 正式校验 — 当前未复现

- 契约：`IsValid`/正式内容校验必须把非法 `{"$id":"BAD"}` 转为字段 report；`Parse` 的内容格式错误不得从 Quest/Dialog validation 入口冒泡 `ArgumentException`。
- 触发：正式 `ContentValidationAssembly.Run` 装配下，Quest reward world flag 与 Dialog `set_flag` action 使用非法 Id；同时覆盖 Bool、Int/Number、String、合法 Id。
- 源码：`core/gameplay/common/contracts/ExprValueJson.cs:28-44` 通过 `Id.TryParse` 将非法 Id 统一为 `FormatException`；`68-79` 的 `IsValid` 安全吞掉格式异常；Quest 规则位于 `core/gameplay/quest/core/QuestContentValidationRule.cs:207-218`，Dialog 规则位于 `core/gameplay/dialog/core/DialogContentValidationRule.cs:147-155`。
- 期望：两条正式路径均返回 blocking issue，字段分别为 `rewards.world_flags[0].value` 与 `options[0].actions[0].params.value`；合法形状通过。
- 实际：Gameplay 项中的 7 个 CORE114-04 Quest/Dialog 断言全部通过，非法 Id 得到对应 blocking report，合法五类形状通过；未观察异常冒泡。
- 严重度：历史 P2 已修复；1.16.1 当前无 P2 证据。
- 修复验收：不要把 `Parse` 改成吞掉所有异常；当前只把合法 JSON 形状中的非法 Id 归入格式错误，保留编程错误可见性。

### Equipment set threshold cache candidate — 条件性收口，仍有明确时机边界

- 契约/候选：`_appliedSetBonuses` 中旧门槛在 `item.set` reload 后若不再存在，下一次装备状态重算不应留下孤儿 aura 句柄。
- 触发：合法两件套装 resident equip 使 count=2 门槛生效；reload 删除门槛或改为不可达 count=99；随后执行装备变化。
- 源码：`core/carriers/item/core/EquipmentHost.cs:159-172` 在数据完成事件后刷新 sets；`880-916` 先收集当前定义门槛并释放 applied 中不存在的旧门槛，再处理当前定义；`918-940` 处理新增/降档门槛。
- 期望：删除/改高门槛后下一次 `Unequip`/装备变化释放旧句柄，stat 与 aura 查询回到无套装加成。
- 实际：Equipment 项的 2 个 candidate 断言全部通过：删除门槛后卸下两件、改高门槛后卸下一件，均不残留 aura，stat 回到基值。
- 严重度：当前不升级 P2；修复已覆盖触发后的 orphan cleanup。
- 修复验收/限制：`item.set` reload 本身不会遍历所有已知 unit 主动重算；若 reload 后没有装备变化，旧 aura 可能持续到下一次重算。这是源码 `857-877` 明确写出的边界，需要另行决定是否增加全单位索引与 reload-time sweep。

## 效果免疫 73cb55e 边界

`core/rules/skill/core/EffectDispatcher.cs:100-105` 将 `interrupt`、`dispel`、`energize`、`teleport`、`move` 等原语统一置于入口免疫门，`107-152` 保持分派，`ApplyAura` 保持单独语义；`core/rules/combat/core/Resolver.cs:180-210` 保持 combat damage/heal 的静态与 aura 免疫结算。Equipment/Immunity 项中的 13 个 `EffectImmunityGateTests` 回归测试全部通过，覆盖动态/静态 interrupt、control-only 区分、cost/cooldown、dispel、energize、teleport、self-movement cancel 与可观察 `ResolveResult.Immune`。本证据是 framework assembly + test doubles，不是 Unity 发布 Runtime，也不称为独立免疫 oracle。

## FindUnits 与普通 Gobj 空间索引候选 — 非缺陷

主审提出的触发假设是：若普通 Gobj 与 Unit 共用空间索引，`SkillHost.FindUnits` 的 `AliveOnly=true` 会把 Gobj id 传给 `WorldUnitAccess.IsAlive`，而后者仅接受 Unit。当前冻结实现的实际默认链不满足该触发前提：`core/carriers/assembly/CarriersAssembly.cs:83-131` 的 `DefaultSpatialSyncKinds` 只含 creature/player/area_trigger，明确不含 gobj；`core/carriers/assembly/EntitySpatialSyncHost.cs:86-105` 对未配置 kind 直接跳过登记；`SkillHost.cs:286-304` 还会排除 `trigger_only`，但不会把任意非 Unit 当 Unit。

本版 Equipment 项同时运行 `EntitySpatialSyncHostTests` 与 `ISkillHost_FindUnitsTests` 的空间过滤断言：默认 kind 不含 Gobj，显式配置 Gobj 时按 `gobj` 标签登记，`FindUnits` 对 trigger-only 做排除。源码契约进一步明确：需要 Gobj 查询的消费方必须显式配置 `spatialSyncKinds`，并自行使用 `RequiredTags=["unit"]` 或 `IUnitAccess.Exists` 防止混入（`CarriersAssembly.cs:89-103`）。因此“默认链必崩”未复现，也不列 P2；若未来把 gobj 加入默认清单，必须先补 nearest/FindUnits 全部调用点的标签或 Exists 防御，并添加真实 `WorldUnitAccess + EntitySpatialSyncHost + spatial index` oracle。

## 跨模块存档、任务和资源状态

本轮有界子集另运行了 QuestPersistable、TP-111 loading teleport、ProgressionPersistable、PowerHost reload、Inventory/EquipmentPersistable、UnitPersistable 测试，共 37 个断言，全部通过。它们分别验证任务快照替换/幂等/跨 unit 隔离，装配根的加载中传送状态，等级/XP 及 growth 重放，资源 current/max reload 语义，背包/装备完整替换与链接重建，以及 unit map/position/archetype/race 快照。`SaveSystem.cs:344-486` 的预快照、分段 load、失败回滚和完成事件路径只通过这些现有有界断言间接覆盖；本轮没有声称 Unity 文件系统、真实发布宿主或完整全仓门禁已通过。

## 未证项目

未运行 Unity/IL2CPP、发布包消费、真实 FileSystemWatcher、完整 `check.ps1` 或完整 solution gate；这些由主审/其他审计范围处理。Talent allocator、离散 summon、消费方自定义 `FindUnits` 索引等没有从当前通用契约推出缺陷。

## 证据入口

- [scope-review.md](scope-review.md)
- [coverage.md](coverage.md)
- [bounded runner](repro/run-core-bounded.ps1)
- [archive prepare/run entry](repro/prepare-and-run.ps1)
- [runner raw log](logs/runner-current.log)
- [runner exit marker](logs/runner-current.exit.txt)
- [independent oracle log](logs/CoreBoundaryProbe.current.log)
- [independent oracle runner](repro/run-independent-probe.ps1)
- [source provenance](evidence/source-provenance.sha256)
