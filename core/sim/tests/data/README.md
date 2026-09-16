# 嵌入式最小仿真数据集（T-N6-2b）

职责：ADR-0035 决策 2～6（`architecture/adr/0035-数值仿真骨架为框架交付物.md`）与
`architecture/落地计划/数值设计分阶段落地计划.md` §12 ⑤ 线"阶段 N6 任务级拆分"T-N6-2 的落地——一套
**自洽、可被 `Core.Sim.HeadlessWorldBuilder` 装配、可跑通一场战斗**的最小内容数据集，供后续
T-N6-3～T-N6-6（标准玩家生成器、战斗/成长/覆盖三级仿真）复用。

**拍板 10**：本数据集只嵌入 `core/sim/tests/data/`，**不放进 `data/_sample`**——`data/_sample` 已有
`sim/sim.anchor.json`/`sim/sim.scenario.json` 两张表的 schema 覆盖样例（T-N6-2a），职责是"演示这两张
表的字段形状"，与本数据集"演示一整套能跑通仿真的内容"用途不同，混在一起会让 `data/_sample` 从"框架
薄样例"膨胀成"一份微型游戏内容"，违反 `data/_sample` 现有定位。

与 `data/_framework` 合并加载（惯例同 `Tests.Sim.SimTestWorldFactory.BuildWorld` 的
`data/_framework` + `data/_sample` 两根），装载入口：
`Core.Sim.HeadlessWorldBuilder.Build`，测试工厂 `SimTestWorldFactory.BuildFromEmbeddedDataset`。

内容与 `data/_sample`（`arch.class.sample_a`/`creature.sample_beast` 等）**完全独立**——两套数据集的
同名概念表各自登记自己的一套 id，互不引用、互不合并到同一次装配里；本数据集全部内容 id 一律
`sim_*` 前缀（`arch.class.sim_warrior`、`creature.sim_wolf_l5`……），不出现任何游戏代号。

## 数据集清单（45 个表文件，334 行——T-N6-5 追加见下表 `skill.def`/`item.template`/
`display.map`/`l10n.text`/`loot.table` 行，`stat.definition`/`sim.anchor` 两行的行数是
T-N6-4/T-N6-4b 已落地但本表此前未同步更新的既有 drift，本次一并更正）

| 域 | 表 | 行数 | 说明 |
|---|---|---:|---|
| stat | `stat.definition` | 10 | 四主属性 + 攻击强度（派生）+ 暴击/闪避/命中等级 + 护甲 + `stat.move_speed`（T-N6-4 补，见该 README 判断记录 25） |
| stat | `stat.rating_conversion` | 2 | 暴击/闪避评级换算曲线 |
| stat | `stat.weight` | 9 | 逐属性预算权重（均 1.0，简化装备预算手算） |
| arch | `arch.power_type` | 2 | `arch.power.health`（**覆盖**框架默认为 `kind:stat→stat.stamina`）+ `arch.power.sim_fury`（积累型资源） |
| arch | `arch.class` | 1 | `arch.class.sim_warrior`（唯一职业，近战） |
| arch | `arch.race` | 1 | `arch.race.sim_default`（属性修正留空） |
| arch | `arch.talent_tree` | 1 | 最小两节点天赋树 |
| skill | `skill.base_curve` | 3 | Execute 基础值曲线（常数 20）+ 两条生物基础攻击曲线 |
| skill | `skill.def` | 8 | 5 个玩家技能 + 2 个生物攻击技能 + 1 条 T-N6-5 覆盖仿真探针（`skill.def.sim_probe_overbudget`，不进任何 skill.book/ai.rotation） |
| skill | `skill.aura_def` | 2 | 坚韧被动光环 + 战吼增益光环 |
| skill | `skill.book` | 1 | `skill.book.sim_warrior`，1/4/8/12/16 级逐级解锁 |
| skill | `skill.budget_rule` | 1 | `skill.budget_rule.sim_default`（预算比对未接入锚点，天然零告警） |
| ai | `ai.rotation` | 3 | 玩家优先级表 + 两条生物攻击循环 |
| ai | `ai.behavior_profile` | 2 | 普通/精英生物行为档案 |
| combat | `combat.hit_table_config` | 1 | `combat.hit_table.default`（**id 必须等于** `CombatOptions.HitTableConfigId` 默认值，见判断记录 1） |
| combat | `combat.level_diff_table` | 1 | 越级命中/暴击/经验系数表 |
| combat | `combat.resist_curve` | 1 | 物理抗性饱和曲线 |
| target | `target.chain_def` | 2 | 最近敌人 / 自身 两条目标链 |
| item | `item.slot_definition` | 5 | 主手（武器）+ 头/胸/腿/脚（护甲） |
| item | `item.quality_definition` | 2 | `sim_common`/`sim_rare` |
| item | `item.budget_curve` | 1 | `item.budget.default`（**id 同上，见判断记录 1**），断点 1/5/10/15/20，`exponent:1.5` |
| item | `item.armor_curve` | 1 | `item.armor.default` |
| item | `item.weapon_dps_curve` | 1 | `item.weapon_dps.default`（**id 同上**） |
| item | `item.req_level_curve` | 1 | 断点 1/5/10/15/20，等级=物品等级 |
| item | `item.affix` | 5 | 带预算份额的词缀包（2 个 common 池 + 3 个 rare 池） |
| item | `item.template` | 51 | 5 档物品等级 × 2 品质 × 5 槽位（1 武器 + 4 护甲）+ 1 条 T-N6-5 覆盖仿真探针（`item.template.sim_probe_underbudget`，不进任何 loot.table） |
| creature | `creature.tier_definition` | 2 | `sim_normal`（×1.0）/ `sim_elite`（×1.5） |
| creature | `creature.template` | 6 | 5 个普通怪档位（L1/5/10/15/20）+ 1 个精英（L10） |
| loot | `loot.table` | 6 | 每档生物一张（货币 + 普通/稀有装备条目；T-N6-5 起 5 档普通怪各追加 chest/legs/feet 三条 common 条目，货币条目 `count_range` 改为常量 1，见"T-N6-5 调参记录"） |
| econ | `econ.currency` | 1 | `econ.currency.sim_gold` |
| econ | `econ.gold_base_curve` | 1 | 断点 1/5/10/15/20 |
| econ | `econ.value_curve` | 1 | 出售价值曲线 |
| econ | `econ.vendor` | 1 | 最小商人（两件商品） |
| diff | `diff.tier` | 2 | `sim_normal`/`sim_veteran` 两档难度 |
| world | `world.map` | 1 | `world.sim_arena` |
| fac | `fac.faction` | 2 | `fac.player`（复用框架惯例 id）/ `fac.sim_hostile` |
| fac | `fac.reaction_matrix` | 1 | 玩家 vs 敌对生物 = hostile |
| prog | `prog.level_curve` | 1 | `prog.level_curve.sim_warrior`，1～20 级连续，`max_level:20` |
| prog | `prog.xp_base_curve` | 1 | 断点 1/5/10/15/20 |
| prog | `prog.xp_source` | 2 | kill / quest 两个来源 |
| sim | `sim.anchor` | 25 | 1～25 级连续（T-N6-4b 扩表，见下"锚点推导"） |
| sim | `sim.scenario` | 3 | arena / growth / coverage 各一条 |
| display | `display.map` | 67 | `skill.def`/`skill.aura_def`/`item.template`/`creature.template` 全部逻辑 id 的最小外形映射（`DisplayMapCoverageRule` 阻断要求，T-N6-5 起含两条覆盖仿真探针） |
| l10n | `l10n.locale` | 1 | `l10n.locale.zh_cn`（**id 必须等于** `DataRegistryOptions.DefaultLocale` 默认值，见判断记录 1） |
| l10n | `l10n.text` | 91 | 全部 `name_key`/`TextKey` 字段的中文文本（`text_key_exists` 阻断要求） |

