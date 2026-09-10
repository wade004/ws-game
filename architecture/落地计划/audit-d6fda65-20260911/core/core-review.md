# Core 深度审核记录 — ws-game 1.18.0

- 冻结源码：`D:\workespace\ws-game-audit-d6fda65-20260911`。
- HEAD：`d6fda65cd6b00ede5c5f6f606f724d2ab0f60603`；VERSION：`1.18.0`。
- 唯一写范围：`D:\workespace\ws-game-artifacts\audit-d6fda65-20260911\core`；未修改产品源码。
- 审核者：Astra；本记录没有转派执行。按主审恢复指令停止扩展实验，先交付已证实内容。
- 证据等级：两项缺陷有独立 .NET 8 源码探针；grid_snap 为文档/代码静态对照；其余未执行的候选没有升级为已确认缺陷。
- 此探针将冻结 core 源码以 Compile Include 编入独立可执行工程；不是正式预编译包 ABI 证明，不是 Unity Runtime 验收。基础目标类型/空间对象部分使用仓库既有测试替身，SkillHost、QuestHost、EventBus、DataRegistry、StatHost、PowerHost 使用真实实现。

## 已确认缺陷

### CORE-118-01 [P2] 死亡事件延后派发时，已接受施法与队列请求没有终结事件

**位置**：`core/rules/skill/core/CastPipeline.cs:445-448`。同类结束防御路径位于 `486-494`；正常终结通知集中于 `576-609`。

**触发**：一个读条已接受，并接受一个队列请求。施法者状态先变为死亡、`UnitDiedEvent` 已 Enqueue，但先运行 `SkillHost.Update`，随后才 DispatchPending。

**实际**：`AdvanceOne` 检出失效施法者，直接 `_casting.Remove(casterId)`。这既绕过 `Interrupt` 的 `SkillCastInterruptedEvent`，也绕过排队请求的 `QueueCleared` 失败事件。随后死亡事件派发时 `Interrupt` 已查不到状态，无从补发。调用方持有的两个 `CastInstanceId` 均没有终结通知。

**预期**：拒绝继续结算的同时，以原实例 id 为当前读条发一次 Interrupted，以队列实例 id 发一次 QueueCleared；死亡重复通知不应重复发出终结事件。

**可复现输出**（原文见 `probe/probe.log`）：

```text
death_dispatch_before_update=True active=skill.cast_inst_1 queued=skill.cast_inst_2 interrupted=1 queueFailed=1 expected=1,1 casting=False
death_dispatch_before_update=False active=skill.cast_inst_1 queued=skill.cast_inst_2 interrupted=0 queueFailed=0 expected=1,1 casting=False
```

**影响/生产路径依据**：1.18 新增的实例生命周期关联在该时序下不完整，依赖终结事件释放读条/请求状态的消费方会保留悬挂状态。`WorldSim.cs:262-268` 先执行各 tick phase 再派发事件，不能假定死亡通知总在后续施法推进前到达；`SkillHost.cs:645-646,679` 还先推进光环后推进施法。探针已证明独立宿主合法调用序下的故障；本轮没有构造 Unity 完整生产回放，不能声称已在 Unity 复现。

**修复方向**：把所有失效施法者退出路径收敛到同一个带事件的终结 helper，删除前保留 active 和 queued 的实例 id；检查 `FinishCast` 已先删字典的分支，不能只机械调用依赖字典存在的 `Interrupt`。新增事件延后派发测试，不只测试先 Flush 再 Update。

**合同依据**：`architecture/落地计划/消费方反馈-2026-09-10-施法时序与实例标识.md:242-252` 明确列出队列覆盖/中断清队列的实例通知；`CastPipeline.cs:85-99,598-609` 对当前施法和排队请求各自 id 的注释与正常实现。现有 `CastPipelineDeathDestroyTests` 多数先 Flush，再检查停止施法；其不经死亡事件的防御测试未检查新终结事件，未覆盖此差异。

### CORE-118-02 [P2] 活跃任务定义删除再恢复的热重载留下无法消费的运行态

**位置**：`core/gameplay/quest/core/QuestHost.cs:221-229`；具体异常消费点 `555` 与 `383`。

