# 文档与代码深度审计报告

基线为 `D:/workespace/ws-game` 的 HEAD `5e779c600d7dd3c9ef995d34e844c48641f20a85`，VERSION `1.1.0`，审计日期 2026-09-07。源仓库只读，审计产物写在本目录。审查采用专题调用链和可执行机制探针，覆盖 1051 个 tracked C#、175 个 Markdown、15 个 HTML 的库存盘点与重点语义核对；这不是对所有文件逐行证明无缺陷。

结论是：分层、契约、数据管线和自动化基础具备复用价值，但组合状态、时间单位、事务事件边界、物件接线、表现真效果和发行消费边界仍未完全闭合。本报告不批准整体生产就绪，也不宣称 Unity 已构建或运行。当前发现 13 项：P1 2 项、P2 11 项；文档漂移和未实现/未默认接入能力另列，不借此凑运行时缺陷数量。

## 证据分级和审查边界

- E1：以 HEAD 源码直接编译的独立 console/Stub 机制复现，运行观察到缺陷行为；console 退出码 0 只表示探针运行成功，不能表示被测功能正确。GP26-01、GP26-02、FR-01—FR-05、U03 属此级别；U01、U02 是真实音频实现加 Unity 最小 Stub 的机制证据。
- E2：源码完整调用链或配置静态证据，尚未完成 Unity/完整场景运行复现。GP26-03、U04、U05 属此级别。
- E3：文档、README 或能力索引与实现不一致，或能力明确声明为预留/未默认接入；不等于新的运行时 bug。

## 发现清单

### GP26-01 — P1：失败发奖的已排队 `item.added` 仍会消耗旧任务物品

触发：先向 `InventoryHost(MaxSlots=1, FullPolicy=Reject)` 放入 A5（stack=10）并派发完这次入包事件，再接受一个 `consumeOnProgress=true`、目标 A1 的任务，随后以 `RewardDispatcher.Grant([A1,B1])` 发放。A1 先加入并排队事件，B1 使事务失败；该最小 item schema 只需合法模板、数量和容量策略即可复现。

实际影响：库存回滚到 A5 后，调用栈外的 pending `ItemAdded(A1)` 仍被派发。`QuestHost` 按事件数量从当前库存扣 A1，得到 A4、任务进度 1 并完成目标。失败事务因此产生真实持久副作用；其他 `item.added` 消费者也可能被推进，本次不扩展为已复现结论。

证据：E1，独立复现日志 [successful-run.log](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5e779c6/gameplay-repros/successful-run.log)。触发链为 [RewardDispatcher.cs:101](D:/workespace/ws-game/core/gameplay/common/core/RewardDispatcher.cs:101)–[RewardDispatcher.cs:113](D:/workespace/ws-game/core/gameplay/common/core/RewardDispatcher.cs:113)（失败只按数量补偿）、[InventoryHost.cs:163](D:/workespace/ws-game/core/carriers/item/core/InventoryHost.cs:163)（成功即入队）、[QuestHost.cs:699](D:/workespace/ws-game/core/gameplay/quest/core/QuestHost.cs:699)–[QuestHost.cs:745](D:/workespace/ws-game/core/gameplay/quest/core/QuestHost.cs:745)（按事件扣当前库存）。

建议：把新增实例 delta 和事件队列置于同一提交边界；失败时同时撤销实例和未提交事件，或整批成功后才发布 `item.added`。不要只给 Quest 加特判。

验收标准：上述失败路径随后派发任意轮事件，库存仍为 A5、consume 任务进度为 0；成功事务只发布真实新增量；覆盖 Quest 交付失败和 Loot/Economy 补偿路径。

### GP26-02 — P2：共享套装 Aura 在卸去高门槛装备后误删低门槛效果

触发：同一 `item.set` 的 1 件和 2 件门槛引用同一个 `aura_def`，默认 `AllowMultiSourceTiming=false`、`max_stacks=1`、`Replace`。穿 A 得到 h1，穿 B 以 h2 替换；卸 B 后 A 仍在身上。