校验命令与结果：

```
python toolchain/validate_data.py --strict --framework-root data/_framework --data-root core/sim/tests/data
# tables 49, records 459, errors 0, warnings 5, overrides 1（覆盖=arch.power.health）
# 5 条警告均为 skill_budget_deviation/item_budget_utilization_low 已确认/预期内探针项，见本文件
# "T-N6-5 调参记录"与 core/sim/README.md 判断记录 35
```

## 锚点推导（`sim.anchor`，数值总纲第 4.1/4.2/4.7 节）

**自上而下**（1～20 级各字段的推导公式，`L` 为角色等级）：

- `ttk_seconds(L) = 8 + 4·(L-1)/19`（8 → 12 秒线性）
- `ttd_seconds(L) = 30 + 10·(L-1)/19`（30 → 40 秒线性）
- `level_duration_seconds(L) = 300 + 20·(L-1)`（5 分钟 → 11.3 分钟）
- `kill_interval_seconds(L) = 15 + 0.5·(L-1)`（15 → 24.5 秒，含赶路/拾取）
- `quest_share(L) = 0.3 + 0.2·(L-1)/19`（0.3 → 0.5）
- `expected_item_level(L)`：不高于 `L` 的最大档位（1/5/10/15/20 阶梯），与 `item.template`/
  `creature.template` 的分档一一对应
- `dps(L)`、`hp(L)`、`ttd_seconds(L)`：T-N6-2b 阶段曾用下方"自下而上手算"（简化循环，忽略
  `rampage`/`battle_shout`）直接定义；**T-N6-4 起改为仿真实测值**（真跑 `FightRunner`/
  `ArenaSimulation` 后按数值总纲第 5 节"锚点表与对账等式"的方法论重新标定，手算已不再是权威来源），
  见本文件"T-N6-4 调参记录"一节——手算公式仍保留在下方作历史参照，**不代表当前 `sim.anchor.json`
  的实际取值**

标准战士 `arch.class.sim_warrior` 的等级函数（`prog.level_curve.sim_warrior` 的
`base_stats`/`growth` 换算）：

- `strength(L) = 12 + 3·(L-1)`
- `stamina(L) = 150 + 50·(L-1)`
- `attack_power(L) = strength(L)`（`derivation_overrides` 系数 1.0）

技能（`skill.sim_warrior_strike` 主要单体攻击 / `skill.sim_warrior_execute` 12 秒冷却高伤终结技）的
效果值：

- `strike_dmg(L) = 5 + 0.5 · attack_power(L)`
- `execute_dmg(L) = 20 + 1.5 · attack_power(L)`（`base_curve_ref` 常数 20 + `scaling` 系数 1.5）

**（历史参照，T-N6-2b 阶段的手算，已被 T-N6-4 仿真实测值取代）自下而上标准玩家秒伤**（假设
GCD=1.5 秒的简化循环：终结技 12 秒冷却一次、其余 7 个 GCD 用主要攻击填充——
`skill.sim_warrior_rampage`/`skill.sim_warrior_battle_shout` 未计入本手算，作保守简化）：

```
dps_bottom_up(L) = (execute_dmg(L) + 7 · strike_dmg(L)) / 12
```