**触发 A**：接取含一个 kill 目标的任务并取得进度，`Reload(Array.Empty<QuestDefinition>())` 删除其定义，随后调用单位级 `GetActiveObjectives(player)`。

**实际 A**：Reload 按现行策略保留 `_progress`；追踪查询通过 `_definitions[kv.Key.QuestId]` 访问已删除定义，抛 `KeyNotFoundException`。这不是用户向 GetState 传入未知任务 id 的合同异常：GetActiveObjectives 只收到仍有效的玩家 id。

**触发 B**：沿用上述宿主，再 Reload 同一个任务 id，但新定义含两个目标，随后对合法的新目标 index=1 调用 UpdateProgress。

**实际 B**：迁移只保存紧邻上次的 `_definitions`；中间删除后旧定义已经丢失，`oldDefinitions.TryGetValue` 失败被当成“不应发生”而 continue，ObjectiveCounts 仍是长度 1，抛 `IndexOutOfRangeException`。

**可复现输出**（原文见 `probe/probe.log`）：

```text
QUEST removed GetActiveObjectives=KeyNotFoundException expected=no exception for unit-only query
QUEST restored UpdateProgress=IndexOutOfRangeException expected=no exception, second objective valid
```

**预期**：删除定义时既然保留进度，单位级跟踪/领域事件消费者应安全跳过暂停的孤立任务；恢复定义时按保留的旧目标身份迁移，或有明确的安全重置策略，至少保证新定义与计数数组同形。另一可行策略是在原子替换前拒绝有活跃进度的结构删除，并返回明确诊断。

**影响/生产接线**：开发期热重载删除/恢复任务后，追踪读取可中断，恢复后合法进度更新仍失败。`GameplayAssembly.cs:731-735` 把真实 DataLoadCompleted 接到 Quest.Reload。`QuestHost.cs:908,939,997,1040,1115` 等领域消费者也直接索引定义，静态看存在同一删除态风险；本轮只动态验证了上述两个公开调用，不把全部领域事件路径写成已运行。

**修复方向**：维护孤立进度和其原目标身份，所有按玩家/事件枚举进度的入口过滤缺失定义；重现定义后重新迁移数组和任务状态。回归至少包含：删除后的追踪/无关领域事件、删除后原样恢复、删除后增加/重排目标恢复。

**合同依据/旧问题复核**：`QuestHost.cs:164-166` 明确目标数组迁移应避免 UpdateProgress/GetActiveObjectives 越界；`201-205` 明确删除定义时保持进度。现有 `CORE114_01_QuestHostReloadMigrationTests` 已覆盖直接增加/减少/重排目标的修复实现，但删除测试只断言 Reload 不抛和 GetState(删除 id) 按合同抛；它没有证明删除后单位级消费者或后续恢复安全。因此旧 CORE114-01 不能概括为“所有热重载形态彻底修复”。

## 文档—代码差距：可选能力已声明，但没有运行时消费

### CORE-DOC-118-01 grid_snap / 按格子中心采样

- `architecture/04_数据与内容管线.md:211` 声明 `grid_snap: Optional<{cell_size: Number}>`，启用时做格子吸附、范围按格子中心采样。
- `architecture/adr/0013-时间模型可替换即时与回合制同一规则层.md:18` 决策 6 同样承诺该可选策略；其明确延期的是 atb，没有把格子吸附归为延期或游戏独有职责。
- `core/foundation/sim_loop/schema/TimeModelSchema.cs:48-49` 只有 Object 字段声明。
- `core/foundation/sim_loop/README.md:276-282` 明确承认没有任何运行时代码读取 grid_snap/cell_size，“格子吸附本身尚未落地”；因此没有为它补子结构登记。
- 分类：**有文档声明、尚未实现的可选框架能力**。不是要求所有游戏使用格子，不是导航性能缺陷，也不是把游戏口味误判为框架必须提供的内容。
- 建议：能力索引和数据文档显式标明当前未实现/保留位，或实现完整的读取、吸附、目标采样及验收。仅补 schema 元数据不等于能力落地。本项提供给主审归并文档发现，避免重复计缺陷。

## 静态候选，尚未动态验证，不计入已确认缺陷

