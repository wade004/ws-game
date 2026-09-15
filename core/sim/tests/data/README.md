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

## 数据集清单（45 个表文件，321 行）

| 域 | 表 | 行数 | 说明 |
|---|---|---:|---|
| stat | `stat.definition` | 9 | 四主属性 + 攻击强度（派生）+ 暴击/闪避/命中等级 + 护甲 |
| stat | `stat.rating_conversion` | 2 | 暴击/闪避评级换算曲线 |
| stat | `stat.weight` | 9 | 逐属性预算权重（均 1.0，简化装备预算手算） |
| arch | `arch.power_type` | 2 | `arch.power.health`（**覆盖**框架默认为 `kind:stat→stat.stamina`）+ `arch.power.sim_fury`（积累型资源） |
| arch | `arch.class` | 1 | `arch.class.sim_warrior`（唯一职业，近战） |
| arch | `arch.race` | 1 | `arch.race.sim_default`（属性修正留空） |
| arch | `arch.talent_tree` | 1 | 最小两节点天赋树 |
| skill | `skill.base_curve` | 3 | Execute 基础值曲线（常数 20）+ 两条生物基础攻击曲线 |
| skill | `skill.def` | 7 | 5 个玩家技能 + 2 个生物攻击技能 |
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
| item | `item.template` | 50 | 5 档物品等级 × 2 品质 × 5 槽位（1 武器 + 4 护甲） |
| creature | `creature.tier_definition` | 2 | `sim_normal`（×1.0）/ `sim_elite`（×1.5） |
| creature | `creature.template` | 6 | 5 个普通怪档位（L1/5/10/15/20）+ 1 个精英（L10） |
| loot | `loot.table` | 6 | 每档生物一张（货币 + 普通/稀有装备条目） |
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
| sim | `sim.anchor` | 20 | 1～20 级连续（见下"锚点推导"） |
| sim | `sim.scenario` | 3 | arena / growth / coverage 各一条 |
| display | `display.map` | 65 | `skill.def`/`skill.aura_def`/`item.template`/`creature.template` 全部逻辑 id 的最小外形映射（`DisplayMapCoverageRule` 阻断要求） |
| l10n | `l10n.locale` | 1 | `l10n.locale.zh_cn`（**id 必须等于** `DataRegistryOptions.DefaultLocale` 默认值，见判断记录 1） |
| l10n | `l10n.text` | 89 | 全部 `name_key`/`TextKey` 字段的中文文本（`text_key_exists` 阻断要求） |

校验命令与结果：

```
python toolchain/validate_data.py --strict --framework-root data/_framework --data-root core/sim/tests/data
# tables 49, records 446, errors 0, warnings 0, overrides 1（覆盖=arch.power.health）
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
- `dps(L)`、`hp(L)`：见下"自下而上手算"——本数据集**反过来**用标准玩家的自下而上口径直接定义
  `dps(L)`/`hp(L)`，见判断记录 2（"自上而下=自下而上"的构造方式）

标准战士 `arch.class.sim_warrior` 的等级函数（`prog.level_curve.sim_warrior` 的
`base_stats`/`growth` 换算）：

- `strength(L) = 12 + 3·(L-1)`
- `stamina(L) = 150 + 50·(L-1)`
- `attack_power(L) = strength(L)`（`derivation_overrides` 系数 1.0）

技能（`skill.sim_warrior_strike` 主要单体攻击 / `skill.sim_warrior_execute` 12 秒冷却高伤终结技）的
效果值：

- `strike_dmg(L) = 5 + 0.5 · attack_power(L)`
- `execute_dmg(L) = 20 + 1.5 · attack_power(L)`（`base_curve_ref` 常数 20 + `scaling` 系数 1.5）

**自下而上标准玩家秒伤**（假设 GCD=1.5 秒的简化循环：终结技 12 秒冷却一次、其余 7 个 GCD 用主要
攻击填充——`skill.sim_warrior_rampage`/`skill.sim_warrior_battle_shout` 未计入本手算，作保守简化，
精调归 T-N6-4）：

```
dps_bottom_up(L) = (execute_dmg(L) + 7 · strike_dmg(L)) / 12
```

`sim.anchor.dps`/`sim.anchor.hp` 直接取

```
dps(L) = dps_bottom_up(L)
hp(L)  = stamina(L)
```

即本数据集按构造方式让"自上而下"锚点与"自下而上"标准玩家估算**恒等**（偏离 0%，远低于任务书
"≤25%"验收线）——见判断记录 2 说明这样做的理由与局限。5 个锚点等级的手算表：

| L | dps 自上而下 | dps 自下而上 | 偏离 | hp | TTK(s) | TTD(s) | E(L) |
|--:|---:|---:|---:|---:|---:|---:|--:|
| 1 | 9.583 | 9.583 | 0.00% | 150 | 8.000 | 30.000 | 1 |
| 5 | 14.583 | 14.583 | 0.00% | 350 | 8.842 | 32.105 | 5 |
| 10 | 20.833 | 20.833 | 0.00% | 600 | 9.895 | 34.737 | 10 |
| 15 | 27.083 | 27.083 | 0.00% | 850 | 10.947 | 37.368 | 15 |
| 20 | 33.333 | 33.333 | 0.00% | 1100 | 12.000 | 40.000 | 20 |

**生物反推**（数值总纲 4.2 节公式，`tier_mult`：普通 1.0、精英 1.5，攻击间隔固定 2 秒）：

```
creature_hp(L, tier)      = round(dps(L) · ttk(L) · tier_mult)
creature_dps(L, tier)     = hp(L) / ttd(L) · tier_mult
creature_dmg_per_hit(L,t) = round(creature_dps(L, tier) · 2.0, 1)
```

| 生物 | 等级 | 分档 | 血量 | 每次伤害 |
|---|--:|---|---:|---:|
| `creature.sim_wolf_l1` | 1 | 普通 | 77 | 10.0 |
| `creature.sim_wolf_l5` | 5 | 普通 | 129 | 21.8 |
| `creature.sim_wolf_l10` | 10 | 普通 | 206 | 34.5 |
| `creature.sim_wolf_l15` | 15 | 普通 | 296 | 45.5 |
| `creature.sim_wolf_l20` | 20 | 普通 | 400 | 55.0 |
| `creature.sim_wolf_elite_l10` | 10 | 精英 | 309 | 51.8 |

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
  `opponent.level_offsets=[-5,-3,-1,0,1,3,5]`（越级矩阵），`runs=20`，`max_ticks=1200`，带宽全部
  先给 0.25（精调归 T-N6-4）。
- `sim.scenario.sim_growth_full`：`kind=growth`，`level_from=1`，`level_to=20`。
- `sim.scenario.sim_coverage_all`：`kind=coverage`，`levels=[1,5,10,15,20]`。

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
8. **1 级野狼血量为何只有 77、每次伤害仅个位数**：完全由锚点公式在 `L=1` 处的自然取值决定
   （`dps(1)≈9.58`、`hp(1)=150` 反推 `creature_hp=77`、`hp(1)/ttd(1)*2≈10`），不是另起的拍脑袋数字——
   这也顺带保证了 `EmbeddedDatasetTests.StandardWarrior_LearnsSkillAtLevelOne_AndDefeatsNormalCreature`
   在默认 `maxAttempts=200` 内稳定获胜（`strike_dmg(1)=11`，约 7 次命中即可击杀 77 血的目标，`respects_gcd
   :false` 允许逐 tick 连续施放不受 GCD 限制）。