实际影响：`AuraQuery.HasAura=false`、`stat.power` 从 101 回到 1；满足 1 件门槛的效果丢失。普通装备 grant 的引用计数修复没有覆盖 `_appliedSetBonuses` 的独立句柄。

证据：E1，复现日志同上，且装载数据通过 `CarriersSchemaCatalog`（仅本地化表 warning）。调用链为 [EquipmentHost.cs:104](D:/workespace/ws-game/core/carriers/item/core/EquipmentHost.cs:104)（套装句柄）、[EquipmentHost.cs:167](D:/workespace/ws-game/core/carriers/item/core/EquipmentHost.cs:167)–[EquipmentHost.cs:192](D:/workespace/ws-game/core/carriers/item/core/EquipmentHost.cs:192)（Replace 未迁移套装持有者）、[EquipmentHost.cs:578](D:/workespace/ws-game/core/carriers/item/core/EquipmentHost.cs:578)–[EquipmentHost.cs:590](D:/workespace/ws-game/core/carriers/item/core/EquipmentHost.cs:590)（阈值独立 Apply/Remove）。

建议：普通 grant 与套装门槛统一 Aura 来源/引用计数，在 Replace 时迁移所有来源；共享 Aura 仍须保留各门槛持有者。

验收标准：穿 A、穿 B、卸 B 后保留 1 件效果；卸 A 后清空；覆盖两种卸载顺序、不同套装共享 Aura 和 `RefreshOnly` 策略。

### GP26-03 — P2：默认 interact 意图链丢弃跨图传送引用

触发：默认 `interact` 意图交互 teleporter，解析到另一地图。

实际影响：`GameObjectHost` 返回成功的 `DispatchedRef`，但默认 tick handler 只检查 `Success`，没有消费引用；玩家地图和位置不变。静态链未完成完整 SceneRouter/Unity 运行复现。

证据：E2。源码锚点：[GameObjectHost.cs:338](D:/workespace/ws-game/core/carriers/gobj/core/GameObjectHost.cs:338)–[GameObjectHost.cs:364](D:/workespace/ws-game/core/carriers/gobj/core/GameObjectHost.cs:364)、[GameObjectHost.cs:94](D:/workespace/ws-game/core/carriers/gobj/core/GameObjectHost.cs:94)–[GameObjectHost.cs:100](D:/workespace/ws-game/core/carriers/gobj/core/GameObjectHost.cs:100)、[InteractIntentTickHandler.cs:59](D:/workespace/ws-game/core/carriers/gobj/core/InteractIntentTickHandler.cs:59)–[InteractIntentTickHandler.cs:65](D:/workespace/ws-game/core/carriers/gobj/core/InteractIntentTickHandler.cs:65)。默认处理器由 `CarriersAssembly` 注册；`GameplayAssembly` 仅注入 resolver，生产 `TeleportUnit` 调用点只覆盖 Dialog/AreaTrigger。

建议：默认处理器输出明确传送请求，或让 L4 订阅并调用统一导航入口。

验收标准：真实 interact→world.Tick→router.Update 后旧图实体清理、玩家进入目标地图和坐标；同图传送继续只改位置。

### FR-01 — P2：混合时间模式只换算通用计时器

触发：continuous 探索切入 discrete 战斗，`seconds_per_turn=5`，已有 10 秒光环/冷却。

实际影响：`TimeModelSwitch` 只换算 `world.Timers`；`CooldownTracker` 的 skill/category cooldown、charges、GCD 以及 `AuraHost` 的寿命/周期累积仍按原单位推进。探针显示切换后通用计时器为 2、技能冷却仍为 10，两轮后冷却为 8、Aura 仍活跃。