1. **Projectile 的上下文标识丢失**：`core/carriers/projectile/core/ProjectileHost.cs:128-146,359-374,442-463` 保存了 Source/Skill/Tags，但没有保存 TriggerChainDepth/AttackInstanceId；命中时新 EffectContext 因省略参数会回到 depth=0、attack id=null。静态可能造成 projectile→trigger_spell 链绕过深度预算，以及命中批次关联失真。按恢复指令，本轮未再构造投射物探针；没有宣称出现无限循环、Unity 动作错批或可观测运行影响。建议独立后续审核这两项语义。
2. **Vec2 非有限数校验差异**：主审提示 DataRegistry 的 Vec2 分支只检查 x/y 为 JsonNumber；本分支定位了 `DataRegistry.cs:1228-1232`，没有继续做异常数据探针，交由主审统一处理。
3. **其他定义缓存删除态**：静态读取发现 Dialog 保留 session 后整体替换定义，Aura 更新按 DefId 重取定义；删除仍在使用的定义可能有相邻边界，但未完成当前契约与独立动态验证，不给缺陷评级。
4. **超大整数边界**：Inventory 数量/容量、Economy 价格/余额使用普通整数算术。未验证正式内容门禁和可达数据范围，不把理论溢出当已确认产品缺陷。

## 逐模块覆盖表与证据边界

“重点读”表示读取了关键实现/契约或测试；“装配/索引”表示只确认路径或装配，不是完整源码逐行验收；“未深入”明确保留覆盖缺口。表内“未新增确认”不等于模块已证明无缺陷。

