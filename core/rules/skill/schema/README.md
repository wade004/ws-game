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
| `action_cost` | Number | 否 | 离散模式行动点消耗；本项目未启用离散模式（ADR-0013），本模块只解析不使用 |
| `respects_gcd` | Bool | 是 | 是否受公共冷却影响 |
| `target_shape_ref` | Id | 是 | 指向 `target.chain_def`（本模块施法管线步骤 6 按此语义直接传给 `ITargetHost.Resolve`，见 README"判断记录"第 1 条） |
| `effects` | Array | 是 | `[{kind: String(snake_case), params: Object}, ...]`，`kind` 取值见 `EffectKindNames` |
| `interrupt_flags` | Array\<String\> | 否 | `movement`\|`damage_taken`\|`control` 的子集 |

## `skill.aura_def`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `skill.aura_def.<name>` |
| `duration` | Number | 否 | 空表示永久直到被移除 |
| `max_stacks` | Int | 否 | 缺省 1 |
| `stack_category` | Id | 否 | 叠加冲突检测用类别（见校验规则"叠加类别冲突"） |
| `dispel_type` | Id | 否 | 供 `dispel` 效果按类别筛选 |
| `effects` | Array | 是 | `[{kind: String, params: Object}, ...]`，`kind` 取值见 `AuraEffectKindNames`（10 项） |

`effects[].params` 按 `kind` 解释（见 06 第 3.3 节）：

| `kind` | `params` 形状 |
|---|---|
| `mod_stat` | `{stat: Id, op: "flat"\|"pct"\|"mult", value: Number}` |
| `periodic_damage`/`periodic_heal` | `{interval: Number, base_value: Number, coefficient: Number, school: Id}` |
| `absorb` | `{amount: Number, school: Id?}` |
| `immunity` | `{schools: [Id], effect_kinds: [String]}` |
| `proc_trigger` | `{proc_ref: Id}` |
| `spell_mod` | `{spell_mod_ref: Id}` |
| `override_skill` | `{from: Id, to: Id}` |
| `control` | `{flags: ["no_move"\|"no_cast"\|"no_attack"\|"no_interact", ...]}` |
| `flag` | 无参 |

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
| `affects` | Object | 否 | `{schools: [Id], tags: [Id], skill_ids: [Id]}` |

## `skill.book`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `skill.book.<name>` |
| `entries` | Array | 是 | `[{level: Int, skill_id: Id}, ...]` |

## 校验规则（`SkillValidationRules.cs`）

| 规则 | 检查项名 | 说明 |
|---|---|---|
| `MaxEffectsPerSkillRule` | `max_effects_per_skill` | `skill.def.effects`/`skill.aura_def.effects` 长度均不超过 `SkillOptions.MaxEffectsPerSkill`（04 第 5 节"效果数上限"） |
| `StackCategoryConflictRule` | `stack_category_conflict` | 同一 `stack_category` 下不得同时存在 `max_stacks>1` 与 `max_stacks==1` 两条记录（04 第 5 节"叠加类别冲突"；判断记录：06 第 3.8 节未给出"互斥叠加语义"的精确定义，本模块取"是否可叠加"这一最小可判定解释，见规则源码注释） |
| `EffectKindRegisteredRule` | `unknown_effect_kind`/`unknown_aura_effect_kind` | `effects[].kind` 必须是已登记的 `EffectKind`/`AuraEffectKind`（ADR-0010"新增原语走审批"的落地） |
| `CastTimeChannelTimeExclusiveRule` | `cast_time_channel_time_exclusive` | `cast_time`/`channel_time` 不得同时非零 |
| `PassiveSkillNoCastTimeRule` | `passive_skill_no_cast_time` | `kind: passive` 的技能不得声明非零 `cast_time` |

以上规则通过 `IDataRegistry.RegisterValidationRule` 注册；`MaxEffectsPerSkillRule` 的构造参数
（`SkillOptions.MaxEffectsPerSkill`）由宿主在注册时传入，本模块不在 `SkillSchemas`/`SkillValidationRules`
内部读取任何全局单例配置。