证据：E1，[observed-output.txt](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5e779c6/foundation-rules-repro/observed-output.txt)。锚点：[TimeModelSwitch.cs:321](D:/workespace/ws-game/core/gameplay/assembly/TimeModelSwitch.cs:321)、[TimeModelSwitch.cs:370](D:/workespace/ws-game/core/gameplay/assembly/TimeModelSwitch.cs:370)、[TimeModelSwitch.cs:430](D:/workespace/ws-game/core/gameplay/assembly/TimeModelSwitch.cs:430)–[TimeModelSwitch.cs:435](D:/workespace/ws-game/core/gameplay/assembly/TimeModelSwitch.cs:435)、[CooldownTracker.cs:23](D:/workespace/ws-game/core/rules/skill/core/CooldownTracker.cs:23)–[CooldownTracker.cs:26](D:/workespace/ws-game/core/rules/skill/core/CooldownTracker.cs:26)、[SkillHost.cs:313](D:/workespace/ws-game/core/rules/skill/core/SkillHost.cs:313)–[SkillHost.cs:328](D:/workespace/ws-game/core/rules/skill/core/SkillHost.cs:328)。

建议：统一时间状态换算入口，覆盖冷却、充能、GCD、Aura、周期进度、读条、Proc 与学派锁；明确切换后新建状态的单位。

验收标准：双向切换和往返不漂移，既有状态按比例转换，切换后新建状态按目标模式计时。

### FR-02 — P2：充能恢复时间为 0 时耗尽后永久不可用

触发：合法 `max=1,recharge_time=0`，或 SpellMod 将有效恢复时间压到 0。

实际影响：耗尽后 `RechargeRemaining=0` 触发早退，不进入补充循环；UI 冷却为 0 但施法持续返回 `NoCharges`。schema 当前放行该值。

证据：E1，[observed-output.txt](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5e779c6/foundation-rules-repro/observed-output.txt) 的 `ZERO_RECHARGE` 与校验输出。锚点：[CooldownTracker.cs:129](D:/workespace/ws-game/core/rules/skill/core/CooldownTracker.cs:129)–[CooldownTracker.cs:131](D:/workespace/ws-game/core/rules/skill/core/CooldownTracker.cs:131)、[CooldownTracker.cs:217](D:/workespace/ws-game/core/rules/skill/core/CooldownTracker.cs:217)–[CooldownTracker.cs:220](D:/workespace/ws-game/core/rules/skill/core/CooldownTracker.cs:220)、[CooldownTracker.cs:273](D:/workespace/ws-game/core/rules/skill/core/CooldownTracker.cs:273)、[SkillSchemas.cs:40](D:/workespace/ws-game/core/rules/skill/schema/SkillSchemas.cs:40)–[SkillSchemas.cs:41](D:/workespace/ws-game/core/rules/skill/schema/SkillSchemas.cs:41)。

建议：明确 0 为立即恢复还是禁止配置；若允许立即恢复，不能与“不需要恢复”共用早退分支。

验收标准：原值 0、运行期 SpellMod=0、正数恢复三组均得到定义且可施法结果。

### FR-03 — P2：Aura 寿命结束后的剩余周期仍结算

触发：`duration=0.5, interval=1` 的 Aura 执行 `Update(1)`，或固定 `dt=0.1,duration=0.25,interval=0.3`。

实际影响：周期累积先无条件加入完整 dt，寿命后才移除，因此到期后的 DOT/HOT 仍多出一跳。两种步长均由探针得到 1 次调用。

证据：E1，同 [observed-output.txt](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5e779c6/foundation-rules-repro/observed-output.txt)。锚点：[AuraHost.cs:325](D:/workespace/ws-game/core/rules/skill/core/AuraHost.cs:325)–[AuraHost.cs:367](D:/workespace/ws-game/core/rules/skill/core/AuraHost.cs:367)。

建议：每个实例以 `min(dt, Remaining)` 推进周期累积，并明确恰好到期时是否包含最后一跳。

验收标准：上述两个反例不在寿命后结算；寿命内的合法尾跳仍按明确边界触发。

### FR-04 — P2：读档恢复等级不失效评级属性缓存

触发：评级缓存先按 level 1 查询，再 `ProgressionHost.RestoreState(level=2)`。

实际影响：等级和成长已恢复，但只 `ApplyGrowth`，没有等级变化/刷新事件；`StatHost` 继续读旧 rating cache。探针显示 level=2、cached=10，显式重算才为 20。这是 API 机制和默认 Load 接线的静态可达缺陷，不是完整 Shell.Load E2E 结论。