| 层/模块 | 本轮实际覆盖 | 结论与边界 |
|---|---|---|
| foundation/common | Id/Json 在探针与各模块的使用，目录索引 | 未深入全部数值/JSON 实现 |
| foundation/app_lifecycle | GameplayAssembly 创建状态、装配入口 | 装配/索引；未重跑状态机 |
| foundation/data_registry | ValidateFieldValue、Map/引用/IdList 校验路径；Skill 探针真实 LoadAll | 1.18 Map 落地存在；Vec2 候选未动态验证；未全遍历加载回滚路径 |
| foundation/expr | 1.17-1.18 变更记录、RecordExprMapping/Expr 字段取消引用声明 | 索引/契约；未独立重验 lexer、span、KnownKeys 全量行为 |
| foundation/event_bus | probe 的真实延后派发/先派发差异、WorldSim 调用顺序 | 支撑 CORE-118-01；未发现 EventBus 自身新缺陷 |
| foundation/hook_registry | GameplayAssembly Declare/调用接线 | 装配/索引，未深入 |
| foundation/sim_loop | WorldSim Tick、删除、ClearAll、intents/phase routing；ADR-0013 | 重点读；grid_snap 未实现；未重跑完整离散调度矩阵 |
| foundation/scene_router | 游戏装配 SceneRouter/RestoreFromSlot 接线索引 | 未深入异步路由实现 |
| foundation/input_map | 模块/契约索引 | 未深入 |
| foundation/save_system | SaveSystem 读段顺序、预快照、SuppressDispatch、失败正序回滚/派生重算、PlayerVitals 对账接点、meta/备份检查 | 重点读；旧修复实代码存在，但本轮未重跑成功/失败全部存档 oracle |
| foundation/rng | probe 使用真实 RngHost；rng 持久化文档 | 未动态重验存档流恢复 |
| foundation/localization | 模块/契约索引 | 未深入 |
| foundation/display_info | 模块/契约索引 | 交表现审核代理，本分支不验外形效果 |
| foundation/engine_adapter | ISpatialQuery/Shape/QueryFilter 窄接口 | 契约读；具体适配交主审/表现 |
| numbers/archetype | RulesAssembly/GameplayAssembly 读档重建接线索引 | 未重新跑换职业/种族 Runtime |
| numbers/faction | FindUnits 关系过滤接线 | 未深入完整矩阵加载 |
| numbers/stat_block | Skill probe 使用真实 StatHost；Reload/旧回归测试索引 | 缓存旧问题不能仅因测试文件存在而判动态通过 |
| numbers/power_set | Skill probe 使用真实 PowerHost；RulesAssembly Reload/重算接线 | 基础probe通过；未重验全部资源类型迁移 |
| numbers/progression | GameplayAssembly、SaveSystem 恢复依赖；文档评级重算合同 | 装配/契约读；未独立回归 |
| rules/common | CastResult、事件/EffectContext 的实际调用与新实例 id 合同 | 重点读；支撑生命周期缺陷 |
| rules/skill | SkillHost、CastPipeline 主要路径、AuraHost/ProcHost/EffectDispatcher 关键段、死亡/时序/实例测试 | 重点读+真实宿主 probe；CORE-118-01 |
| rules/targeting | FindUnits 真实委托、筛选语义与接口 | 重点读 SkillHost 入口；未全读策略实现 |
| rules/combat | Resolver 全结算/命中/减免/死亡/治疗仇恨、ThreatTable 增改裁剪清理、CombatHost 进出战/销毁、ResistCurve；对应关键测试 | 已完成有界实现静态审核；新增偏斜/格挡与暴击文档差异，见补充；未新跑 Runtime |
| rules/ai | AiHost 注册/状态切换、Evaluate、部分 Chase/Patrol；数据 Map transitions 新登记索引 | 关键段读，未完整 AI 时序回归 |
| rules/expr_host | GameplayAssembly 构造与组注册、schema 契约索引 | 装配读；未独立表达式求值矩阵 |
| rules/assembly | Power Reload、等级派生通知、移动通知、职业种族重建位置 | 重点装配读，未把静态接线等同运行通过 |
| carriers/common | IInventoryHost/IUnitAccess 等被探针/装配消费 | 窄合同读 |
| carriers/unit | Persistable/Movement/SkillBinding 路径索引、装配入口 | 未深入完整移动和资源销毁 |
| carriers/item | InventoryHost 添加/移除、容量/事务/实例恢复代码；Equipment 根/存档回归索引 | Inventory 重点读；Equipment 未完成独立存档验证 |
| carriers/creature | GameplayAssembly 工厂/死亡掉落装配 | 装配/索引，未深入 |
| carriers/gobj | 稳定摆放键与 pending loot 存档合同 | 契约/索引，未重新复现物件交互 |
| carriers/summon | 游戏装配 owner/可控召唤引用 | 装配/索引，未深入 |
| carriers/projectile | Spawn/Advance/命中 effects/state、README、测试入口 | 重点静态读；标识传播待动态验证 |
| carriers/assembly | GameplayAssembly 构造 Carriers 的依赖、spatial kind/技能装备接线 | 装配读 |
| gameplay/common | RewardDispatcher 的事务合同与 Quest 调用接口 | 接口/调用读；未完整奖励流程动态验证 |
| gameplay/world_state | WorldState Set/Remove/Get/回调/Schema/Save/Load、WorldExprGroupProvider；五类型与坏形状测试 | 已完成有界实现静态审核；Load 命名空间边界见补充；未新跑 Runtime |
| gameplay/quest | 定义/迁移/接受/进度/交付/跟踪/事件/恢复关键路径，旧回归用例 | 重点读+独立 probe；CORE-118-02 |
| gameplay/dialog | 构造、Reload、菜单视图/选项重验、session 保留 | 关键实现读；删除态仅候选 |
| gameplay/loot | LootHost 抽取/掉落/Reject和Partial拾取/回挂/清理；DroppedLootPersistable；真实背包失败事务与跨图回挂测试 | 已完成有界实现静态审核；正常背包容量失败事务未发现新缺陷，异图拾取边界见补充；未新跑 Runtime |
| gameplay/economy | Reload/库存模式对账、Add/Pay/Buy/Sell、计时/读档设置 | 重点读；none→timer 修复代码存在，未重新跑过往oracle |
| gameplay/achievement | 定义编译、事件Evaluate、progress、pending reward 部分 | 关键段读；未完整持久化验收 |
| gameplay/encounter | 编译定义、阶段/波次字段、Start/participants | 关键段读；未完整结束/失败回滚验收 |
| gameplay/difficulty | GameplayAssembly 构造与 LootMultiplier 接线 | 装配/索引，未深入 |
| gameplay/area_trigger | Schema/GameplayAssembly 创建接线 | 装配/索引，未深入 |
| gameplay/spawn | 持久化/装配和旧失败恢复契约索引 | 未深入，旧问题未动态重验 |
| gameplay/death | 默认 DeathPolicy/RestoreFromSlot 接线索引 | 装配/索引，未独立死亡政策oracle |
| gameplay/assembly | 主要构造、Quest/Dialog Reload、Reward/Economy/Expr/Scheduler/保存依赖接线 | 重点装配读；未宣称全生产Root运行通过 |

