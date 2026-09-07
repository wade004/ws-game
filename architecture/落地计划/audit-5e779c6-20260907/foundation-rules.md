# Foundation / Numbers / Rules 审核笔记（供主审汇编）

基线：`D:/workespace/ws-game`，HEAD `5e779c600d7dd3c9ef995d34e844c48641f20a85`，1.1.0。源仓库只读；末次 `git status --short` 无输出。审核者亲自审阅核心实现、近期修复及前三轮报告，以下不重报已经闭合的历史项。

范围：foundation 的数据加载/字段校验、事件、模拟/计时器/模式切换、场景与存档；numbers 的属性/成长/职业；rules 的施法/充能/光环/目标/AI/表达式与装配。架构重点 03、04、05、06、10 和对应 README/schema。此为重点语义审核，不能解读成全部 C# 逐行形式证明。

复现工程：`foundation-rules-repro/Repro.csproj`，直接编译当前仓库源码（绝对 Compile Include，排除源仓库 tests/bin/obj，仅显式复用数个测试夹具）。所有构建/临时输出在本目录。运行：`dotnet run --project <本目录>/foundation-rules-repro/Repro.csproj`。SDK 8.0.424 / net8.0；退出码 0 表示探针运行完成，**不表示被探测的行为正确**。完整结果见 `foundation-rules-repro/observed-output.txt`。用了真实 SkillHost/CooldownTracker/AuraHost/StatHost/ProgressionHost/TimeModelSwitch/TurnScheduler 等，单位查询和战斗出口部分为明确的测试替身；不是 Unity /完整 Shell E2E。

## FR-01（P2）混合时间模式只换算通用计时器，真实技能计时器未换算

触发：探索 continuous，战斗 discrete，`seconds_per_turn=5`；进入战斗前已有 10 秒光环/冷却。03:94 明确要求现存光环等剩余时间在模式切换时按系数换算，03:173 又说冷却/光环统一使用通用计时器。

证据：`core/gameplay/assembly/TimeModelSwitch.cs:321,370,430-435` 的全部换算只操作 `world.Timers as SimTimers`。真实 `CooldownTracker` 的 `_skillCooldowns/_categoryCooldowns/_charges/_gcdRemaining`（同文件:23-26）和 `AuraHost` 的 `Remaining/PeriodicAccumulators` 并不使用该宿主。`core/rules/skill/core/SkillHost.cs:313-328` 在回合中按 `dt=1` 分别推进这些私有计时器。rules/numbers 无相应换算入口或调用。

真实机制复现输出：`HYBRID switched=Discrete,simtimer=2,skillcooldown=10`；推进两轮后 `skillcooldown=8,auraActive=True`，应在正确换算后于两轮后归零/过期。复现包含真实 TimeModelSwitch 事件切换和 SkillHost，通用 timer 的正确 10→2 是对照。反向退出也只有通用 timer 被换算，因此不是只缺一侧。

影响：混合模式下已有技能状态被错误地重新解释单位，效果持续与冷却按系数偏移；不能将现有 SimTimers 换算单测当成真实技能换算证明。纯 continuous、不切换或系数 1 不受此项影响。建议统一时间状态换算入口，覆盖冷却/充能/GCD/光环/周期进度/读条/Proc/学派锁，明确切换后新建状态的单位。验收双向切换及往返不漂移。

## FR-02（P2）有效充能恢复时间为 0 时，技能耗尽后永久不可用

证据：`core/rules/skill/core/CooldownTracker.cs:129-131` 将恢复剩余设为有效恢复时间；`:217-220` 遇 `RechargeRemaining <= 0` 直接返回，永不到下面的补充充能循环。`:273` 又显式允许 SpellMod 把恢复时间压至 0。`SkillSchemas.cs:40-41` 未限定恢复时间必须正数；通过真实 `RulesSchemaCatalog.RegisterAll` 完整加载得到 zero_recharge `errors=0,warnings=0,blocking=False`，SkillDefCache 解析正常，因此不能归为被 schema 拒绝的非法输入。