证据：E1，[observed-output.txt](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5e779c6/foundation-rules-repro/observed-output.txt)。锚点：[ProgressionHost.cs:281](D:/workespace/ws-game/core/numbers/progression/core/ProgressionHost.cs:281)–[ProgressionHost.cs:292](D:/workespace/ws-game/core/numbers/progression/core/ProgressionHost.cs:292)、[RulesAssembly.cs:227](D:/workespace/ws-game/core/rules/assembly/RulesAssembly.cs:227)、[StatHost.cs:209](D:/workespace/ws-game/core/numbers/stat_block/core/StatHost.cs:209)–[StatHost.cs:212](D:/workespace/ws-game/core/numbers/stat_block/core/StatHost.cs:212)、[ProgressionPersistable.cs:61](D:/workespace/ws-game/core/numbers/progression/core/ProgressionPersistable.cs:61)。

建议：恢复流程提供独立失效/重建通知，避免伪造普通升级事件导致重复奖励。

验收标准：跨等级读档后评级立即按新等级计算；不重复触发升级奖励；装备恢复和无装备路径均通过。

### FR-05 — P2：内容校验放行损坏技能嵌套字段，首次施法才抛异常

触发：登记 `effects:[{}]` 或 `cost:[42]` 等外层合法、内层错误的技能数据。

实际影响：完整校验返回 0 errors/0 warnings/nonblocking；首次 `GetSkillDef` 分别抛 `KeyNotFoundException` 或 `InvalidCastException`。内容门槛没有阻断运行期不可读取数据。

证据：E1，[observed-output.txt](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5e779c6/foundation-rules-repro/observed-output.txt)。锚点：[SkillSchemas.cs:36](D:/workespace/ws-game/core/rules/skill/schema/SkillSchemas.cs:36)–[SkillSchemas.cs:47](D:/workespace/ws-game/core/rules/skill/schema/SkillSchemas.cs:47)、[DataRegistry.cs:928](D:/workespace/ws-game/core/foundation/data_registry/core/DataRegistry.cs:928)–[DataRegistry.cs:934](D:/workespace/ws-game/core/foundation/data_registry/core/DataRegistry.cs:934)、[SkillValidationRules.cs:136](D:/workespace/ws-game/core/rules/skill/schema/SkillValidationRules.cs:136)–[SkillValidationRules.cs:144](D:/workespace/ws-game/core/rules/skill/schema/SkillValidationRules.cs:144)、[SkillDefCache.cs:194](D:/workespace/ws-game/core/rules/skill/core/SkillDefCache.cs:194)–[SkillDefCache.cs:197](D:/workespace/ws-game/core/rules/skill/core/SkillDefCache.cs:197)。

建议：为技能/Aura 嵌套结构增加精确必填字段、类型和参数校验，并输出行/字段路径诊断。

验收标准：两个反例在 LoadAll 阶段阻断并给出字段路径；合法技能仍能解析和施放。

### U01 — P1：音乐声源反选导致淡入与停止操作对象错误

触发：首次调用 `PlayMusic(track, fadeInSeconds:1, loop:true)` 切换 flag 后，再 Tick 淡入或调用 `StopMusic(0)`；此时实际 incoming 声源为 B。

实际影响：B 正在播放但音量为 0，Tick 更新 A 到 0.5；StopMusic(0) 停 A，B 继续播放。证据来自真实 `UnityAudio.cs` 加最小 Unity/资源 Stub，尚无真实 Unity PlayMode。

证据：E1（非 Unity 机制探针），[unityaudio-probe.log](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5e779c6/unityaudio-probe.log)。锚点：[UnityAudio.cs:131](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityAudio.cs:131)–[UnityAudio.cs:154](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityAudio.cs:154)、[UnityAudio.cs:175](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityAudio.cs:175)–[UnityAudio.cs:177](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityAudio.cs:177)、[UnityAudio.cs:216](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityAudio.cs:216)–[UnityAudio.cs:232](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityAudio.cs:232)。

建议：先确定本次 active source，再将同一 source 传入 fade/update/stop；增加 A→B→A 与 loop/non-loop 测试。