## architecture 03–08、10 对照结论

| 章节 | 核查主题 | 分类/当前结论 |
|---|---|---|
| 03 运行时骨架 | Tick固定phase、事件尾派发、离散入口、生命周期清理 | 框架合同；代码存在，施法消费者需兼容尾派发（CORE-118-01） |
| 04 数据管线 | schema/Map/字段校验、Expr类型/工具引用、新元数据 | 框架与工具合同；Map有递归实现；grid_snap只是声明无消费 |
| 05 对象与世界 | Entity/WorldSim 生命周期、移动/投射物、世界状态 | 通用框架合同；具体导航性能/游戏世界数据不是本轮缺陷；投射物标识待验证 |
| 06 属性技能战斗AI | 1.18读条时序、施法实例id、触发链、数值装配 | 框架合同；Update已调整为先既有timer后结算，未独立跑全时序组；死亡终结不完整已证实 |
| 07 载体 | Inventory、Equipment持久化、Creature/Gobj/Projectile边界 | 通用机制属框架，具体装备表/宠物操作口味属游戏；大部分仅静态覆盖 |
| 08 玩法 | 任务/对话、交易、遭遇/成就、owner/day/vendor扩展 | 可选通用框架模块；删除/恢复任务定义异常已证实；具体任务剧情/商人UI属游戏接入 |
| 10 存档 | 分段、依赖顺序、回滚、抑制业务事件、vitals/种族职业派生 | 通用框架合同；历次回滚/派生修复代码可见，本轮没有新增完整Runtime证明 |

## 明确不作为缺陷

- Talent 完整分配器、escort 自动路线、游戏专属新开局重置、具体技能/任务/战利品内容，不因只存在扩展点而自动当作框架遗漏。
- ADR-0013 明示预留的 atb 不计未实现缺陷；grid_snap 不满足这一延期分类。
- 各种 optional 参数、可替换策略和宿主回填义务不等于默认提供某个具体游戏产品。
- 历史审核记录仅用于定位复核；未沿用旧版 PASS/FAIL 或版本哈希为当前运行证据。

## 复现与交付

- 工程：`probe/Probe.csproj`；隔离构建规则：`probe/Directory.Build.props`。
- 施法探针：`probe/Program.cs`；任务探针：`probe/QuestProbe.cs`。
- 原始输出：`probe/probe.log`。
- 命令：`dotnet run --project D:\workespace\ws-game-artifacts\audit-d6fda65-20260911\core\probe\Probe.csproj`。
- runner 的退出码为 0 表示记录完成；两条缺陷以日志 actual/expected 差异证明，不能把退出 0 解释为 PASS。
- 编译使用 .NET 8 的独立统一程序集，出现 LootHost CS8601 与 FakeRewardDispatcher 未赋值 CS0649 警告；它们不妨碍本次 oracle 执行，也没有被转换成产品行为缺陷。
- `probe/bin`/`probe/obj` 是本次隔离构建临时输出，主审制作便携证据时应排除；本报告和 source/raw log 不依赖把这些二进制打包。
- 本轮按主审收敛指令交付；不得将表内“装配/索引”覆盖升级为所有 core 模块均已逐行/全运行验收。


## 补充：Combat / WorldState / Loot 有界只读实现审核

本节由主审在 Unity 测试等待期间追加授权。只读同一冻结源码，未改产品、未扩展其它模块、未运行新增实验或大测试。以下是实现/合同审核，不能记成新的运行通过。

### DOC-118-09 [P3，确定的文档/实现不一致] 偏斜/格挡后仍可叠加暴击

