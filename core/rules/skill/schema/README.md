# `skill.*` 数据表字段

对应 [`SkillSchemas.cs`](SkillSchemas.cs) 的 `TableSchema` 声明；字段语义详见
[06_规则层_属性技能战斗AI.md](../../../../architecture/06_规则层_属性技能战斗AI.md) 第 3.1/3.2/3.3/3.4/3.5
节、[04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 1.1 节表清单。宿主在构造
`IDataRegistry` 后需要依次 `RegisterSchema(SkillSchemas.Def)` 等注册本文件列出的六张表才能加载对应数据
（本模块不自动注册，与 `power_set.PowerSchemas`/`stat_block.StatSchemas` 同一惯例）。第六张
`skill.base_curve` 是 T-N3-2 新增，见下"效果原语参数表"`base_curve_ref` 行与本文件末尾同名小节。

## `skill.def`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `skill.<name>` |
| `school` | Id | 是 | 学派 |
| `kind` | Enum(`active`\|`passive`) | 是 | 主动技能走施法管线；被动技能不可主动施放 |
| `range` | Number | 是 | 射程，`0` 表示无限制/作用于自身，不做范围检查 |
| `tags` | List\<Id\> | 否 | 供 `SpellMod.affects` 与 Expr 按标签过滤 |
| `cast_time` | Number | 是 | 读条时间，`0` 表示瞬发；同时承担"动作时长"语义——期间不能开始下一个技能，效果在动作结束时生效，逻辑层不区分读条与快速挥砍（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 10，见 06 第 3.1 节 2026-09-14 修订段。**契约疑点**：数值总纲"一拍常数"只是技能预算公式的记账单位，ADR-0031 决策 10 原文明确"运行期不存在任何锁"——`cast_time: 0` 的瞬发技能不因这个记账常数而占用任何实际节拍窗口，是否受节拍锁约束完全由 `respects_gcd` 决定，与"仍占一个节拍"这类表述无关） |
| `channel_time` | Number | 否 | 引导时长，与 `cast_time` 互斥（不得同时非零，见校验规则） |
| `cost` | Array | 否 | 消耗资源列表，只有固定值写法（不做资源上限百分比、引导每秒等模式）；引导技能施法开始时一次性扣；非战斗技能（开锁、传送、坐骑、造物等）消耗为零，约束改由 `use_condition`、动作时长与冷却承担（ADR-0031 决策 3）：`[{power_type: Id, amount: Number}, ...]` |
| `cooldown_category` | Id | 否 | 冷却分类引用，同分类技能共享冷却 |
| `cooldown_duration` | Number | 否 | 冷却时长，缺省 0 |
| `charges` | Object | 否 | `{max: Int, recharge_time: Number}`；存在时冷却判定改走充能而非 `cooldown_duration` |
| `action_cost` | Number | 否 | 离散模式行动点消耗；`SkillDefCache` 解析为 `SkillDef.ActionCost`（缺省 0），`CastPipeline` 经注入的 `SkillOptions.TryConsumeActionPoints`/`IsDiscreteStep` 在离散步内扣减（连续模式或未装配注入点时忽略本字段，见 W1 收边补齐、A3 审计 #5） |
| `respects_gcd` | Bool | 是 | 字段名保留，语义由"是否受公共冷却影响"扩展为"是否受节拍锁约束"——开公共冷却时节拍锁是公共冷却，关公共冷却（默认）时节拍锁是当前动作时长（`cast_time`）；声明为 `false` 的反应类技能（打断/格挡/保命）可在他技能动作中插入（ADR-0031 决策 10） |
| `target_shape_ref` | Id | 是 | 指向 `target.chain_def`（本模块施法管线步骤 6 按此语义直接传给 `ITargetHost.Resolve`，见 README"判断记录"第 1 条） |
| `effects` | Array | 是 | `[{kind: String(snake_case), params: Object}, ...]`，`kind` 取值见 `EffectKindNames`；ADR-0019 起元素结构登记为 `SkillSchemas.EffectsItemSchema`（按 `kind` 分派的 `Variants`，19 种原语各自的 `params` 结构见下"效果原语参数表"），加载期递归校验 |
| `interrupt_flags` | Array\<String\> | 否 | `movement`\|`damage_taken`\|`control` 的子集，元素登记为 `Enum(InterruptFlagValues)` |
| `use_condition` | Expr | 否 | 使用条件（宿主为施法者上下文，`self`/`combat`/`target` 分组，见 04 第 6.2 节"宿主引用分组"）；为假时施法返回 `ConditionNotMet`（T-N3-4 落地），就绪查询（`getSkillReadiness`）同步反映；"脱战才能用""仅限战斗中""目标是物件"均用它表达（ADR-0031 决策 9，见 06 第 3.1 节 2026-09-14 修订段）。登记为 `FieldKind.Expr` 后自动获得 `DataRegistry` 内建 `expr_parsable` 校验，本任务不需要额外注册校验规则 |
| `budget_note` | String | 否 | 超模说明：技能预算（锚点秒伤(技能等级) × 施放时间当量 × 冷却溢价 × 范围折价 × 消耗溢价，见 06 第 3.10 节）偏离带宽时填写意图，`SkillBudgetAnalyzer`（T-N3-9，本任务未落地）按有无本字段把带宽外的技能分为已确认/待确认两组；比值超硬上限且本字段为空为阻断（ADR-0031 决策 2） |

`cost`/`charges` 同样在 ADR-0019 起登记了子结构（`cost` 元素 `{power_type: Id 必填, amount:
Number 必填}`；`charges` `{max: Int 必填, recharge_time: Number 必填}`），加载期递归校验其内部
子字段必填与类型，不再需要额外的手写规则（见下"退役规则"）。

## `skill.aura_def`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `skill.aura_def.<name>` |
| `name_key` | TextKey | 否 | 光环名称文本键，缺省时表现层不渲染名称（不回退占位文案），见 [ADR-0056](../../../../architecture/adr/0056-施法条与光环列表数据补全.md) |
| `polarity` | Enum(`beneficial`\|`harmful`) | 否 | 光环极性（对承受者有利/有害），缺省未声明——不代表任一极性，见 [ADR-0060](../../../../architecture/adr/0060-光环极性与图标引用字段补全.md) |
| `icon_ref` | Id | 否 | 光环图标资源引用；类别前缀限定 `icon`（资产根相对路径），见 [ADR-0038](../../../../architecture/adr/0038-资源引用类别前缀唯一决定路径空间.md)/[ADR-0039](../../../../architecture/adr/0039-内容数据schema破坏性变更政策.md)/ADR-0060 |
| `duration` | Number | 否 | 空表示永久直到被移除 |
| `max_stacks` | Int | 否 | 缺省 1 |
| `stack_category` | Id | 否 | 跨定义的静态叠加校验分组，仅供加载期内容校验使用（见校验规则"叠加类别冲突"）；不是运行时叠加槽位维度，运行时按 `(target, aura_def, sourceKey)` 分槽，与本字段无关（见 [ADR-0023](../../../../architecture/adr/0023-光环叠加类别为静态校验分组.md)） |
| `dispel_type` | Id | 否 | 供 `dispel` 效果按类别筛选 |
| `effects` | Array | 是 | `[{kind: String, params: Object}, ...]`，`kind` 取值见 `AuraEffectKindNames`（10 项）；ADR-0019 起元素结构登记为 `SkillSchemas.AuraEffectsItemSchema`，10 种取值各自的 `params` 结构见下"光环效果参数表" |

## `skill.proc_def`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `skill.proc_def.<name>` |
| `trigger_event` | Id | 是 | 监听的事件 key（见 06 第 8 节事件词汇表） |
| `condition` | Expr | 否 | 触发条件 |
| `trigger_skill` | Id | 是 | 触发后释放的技能 |
| `internal_cooldown` | Number | 否 | 触发器自身冷却 |
| `proc_chance` | Number | 是 | 触发概率 0~1 |

## `skill.spell_mod_def`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `skill.spell_mod_def.<name>` |
| `target_dimension` | Enum | 是 | `cast_time`\|`cost`\|`cooldown`\|`crit_chance`\|`effect_value`\|`charges` |
| `op` | Enum | 是 | `flat`\|`pct` |
| `value` | Number | 是 | 修正数值 |
| `affects` | Object | 否 | `{schools: IdList, tags: IdList, skill_ids: IdList}`（ADR-0019 起登记 `Fields`，三者均按方案字面登记为 `IdList`，不做跨表存在性检查，加载期递归校验类型） |

## `skill.book`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `skill.book.<name>` |
| `entries` | Array | 是 | `[{level: Int, skill_id: Reference(skill.def)}, ...]`（ADR-0019 起登记 `Item`：`level` 必填 Int，`skill_id` 必填且为 `Reference(skill.def)`——同模块内引用，目标必须已加载） |

## `skill.base_curve`（T-N3-2 新增）

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `skill.base_curve.<name>` |
| `entries` | Array | 是 | 断点表 `[{x, y}]`（04 第 3.6 节通用曲线形态，`CurveSchema.BreakpointsField`，横轴 `CurveAxis.Level`——`x` 为施法者等级 Int，`y` 为该等级对应的效果基础值 Number），按 `x` 线性插值、越界夹取到端点；`curve_monotonic_finite`（T-N0-3）自动覆盖 |

供 `school_damage`/`heal`/`periodic_damage`/`periodic_heal` 的可选 `base_curve_ref` 引用，存在时取代
`base_value`（见下"效果值契约"）。**设计层裁定（2026-09-15）：采纳**——06 第 3.2 节 2026-09-14
修订段与 [ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 1 均只说"基础值
可选引用等级曲线（`base_curve_ref`）"，目标表登记为本表 `skill.base_curve`（横轴施法者等级），与
`item.armor_curve` 等新曲线表同一惯例（新表，直接按通用断点表形态登记，不需要迁移），不影响
已落地的 `scaling` 列表求和这一主线契约（决策 1 的另一半，已明确落地，不依赖本表是否存在）——见
`SkillSchemas.BaseCurve` 类型注释、`SkillDefCache.TryGetBaseCurve` 判断记录。

## `skill.budget_rule`（T-N3-3 最小骨架，T-N3-9 补完整字段）

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `skill.budget_rule.<name>` |
| `beat_seconds` | Number | 否（缺省 1.0） | 一拍常数：06 第 3.10 节预算公式的记账单位（施放时间当量 = max(动作时长, 一拍常数)），T-N3-3 起同时是 `weapon_damage_pct` 原语运行期公式"武器秒伤 × 一拍常数 × 百分比"的乘数；`> 0`（`FieldRange.Range(min: 0, minExclusive: true)`）；`FieldUnit.Time`/表 `TimeScope.Combat` |
| `periodic_time_discount` | Number | 否（缺省 1.0） | T-N3-9：周期效果（`apply_aura` 引用的光环含 `periodic_damage`/`periodic_heal` 且 `duration` 非空）的施放时间当量折价系数——T = 光环总持续时间 × 本字段；`(0,1]`；字段名为临时判定，见下方判断记录 |
| `cooldown_premium_curve` | Array（断点表） | 否 | T-N3-9：冷却溢价曲线，横轴"冷却÷T"（`CurveAxis.Value`），纵轴溢价倍数；缺失/空断点表时 `SkillBudgetAnalyzer` 取中性倍数 1.0 |
| `range_discount_curve` | Array（断点表） | 否 | T-N3-9：范围折价**除数**曲线（不是直接倍数，见下方判断记录），横轴 `max_targets`，纵轴除数；`SkillBudgetAnalyzer` 按 `1.0/曲线取值` 换算实际折价倍数；缺失/空断点表/`max_targets=0`（不限）时取中性倍数 1.0 |
| `cost_premium_curve` | Array（断点表） | 否 | T-N3-9：消耗溢价曲线，横轴"消耗÷期望回复率"（期望回复率取 `cost[0].power_type` 的 `arch.power_type.regen_in_combat`），纵轴溢价倍数；缺失/空断点表/无消耗时取中性倍数 1.0 |
| `player_bandwidth` | Number | 否（缺省 0.2） | T-N3-9：玩家档带宽——比值 ≤ 1+本值时通过（本实现只检查超出上界，不对称检查偏低）；`>= 0` |
| `monster_bandwidth` | Number | 否（缺省 5.0） | T-N3-9：怪物档带宽，语义同上；怪物技能合理超模幅度远大于玩家技能，缺省值远大于玩家档 |
| `player_hard_cap` | Number | 否（缺省 3.0） | T-N3-9：玩家档硬上限——比值超本值且 `skill.def.budget_note` 为空则阻断；`> 1` |
| `monster_hard_cap` | Number | 否（缺省 50.0） | T-N3-9：怪物档硬上限，语义同上；`> 1` |
| `control_category_weights` | Object | 否 | T-N3-9：控制类别权重，六个具名可选 Number 子字段（`stun`/`root`/`silence`/`disarm`/`fear`/`polymorph`，对应 `ControlCategoryValues.All`），各缺省 1.0，供 `control` 价值公式"时长×目标数×控制类别权重"消费 |

**T-N3-9 设计层裁定（2026-09-15）：采纳（见 `SkillSchemas.BudgetRule`/
`SkillBudgetAnalyzer` 类型判断记录）**：

1. 06 原文把"玩家档/怪物档"与"带宽""硬上限"并列写在同一句描述表结构，但另一句明确"玩家档/怪物档
   由反向引用决定"——裁定采纳后一句："玩家档/怪物档"不是表本身的字段，只是"用哪一组带宽/硬上限"的
   选择依据，故按档位拆成 `player_*`/`monster_*` 两组字段而不是单一 `bandwidth`/`hard_cap`。
2. "范围折价"语义上应随 `max_targets` 增大而递减，但 04 第 5 节 `curve_monotonic_finite` 对全部
   断点表统一要求纵轴不递减——裁定 `range_discount_curve` 登记为随 `max_targets` 增大而递增的除数，
   运行期取倒数换算成实际倍数。
3. "施放时间当量规则"未给出独立字段名，裁定周期效果的折价系数字段名为 `periodic_time_discount`。

供 `EffectDispatcher.ResolveBeatSeconds`（经 `SkillDefCache.TryGetBeatSeconds`）按
`SkillOptions.BudgetRuleId`（缺省 `skill.budget_rule.default`）查询；表未注册或该 id 没有对应
记录时按缺省 1.0 处理并记一条警告，不阻断结算。**判断记录（分两步落地，不是自行发明契约）**：
06 第 3.10 节给出的是完整表（一拍常数、施放时间当量规则、冷却溢价/范围折价/消耗溢价三条曲线、
带宽、硬上限、控制类别权重、玩家档/怪物档），落地改动点清单把它整体排到 T-N3-9；但 T-N3-3
（`weapon_damage_pct` 改接秒伤 × 一拍常数）在 T-N3-9 之前就需要运行期读到一拍常数，任务书就此
裁定"本任务先登记最小骨架（只含 `id`/`beat_seconds`），其余字段先不登记"——T-N3-9 会在**同一张**
`TableSchema` 上继续补登记其余字段（新增字段，非破坏性，不需要 schema 版本递增）。**设计层裁定
（2026-09-15）：采纳**——06 第 3.10 节原文只有"一拍常数只是记账单位（如半秒）"这句散文描述，
字段名按落地方案措辞、参照 `cast_time`/`cooldown_duration` 既有时间字段的命名惯例定为
`beat_seconds`，全部读取点（`SkillDefCache.TryGetBeatSeconds`、`EffectDispatcher.ResolveBeatSeconds`）
与本表字段名一致。另：`FieldUnit.Time`/`TimeScope.Combat` 只是满足 `SchemaAudit`
"time_scope_declared"元数据门禁的声明，`beat_seconds` 按秒表达即为权威值，不接入
`CooldownTracker`/`AuraHost` 那一套连续/离散模式切换换算系数，离散模式下的换算核对留给阶段 N6
仿真接入锚点时统一核对，不在效果层做，见 `SkillSchemas.BudgetRule` 类型注释。

## weapon_damage_pct：秒伤 × 一拍常数（T-N3-3）

[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 1/2；
[ADR-0032](../../../../architecture/adr/0032-装备预算消耗与词缀份额.md) 决策 4；06 第 3.2 节
2026-09-14 修订段"`weapon_damage_pct` 改为基于武器秒伤 × 一拍常数 × 百分比而不是基于单次武器
伤害"：

- **公式**：`效果值 = GetWeaponDps(施法者) × ResolveBeatSeconds() × pct`——不叠加"效果值契约"
  （上一节）的 `scaling`/`base_curve_ref`，06 原文把 `weapon_damage_pct` 单独成句，与
  `school_damage`/`heal`/周期效果那一句（"统一为基础值 + Σ(...)"）分开表述，本任务据此判定
  `weapon_damage_pct` 只替换基础值来源，不叠加缩放项（`EffectDispatcher.ApplyDamageOrHeal` 的
  `WeaponDamagePct` 分支自成一路，与 `else` 分支的 `scaling` 求和逻辑互斥，代码层面已隔离）。
- **`GetWeaponDps`**：T-N2-6 新增（07 第 1.2 节"武器秒伤 = `item.weapon_dps_curve(item_level)` ×
  品质预算倍率 × 武器槽位系数"），无武器为 0；与 `weapon_profile.speed` 无关（速度已是"秒伤"
  定义的一部分，不需要在这一层再乘一次），因此换同一武器模板的攻速不改变 `weapon_damage_pct`
  技能伤害——见 `core/carriers/item/tests/T_N2_6_WeaponDpsDeviationTests.cs`"验收组 3"。
- **`ResolveBeatSeconds`**：见上一节 `skill.budget_rule`。
- **禁止事项落地**：`IWeaponDamageQuery.GetWeaponBaseDamage`（武器单次基础伤害均值）不再被本
  分支调用——方法本身保留供其它消费方，签名与既有语义不变。
- **迁移说明（行为变化）**：旧语义"武器单次伤害（`(damage_min+damage_max)/2`）× 百分比"→新
  语义"武器秒伤 × 一拍常数 × 百分比"，数值会变，游戏侧已有的 `weapon_damage_pct` 技能百分比
  需要重新校准（详见 `CHANGELOG.md` `[Unreleased]` 段 T-N3-3 行）。

## 结算类原语集合（T-N3-1，`Core.Rules.Common.SettlementEffectKinds`）

06 第 3.2 节 2026-09-14 修订段"结算类原语集合"——供技能预算校验（3.10 节，T-N3-9）适用范围判定与
一键智能释放候选集（T-N3-10）共用：一个技能的 `effects[]` 只要含 `SettlementEffectKinds.All`
内任一 `kind`，就参与预算校验、进入智能释放候选集；效果列表不含以上任一项的技能（开锁、传送、
造物、学习、写世界标志等）两者都不参与。本任务只登记这份集合常量（`core/rules/common/contracts/
SettlementEffectKinds.cs`），不落地 T-N3-9/T-N3-10 的消费实现。

集合取值：`school_damage`、`weapon_damage_pct`、`heal`、`projectile`、`apply_aura`。**设计层裁定
（2026-09-15）：采纳**——06 原文前四项是无条件的效果原语类型，但 `apply_aura` 是有条件的——"这条
`apply_aura` 算不算结算类"取决于它引用的 `skill.aura_def` 自身是否含 `periodic_damage`/
`periodic_heal`/`mod_stat`/`control`/`absorb` 任一效果，不是 `apply_aura` 这个 `EffectKind` 本身
的固有属性；`SettlementEffectKinds.All`/`IsSettlement(string kind)` 按效果原语类型这一层无条件把
`apply_aura` 计入集合（"可能是结算类"），不下钻解析具体引用的光环定义，这一层更细的判定由
T-N3-9/T-N3-10 在真正消费这份集合时解析落实，详见该类型顶部判断记录。

## 效果原语参数表（ADR-0019 / F1a，`SkillSchemas.EffectsItemSchema`）

`skill.def.effects`（技能效果列表）与 `projectile.params.on_hit_effects`（弹道命中后效果列表）
共用同一份元素结构：`{kind: String, params: Object}`，按 `kind` 分派到下表某一行的参数清单
（对照运行时解析代码：`EffectDispatcher.cs`、`ProjectileHost.cs`、
`Core.Carriers.{Summon,Item,Gobj}` 三个 `IEffectExtension` 实现；来源列标注对应实现位置）。
`params` 是否必填：19 种原语中仅 `open_lock`（无参数）`params` 可省略，其余均要求 `params`
本身存在（哪怕是空对象），内部子字段各自的必填见下表"必填"列。

| `kind` | 参数（名/类型/必填/引用目标） | 运行时代码位置 |
|---|---|---|
| `school_damage` | `base_value`:Number/否、`coefficient`:Number/否、`school`:Id/否（缺省取 `skill.def.school`）、`scaling_stat`:Reference(`stat.definition`)/否（旧单字段写法，见下"效果值契约"）、`scaling`:Array\<{stat:Reference(`stat.definition`)/是, coefficient:Number/是}\>/否（T-N3-2 新增，权威写法，允许多条求和）、`base_curve_ref`:Reference(`skill.base_curve`)/否（T-N3-2 新增，存在时取代 `base_value`） | `EffectDispatcher.ApplyDamageOrHeal` |
| `heal` | 同 `school_damage`（同一组参数、同一处实现分支） | `EffectDispatcher.ApplyDamageOrHeal` |
| `weapon_damage_pct` | `pct`:Number/否（缺省 0；RC-11：也可退回 `params.base_value`，本登记只列权威参数名 `pct`） | `EffectDispatcher.ApplyDamageOrHeal`（`WeaponDamagePct` 分支，T-N3-3 起公式为"武器秒伤 × 一拍常数 × pct"，见下方专节） |
| `apply_aura` | `aura_def`:Reference(`skill.aura_def`)/是、`duration_override`:Number/否 | `EffectDispatcher.ApplyAuraEffectPrimitive` |
| `dispel` | `category`:Id/否（对应 `aura_def.dispel_type`）、`count`:Int/否（缺省 1） | `EffectDispatcher.ApplyDispel` |
| `energize` | `power_type`:Id/是（判断记录：无独立可跨层引用登记表，同 04 §5.1 口径按 Id 登记）、`amount`:Number/否（缺省 0） | `EffectDispatcher.ApplyEnergize` |
| `trigger_spell` | `skill_id`:Id/是（判断记录：见下"分层与既有测试"一节，收窄为 Id） | `EffectDispatcher.ApplyTriggerSpell` |
| `modify_cooldown` | `skill_id`:Id/否、`category`:Id/否（二者至少填一，业务判断不进登记）、`delta`:Number/否 | `EffectDispatcher.ApplyModifyCooldown` |
| `add_charge` | `skill_id`:Id/是、`amount`:Int/否（缺省 1） | `EffectDispatcher.ApplyAddCharge` |
| `projectile` | `travel_mode`:Enum(straight\|arc\|homing)/否（缺省 straight）、`hit_behavior`:Enum(impact_on_first\|pierce\|impact_on_expiry)/否（缺省 impact_on_first）、`speed`:Number/否（缺省 `ProjectileOptions.DefaultSpeed`）、`max_range`:Number/否（缺省 `ProjectileOptions.DefaultMaxRange`）、`arc_height`:Number/否（缺省 `ProjectileOptions.DefaultArcHeight`）、`impact_radius`:Number/否（缺省 `ProjectileOptions.DefaultImpactRadius`）、`max_pierce_count`:Int/否、`relation_policy`:Enum(default\|hostile_only\|friendly_only\|locked_target_only)/否（缺省 default，ADR-0028）、`pierce_order`:Enum(nearest\|hostile_first)/否（缺省 nearest，仅 pierce 生效，ADR-0028）、`display_ref`:Id/否（分层边界退回 Id）、`on_hit_effects`:Array(Item=`EffectsItemSchema` 自身，惰性递归)/否 | `ProjectileHost.Spawn`/`AdvanceOne`/`PassesRelationPolicy`/`BuildPierceComparer` |
| `move` | `mode`:Enum(charge\|leap\|knockback)/否（缺省 charge）、`point`:Vec2/否（leap 用）、`distance`:Number/否（knockback 用，缺省 5）、`stop_distance`:Number/否（charge 用，缺省 1.0）——判断记录：四字段按 mode 分别只用其中一部分，本次登记不再拆二级 Variants（见类型注释） | `EffectDispatcher.ApplyMove` |
| `summon` | `creature_template`:Id/否（分层边界退回 Id 且收窄为非必填，见下）、`duration`:Number/否、`position`:Vec2/否 | `Core.Carriers.Summon.SummonEffectExtension.TryHandle` |
| `interrupt` | `lock_school`:Id/否、`lock_duration`:Number/否 | `EffectDispatcher.ApplyInterrupt` |
| `teleport` | `point`:Vec2/否（缺省目标当前位置，等价 no-op） | `EffectDispatcher.ApplyTeleport` |
| `open_lock` | 无参数（`params` 本身可省略） | `Core.Carriers.Gobj.GobjEffectExtension.TryHandle` |
| `create_item` | `item_template`:Id/否（分层边界退回 Id 且收窄为非必填）、`count`:Int/否（缺省 1） | `Core.Carriers.Item.ItemEffectExtension.TryHandle` |
| `learn_skill` | `skill_id`:Id/是（判断记录：收窄为 Id，同 `trigger_spell`） | `EffectDispatcher.ApplyLearnSkill` |
| `set_world_flag` | `flag_key`:Id/否（判断记录：本模块尚无落地 `IEffectExtension` 实现，`value` 因联合类型 Bool\|Id 无法用 `FieldKind` 表达，未登记） | 无落地实现（扩展点，见 `IEffectExtension.cs`） |
| `script` | `hook_id`:Id/否（消费方反馈第 39 条：补 `SoftReferenceTable("found.hook")`，不升级 Reference） | 无落地实现（扩展点） |

**分层边界（04 §5.1 口径）**：`create_item.item_template`→`item.template`、
`summon.creature_template`→`creature.template`、`projectile.params.display_ref`→`display.map`
三处概念上想引用 L3/L5 层的表，但 `skill` 模块属 L2、依赖方向单向不可"向上"引用，均退回 `Id`
（只做格式校验，不做跨表存在性检查）。`script.hook_id` 概念上指向 L0 的 `found.hook`，方向合法，
消费方反馈第 39 条（2026-09-13）核实该表早已登记 `TableSchema`，本字段补
`SoftReferenceTable("found.hook")`；仍不升级为 `Reference`——`found.hook` 是运行时按 id 分发的
挂载点注册表，不希望把"是否存在"变成加载期硬阻断。

**分层与既有测试（判断记录，偏离方案第 36 行"skill_id→skill.def"示例）**：`trigger_spell`/
`modify_cooldown`/`add_charge`/`learn_skill` 四处 `skill_id`、以及 `summon.creature_template`/
`create_item.item_template`/`set_world_flag.flag_key`/`script.hook_id` 均未登记为强校验的
`Reference`/必填——`EffectDispatcher` 对目标技能缺失均已有专门的 `Warn` 降级路径（不抛异常，
不同于 `apply_aura.aura_def` 缺目标即崩溃的场景），且既有测试套件
（`EffectPrimitiveDispatchTests.AddCharge_UnknownSkillId_WarnsAndDoesNotThrow`、
`LearnSkillEffect_GrantsSkillToTarget`、`ExtensionKinds_With(out)ExtensionInjected_*` 等）已经在
多处覆盖"引用一个当前未加载的技能 / 未注入扩展实现"这类场景；若登记为强 `Reference`/必填会与
这些既有测试的语义直接冲突（详见 `SkillSchemas.cs` 对应字段旁的判断记录）。

## 光环效果参数表（ADR-0019 / F1a，`SkillSchemas.AuraEffectsItemSchema`）

`skill.aura_def.effects` 元素结构 `{kind: String, params: Object}`，按 `kind` 分派到下表；对照
`AuraHost.cs`（`ApplyStaticEffects`/`ReapplyStatMods`/`FirePeriodic`）。

| `kind` | 参数（名/类型/必填/引用目标） | 运行时代码位置 |
|---|---|---|
| `mod_stat` | `stat`:Reference(`stat.definition`)/是、`op`:Enum(flat\|pct\|mult)/否（缺省 flat）、`value`:Number/否（缺省 0） | `AuraHost.ReapplyStatMods` |
| `periodic_damage` | `interval`:Number/是（无缺省，`interval<=0` 时周期永远不触发）、`base_value`:Number/否、`coefficient`:Number/否、`school`:Id/是（判断记录：`FirePeriodic` 读取时 fallback 是零值 Id，不同于顶层效果的"缺省取 skill.def.school"，缺失会产生无效 Id，登记层拦下）、`scaling_stat`:Reference(`stat.definition`)/否（旧单字段写法）、`scaling`:Array\<{stat, coefficient}\>/否（T-N3-2 新增，与 `school_damage.scaling` 共用同一份字段实例）、`base_curve_ref`:Reference(`skill.base_curve`)/否（T-N3-2 新增，与 `school_damage.base_curve_ref` 共用同一份字段实例） | `AuraHost.Update`/`FirePeriodic` |
| `periodic_heal` | 同 `periodic_damage` | `AuraHost.Update`/`FirePeriodic` |
| `absorb` | `amount`:Number/是、`school`:Id/否（缺省吸收全部学派） | `AuraHost.ApplyStaticEffects`/`AbsorbPerStack` |
| `immunity` | `schools`:IdList/否、`effect_kinds`:Array(Item=Enum，19 种 `EffectKind` 全集)/否 | `AuraHost.ApplyStaticEffects` |
| `proc_trigger` | `proc_ref`:Reference(`skill.proc_def`)/是 | `AuraHost.ApplyStaticEffects`/`ProcHost.Attach`（同一光环可登记多个本类型条目，各自独立挂载/结算/注销，见 `AuraInstanceState.ProcDefRefs` 判断记录；同一光环内重复引用同一个 `proc_ref` 由下表 `AuraProcTriggerDuplicateRule` 在加载期拒绝） |
| `spell_mod` | `spell_mod_ref`:Reference(`skill.spell_mod_def`)/是 | `AuraHost.ApplyStaticEffects`/`SpellModResolver` |
| `override_skill` | `from`:Reference(`skill.def`)/是、`to`:Reference(`skill.def`)/是 | `AuraHost.ApplyStaticEffects`/`ResolveSkillOverride` |
| `control` | `flags`:Array(Item=Enum(no_move\|no_cast\|no_attack\|no_interact))/否、`category`:Enum(stun\|root\|silence\|disarm\|fear\|polymorph)/否（T-N3-6 新增，缺省未分类，取值集合 `Core.Rules.Common.ControlCategoryValues.All`） | `AuraHost.ApplyStaticEffects`/`ParseControlFlags`（`category` 分支另见下方"控制类别与按类别免疫"一节） |
| `flag` | 无参数 | 仅供 `self.has_aura(...)` 判断，不产生其它效果 |

## 控制类别与按类别免疫（T-N3-6）

[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 8；06 第 3.3 节 2026-09-14
修订段：`control` 光环效果新增可选 `category`（固定六值
`stun|root|silence|disarm|fear|polymorph`，取值集合登记为 `Core.Rules.Common.ControlCategoryValues.All`，
与 `creature.tier_definition.control_immune_categories`——见 `core/carriers/creature/README.md`
判断记录 8——共用同一份取值，避免两个模块各自维护一份可能漂移的字符串数组）。

- **缺省（未分类）**：`AuraHost.ApplyStaticEffects` 不查询新增的
  `IStaticImmunityProvider.IsControlCategoryImmune`，逐位维持改动之前的既有行为——按 `flags` 解析
  出控制标志位后，只经既有 `GetControlImmunity(unitId)` 掩码剔除静态免疫的标志位（同 06 第 3.9 节
  "Boss/精英级免疫标志"既有语义）。旧数据/旧测试不声明 `category` 时零改动。
- **声明了 `category`**：额外查询 `IsControlCategoryImmune(targetId, category)`，命中则本条目对
  目标完全不生效（`ControlFlags.None`，语义同"完全没吃到这个光环的控制效果"，不是"吃到了又立刻
  解除"）——这是在既有按标志位掩码之外叠加的一层判定，不取代它。
- **`IStaticImmunityProvider.IsControlCategoryImmune(Id unitId, string category): bool`**（新增默认
  接口成员，ABI 安全）：默认实现 `GetControlImmunity(unitId) != ControlFlags.None`（"存在任意旧式
  静态控制免疫标志即视为对任意类别都免疫"），对尚未升级到按类别精细声明的实现是"未分类退回旧布尔
  语义"这一约定的自然结果。本仓库内 `Core.Carriers.Creature.CreatureImmunityProvider`（真正按类别
  精细声明）与 `NullStaticImmunityProvider`（一律不免疫）均已显式覆盖，过
  `InterfaceDefaultMemberForwardingTests` 通用门禁。
- **旧布尔 `creature.tier_definition.control_immune` 迁移等价**：`control_immune=true` 时
  `CreatureImmunityProvider.IsControlCategoryImmune` 对任意类别查询都返回 true（不需要在数据里
  逐一枚举六值）；`control_immune=false`（且未声明 `control_immune_categories`）时对任意类别都
  返回 false——与改动之前"该单位完全不受任何控制免疫影响"逐位一致。
- **禁止事项（硬性规则）**：控制递减（06 第 3.9 节"控制递减"，同类控制连续命中效果减半直至免疫）
  不在本次改动范围，钩子仍待后续任务在光环叠加规则策略上落地。

## 效果值契约：`scaling` 列表与 `base_curve_ref`（T-N3-2）

[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 1；06 第 3.2 节 2026-09-14
修订段：`school_damage`/`heal`/`periodic_damage`/`periodic_heal` 四种效果原语统一为
`效果值 = 基础值 + Σ(缩放属性最终值 × 系数)`。

- **`scaling`（权威写法）**：`Array<{stat: Reference(stat.definition), coefficient: Number}>`，允许
  多条，`EffectDispatcher.ApplyDamageOrHeal` 对每一项取 `coefficient × 来源单位当前 stat 最终值`
  求和。
- **`base_curve_ref`（可选）**：`Reference(skill.base_curve)`，存在且能解析出曲线时取代
  `base_value`——按施法者当前等级（`IUnitAccess.GetLevel`）在曲线上取值；来源单位已不存在时按
  等级 1 处理（同 `Core.Rules.Combat.Resolver.ResolveEffectiveLevel` 判断记录），每次结算都重新
  查询，不缓存（同 06 第 3.3 节周期效果"动态计算，不做快照"惯例）。
- **读取优先级（判断记录，硬性规则"禁止删除旧 `scaling_stat` 读取路径"）**：`scaling` 列表非空时
  权威，忽略旧单字段 `scaling_stat`/顶层 `coefficient`；`scaling` 缺失（含尚未经 1→2 迁移的旧
  数据）时回退旧单字段读取路径——两条路径互斥，不叠加，共享同一条"来源单位未注册/已销毁时缩放
  贡献按 0 处理"降级规则（C02 判断记录）。`base_curve_ref` 与 `base_value` 同理互斥：前者存在且
  可解析时后者被忽略。
- **迁移（`schema_version` 1→2）**：`skill.def`/`skill.aura_def` 均已升版本，各自登记
  `TableMigration(1, 2, ...)`——扫描 `effects[]` 中 `kind` 为 `school_damage`/`heal`
  （`skill.def`）或 `periodic_damage`/`periodic_heal`（`skill.aura_def`）的条目，若 `params` 声明了
  `scaling_stat` 且尚未声明 `scaling`，追加 `scaling: [{stat: <scaling_stat 的值>,
  coefficient: <coefficient 缺省 0>}]`；旧字段（`scaling_stat`/`coefficient`）原样保留，不删除；
  已声明 `scaling` 的条目、其余效果原语的 `params` 原样透传。迁移只做结构转换，不做任何校验或
  数值改写；`data/_sample/skill/skill.def.json`/`skill.aura_def.json` 未改动（现有样例不含
  `scaling_stat`，保留在 `schema_version: 1`，迁移对它们是逐字节透传的空操作，天然覆盖"迁移链
  在真实数据上跑一遍"这一验收路径）。
- **周期效果共用同一条结算逻辑**：`periodic_damage`/`periodic_heal` 与非周期 `school_damage`/
  `heal` 共用同一条 `EffectDispatcher.ApplyDamageOrHeal`（`AuraHost.FirePeriodic` 把
  `entry.Params` 原样转发进 `EffectContext.Params`，见 P3-04 判断记录），因此本次改动零改动
  `AuraHost.cs`，只在 `SkillSchemas.PeriodicParamsCase` 补登记 `scaling`/`base_curve_ref` 两个
  字段（与 `DamageOrHealParams` 共用同一份 `FieldSchema` 实例，"登记一次、多处复用"）。

## 校验规则（`SkillValidationRules.cs`）

| 规则 | 检查项名 | 说明 |
|---|---|---|
| `MaxEffectsPerSkillRule` | `max_effects_per_skill` | `skill.def.effects`/`skill.aura_def.effects` 长度均不超过 `SkillOptions.MaxEffectsPerSkill`（04 第 5 节"效果数上限"） |
| `StackCategoryConflictRule` | `stack_category_conflict` | 同一 `stack_category` 静态分组下不得同时存在 `max_stacks>1` 与 `max_stacks==1` 两条记录（04 第 5 节"叠加类别冲突"；判断记录：06 第 3.8 节未给出"互斥叠加语义"的精确定义，本模块取"是否可叠加"这一最小可判定解释，见规则源码注释）。仅是加载期静态内容校验，不改变运行时叠加行为——`AuraHost` 运行时按 `(target, aura_def, sourceKey)` 分槽，不同 `aura_def` 即使类别相同也各自独立叠加、互不影响（见 [ADR-0023](../../../../architecture/adr/0023-光环叠加类别为静态校验分组.md)，判断记录追加条目见下） |
| `CastTimeChannelTimeExclusiveRule` | `cast_time_channel_time_exclusive` | `cast_time`/`channel_time` 不得同时非零 |
| `PassiveSkillNoCastTimeRule` | `passive_skill_no_cast_time` | `kind: passive` 的技能不得声明非零 `cast_time` |
| `ChargesRechargeTimeZeroWarningRule` | `charges_recharge_time_zero` | `charges.recharge_time <= 0` 提醒复核（引擎解读为"即时恢复"），Warning 不阻断 |
| `ChargesMaxAtLeastOneRule` | `charges_max_invalid` | `charges.max` 必须 &gt;= 1 的整数（ADR-0019 收窄版，见下"退役规则"） |
| `AuraProcTriggerDuplicateRule` | `aura_proc_trigger_duplicate` | 同一 `skill.aura_def.effects` 内两条以上 `proc_trigger` 引用同一个 `proc_def`（`proc_ref` 重复）报错，定位到重复出现的那一条 `effects[index].params.proc_ref`（04 第 5 节"光环内触发器重复引用"；消费方反馈 2026-09-10"同一光环多个 Proc 触发器静默忽略问题"）。单光环登记多个不同 `proc_trigger` 本身合法，见上表 `proc_trigger` 行 |

以上规则通过 `IDataRegistry.RegisterValidationRule` 注册；`MaxEffectsPerSkillRule` 的构造参数
（`SkillOptions.MaxEffectsPerSkill`）由宿主在注册时传入，本模块不在 `SkillSchemas`/`SkillValidationRules`
内部读取任何全局单例配置。

### 退役规则（ADR-0019 / F1a）

`EffectKindRegisteredRule`（`effect_entry_not_object`/`effect_kind_missing`/
`effect_kind_not_string`/`unknown_effect_kind`/`unknown_aura_effect_kind`）、`CostEntryShapeRule`
（`cost_entry_not_object`/`cost_power_type_invalid`/`cost_amount_invalid`）两条已整条删除；
`ChargesShapeRule` 的结构部分（`charges_max_missing`/`charges_recharge_time_missing`/
`charges_recharge_time_not_number`）已删除，唯一的业务判断（`charges.max >= 1`，`FieldSchema`
无法表达的数值范围约束）收窄保留为上表的 `ChargesMaxAtLeastOneRule`。三条规则检查的结构性坏
形状全部由 `SkillSchemas.Def`/`AuraDef` 的 `Fields`/`Variants` 递归登记 + `DataRegistry` 的
`required_field`/`field_type`/`variant_discriminator` 覆盖，不再需要平行的手写规则（详见
`SkillValidationRules.cs` 顶部判断记录、`../README.md` 判断记录 39）。