T-N6-2b 阶段 `sim.anchor.dps`/`sim.anchor.hp` 曾直接取 `dps(L)=dps_bottom_up(L)`、
`hp(L)=stamina(L)`，让"自上而下"锚点与"自下而上"估算按构造方式恒等（偏离 0%）——T-N6-4 真跑
`FightRunner`（真实命中/暴击/闪避表、真实 `ai.rotation` 优先级表，`rampage` 会被实际使用）后
发现真实秒伤比这份简化手算高约 1.5～2.3 倍（`rampage` 贡献被完全忽略），因此不再沿用这份手算，
详见下方"T-N6-4 调参记录"。

**生物反推公式**（数值总纲 4.2 节，`tier_mult`：普通 1.0、精英 1.5，攻击间隔固定 2 秒）：

```
creature_hp(L, tier)      = round(dps(L) · ttk(L) · tier_mult, 1)
creature_dps(L, tier)     = hp(L) / ttd_design(L) · tier_mult   （见判断记录，ttd_design ≠ sim.anchor.ttd_seconds）
creature_dmg_per_hit(L,t) = round(creature_dps(L, tier) · 2.0, 1)
```

当前（T-N6-4 调参后）5 档普通怪 + 1 档精英的实际登记值，见下方"T-N6-4 调参记录"表。

**升级所需经验反推**（数值总纲 4.7 节："升级所需(L) = 击杀基数(L) × 每级怪当量(L) × (1+Q(L))"，
`每级怪当量(L) = level_duration_seconds(L) ÷ (ttk_seconds(L) + kill_interval_seconds(L))`，
`击杀基数(L) = kill_xp_base(L) = 20 + 2·(L-1)`，即 `prog.xp_base_curve.sim_default` 的断点公式）：

`prog.level_curve.sim_warrior.entries[].xp_to_next`（1～19 级，20 级为满级 0）：

```
L1=339  L2=389  L3=441  L4=496  L5=553  L6=611  L7=672  L8=735  L9=799  L10=865
L11=934 L12=1003 L13=1075 L14=1148 L15=1223 L16=1299 L17=1377 L18=1457 L19=1538 L20=0
```

## 装备预算手算口径（数值总纲第 4.4 节，`ItemBudgetCurve.ComputeConsumed`）

`item.template.stats` 单条属性词条的预算消耗 = `属性值 × stat.weight`（本数据集全部权重为 1.0，
`exponent` 对单一词条不改变结果）。预算上限 `B(item_level, quality, slot) = item.budget_curve(item_level)
× quality.budget_multiplier × slot.budget_coefficient`。40 件护甲模板统一取
`stat_value = round(B × 0.72)`（利用率 ≈72%，高于 `ItemBudgetValidationRule` 警告阈值 70%，低于
上限 100%），词缀白名单按品质挑选（common 1 件、rare 2 件），预算余量（common ≥28%、rare 两条份额
共 0.24）均满足 `ItemTemplateAffixShareExceedsBudgetRule`（`consumed + maxShare×B ≤ B`）。10 件主手
武器模板不携带 `stats`（跳过预算校验，见判断记录 3），`weapon_profile.damage_min/max` 取
`item.weapon_dps_curve(item_level) × quality.budget_multiplier × slot.budget_coefficient × speed`
的 ±10%，理论均值偏离 0%（`ItemWeaponDamageDeviatesDpsCurveRule` 零告警）。

## 场景表（`sim.scenario`，ADR-0035 决策 3）

- `sim.scenario.sim_arena_matrix`：`kind=arena`，`levels=[1,5,10,15,20]`，
  `opponent.level_offsets=[-5,-3,-1,0,1,3,5]`（越级矩阵），`runs=60`（T-N6-4 从 20 调至 60，见
  下方调参记录），`max_ticks=1200`，带宽全部 0.25（T-N6-4 核算后维持，未触达 ≥0.5 的上限）。
- `sim.scenario.sim_growth_full`：`kind=growth`，`level_from=1`，`level_to=20`。
- `sim.scenario.sim_coverage_all`：`kind=coverage`，`levels=[1,5,10,15,20]`。

## T-N6-4 / T-N6-4b 调参记录

T-N6-4（`core/sim/core/FightRunner.cs`/`ArenaSimulation.cs`）第一次真正跑通 `sim_arena_matrix`
后，按数值总纲第 5 节"锚点表与对账等式"的方法论重新核算；设计层复核 T-N6-4 首次提交时发现 L20
行胜率矩阵非单调（断层），要求先查根因再调数据——根因排查见下，定位到两处需要根治的问题（一处
装配根代码缺陷、一处锚点表范围缺口），修完后按同一方法论重新核算了全部数值。本节只记录
**最终生效**的调参结果与关键理由；过程中的中间尝试（如曾用过的 `TTD_MULT=2.0`/`=1.3`、
`STEEPEN=3.0` 等）不再逐一列出，只在下方"调参方法"小节概述搜索过程。

### T-N6-4b 根因排查

设计层复核指出的现象：玩家 20 级一行的越级矩阵胜率不单调——偏移 −3 是 97%，偏移 −1 却只有 8%，
偏移 0 只有 5%，偏移 +1～+5 又回到 7%～15%（断层，不是渐变）。逐格打印单场明细
（`CombatOptions.ResolveTrace`/事件流，见下方"诊断方法"）定位到两个独立成因：