- 声明：`core/rules/combat/README.md:61-65` 与 `core/rules/combat/core/Resolver.cs:28-31` 明确说偏斜/格挡命中后不再参与暴击，isCrit 恒 false。
- 实现：`Resolver.cs:303-316` 将 special 设为 GlancingBlow 或 Block 后，继续执行 `325-330` 的独立暴击分支；条件只有 `context.CanCrit && table.Crit.Enabled`，不排除 special。`337` 返回 special 作为 Hit 标签；`135-141` 又按 isCrit 乘暴击倍率，`254` 事件也携带 true。
- 具体触发：miss/dodge/parry 关闭，glancing 与 crit 都开启且概率为 1；基础值 100、偏斜比例 0.5、暴击倍率 2、无其他乘区/护甲/吸收。按代码可推导得到 Hit=GlancingBlow、isCrit=true、FinalAmount=100；README 承诺的结果是 isCrit=false、FinalAmount=50。Block 同理，例如固定格挡 30 时得到 140 而非声明的 70。
- 此为静态控制流/算术推导，未新增动态输出。主审另行追溯报告该算法自初始 e8b82386 即允许 special+crit，本分支未自行重跑历史版本，不把它描述为 1.18 新引入回归。06 本身未规定互斥，README 的未决问题（168）也承认该项属于实现拍板。因此现阶段按文档准确性 P3 记录，不能凭 WoW 惯例强行认定代码玩法错误；主审应确认最终语义后修代码或更正文档，并加“两个分支同时开启”的组合测试。
- 已读 `ResolverHitTableTests.cs:112-146` 的偏斜/格挡用例、`150-174` 手算全链、`177-249` 免疫/治疗及满血 clamp、`253-291` 死亡。它们分别覆盖分支，没有在同一用例中开启特殊分支与必暴击。

**其余已检查的方法及结论**：

- `Resolver.Resolve`：尸体前置拒绝；命中终止分支；基础值/系数职责；偏斜/格挡、暴击、施法者乘区、护甲抗性、目标乘区、免疫吸收、资源落地、死亡快照、后置事件；上下文 TriggerChainDepth/AttackInstanceId 在该模块透传存在。
- `DetermineHit`、`RollBranch`、`ComputeMitigation`、`ResolveAttackerLevelForMitigation`：概率读取主体、禁用分支、来源销毁后的等级降级均已读。`ResistCurve.ComputeReduction/InterpolateTable` 的饱和公式、最大减免、区间插值已读；未重新运行随机统计或全曲线非法输入矩阵。
- `ThreatTable.AddThreat/SetThreat/GetTopThreat/PruneDead/RemoveSourceEverywhere/EnforceCap`：已读确定性 tie-break 和 cap，检查了相应 `ThreatTableTests` 的同值并列、17→16裁剪、置顶、死亡来源清理断言。未对浮点极端值另开实验。
- `CombatHost.NotifyCombatEvent/Update/HasLivingHostileThreatSource/OnEntityDestroyed`：已读自身与反向仇恨对脱战的约束以及销毁时双向清理。`CombatEnterLeaveTests` 的治疗仇恨写入受疗者是项目明确拍板简化，不按其它游戏的治疗者仇恨规则报错。
- `ResolverHitTableTests.cs:239-249` 明确 FinalAmount 是计算量、非实际生命变化量，所以满血治疗仍报告计算量不能直接报作 bug。

### S-WORLD-01 [静态边界缺口，未动态分级] Load 接受 Set/Remove 禁止的非 world 键

- 位置：`core/gameplay/world_state/core/WorldState.cs:188-197`，尤其 `191`。
- 具体触发：`Load({"item.audit": true})`。item.audit 是合法 Id，`new Id` 成功；FromJson 布尔转换成功，整份快照提交到 `_flags`。随后 `Keys/Get/Has` 可见该键，但 `Remove(new Id("item.audit"), writer)` 走 `80/249-257` 的 world 域检查而抛 ArgumentException。
- 对照合同：`contracts/IWorldState.cs:33-38` 明确 flagKey 使用 world 命名空间；`Set:56` 和 `Remove:80` 共用同一验证。Load 仅验证“合法 Id”，未验证“合法世界标志键”。属于 malformed/迁移存档入口与运行时写接口的约束不一致，不涉及写权限控制。
- 建议在临时 parsed 阶段复用 RequireWorldFlagKey，保留现有“全解析成功后才 clear/commit”原子性。先用正式 DLL 的小探针确认再决定是否升为 P2；本节未伪造运行记录。
- **不报的问题**：EnforceSchema 文档明确仅约束 Set，且默认关闭；Load 未执行可选 schema 不据此单独认定违反合同。Bool/Int 之外三种标量在 README:111 起明确作为实现期补录，因此不是“10 文档只列两种”就判实现错误。