复现：max=1/recharge_time=0，施放一次后推进 100 秒：`after100s=0,ready=False,cooldown=0`。同样可由原本正数时长经合法 charges SpellMod 降为 0 触发。UI 查询显示冷却 0，却仍以 NoCharges 拒绝施放。

建议：明确 0 是立即恢复还是禁止配置；若允许立即恢复，不能把 0 与“不需要恢复”共用早退条件，同时处理 SpellMod 的运行期 0。验收原值 0、SpellMod 得到 0、正常正值三种情况。

## FR-03（P2）周期光环在寿命结束后的剩余 tick 时间仍结算

证据：`core/rules/skill/core/AuraHost.cs:347-352` 无条件将完整 dt 加入周期累积并触发效果，`:357-367` 才扣剩余寿命并移除。没有把本步有效时间裁到 Remaining。

复现一：duration=0.5，interval=1，Update(1) 仍产生 1 次伤害结算（预期 0）。复现二排除“大步长才触发”：固定 dt=0.1，duration=0.25，interval=0.3，三个固定步后输出 `EXPIRED_PERIODIC_FIXED ... calls=1`；周期触发点 0.3 已晚于光环寿命 0.25，仍多造成一次伤害。伤害出口为记录调用的 FakeCombatHost；证明结算请求实际产生，不声称 Unity HP E2E。

影响：非整步 duration/interval 的 DOT/HOT 可多打/多回一跳。建议每实例用 min(dt, Remaining) 推进周期累积，并明确边界时刻恰好相等是否包含最后一跳；不要简单先删光环导致合法尾跳遗漏。

## FR-04（P2）读档恢复等级没有失效评级属性缓存

证据：`core/numbers/progression/core/ProgressionHost.cs:281-292` 的 RestoreState 替换 level/xp 后只 ApplyGrowth，不发等级变化或刷新事件；`core/rules/assembly/RulesAssembly.cs:227` 仅在 LevelUpEvent 调用 Stats.RecomputeRatingStats；`core/numbers/stat_block/core/StatHost.cs:209-212` 优先读旧缓存。`ProgressionPersistable.cs:61` 的 Load 调用该 RestoreState，默认 `GameplayAssembly.cs:1109` 已注册该 Persistable。检索 core/carriers/core/gameplay 未见 SaveLoaded 再重算评级的兜底。

复现按默认装配的相同 host/LevelUp 接线：level1 的 rating 原始值 100，level1 points_per_percent=10，level2=5，growth 不涉及此 rating。先查询缓存 10，再 RestoreState(level2) 并派发事件，GetLevel=2 但 rating 仍=10；显式 RecomputeRatingStats 后=20。这里是机制/API + 默认 Load 可达静态证明；未执行完整 Shell.Load，不可将此探针称作完整读档 E2E。

影响：同宿主跨等级读档/回档后的命中/暴击等评级可沿用旧等级换算，直到碰巧写该属性才恢复；若有同属性装备重建可偶然掩盖。此前修复 N08 只解决 AddXp 的“发事件前先提交等级”，未覆盖 RestoreState。建议恢复流程有独立重建/失效通知，避免伪造普通升级事件造成重复奖励。

## FR-05（P2）完整内容校验放行损坏的技能嵌套字段，首次施法才抛异常

证据：`core/rules/skill/schema/SkillSchemas.cs:36-47` cost/effects 仅 Array、charges 仅 Object；`core/foundation/data_registry/core/DataRegistry.cs:928-934`只检查外层 JSON 种类；`core/rules/skill/schema/SkillValidationRules.cs:136-144` 只有元素是对象且已有字符串 kind 才判断名称是否合法，缺 kind 或元素非对象被跳过。`SkillDefCache.cs:194-197,245-247` 随后直接强转/索引，惰性解析发生在首次使用。

使用真实 RulesSchemaCatalog.RegisterAll、合法 self 目标链，分别仅注入两种坏数据：
- `effects:[{}]`：校验 0 错误 /0 警告/nonblocking，GetSkillDef 抛 KeyNotFoundException（缺 kind）。
- `cost:[42]`：校验同样通过，GetSkillDef 抛 InvalidCastException（JsonNumber→JsonObject）。