1. **玩家按等级 > 1 直接出生时，等级成长从未写入基础属性（装配根代码缺陷，已修复）**——
   `Core.Rules.Assembly.RulesAssembly.RegisterUnit`（`HeadlessWorldBuilder.Build` 给玩家调用的
   那个重载）只调用 `Progression.RegisterUnit(unitId, curveId, level)`（登记"当前在哪条曲线的
   第几级"这一记账状态），不像 `Core.Carriers.Creature.CreatureFactory.SpawnCore` 那样紧接着调用
   `Progression.ApplyGrowthToCurrentLevel` 把"2 级到出生等级"的曲线成长写成属性修正——这正是
   `Core.Numbers.Progression.IProgressionHost.RegisterUnit` 契约注释原文要求调用方自己做的第二步
   （"`startLevel > 1` 时，调用方如果需要……紧随其后显式调用 `ApplyGrowthToCurrentLevel`"）。
   诊断复现：L20 标准玩家在此前从未被 `HeadlessWorldOptions.PlayerLevel` 以非默认值（1）调用
   过——仓库内所有既有端到端测试与 T-N6-1～T-N6-3 的既有用例恒为默认值 1，这条"调用方自己负责
   第二步"的义务从未被触发、因此从未暴露。缺陷现象：`GetPowerMax(Health)` 只有 150（1 级
   `arch.class.base_stats` 原始值，装备加成仍正常叠加，但成长量完全缺失），而不是应有的
   ~2000（见下方新表）。这不是"数值不对"，是"玩家实际战斗力远低于设计意图"——任何一点等级差表/
   生物伤害曲线的正常小幅波动，都会在"玩家血量只有 150"这个错误基线上被放大成断层。**修复**：
   `core/sim/core/HeadlessWorldBuilder.cs` 在 `RegisterUnit` 之后补一段（仅限该类有
   `level_curve_ref` 时才执行，与 `RulesAssembly.RegisterUnit` 内部同一判据）：
   `Progression.ApplyGrowthToCurrentLevel` → `Powers.RecomputeMax` → `Powers.RefillAll`（`sourceId`
   复用 `ProgressionEventKeys.LevelUp`，同 T-N4-5"升级回满"既有惯例）——只是把
   `CreatureFactory.SpawnCore` 早已示范过的同一套调用顺序在玩家这一侧也照做一遍，不改动
   `core/rules`/`core/numbers` 任何一行、不新增任何公开成员。手动补 `RecomputeMax`/`RefillAll`
   而不是只指望既有的 `stat.changed → Powers.RecomputeMax` 事件订阅自动生效，是因为
   `RulesAssembly.RegisterUnit` 内部 `Powers.RegisterUnit`（经 `Archetypes.ApplyTo`）发生在
   "成长写入"之前——资源池按"成长前"的基础值把当前值/上限都定格为 `StartFull` 的那个数字，
   `RecomputeMax` 的判断记录原文只处理"上限下降时当前值随之夹取"，不处理"上限上升后当前值该不
   该跟着涨"，必须显式 `RefillAll` 才能让当前值追上成长后的上限，且必须在首次
   `world.Clock.Advance` 之前完成（不能依赖事件何时被 `DispatchPending` 处理）。
2. **`AnchorTable`/`skill.base_curve.sim_creature_bite*` 原本只到 20 级，越级矩阵 +5 偏移在玩家
   满级时把生物 21～25 级全部夹到 20 级等效强度（数据范围缺口，已扩表）**——`sim_arena_matrix`
   的 `level_offsets` 含 +5，玩家 20 级时对手出生等级达到 25；`AnchorCreatureLevelScaler` 越界
   夹到 `AnchorTable.MaxLevel`（此前 20），`skill.base_curve` 曲线在末端也是"夹到最后一个断点"
   （`PiecewiseCurve.Evaluate` 既有行为），两者共同导致玩家 20 级这一行的偏移 +1/+3/+5 全部对上
   同一个强度天花板——不是这一行"该有的形状"，是锚点表range 不够覆盖越级矩阵实际会用到的生物
   等级范围。**修复**：`sim.anchor` 扩到 25 级（21～25 行为新增，`level_duration_seconds`/
   `kill_interval_seconds`/`quest_share`/`expected_item_level` 延续既有公式或直接复用 20 级值
   ——这几个字段本任务门禁不检验、玩家也不会真的到这些等级，纯粹满足 schema 必填），
   `skill.base_curve.sim_creature_bite{,_elite}` 新增 25 级断点，`EmbeddedDatasetTests
   .AnchorTable_HasAllTwentyFiveLevelsContinuous`（原 `…TwentyLevels…`）同步更新为 25。

修完这两处之后，**全部 T-N6-4 的数值调参需要基于修复后的仿真结果重新做一遍**（修复前的仿真数据
系统性偏低，此前一版 README 记录的中间值已作废，不再收录）。

### 调参方法（最终生效结果）

**调参 1：`sim.anchor.dps`/`sim.anchor.hp`/`sim.anchor.ttk_seconds` 取修复后仿真实测的"自下而上"
值**（`sim_arena_matrix` 每格 60 次、offset=0 的 `PlayerDpsMean`/`PlayerMaxHealth`/
`TtkMeanSeconds`），1/5/10/15/20 级精确值，2～4/6～9/11～14/16～19 级按相邻两个真实锚点线性
插值（判断记录 9），21～25 级按 15→20 级斜率的 5 倍延长（判断记录见下"调参 3"）：

| L | dps（T-N6-2b 手算，已作废） | dps（最终，仿真实测） | hp（T-N6-2b 手算，已作废） | hp（最终，仿真实测） | ttk（最终） |
|--:|---:|---:|---:|---:|---:|
| 1 | 9.583 | 32.262 | 150.0 | 217.6 | 3.70 |
| 5 | 14.583 | 54.483 | 350.0 | 551.8 | 4.21 |
| 10 | 20.833 | 92.162 | 600.0 | 1002.6 | 3.61 |
| 15 | 27.083 | 123.504 | 850.0 | 1455.4 | 3.64 |
| 20 | 33.333 | 157.797 | 1100.0 | 1972.8 | 3.68 |

