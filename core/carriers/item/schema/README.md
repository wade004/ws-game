# `item.*` 六张表字段说明

对应 [07_载体层_物品生物物件.md](../../../../architecture/07_载体层_物品生物物件.md) 第 1.1/1.2/1.6
节、[04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 1.1 节 `item.*` 六张表
清单。04 只给出每张表一句话摘要，具体字段是本模块实现期按 07 原文与任务书拍板补录，取舍与判断
记录见各小节。

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
| `stat_roll_ref` | Id | 否 | — | 07 第 1.6 节扩展位：指向未来随机属性生成规则表，本版不展开 |

判断记录：`stats`/`grants`/`weapon_profile`/`requirements` 四个字段的内部结构（子字段）不由
`data_registry` 的 `field_type` 校验展开检查（`Object`/`Array` 类型只做"是对象/是数组"检查），由
`ItemBudgetCurve`/`EquipmentHost` 自行解析并防御性处理缺失/类型不符的子字段（缺失按默认值处理，
不抛异常中断整条记录）。

## `item.slot_definition`

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `id` | Id | 是 | — | `item.slot.<name>` |
| `name_key` | TextKey | 是 | — | 显示名文本键 |
| `sort_weight` | Int | 否 | `0` | 排序权重 |
| `is_weapon` | Bool | 否 | `false` | 实现期补录：该槽位是否为武器槽 |
| `accepts` | IdList | 否 | `[]` | 允许放入本槽位的物品 `slot` 取值列表（跨槽兼容）；未提供时只接受与本槽位 id 完全相同的 `item.template.slot` |
| `is_equipment` | Bool | 否 | `true` | 阶段 3 整理补录：该槽位是否为真正的装备位；`false` 表示分类桶（消耗品/材料一类，仅用于满足 `item.template.slot` 的引用完整性），不可经 `EquipmentHost.Equip` 装备（返回 `SlotMismatch`），也不受 `ItemStackSizeRule` 的"装备类 stack_size 必须为 1"约束 |

## `item.quality_definition`

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `id` | Id | 是 | — | `item.quality.<name>` |
| `name_key` | TextKey | 是 | — | 显示名文本键 |
| `sort_weight` | Int | 否 | `0` | 排序权重 |
| `budget_multiplier` | Number | 否 | `1` | 实现期补录：预算曲线值的品质系数 |

## `item.budget_curve`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `item.budget.<name>` |
| `entries` | Array | 是 | `[{item_level:Int, budget:Number}]`，按 `item_level` 线性插值（`ItemBudgetCurve.Interpolate`），越界夹取到边界项 |

预算校验公式（07 第 1.2 节）：`Σ|stats[].value|`（`pct`/`mult` 按 ×100 折算，`flat` 原值）不得超过
`budget_curve(item_level) × quality.budget_multiplier`。曲线 id 由 `ItemOptions.BudgetCurveId`
指定（构造 `ItemBudgetValidationRule` 时传入）。

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
| （内置）`reference_integrity` | `data_registry` | `item.template.slot`/`quality`/`set_id` 三个 `Reference` 字段的存在性 |

四条 `IValidationRule` 均需调用方显式 `registry.RegisterValidationRule(...)` 才会生效，本模块不
自动注册（同 `progression`/`stat_block`/`power_set` 惯例）。