验收标准：新曲目获得正确音量曲线，旧曲目按 fade-out 停止；Stop 始终作用于当前 active source。

### U02 — P2：SFX 自然结束后池位不回收

触发：SFX source 自然结束，未调用 `StopSfx`，随后再次播放。

实际影响：自然结束后 slot 仍 `Active=true`；第二次播放 pool 从 1 增为 2。底层直接 `IAudio` 调用者可能持续增长；默认 `SfxPlayer` 有每层 8 个上限，不能据此宣称默认无界。

证据：E1（非 Unity 机制探针），同 [unityaudio-probe.log](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5e779c6/unityaudio-probe.log)。锚点：[UnityAudio.cs:119](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityAudio.cs:119)–[UnityAudio.cs:128](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityAudio.cs:128)、[UnityAudio.cs:239](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityAudio.cs:239)–[UnityAudio.cs:249](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityAudio.cs:249)。

建议：Tick 检测 `isPlaying=false` 并清理池位；明确 loop、one-shot 和释放资源语义。

验收标准：自然结束与显式 Stop 都可复用原 slot；loop 不被误回收；连续播放受明确容量策略约束。

### U03 — P2：序列帧跨帧更新漏发关键帧命中事件

触发：10 帧、10 fps 的动画一次 `Update(.35)` 从起始帧跳到第 3 帧。

实际影响：`FrameAnimPlayer.Update`/`FireKeyframesAt` 只按落点帧发事件，跳过中间帧，10 帧动画预期 hit=2 时会漏发。独立机制探针观察到 `frame=3, frames0,3, events0`。

证据：E1，非 Unity 机制复现 [frameanim-probe.log](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5e779c6/frameanim-probe.log)。锚点：[FrameAnimPlayer.cs:145](D:/workespace/ws-game/presentation/render/core/FrameAnimPlayer.cs:145)–[FrameAnimPlayer.cs:166](D:/workespace/ws-game/presentation/render/core/FrameAnimPlayer.cs:166)、[FrameAnimPlayer.cs:178](D:/workespace/ws-game/presentation/render/core/FrameAnimPlayer.cs:178)。

建议：按时间区间遍历经过的正向帧并逐个发 keyframe 事件，处理循环跨界、多个循环和大 dt；speed 契约为正数，不引入反向播放要求。

验收标准：任意 dt 均不漏经过的关键帧；每经过一次标记就发一次；覆盖循环跨界、多个循环和大 dt。

### U04 — P2：默认序列帧 root Renderer 未接入 height/fade/flash

触发：默认 `UnityViewFactory` 创建的序列帧动画在 sprite root 上渲染，调用表现层位移或颜色反馈。

实际影响：`UnityRenderer2D.SetTransform` 的 height 偏移只移动 `LayersRoot`，而 `ApplyColor` 只遍历 LayerRenderers；root 动画 Renderer 不受 height、alpha、flash 影响（root 的位置/旋转仍由 SetTransform 处理）。该结论来自完整静态调用链，尚未 Unity 实跑。

证据：E2。锚点：[UnityViewFactory.cs:254](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:254)、[UnityFrameAnimPlayer.cs:54](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityFrameAnimPlayer.cs:54)–[UnityFrameAnimPlayer.cs:61](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityFrameAnimPlayer.cs:61)、[UnityFrameAnimPlayer.cs:143](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityFrameAnimPlayer.cs:143)、[UnityRenderer2D.cs:172](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer2D.cs:172)、[UnityRenderer2D.cs:287](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer2D.cs:287)–[UnityRenderer2D.cs:294](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer2D.cs:294)。

建议：统一 root 与 layers 的表现实例管理，使默认序列帧 Renderer 参加同一变换和反馈遍历。

验收标准：root 动画与纸娃娃层同时响应 height、alpha、flash；影子仍按约定独立处理。

### U05 — P2：Release workflow 对已有 zip 的 fallback 会跳过三个独立 tgz 附件

触发：`release.yml` 看到 zip 已存在时，直接 skip 附件上传；`build.ps1` 发布路径仅上传 zip 与 lock。