hp 相比 T-N6-4 首次提交时的中间值（217.6/351.8/552.6/755.4/1022.8）在 L5 及以上大幅升高，正是
修复"成长未写入"之后玩家真实血量的体现——L1 无成长可写（曲线 2..1 区间不存在），因此 L1 这一行
从一开始就没受这个缺陷影响，数值前后一致。

**调参 2：怪物伤害改用独立设计常数 `ttd_design(L) = TTD_MULT × ttk(L)`（内部，`TTD_MULT = 1.0`），
`sim.anchor.ttd_seconds` 另取仿真实测的 `FightResult.TtdEstimate` 均值（两者刻意不是同一个数，
判断记录见数值总纲"怪物伤害"公式的 `TTD(L)` 一词有两种用途）**：

| L | ttd_design（驱动伤害，内部，不写入数据） | anchor.ttd_seconds（写入数据、对账用） |
|--:|---:|---:|
| 1 | 3.70 | 14.4 |
| 5 | 4.21 | 11.3 |
| 10 | 3.61 | 9.4 |
| 15 | 3.64 | 10.4 |
| 20 | 3.68 | 9.8 |

对账偏离 0.1%～0.4%，5 个等级全部通过（`FullScenario_TtdReconciliation_AllLevelsWithinBandwidth`）。

**调参 3：`creature.template` 血量 + `skill.base_curve.sim_creature_bite`/`_elite` 伤害曲线按
调参 1～2 重算**（`creature_hp(L)=round(dps(L)×ttk(L),1)`，
`dmg_per_hit(L)=round(hp(L)/ttd_design(L)×2.0,1)`，攻击间隔固定 2 秒，精英 ×1.5）——21～25 级的
`dps(L)`/`hp(L)` 单独按"15→20 级斜率 × 5"延长（不是直接延续 15→20 的斜率）：仅按 1 倍斜率延长
时（判断记录：曾实测，越级矩阵在玩家满级这一行完全测不出正偏移的胜率下降，因为一个"名义 25 级"
的生物强度提升幅度不足以在"玩家血量已经涨到近 2000"这个新基线上造成可观测差异），×3 倍时 +3
偏移仍不达标（62%>50%），×5 倍才让 +3/+5 都落到 ≤0.5：

| L | 血量（T-N6-2b 手算，已作废） | 血量（最终） | 每次伤害（T-N6-2b 手算，已作废） | 每次伤害（最终） |
|--:|---:|---:|---:|---:|
| 1 | 77 | 119.4 | 10.0 | 117.6 |
| 5 | 129 | 229.4 | 21.8 | 262.1 |
| 10 | 206 | 332.7 | 34.5 | 555.5 |
| 15 | 296 | 449.6 | 45.5 | 799.7 |
| 20 | 400 | 580.7 | 55.0 | 1072.2 |
| 21（新增） | — | 706.9 | — | 1353.4 |
| 23（新增） | — | 959.3 | — | 1915.8 |
| 25（新增） | — | 1211.7 | — | 2478.2 |
| 精英 10 | 309 | 332.7（登记值，×1.5 分档倍率后实为 499.1） | 51.8 | 833.2（`skill.base_curve.sim_creature_bite_elite`，= 同级普通怪 555.5 × 1.5） |

`stat.strength`（生物基础攻击属性）未随之调整——本数据集里生物伤害完全由 `skill.def
.effects[].base_curve_ref` 按施法者等级直接查表（`EffectDispatcher` 源码：`base_curve_ref` 存在
时取代 `base_value`/`scaling`），不经 `stat.attack_power` 派生，`strength` 数值对本数据集的战斗
输出没有可观测影响。`skill.sim_creature_bite_elite` 的技能预算比值（9.04，超出 Monster 档带宽
[-4,6]）已补 `budget_note`——精英本就设计成比同级普通怪强 1.5 倍，是分档系统的既定意图。

**调参 4：`sim.scenario.sim_arena_matrix.runs` 从 20 提到 60**——20 次/格在低胜率格子上二项分布
抽样噪声偏大，个别相邻偏移会出现"胜率不降反升"的局部非单调（纯统计噪声，不代表仿真/数据有
问题），60 次/格后全部 5 个等级的 7 个偏移点均满足严格版矩阵形状（单调不增 ±0.05 抖动、偏移
≤−3 胜率 ≥0.95、偏移 ≥+3 胜率 ≤0.5，对全部等级成立，不再有"至少一个"的例外）。完整
`sim_arena_matrix` 总耗时约 8～9 秒（35 格 × 60 次 = 2100 场），远在 90 秒预算内。

**调参 5：`fac.reaction_matrix` 补反向敌对行、`stat.definition` 补 `stat.move_speed`**——这两项
不是"数值精调"，是 T-N6-4 首次提交时发现的既有数据缺口（生物在此之前从未真正尝试过主动攻击
玩家），详见 `core/sim/README.md` 判断记录 25、`core/numbers/stat_block/README.md`
"T-N6-4b"小节，此处不重复。

**未改动的量**：`expected_item_level`/`level_duration_seconds`/`kill_interval_seconds`/
`quest_share`（1～20 级）、`prog.level_curve.sim_warrior.entries[].xp_to_next`——`sim.anchor
.ttk_seconds` 虽然变了，"升级所需经验反推"公式（数值总纲 4.7 节）理论上应联动重算，但这是成长
仿真（`kind=growth`，T-N6-5 及之后）的输入，T-N6-4/4b 门禁不检验经验曲线，为避免一次性改动过多
且缺少成长仿真验证手段而引入新的不自洽，本任务刻意不动它，留给 T-N6-5 跑通成长仿真时一并核算
——如实记录这一已知的、有意延后的不一致。

