# `skill.*` 数据表字段

对应 [`SkillSchemas.cs`](SkillSchemas.cs) 的 `TableSchema` 声明；字段语义详见
[06_规则层_属性技能战斗AI.md](../../../../architecture/06_规则层_属性技能战斗AI.md) 第 3.1/3.3/3.4/3.5
节、[04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 1.1 节表清单。宿主在构造
`IDataRegistry` 后需要依次 `RegisterSchema(SkillSchemas.Def)` 等注册本文件列出的五张表才能加载对应数据
（本模块不自动注册，与 `power_set.PowerSchemas`/`stat_block.StatSchemas` 同一惯例）。

## `skill.def`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `skill.<name>` |
| `school` | Id | 是 | 学派 |
| `kind` | Enum(`active`\|`passive`) | 是 | 主动技能走施法管线；被动技能不可主动施放 |
| `range` | Number | 是 | 射程，`0` 表示无限制/作用于自身，不做范围检查 |
| `tags` | List\<Id\> | 否 | 供 `SpellMod.affects` 与 Expr 按标签过滤 |
| `cast_time` | Number | 是 | 读条时间，`0` 表示瞬发 |
| `channel_time` | Number | 否 | 引导时长，与 `cast_time` 互斥（不得同时非零，见校验规则） |
| `cost` | Array | 否 | `[{power_type: Id, amount: Number}, ...]` |
| `cooldown_category` | Id | 否 | 冷却分类引用，同分类技能共享冷却 |
| `cooldown_duration` | Number | 否 | 冷却时长，缺省 0 |
| `charges` | Object | 否 | `{max: Int, recharge_time: Number}`；存在时冷却判定改走充能而非 `cooldown_duration` |
| `action_cost` | Number | 否 | 离散模式行动点消耗；`SkillDefCache` 解析为 `SkillDef.ActionCost`（缺省 0），`CastPipeline` 经注入的 `SkillOptions.TryConsumeActionPoints`/`IsDiscreteStep` 在离散步内扣减（连续模式或未装配注入点时忽略本字段，见 W1 收边补齐、A3 审计 #5） |
| `respects_gcd` | Bool | 是 | 是否受公共冷却影响 |
| `target_shape_ref` | Id | 是 | 指向 `target.chain_def`（本模块施法管线步骤 6 按此语义直接传给 `ITargetHost.Resolve`，见 README"判断记录"第 1 条） |
| `effects` | Array | 是 | `[{kind: String(snake_case), params: Object}, ...]`，`kind` 取值见 `EffectKindNames`；ADR-0019 起元素结构登记为 `SkillSchemas.EffectsItemSchema`（按 `kind` 分派的 `Variants`，19 种原语各自的 `params` 结构见下"效果原语参数表"），加载期递归校验 |
| `interrupt_flags` | Array\<String\> | 否 | `movement`\|`damage_taken`\|`control` 的子集，元素登记为 `Enum(InterruptFlagValues)` |

`cost`/`charges` 同样在 ADR-0019 起登记了子结构（`cost` 元素 `{power_type: Id 必填, amount:
Number 必填}`；`charges` `{max: Int 必填, recharge_time: Number 必填}`），加载期递归校验其内部
子字段必填与类型，不再需要额外的手写规则（见下"退役规则"）。

## `skill.aura_def`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `skill.aura_def.<name>` |
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

## 效果原语参数表（ADR-0019 / F1a，`SkillSchemas.EffectsItemSchema`）

`skill.def.effects`（技能效果列表）与 `projectile.params.on_hit_effects`（弹道命中后效果列表）
共用同一份元素结构：`{kind: String, params: Object}`，按 `kind` 分派到下表某一行的参数清单
（对照运行时解析代码：`EffectDispatcher.cs`、`ProjectileHost.cs`、
`Core.Carriers.{Summon,Item,Gobj}` 三个 `IEffectExtension` 实现；来源列标注对应实现位置）。
`params` 是否必填：19 种原语中仅 `open_lock`（无参数）`params` 可省略，其余均要求 `params`
本身存在（哪怕是空对象），内部子字段各自的必填见下表"必填"列。

| `kind` | 参数（名/类型/必填/引用目标） | 运行时代码位置 |
|---|---|---|
| `school_damage` | `base_value`:Number/否、`coefficient`:Number/否、`school`:Id/否（缺省取 `skill.def.school`）、`scaling_stat`:Reference(`stat.definition`)/否 | `EffectDispatcher.ApplyDamageOrHeal` |
| `heal` | 同 `school_damage`（同一组参数、同一处实现分支） | `EffectDispatcher.ApplyDamageOrHeal` |
| `weapon_damage_pct` | `pct`:Number/否（缺省 0；RC-11：也可退回 `params.base_value`，本登记只列权威参数名 `pct`） | `EffectDispatcher.ApplyDamageOrHeal`（`WeaponDamagePct` 分支） |
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
| `script` | `hook_id`:Id/否（分层边界：`found.hook` 当前无实现级 schema 登记，无法 Reference） | 无落地实现（扩展点） |

**分层边界（04 §5.1 口径）**：`create_item.item_template`→`item.template`、
`summon.creature_template`→`creature.template`、`projectile.params.display_ref`→`display.map`
三处概念上想引用 L3/L5 层的表，但 `skill` 模块属 L2、依赖方向单向不可"向上"引用，均退回 `Id`
（只做格式校验，不做跨表存在性检查）。`script.hook_id` 概念上指向 L0 的 `found.hook`，方向合法，
但该表当前无实现级 schema 登记（Reference 到未加载表恒判定为引用失效），暂退回 `Id`，待
`found.hook` 补齐登记后再升级。

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
| `periodic_damage` | `interval`:Number/是（无缺省，`interval<=0` 时周期永远不触发）、`base_value`:Number/否、`coefficient`:Number/否、`school`:Id/是（判断记录：`FirePeriodic` 读取时 fallback 是零值 Id，不同于顶层效果的"缺省取 skill.def.school"，缺失会产生无效 Id，登记层拦下） | `AuraHost.Update`/`FirePeriodic` |
| `periodic_heal` | 同 `periodic_damage` | `AuraHost.Update`/`FirePeriodic` |
| `absorb` | `amount`:Number/是、`school`:Id/否（缺省吸收全部学派） | `AuraHost.ApplyStaticEffects`/`AbsorbPerStack` |
| `immunity` | `schools`:IdList/否、`effect_kinds`:Array(Item=Enum，19 种 `EffectKind` 全集)/否 | `AuraHost.ApplyStaticEffects` |
| `proc_trigger` | `proc_ref`:Reference(`skill.proc_def`)/是 | `AuraHost.ApplyStaticEffects`/`ProcHost.Attach`（同一光环可登记多个本类型条目，各自独立挂载/结算/注销，见 `AuraInstanceState.ProcDefRefs` 判断记录；同一光环内重复引用同一个 `proc_ref` 由下表 `AuraProcTriggerDuplicateRule` 在加载期拒绝） |
| `spell_mod` | `spell_mod_ref`:Reference(`skill.spell_mod_def`)/是 | `AuraHost.ApplyStaticEffects`/`SpellModResolver` |
| `override_skill` | `from`:Reference(`skill.def`)/是、`to`:Reference(`skill.def`)/是 | `AuraHost.ApplyStaticEffects`/`ResolveSkillOverride` |
| `control` | `flags`:Array(Item=Enum(no_move\|no_cast\|no_attack\|no_interact))/否 | `AuraHost.ApplyStaticEffects`/`ParseControlFlags` |
| `flag` | 无参数 | 仅供 `self.has_aura(...)` 判断，不产生其它效果 |

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
