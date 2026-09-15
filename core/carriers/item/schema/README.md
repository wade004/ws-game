# `item.*` 九张表字段说明

对应 [07_载体层_物品生物物件.md](../../../../architecture/07_载体层_物品生物物件.md) 第 1.1/1.2/1.6
节、[04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 1.1 节 `item.*` 表清单。
04 只给出每张表一句话摘要，具体字段是本模块实现期按 07 原文与任务书拍板补录，取舍与判断记录见
各小节。

分阶段落地计划 T-N2-1（ADR-0032）起，本模块从六张表扩为九张：新增 `item.armor_curve`/
`item.weapon_dps_curve`/`item.req_level_curve` 三条曲线表（见对应小节），`item.slot_definition`/
`item.quality_definition`/`item.template` 三张既有表各自补充若干新字段（见各自小节标注
"T-N2-1 起"的行）。本任务只登记 schema、注册表与曲线单调有限校验，消耗公式/护甲武器秒伤求值/
需求等级反推/授予预算校验等运行时消费实现留给后续任务（T-N2-4/6/8/9），各字段说明已逐一标注。

## `item.template`

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `id` | Id | 是 | — | `item.<name>` |
| `slot` | Reference→`item.slot_definition` | 是 | — | 槽位枚举 |
| `quality` | Reference→`item.quality_definition` | 是 | — | 品质分档 |
| `item_level` | Int | 是 | — | 物品等级 |
| `stats` | Array | 否 | `[]` | `[{stat:Id, op:flat\|pct\|mult, value:Number}]`，装备后提供的属性修正 |
| `grants` | Object | 否 | `{}` | `{skills:List<Id>, auras:List<Id>}` |
| `affixes` | IdList | 否 | `[]` | 指向 `item.affix`，扩展位，本版不实现效果 |
| `set_id` | Reference→`item.set` | 否 | — | 所属套装 |
| `weapon_profile` | Object | 视 slot | — | `{damage_min, damage_max, speed, weapon_school}`；当且仅当 `slot_definition.is_weapon` 为 true 时必须存在（`ItemWeaponProfileRule`） |
| `display_ref` | Id | 是 | — | 指向 `display.map`；本模块不校验其引用完整性（不引用 `display_info` 模块类型） |
| `stack_size` | Int | 是 | — | 最大堆叠数量；装备类（`slot` 指向 `is_equipment` 不为 false 的 `slot_definition`）必须为 1（`ItemStackSizeRule`，阶段 3 整理改判定依据，见下） |
| `name_key` | TextKey | 是 | — | 显示名文本键（04 未展开，实现期补录） |
| `requirements` | Object | 否 | — | `{level:Int?}`，可空；实现期补录，供 `EquipmentHost.Equip` 的 `RequirementNotMet` 判定 |
| `enchant_slot` | Id | 否 | — | 07 第 1.6 节扩展位：指向未来 `item.enchant`，本版不展开 |
| `socket_count` | Int | 否 | `0` | 07 第 1.6 节扩展位：宝石镶嵌槽数 |
| `socket_ids` | IdList | 否 | `[]` | 07 第 1.6 节扩展位：宝石镶嵌结果 |
| `has_durability` | Bool | 否 | `false` | 07 第 1.6 节扩展位：是否具备耐久字段位 |
| `bind_type` | Enum(`none\|on_pickup\|on_equip`) | 否 | — | 07 第 1.6 节扩展位：绑定方式，单机默认不产生实际限制 |
| `stat_roll_ref` | Id | 否 | — | T-N2-1 起改为**兼容位**：随机属性由 `item.affix`（budget_share/stat_mix/quality_pool/weight，T-N2-2）取代，本字段不再是待展开的扩展位，只作历史数据兼容读取位（07 第 1.6 节修订段） |
| `value_override` | Number | 否 | — | T-N2-1 新增（ADR-0032/ADR-0034）：基准价值覆盖，未填按 `econ.value_curve` 公式（未落地），填了偏离公式超带宽报警告；范围 `>= 0`；消费实现随 ADR-0034 落地任务接入 |
| `budget_note` | String | 否 | — | T-N2-1 新增（ADR-0032 决策 6）：超模说明原文，橙装独特技能凭此免预算/授予校验；消费与接线随 T-N2-4/T-N2-6 落地 |

### ADR-0019 F1c 子结构登记

| 字段 | 子字段 | 类型 | 必填 | 默认 | 引用/说明 |
|---|---|---|---|---|---|
| `stats[]` | `stat` | Reference→`stat.definition` | 是 | — | `stat.definition` 属 L1，本模块（L3）依赖方向合法 |
| | `op` | Enum(`flat\|pct\|mult`) | 否 | `flat` | 以 `EquipmentHost.ParseOp` 为准，非 07 原文 `flat\|pct` 两种 |
| | `value` | Number | 否 | `0` | |
| `grants` | `skills[]` | Reference→`skill.def` | — | `[]` | 任务书额外要求：L3 引用 L2 程序集合法 |
| | `auras[]` | Reference→`skill.aura_def` | — | `[]` | 重复引用见 `ItemGrantsAurasDuplicateRule`（Warning） |
| `weapon_profile` | `damage_min`/`damage_max`/`speed` | Number | 否 | `0` | |
| | `weapon_school` | Id | 否 | — | 无独立跨层可引用表，按 Id 登记 |
| `requirements` | `level` | Int | 否 | 不限等级 | |

`item.set.bonuses[]`：`{count:Int(缺省0), aura_ref:Reference(skill.aura_def)}`（`aura_ref` 同 `grants.auras`
判断记录，L3 引用 L2 合法）。`item.budget_curve.entries[]`：04 第 3.6 节通用断点表 `{x:Int, y:Number}`（均必填，
横轴语义为物品等级；T-N0-4 起，v1 的 `{item_level, budget}` 经 1→2 迁移改名；`ItemBudgetCurve.ParseCurve` 缺失即抛异常）。

判断记录（`item.affix.effects` 不登记子结构）：任务书额外要求 1 提到"若要复用技能域的
`EffectsItemSchema`"，但 ADR-0019 通用规则 1"以运行时解析代码为唯一依据……找不到运行时读取的字段
不登记"——全仓库搜索 `item.affix` 只有 `ItemSchemas.cs` 自身的 schema 声明，没有任何运行时解析代码
读取过 `effects`（07 第 1.6 节"附魔……单机初版不做……留空位待需要时再设计"，属纯占位扩展位）。本轮
不登记，待该字段有实际运行时解析实现后再补充结构（含变体或 `EffectsItemSchema` 复用）。

## `item.slot_definition`

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `id` | Id | 是 | — | `item.slot.<name>` |
| `name_key` | TextKey | 是 | — | 显示名文本键 |
| `sort_weight` | Int | 否 | `0` | 排序权重 |
| `is_weapon` | Bool | 否 | `false` | 实现期补录：该槽位是否为武器槽 |
| `accepts` | IdList | 否 | `[]` | 允许放入本槽位的物品 `slot` 取值列表（跨槽兼容）；未提供时只接受与本槽位 id 完全相同的 `item.template.slot` |
| `is_equipment` | Bool | 否 | `true` | 阶段 3 整理补录：该槽位是否为真正的装备位；`false` 表示分类桶（消耗品/材料一类，仅用于满足 `item.template.slot` 的引用完整性），不可经 `EquipmentHost.Equip` 装备（返回 `SlotMismatch`），也不受 `ItemStackSizeRule` 的"装备类 stack_size 必须为 1"约束 |
| `budget_coefficient` | Number | 否 | `1` | T-N2-1 新增（ADR-0032 决策 1/3）：槽位预算系数，预算上限 = 预算曲线(item_level) × 品质预算倍率 × 本系数；范围 `> 0`；消费实现随 T-N2-4 落地 |
| `price_coefficient` | Number | 否 | `1` | T-N2-1 新增（ADR-0032 决策 1；ADR-0034）：槽位价格系数，买价 = 基准价值 × 品质价格倍率 × 本系数；范围 `> 0`；消费实现随 ADR-0034 落地任务接入 |

## `item.quality_definition`

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `id` | Id | 是 | — | `item.quality.<name>` |
| `name_key` | TextKey | 是 | — | 显示名文本键 |
| `sort_weight` | Int | 否 | `0` | 排序权重 |
| `budget_multiplier` | Number | 否 | `1` | 实现期补录：预算曲线值的品质系数；大小顺序须与 `sort_weight` 一致（`ItemQualityMultiplierOrderRule`，T-N2-1） |
| `affix_count` | Int | 否 | 不限 | T-N2-1 新增（ADR-0032 决策 2，明文可选）：该品质副属性条目数上限，不填不限；范围 `>= 0`；消费实现随词缀转正（T-N2-2）落地 |
| `grant_budget_share` | Number | 否 | `0`（判断记录，见类型注释） | T-N2-1 新增（ADR-0032 决策 2/6）：授予技能/光环预算 = 该件预算 × 本占比（警告级"授予价值超特效占比"）；范围 `[0,1]`；消费实现随 T-N2-6 落地 |
| `price_multiplier` | Number | 否 | `1`（判断记录，见类型注释） | T-N2-1 新增（ADR-0032 决策 2；ADR-0034）：品质价格系数；大小顺序须与 `sort_weight` 一致（`ItemQualityMultiplierOrderRule`）；范围 `> 0`；消费实现随 ADR-0034 落地任务接入 |

判断记录（`grant_budget_share`/`price_multiplier` 缺省值）：ADR-0032 决策 2 原文只对 `affix_count`
明文标注"可选"，另两个新列的必填/可选未明确规定。本任务按既有 `budget_multiplier`（同为品质系数类
字段，`required: false` + 缺省 1）的登记口径类推：`price_multiplier` 同为"倍率"，缺省 1；
`grant_budget_share` 是"占比"，缺省 0（未登记视为不授予/特效预算为零，与"紫以上才可带 grants"门槛
语义一致）。属实现期按同类字段既有口径类推的判断，非契约条文明文规定——**上报待设计层确认**。

## `item.budget_curve`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `item.budget.<name>` |
| `entries` | Array | 是 | 通用断点表 `[{x:Int, y:Number}]`（`x` = 物品等级，`y` = 预算上限；04 第 3.6 节 `CurveSchema.BreakpointsField`，横轴 `ItemLevel`），按 `x` 线性插值（`ItemBudgetCurve.Interpolate` → `PiecewiseCurve.Evaluate`），越界夹取到端点；`curve_monotonic_finite` 规则要求非空、有限、`x` 无重复、`y` 不递减。schema 版本 2（T-N0-4）：v1 的 `{item_level, budget}` 由 1→2 迁移环节改名，旧数据文件无需手改即可加载 |

预算校验公式（07 第 1.2 节）：`Σ|stats[].value|`（`pct`/`mult` 按 ×100 折算，`flat` 原值）不得超过
`budget_curve(item_level) × quality.budget_multiplier`。曲线 id 由 `ItemOptions.BudgetCurveId`
指定（构造 `ItemBudgetValidationRule` 时传入）。**判断记录**：T-N2-1 只登记三条新曲线表与校验，
本条公式尚未接入槽位系数/权重表/指数 k（ADR-0032 决策 3 的消耗侧补齐），随 T-N2-4 落地。

## `item.armor_curve`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `item.armor.<name>` |
| `entries` | Array | 是 | 通用断点表 `[{x:Int, y:Number}]`（`x` = 物品等级，`y` = 护甲值 `>= 0`；ADR-0032 决策 4），按 `x` 线性插值、越界夹取到端点；`curve_monotonic_finite` 规则自动覆盖。新表，schema 版本 1，无需迁移 |

护甲值 = `item.armor_curve(item_level) × 槽位系数`，仅护甲位使用，不占预算；消费实现（写入
`StatHost` 非 `stats` 来源）随 T-N2-4/E7 落地，本任务只登记 schema 与注册。

## `item.weapon_dps_curve`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `item.weapon_dps.<name>` |
| `entries` | Array | 是 | 通用断点表 `[{x:Int, y:Number}]`（`x` = 物品等级，`y` = 武器基准秒伤 `> 0`；ADR-0032 决策 4），按 `x` 线性插值、越界夹取到端点；`curve_monotonic_finite` 规则自动覆盖。新表，schema 版本 1，无需迁移 |

武器秒伤 = `item.weapon_dps_curve(item_level) × 品质预算倍率 × 武器槽位系数`；伤害范围 = 武器秒伤 ×
`weapon_profile.speed` × (1 ± 浮动)。消费实现（替换 `EquipmentHost.GetWeaponBaseDamage` 当前的
`(min+max)/2`）随 T-N2-4/E8 落地，本任务只登记 schema 与注册。

## `item.req_level_curve`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `item.req_level.<name>` |
| `entries` | Array | 是 | 通用断点表 `[{x:Int, y:Number}]`（`x` = 物品等级，`y` = 需求等级 `>= 0`；ADR-0032 决策 5），按 `x` 线性插值、越界夹取到端点；`curve_monotonic_finite` 规则自动覆盖。新表，schema 版本 1，无需迁移 |

`item.template.requirements.level` 未填时按本曲线由 `item_level` 反推，填了以手填为准。消费实现
（`EquipmentHost.TryGetRequiredLevel` 缺省分支）随 T-N2-9/E9 落地，本任务只登记 schema 与注册。

## `item.set`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `item.set.<name>` |
| `name_key` | TextKey | 是 | 显示名文本键 |
| `pieces` | IdList | 是 | 所属物品模板 id 列表（指向 `item.template`）；与各成员的 `set_id` 反向一致，`ItemSetMembershipRule` 校验 |
| `bonuses` | Array | 是 | `[{count:Int, aura_ref:Id}]`，件数门槛到套装光环的映射 |

## `item.affix`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `item.affix.<name>` |
| `name_key` | TextKey | 是 | 显示名文本键 |
| `effects` | Array | 否 | 扩展位占位字段，本版不解析、不实现（见 07 第 1.5/1.6 节"附魔……单机初版不做……留空位待需要时再设计"） |

## 校验规则清单

| 规则 | 检查项名 | 说明 |
|---|---|---|
| `item_budget_exceeded` | `ItemBudgetValidationRule` | 预算超标（见上）|
| `item_weapon_profile_missing`/`item_weapon_profile_unexpected` | `ItemWeaponProfileRule` | `weapon_profile` 当且仅当 `slot_definition.is_weapon` 为真时存在 |
| `item_set_membership_mismatch` | `ItemSetMembershipRule` | `set_id` 的 `pieces` 包含该物品 |
| `item_stack_size_min`/`item_stack_size_equipment_not_one` | `ItemStackSizeRule` | `stack_size >= 1`；装备类（`slot` 指向 `is_equipment` 不为 false 的 `slot_definition`）`stack_size == 1` |
| `item_grants_auras_duplicate` | `ItemGrantsAurasDuplicateRule`（Warning） | `grants.auras` 同一物品内重复引用同一个 `aura_def` |
| `item_quality_multiplier_order` | `ItemQualityMultiplierOrderRule`（T-N2-1，ADR-0032 决策 2） | `item.quality_definition` 的 `budget_multiplier`/`price_multiplier` 大小顺序须与 `sort_weight` 一致（按 `sort_weight` 升序分组，组内并列不比较，跨组不递减）；检查名按任务书"04 §5 或 item_quality_\* 前缀"取值，**04 第 5 节该行未给出具体检查名，上报待设计层确认** |
| （内置）`reference_integrity` | `data_registry` | `item.template.slot`/`quality`/`set_id` 三个 `Reference` 字段的存在性 |
| （T-N0-3 通用规则）`curve_monotonic_finite` | `CurveMonotonicFiniteRule` | 自动覆盖 `item.budget_curve`/`item.armor_curve`/`item.weapon_dps_curve`/`item.req_level_curve` 四张断点表曲线的 `entries` |

六条本模块 `IValidationRule` 均需调用方显式 `registry.RegisterValidationRule(...)` 才会生效，本模块不
自动注册（同 `progression`/`stat_block`/`power_set` 惯例）；`curve_monotonic_finite` 是 T-N0-3 的
通用规则，随 `PresentationSchemaCatalog.RegisterAll` 全局注册一次即覆盖全部登记为曲线形态的字段，
不需要本模块重复注册。