### 诊断方法（供后续类似排查复用）

定位"L20 断层"根因时使用的两个手段，记录下来供后续任务参考：

1. **单场逐 tick 事件流打印**：直接手工构造 `HeadlessWorldOptions`（不经 `FightRunner`），把
   `CombatOptions.ResolveTrace` 接一个打印 `(sourceId, skillId, hit, requestedAmount,
   finalAmount)` 的回调，`StandardPlayerBuilder.Build` 之后立刻打印 `IStatHost.GetStat`（如
   `stat.armor`/`stat.stamina`）与 `IPowerHost.GetPowerMax`，逐 tick 打印双方当前生命——这是
   发现"L20 玩家 `GetPowerMax(Health)` 只有 150"这个反常值的直接手段（正常路径下这个数字应该
   随等级明显增长，肉眼一眼能看出不对）。
2. **控制变量对比同一格子不同生物等级**（如固定玩家 20 级，分别打一遍生物 17/18/19/20/21 级）：
   把 `winRate`/`creatureDps`/`creatureHitRate`/`playerDps` 按生物等级排成一行，肉眼找"哪两个
   相邻等级之间数字跳变最大"，缩小根因排查范围（本次定位到"生物 18→19 级之间"胜率骤降，进一步
   配合手段 1 的逐 tick 打印，看到是玩家生命基线错误导致同样的伤害绝对值占比骤变）。

## T-N6-5 调参记录

T-N6-5（成长仿真/内容覆盖仿真）第一次真正让"经验/掉落/金币"三条链路在一次持续存活的仿真世界里
连续跑满整条 1→20 级成长曲线，暴露了两处此前从未被验证过的既有缺口（均已根治）与一处本任务自身
实现的联调 bug（已修复，见 `core/sim/README.md` 判断记录 32）：

**联动重算：`prog.level_curve.sim_warrior.entries[].xp_to_next`**——T-N6-4/4b 阶段刻意留白（"如实
记录这一已知的、有意延后的不一致"，见上"未改动的量"一节），本任务按数值总纲 4.7 节公式
`升级所需(L) = 击杀基数(L) × 每级怪当量(L) × (1+Q(L))`（`每级怪当量(L) = level_duration_seconds(L)
÷ (ttk_seconds(L) + kill_interval_seconds(L))`，`击杀基数(L) = prog.xp_base_curve.sim_default(L)
= 20 + 2·(L-1)`，对线性公式在 1/5/10/15/20 断点线性插值在整数等级上精确重现）用 T-N6-4b 校准后的
`sim.anchor`（1～20 级）重新核算：

```
L1=417  L2=477  L3=540  L4=606  L5=673  L6=752  L7=834  L8=919  L9=1008  L10=1101
L11=1190 L12=1282 L13=1376 L14=1473 L15=1572 L16=1673 L17=1776 L18=1882 L19=1989 L20=0
```

（原值：`339/389/441/496/553/611/672/735/799/865/934/1003/1075/1148/1223/1299/1377/1457/1538/0`——
均因 T-N6-4b 校准后 `ttk_seconds`/`level_duration_seconds`/`kill_interval_seconds` 相比 T-N6-2b
阶段的旧手算公式变化很大而系统性上调。）`prog.xp_base_curve.sim_default` 本身复核后确认与数值
总纲 4.7 节"击杀基数"公式一致，未改动。

**调参 6（既有数据缺口，本任务首次真正验证到）：`loot.table.*` 货币条目 `count_range` 当成"最终
掉钱数"填写，实际掉钱系统性偏高约 5 倍**：`Core.Gameplay.Loot.LootHost.ResolveCurrencyOutcome`
的真实公式是"`equivalents`（`count_range` 掷骰所得的当量）× `IEconomyHost.TryGetGoldBaseAmount`
(怪物等级) × 分档倍率 × 难度倍率"——T-N6-2b/T-N6-4 阶段把 `count_range` 直接填成
`econ.gold_base_curve` 断点 ±2（如 L1 的 `{min:3,max:7}`，均值 5，恰好等于
`goldBase(1)=5`），效果是"当量(均值5) × goldBase(5) = 25"，掉钱变成设计意图的约 5 倍。T-N6-4
阶段的仿真只验证战斗胜率/DPS/HP，从未真正累计过货币，这条既有缺口因此从未暴露。**修复**：全部
`loot.table.*` 的货币条目 `count_range` 改为常量 `{"min":1,"max":1}`（当量恒为 1），使掉钱恰好
等于 `goldBase(怪物等级)`，与数值总纲 4.8 节"怪物掉钱 = 金币基数(怪物等级) × 分档倍率 × 难度掉落
倍率"字面公式（无当量项）对齐。修复前后 `sim_growth_full` 金币轨迹偏离：L1 从 131% 降到 6%，
L19 从 966% 降到 4%（叠加 `Q(L)` 联调修复后的最终值，见 `core/sim/README.md` 判断记录 32）。

**调参 7：`loot.table.*`（5 档普通怪）各追加 `chest`/`legs`/`feet` 三条 common 品质条目**：
T-N6-2b/T-N6-4 阶段只登记了主手（0.3 概率）+ 头部（0.05 概率）两个槽位——供最小烟雾测试与越级
矩阵使用，两者都不关心装备等级轨迹。成长仿真需要"各槽平均装备等级"追上
`expected_item_level(L)`，若胸/腿/脚三槽永远没有掉落条目，三者会永远停留在 1 级出生时
`StandardPlayerBuilder` 给的初始装备，均值必然被拖低。按与既有主手条目同一惯例（common 品质，
0.3 概率）各追加一条，不改变任何已有条目的取值。修复后 `sim_growth_full` 装备等级轨迹全部
19 个等级偏离 0～11%（远低于 0.25 带宽）。