**已读方法与测试**：`Set/Get/Has/Remove/OnChanged/KeysUnder/HandleDispatchedChange/ValidateAgainstSchema/Save/Load/ToJson/FromJson/IsIntegerRawText`；`WorldExprGroupProvider.Query/QueryGetInt/RequireFlagKeyArg`。已核对最具体 schema 前缀、回调异常隔离、Load 不发领域事件、五种类型形状、2^53+1 Int64 原文保留、Number whole 与 Int 区分、`{"$id":...}` 与 String 区分、坏顶层形状保留旧值。读取了 `WorldStateTests.cs:274-435` 关键断言；未声称当前重跑通过。非有限 Number、default(Id) 值、get_int 越界等仍属本次未运行的边界，不扩为结论。

### S-LOOT-01 [静态候选，未动态分级] PickUp 未检验单位与掉落物是否同图

- 位置：`core/gameplay/loot/core/LootHost.cs:391-406`。
- 具体路径：A 图 `Drop` 后，场景 ClearAll，B 图 `ReattachToWorld(mapB)` 不重挂 A 掉落；`_dropped/_order` 保留 A 项。B 玩家恰好与 A 掉落的二维坐标接近，调用 PickUp(playerB, lootAId) 时只查 `_dropped` 与 Vec2.Distance，没有查 entity.MapId 或掉落实体当前是否在 WorldSim，静态可到达背包加物品。
- `LootDropPickupTests.cs:520` 起验证 A→B 回挂“不应把 A 图掉落带到 B”，但只检查 WorldSim 不含实体，没有调用同图/异图的 PickUp。地图保留不是猜测：`ReattachToWorld:656-658` 明确筛图而不删非当前地图记账。
- 该项尚未用正式入口动态验证消费方是否总能持有旧 loot id；先列框架公开调用的边界候选。建议小探针检查后，由主审决定是否作为 P2；不要从此推导当前 UI 已可拾取异图掉落。

**拾取事务与背包失败已完成实现审核，未新增确认缺陷**：

- `PickUpReject:409-461` 对 IBatchableInventoryHost 建事务；以 CountOf 前后差而非 AddItem 布尔值判实际加入量；任何堆不足时不 Commit，Dispose 回滚状态和缓存事件。真实 `InventoryHost` 的协议已与 `CR130_01_PickUpRejectTransactionTests` 对照：A9→10成功、B无空格失败后A恢复9，领域事件为0。该结论来自实现和测试断言阅读，本次没有另跑这条测试。
- `PickUpPartial:464-504` 按实际加入数量构建 taken/remaining，全部放不下返回 InventoryFull，部分成功保留未取数量；`LootDropPickupTests:127-179` 覆盖 Partial 与 Reject。非事务第三方 IInventoryHost 的历史补偿行为已在 README 明示，不能把默认真实背包已修好的问题重新泛化为全路径失败。
- `Drop/PurgeExpired/DestroyDropped/RestoreDropped/ReattachToWorld/ClearDroppedExcept` 与 `DroppedLootPersistable.Save/Load/FromJson` 已读；恢复先解析全部记录再提交、同一 live world 覆盖已有实体、回挂按地图筛选、序列保留等代码存在。未动态验 malformed 快照与世界已有非 Loot id 冲突等组合。
- 抽取也已读 `RollTableInto/RollChanceEachGroup/RollWeightedGroup/PickWeighted/ResolveEntryAtDepth/Merge`，但本次验收重点是拾取事务，未新增抽样分布或嵌套保底语义实验。

### 本补充的验收边界

三模块已从“目录/装配索引”提升到“主要实现与关键测试阅读”，覆盖表对应三行已同步更新；不能把它们写成新一轮测试通过。新增 1 项确定文档差异与 2 项静态边界候选，尚未计入前文两项动态确认 P2 的数量。主审若补正式 DLL 独立验证，应把输出归入主审证据并单独说明。