实际影响：本地 `-Publish` 正常路径没有工作流声明的三个 tgz 独立附件；若仅剩 zip 而 lock 缺失也可能被当作完整 fallback。tgz 仍嵌在 zip 内，因此不能写成“包内无法获得”。本条未查询线上 release。

证据：E2 静态配置审阅。锚点：[release.yml:181](D:/workespace/ws-game/.github/workflows/release.yml:181)–[release.yml:188](D:/workespace/ws-game/.github/workflows/release.yml:188)、[build.ps1:1294](D:/workespace/ws-game/build.ps1:1294)。

建议：将 zip、lock、三个 tgz 的存在性和 hash 纳入同一发布完成判定；已有 zip 时仍检查并补齐缺失附件。

验收标准：干净发布流程生成并上传所有声明附件；任何附件缺失时不得报告完整成功；zip 内嵌包和独立包版本/hash一致。

## 文档漂移与能力边界

以下项目是文档或默认接线边界，不能直接等同为 13 项运行时发现。

架构文档需要统一以下口径：

- D01：`architecture/01_分层与依赖.md:31`、`:67`、`:73` 和 `architecture/13_新游戏接入指南.md:123` 的“表现层只读”绝对语句，与 `01:185` 的窄意图命令以及 `09:28`、`:365`、`presentation/ui/core/UiIntents.cs:90`、`:92`、`:124` 的命令入口冲突。应统一为查询只读、命令窄提交、业务裁决下沉。
- D02：`architecture/13_新游戏接入指南.md:128` 将 consumer smoke 说成验证第 1、2、6 项；`games/_template/README.md:45`、`:46` 明确模板只覆盖进图/移动/存读档起步，未含战斗、技能、任务。正文应收窄，不把模板烟测称为完整端到端。
- D03：append-only 修复尾注与旧前文并存，造成同页自相矛盾。典型为 [vfx_sfx/README.md:65](D:/workespace/ws-game/presentation/vfx_sfx/README.md:65)–[vfx_sfx/README.md:67](D:/workespace/ws-game/presentation/vfx_sfx/README.md:67)（旧称 ISfx 无 Update，后文已有 Update）及 [games/_template/README.md:40](D:/workespace/ws-game/games/_template/README.md:40)–[games/_template/README.md:48](D:/workespace/ws-game/games/_template/README.md:48)（Options 启停口径已由尾注纠正）。应将当前规范移到正文，历史修复放 changelog。
- Expr：`core/rules/expr_host/README.md:47`、`:82` 的离散时间描述已过时；`RulesExprHostFactory` 已接 provider，但 `day_cycle` 仍为 0。`architecture/04_数据与内容管线.md:289`、`:290`、`:297` 对未登记 key 报错和 ADR-0015 回退警告的描述需分开普通 domain 与 event 例外。
- Archetype：`core/numbers/archetype/schema/README.md:18`、`:48` 仍称 L2 skill 未实现；当前跨表注册和 SkillHost 已存在，应按引用/校验策略改写。
- 时间：`architecture/03_运行时骨架.md:173` 将计时器概括为共用原语，需与 FR-01 的通用 SimTimers 和 skill/Aura 专用状态区分。
- 事务：`core/gameplay/common/README.md:73` 起、`core/gameplay/quest/README.md` 的“失败不改变任何状态”应补事件提交边界，避免把 actualCount 数量回滚修复扩大成全事务证明。
- 装配漂移：`core/gameplay/assembly/README.md:12` 构造例缺必填 `saveSystem`；`:69` 的 TeleportUnit 与当前实现/N14说明冲突；`:97`–`:98` 仍称 RNG 由调用方额外注册；处理器计数表也少列一项。应按当前构造与注册代码重写。

未实现或未默认接入能力索引：