**新增两条覆盖仿真探针**（验收 4 要求，`CoverageSimulation` 离群值列表前两位分别对应）：
`skill.def.sim_probe_overbudget`（`school_damage` 效果，`stat.attack_power` 系数刻意设为 60.0
——正常技能系数量级在 0.5～1.5，制造预算比值 85.23 的极端超模；已填 `budget_note` 说明是仿真
探针，`SkillBudgetAnalyzer` 判定该技能未出现在任何 `skill.book`/`creature.template
.ai_rotation_ref`，归为 `Unattributed` 档，等级缺省取 1）、`item.template
.sim_probe_underbudget`（`item.slot.sim_chest`/`item.quality.sim_common`/`item_level:20`，仅
1 点 `stat.stamina`，预算消耗比 0.3%——L20 common 胸甲的预算上限本就是全表最大，1 点数值相对
它的利用率最低，离群效果最明显）。两者均不进入 `skill.book`/`ai.rotation`/`loot.table` 等会被
真实消费的接线，只作为对应表全表扫描时才会被看到的数据行，不影响任何既有断言/战斗结算。

## 判断记录

1. **`l10n.locale`/`combat.hit_table_config`/`item.budget_curve`/`item.weapon_dps_curve` 为何用
   通用 id 而非 `sim_*` 前缀**：这四张表的具体行 id 被框架侧写死为默认值消费——
   `Core.Foundation.DataRegistry.DataRegistryOptions.DefaultLocale` 默认值字面是
   `"l10n.locale.zh_cn"`（`text_key_exists` 校验按这个固定 id 去 `l10n.text` 查默认语言文本，不读
   `l10n.locale.is_default` 字段，首次按 `l10n.locale.sim_zh_cn` 起草时触发了 8 处
   `text_key_exists` 报错，改回通用 id 后清零）；`Core.Rules.Combat.CombatOptions.HitTableConfigId`
   默认值字面是 `"combat.hit_table.default"`（`Resolver.RequireHitTable` 按这个固定 id 查
   `combat.hit_table_config`，不接受调用方在数据里另起 id 而不改代码——`HeadlessWorldBuilder`/
   `GameplayAssembly` 均未开放覆盖该默认值的选项）；`Core.Carriers.Assembly.CarriersSchemaCatalog
   .DefaultItemBudgetCurveId`/`DefaultWeaponDpsCurveId` 同理默认值字面是
   `"item.budget.default"`/`"item.weapon_dps.default"`。这四个 id 是框架基础设施的"总线地址"，不是
   本数据集自定义的游戏内容——沿用通用 id（与 `data/_sample` 同款惯例一致）不违反"内容 id 一律
   `sim_*`/`sample_*` 前缀"这条拍板（该拍板约束的是本数据集新增的具体内容——职业/生物/物品/技能/
   场景等），也不会与 `data/_sample` 混合加载产生冲突（本数据集从不与 `data/_sample` 同一次装配
   合并，只与 `data/_framework` 合并）。
2. **锚点 `dps(L)`/`hp(L)` 为何"自上而下=自下而上"，不留人为偏离**：任务书验收线是"偏离 ≤25% 即
   可，精调归 T-N6-4"——这暗示允许（甚至预期）本阶段的锚点数值是粗略估算、后续任务会用真正的仿真
   运行器核算修正。但 T-N6-2b 阶段没有可运行的仿真报告工具（那是 T-N6-3 的范围），无法先"独立"定一
   条锚点曲线、再拿一个独立的仿真结果去对照偏离——退而求其次，本数据集直接令锚点公式**等于**按标准
   玩家自身属性成长函数推导出的简化循环秒伤（"回路"自洽：先定义职业成长函数与技能效果值，
   再让锚点抄那个值），保证"存在一组自洽解"而不是"锚点是拍脑袋数字、生物强度全靠它撑着、谁都没
   验证过它是否可达"。代价：这个"自下而上"本身是一个非常简化的循环假设（忽略 `rampage`/
   `battle_shout`、未建模命中/闪避/暴击的期望值修正、未计入装备加成），T-N6-4 真正跑仿真运行器时
   几乎必然要调整——这正是任务书"精调归 T-N6-4"的本意，本任务只保证"这条链路自洽、能跑通"，不代表
   任何真实数值拍板。
3. **主手武器模板为何不带 `stats`**：`ItemBudgetValidationRule`/
   `ItemTemplateAffixShareExceedsBudgetRule` 都以"`stats` 数组非空"为前提计入预算消耗（`stats`
   为空或缺失时 `ItemBudgetValidationRule` 直接 `continue`，`ItemTemplateAffixShareExceedsBudgetRule`
   則把 `consumed` 当 0 处理）；`data/_sample` 的武器模板（`item.sample_blade`/
   `item.sample_model_sword`）同样只挂 `stats:[{stat.strength,...}]` 而不涉及秒伤曲线预算——本数据集
   进一步简化，主手武器的"强度"完全由 `weapon_profile.damage_min/max`（秒伤曲线口径）承载，不重复
   通过 `stats` 词条再消耗一次装备预算，两条预算轨道（属性词条预算 vs 武器秒伤曲线）互不占用，
   简化手算（见"装备预算手算口径"一节）。