均已记录在 observed-output.txt，不是只基于“看起来少了校验”的推断。04:225 定义校验为内容提交门槛，`:218` 称 LoadAll 跑完校验后方可进入游戏；当前门槛不足以保证这些已登记结构可被运行期读取。建议对技能/光环的嵌套结构实现精确必填字段/类型/原语参数校验，并给运行时解析合理诊断。验收上述反例阻断并给出 rows/field 路径；不能仅要求调用方捕获异常。

## 文档漂移与明确能力边界（不要混为新代码 bug）

| 条目 | 当前证据 | 应处理方式 |
|---|---|---|
| ExprHost 离散时间 README 过时 | `core/rules/expr_host/README.md:47,82-84` 仍写 turn_index/round_index/is_my_turn 恒 0/false、项目不启用离散；`RulesExprHostFactory.cs:464-472` 已读注入 provider，装配已传入 | 更新为已实现、缺 provider 的降级；day_cycle 仍是 0，应分开 |
| Archetype schema “L2 skill 未实现”过时 | `core/numbers/archetype/schema/README.md:18,48`；现有 SkillHost 及 RulesSchemaCatalog 跨表注册已经存在 | 按实际层间契约/引用校验策略重写理由，不继续用历史未实现解释 |
| 04 Expr 描述内部冲突 | `architecture/04_数据与内容管线.md:289,297` 写未登记 key 报错；`:290` 按 ADR-0015 回退 Id 并警告，当前 parser 使用后者，event 为例外 | 合并为统一消歧说明并分别描述 event 与普通 domain |
| 03 把全部计时器写成共用通用原语 | `architecture/03_运行时骨架.md:173` 与私有 skill 计时器实现不符，并产生 FR-01 | 与 FR-01 修复同步，区别通用计时器和专用状态并说明统一换算协议 |
| FindUnits /地面点选 | `SkillHost.cs:139-147` 恒空；`skill/README.md:75-81` 已明确未完成；`SkillTickHandler.cs:16` 把 TargetPoint 留给上层，CastPipeline 只吃单位列表 | 明示缺口；06:420 的契约行与能力总表应直接链接该边界，不能报已实现范围查询/地面施法 |
| day_cycle | `RulesExprHostFactory.cs:461-462` 返回 0 | 未实现能力；不能因为 schema 可解析就视为昼夜系统已交付 |
| ATB | `TimeModelSchema.cs:22-24`、`TurnScheduler.Configure` 明示预留并抛 NotSupported | 预留，非当前 promised feature bug |
| 孤儿检测 /外形覆盖 | `architecture/04_数据与内容管线.md:241` 明示孤儿检查无实现；DisplayMapCoverageRule 需消费者显式登记来源 | 已披露边界；别把没默认跑视为无条件缺陷 |
| Player questLog | `architecture/05_对象模型与世界.md:75` 明示为未读写占位，权威状态在 QuestHost | 占位与实际任务状态分开说明 |
| RNG 旧格式无 master_seed | RngStreamsPersistable:23-27 明示旧格式仍采用不 Reset 兼容路径；新格式已保存 seed/清残留 | 旧档兼容边界，不重报新档 P1-04 |
| source comments 的历史修复理由叠加 | DataRegistry.LoadAll(IReadOnlyList) 注释仍称根顺序不影响结果，与已支持 override 不符；TurnScheduler.SectionKeyConst 注释仍称 sim.turn_state 不在 KnownOrder，实际已加入 | 删除/标注失效判断，不将过期注释当当前行为依据 |

已复核而不重报：备份槽隔离/旧备份身份核验、meta 同版本候选检查、RNG 新格式 reset、施法者死亡/销毁取消读条、周期来源注销防护、absorb depletion 深度传播、技能存档替换、事件等级顺序、已注册默认 progression 存档、显式目标过滤与时间 provider 回填。未把历史报告清单直接搬成本轮 findings。