| 能力 | 当前锚点与边界 |
|---|---|
| Unity 3D 渲染 | [UnityRenderer3D.cs:21](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer3D.cs:21) 为 NotSupported；2D 路线不覆盖 model。 |
| 编辑器工具 | [editor/README.md:5](D:/workespace/ws-game/editor/README.md:5) 仍未开始。 |
| 天赋点激活/撤销/存档 | [archetype/README.md:67](D:/workespace/ws-game/core/numbers/archetype/README.md:67)–[archetype/README.md:90](D:/workespace/ws-game/core/numbers/archetype/README.md:90) 只有查询树。 |
| FindUnits | [SkillHost.cs:138](D:/workespace/ws-game/core/rules/skill/core/SkillHost.cs:138)–[SkillHost.cs:144](D:/workespace/ws-game/core/rules/skill/core/SkillHost.cs:144) 当前实现无条件返回空；即使构造 SkillHost 时传入 spatialQuery，本方法也未消费它。 |
| TargetPoint/地面点选 | [SkillTickHandler.cs:16](D:/workespace/ws-game/core/rules/skill/core/SkillTickHandler.cs:16) 留给上层；默认 CastPipeline 只消费单位列表。 |
| 离散召唤/掉落过期 | `core/carriers/summon/core/SummonTickHandler.cs:56`、`core/gameplay/loot/core/LootExpiryTickHandler.cs:37` 对非 continuous 跳过。 |
| 位移轨迹/沿途碰撞 | `core/rules/skill/core/EffectDispatcher.cs:291`、`:310`、`:322`、`:337` 直接 SetPosition。 |
| 日任务、owner、自动 Quest、商店入口 | [GameplayAssembly.cs:518](D:/workespace/ws-game/core/gameplay/assembly/GameplayAssembly.cs:518) ownerResolver=null、`:519` dayProvider=null、`:691` vendorOpenRequested=null；Quest `Update` 是显式入口，默认无自动调用。 |
| 采集点冷却 | [GobjOptions.cs:74](D:/workespace/ws-game/core/carriers/gobj/contracts/GobjOptions.cs:74) 默认 SimTime 恒 0；GameplayAssembly/GameBootstrap 未注入时，正 `respawn_after_use` 只采一次。 |
| 新局完整重置 | [SampleNewGameStarter.cs:38](D:/workespace/ws-game/games/_template/Runtime/SampleNewGameStarter.cs:38) 仅处理位置、地图、模板，不清库存、任务、货币和生命。 |
| VFX 锚点跟随 | `presentation/vfx_sfx/core/VfxPlayer.cs:144` 只使用出生坐标。 |
| day_cycle | `core/rules/expr_host/core/RulesExprHostFactory.cs:461`–`:462` 返回 0。 |
| ATB | `TimeModelSchema.cs:22`–`:24` 与 `TurnScheduler.Configure` 明示预留并抛 NotSupported。 |
| 孤儿检查/DisplayCoverage | `architecture/04_数据与内容管线.md:241` 明示无孤儿检查；DisplayMapCoverageRule 需消费者显式登记来源。 |
| FeedbackRuleValidator | presentation assembly README 当前仍记为未接入。 |
| Replay 录制/回放 | [ReplayRecorder.cs:1](D:/workespace/ws-game/core/foundation/save_system/core/ReplayRecorder.cs:1)、[ReplayPlayer.cs:1](D:/workespace/ws-game/core/foundation/save_system/core/ReplayPlayer.cs:1) 机制存在；默认生产装配未创建它们，需游戏侧接入。 |
| 装备外观/武器动画/关键帧默认接线 | [UnityViewFactory.cs:430](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:430)–`:438` 构造 AnimClipResolver 未传 weaponStyles/provider；[AnimClipResolver.cs:19](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/AnimClipResolver.cs:19) 明示技能覆盖不支持；WeaponStyleResolver 无生产调用者；[SpriteCharacterRig.cs:55](D:/workespace/ws-game/presentation/render/core/SpriteCharacterRig.cs:55)、`:125` 事件无生产订阅；默认 parser/RegisterClipFromEffect 不读 keyframes；[UnityViewFactory.cs:198](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Presentation/UnityViewFactory.cs:198) 未传装备视图映射，SpriteViewBase 无映射即返回。 |

## 验证记录

验证对象为上述 HEAD，源树末次 `git status --porcelain=v1` 为空。完整证据在 [validation.md](C:/Users/1/.codex/visualizations/2026/09/07/01a07bfd-c935-7323-9a34-6eae278d83b6/audit-5e779c6/validation.md)。