4. **为何不含 `spawn.table`/`encounter.*`**：`Core.Carriers.Creature.CreatureFactory.Spawn(templateId,
   mapId, position, facing)` 允许调用方直接按生物模板 id 生成实体，不要求存在
   `spawn.table`/`encounter.*` 配置（那是"地图自动刷怪"这一上层玩法机制的数据，`HeadlessWorldBuilder`
   装配根本身不消费它们）。`EmbeddedDatasetTests`/`SimTestWorldFactory.RunEmbeddedFightScript` 直接
   调用 `Carriers.Creatures.Spawn` 生成战斗对象，任务书"若装配根需要"这一前提在本数据集不成立，
   因此未登记这两张表，减少数据集体量。
5. **为何生物/技能/光环/物品全部登记 `display.map`**：`Core.Foundation.DisplayInfo
   .DisplayMapCoverageRule`（阻断级）要求 `skill.def`/`skill.aura_def`/`item.template`/
   `creature.template` 的每一个 `id` 都能在 `display.map` 按 `logical_id` 查到至少一行——这是框架级
   通用校验（不区分"是否真的会被表现层渲染"），本数据集虽然是无头仿真专用、不会真正进入任何渲染
   管线，仍必须为全部 65 个逻辑 id 各登记一行最小 `display.map`（`kind:"sprite"`,
   `sprite_set_id:"sprite.sim_placeholder"`，方向数按类别给 4/8）才能通过校验，纯粹是"占位满足阻断
   项"，不代表任何真实表现层拍板。
6. **技能设计取舍**：5 个技能覆盖任务书四类角色——`skill.sim_warrior_strike`（主要单体攻击，
   `respects_gcd:false`，供 `RunEmbeddedFightScript` 逐 tick 连续施放）、
   `skill.sim_warrior_execute`（12 秒冷却高伤终结技，`base_curve_ref` 引用 `skill.base_curve`）、
   `skill.sim_warrior_battle_shout`（自身增益光环）、`skill.sim_warrior_rampage`（消耗怒气资源）；
   `skill.sim_warrior_toughness`（被动光环，1 级之后逐级解锁的第五个技能，凑满"4～6 个"区间富余度）。
   `battle_shout`/`rampage`/`execute` 均补了 `cast_time:1.0`（而非 0）——`SkillNoTimeCostWarningRule`
   对"`cast_time=0` 且 `respects_gcd=true` 且非反应类"的主动技能报警告（"全部瞬发一起放"退化风险），
   补一个非零施法时间即清零告警，且不影响本任务的循环假设（该假设按冷却/资源节奏计算，不依赖
   `cast_time` 具体数值）。
7. **`RunEmbeddedFightScript` 为何不复用 `ai.rotation`/AI 決策循环**：任务书验收点原文是"能对一只
   1 级普通怪施放……直至一方死亡且是玩家获胜"，未要求生物侧必须真的用 AI 循环反击。为把测试聚焦在
   "装配 + 学技能 + 战斗结算链路可用"这一件事上，脚本只反复对生物本身发起
   `skill.sim_warrior_strike`，不驱动生物的 `ai.rotation.sim_creature` 决策（生物在本脚本里始终不
   还手）——`ai.rotation`/`ai.behavior_profile` 两张表仍按任务书要求完整登记为内容（供后续仿真运行器
   真正驱动 AI 时使用），只是本任务这个最小烟雾测试没有消费它们的运行期效果。
8. **1 级野狼血量/每次伤害的推导来源**（T-N6-4 更新：数值已由仿真实测值反推，见"T-N6-4 调参
   记录"，不再是 T-N6-2b 阶段的手算值）：`creature_hp(1)=round(dps(1)×ttk(1),1)=78.4`，
   `dmg_per_hit(1)=round(hp(1)/ttd_design(1)×2.0,1)=59.6`——不是拍脑袋数字，是按调参 1～3 同一条
   公式链算出。`EmbeddedDatasetTests.StandardWarrior_LearnsSkillAtLevelOne_AndDefeatsNormalCreature`
   在默认 `maxAttempts=200` 内仍稳定获胜（`strike_dmg(1)=11`，约 8 次命中即可击杀 78.4 血的目标，
   `respects_gcd:false` 允许逐 tick 连续施放不受 GCD 限制；生物本身虽然现在会追击/还手，但单次
   59.6 点伤害远不足以在 8 次交手窗口内反杀玩家 217.6 点生命），T-N6-4 阶段全量测试已验证无回归。
9. **`sim.anchor` 中间等级（2～4/6～9/11～14/16～19）为何用线性插值而非独立仿真实测**：
   `sim.scenario.sim_arena_matrix.levels` 只测 `[1,5,10,15,20]` 五个玩家等级，仿真只能直接测出
   这五个点的真实 `dps`/`hp`/`ttk`；但越级矩阵的 `level_offsets` 含奇数偏移（±1/±3），会让生物
   出生在 2/4/6/7/8/9/11/12/13/14/16/17/18/19 这些"中间"等级，`AnchorCreatureLevelScaler` 需要
   这些行才能换算——若只更新 5 个真实锚点、其余 15 行仍留着 T-N6-2b 的旧线性公式值，会在 5 个
   真实点前后出现数值"跳变"（旧公式量级远小于新仿真实测值），使越级矩阵在中间偏移上出现不合理
   的跳跃。按 5 个真实点做分段线性插值虽然不是"每个等级都独立仿真验证过"，但保证了整条曲线连续、
   量级一致，且 `SimAnchorValidationRule` 本就只检查等级连续性与 `expected_item_level` 单调性
   （不检查 `dps`/`hp`/`ttk`/`ttd` 本身的单调性/连续性），插值行不违反任何既有校验规则。