- `dotnet build Core.sln -c Release`：PASS，0 warning/0 error。
- `dotnet test Core.sln -c Release --no-build`：6/6 测试程序集通过，共 2238/2238（Foundation 642、Numbers 106、Carriers 288、Rules 348、Presentation.Common 410、Gameplay 444；含 Perf 类别）。
- `check.ps1 -SkipUnity`：14 PASS、6 SKIP；Unity 编译、EditMode、PlayMode、独立版/冒烟和消费方演练均 SKIP。
- 数据/工具：合并根 59 tables/276 records/0 errors/0 warnings/1 override；框架根 5 tables/122 records/0 errors/1 warning（缺 l10n 时跳过文本键检查）；事件常量 88 一致；placeholder 92/92；sample asset check 0 问题；pytest 47 passed；版本、禁用词、包清单均 PASS。
- 真实 `dist/1.1.0` 的 MANIFEST/lock commit 与 HEAD 一致，六个 DLL 实测 hash 与 manifest/lock 一致；zip 内框架 validator 退出 0，5 tables/122 records/0 errors/1 warning，Validator 首次编译有 3 个 CS8632 warning。隔离 git archive 不含 `.git`，其内部打包打印 unknown commit，不能把隔离 manifest 当发行提交证据。
- 真实 dist 的核心 MANIFEST/lock 只对六个 DLL 提供 hash 覆盖；本次另对适配层文件集合和 JSON 内容做了核对，但这不等同于整个 ZIP、工具脚本和数据的全量完整性证明。后续发布应将整包 hash 与各验证结果绑定到同一 commit/hash 记录。
- 独立 UPM toolchain 的框架数据入口已验证通过（5 tables/122 records/0 errors/1 warning，非 Unity）；这证明该入口可消费框架数据，不证明完整 UPM 工程或 Unity 项目运行。
- 独立机制探针：UnityAudio、Foundation/Rules、Gameplay、FrameAnim 复现均 exit 0；该退出码只表示探针成功运行并观察到预期缺陷输出，不表示功能正确。探针不替代 Unity Runtime、完整 Shell E2E、真实音频设备或目标设备性能测量。

## 修复优先次序与项目质量

1. 先修复事务提交/事件边界和读档恢复重建（GP26-01、FR-04），建立“失败无副作用、恢复后状态可查询”的基础。
2. 修复规则层时间单位、充能零值、Aura 周期尾跳和嵌套 schema 门槛（FR-01—FR-03、FR-05）。
3. 修复默认物件跨图传送和采集时钟接线，统一套装 Aura 来源计数（GP26-03、GP26-02 及能力边界）。
4. 修复音频和序列帧表现生命周期/事件，随后用 Unity PlayMode 验证 root renderer 的变换和反馈（U01—U04）。
5. 收紧 Release 附件完整性判定并验证独立包消费（U05）。
6. 最后统一架构正文、README、变更记录和能力矩阵，再用锁定包 hash 做一次新局→移动攻击→奖励/装备/任务→死亡→存读档→退出重入的真实纵切。

项目质量判断：核心分层、数据 schema、策略注入和自动化测试基础可复用；组合状态和时间语义仍有 P1/P2 缺陷，表现层部分能力尚未真实接线，默认模板是最小骨架，发行物虽可做静态 hash 核验却未完成 Unity/消费方完整验收。因此当前适合继续修复和定向接入，不适合宣称任意新游戏或生产环境已通过。现有 [UnityAudioTests.cs:67](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/UnityAudioTests.cs:67)–[UnityAudioTests.cs:77](D:/workespace/ws-game/adapters/unity/Packages/com.gamefoundation.adapter.unity/Tests/Runtime/UnityAudioTests.cs:77) 主要是 DoesNotThrow，不能证明音量、停止和池回收；[FrameAnimPlayerTests.cs:98](D:/workespace/ws-game/presentation/render/tests/FrameAnimPlayerTests.cs:98) 命中单帧，不能证明跨帧事件；奖励回滚测试必须在 `DispatchPending` 后检查终态，不能只断言 `Grant` 返回值。
