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

T-N2-2（ADR-0032 决策 7）起，`item.affix` 由留位（只有 `id`/`name_key`/`effects` 三个占位字段）转正为
预算份额包正式表：新增 `budget_share`/`stat_mix`/`quality_pool`/`weight` 四个必填字段与可选 `grants`；
`effects` 改为已废弃占位字段，保留一个版本周期只作读取兼容；`item.template.affixes` 语义随之改写
（见 `item.template` 小节该行）。本任务同时落地一条阻断校验（`item_affix_stat_mix_ratio_sum`，单条
`stat_mix[].ratio` 之和不超过一），"模板加词缀最大份额超预算"等需要消耗公式的校验仍留给
T-N2-3/T-N2-4。

## `item.template`

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `id` | Id | 是 | — | `item.<name>` |
| `slot` | Reference→`item.slot_definition` | 是 | — | 槽位枚举 |
| `quality` | Reference→`item.quality_definition` | 是 | — | 品质分档 |
| `item_level` | Int | 是 | — | 物品等级 |
| `stats` | Array | 否 | `[]` | `[{stat:Id, op:flat\|pct\|mult, value:Number}]`，装备后提供的属性修正 |
| `grants` | Object | 否 | `{}` | `{skills:List<Id>, auras:List<Id>}` |
| `affixes` | IdList | 否 | `[]` | T-N2-2 起语义改写（ADR-0032 决策 7；落地改动点清单 E2"`item.template.affixes` 语义改为可抽词缀池约束"）：该模板掉落时可抽取的词缀候选白名单，与掉落三次掷骰第三骰（品质骰匹配 `item.affix.quality_pool` 后）取交集；缺省 `[]` 视为不收窄（该品质池下全部词缀均可抽），消费实现随 T-N2-8 落地——设计层裁定（2026-09-15）：采纳，见判断记录 |
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
| `stat_mix[]`（`item.affix`） | `stat` | Reference→`stat.definition` | 是 | — | T-N2-2 新增，同 `stats[].stat` 判断记录，L3 依赖 L1 合法 |
| | `ratio` | Number | 是 | — | 范围 `(0,1]`；跨元素"之和不超过一"由 `ItemAffixStatMixRatioSumRule` 校验，登记表达不了 |
| `grants`（`item.affix`） | `skills[]`/`auras[]` | Reference→`skill.def`/`skill.aura_def` | — | `[]` | T-N2-2 新增，复用 `item.template.grants` 同一 `GrantsSchema` 静态字段实例 |

`item.set.bonuses[]`：`{count:Int(缺省0), aura_ref:Reference(skill.aura_def)}`（`aura_ref` 同 `grants.auras`
判断记录，L3 引用 L2 合法）。`item.budget_curve.entries[]`：04 第 3.6 节通用断点表 `{x:Int, y:Number}`（均必填，
横轴语义为物品等级；T-N0-4 起，v1 的 `{item_level, budget}` 经 1→2 迁移改名；`ItemBudgetCurve.ParseCurve` 缺失即抛异常）。

判断记录（`item.affix.effects` 不登记子结构，T-N2-2 复核未变）：任务书额外要求 1 提到"若要复用技能域的
`EffectsItemSchema`"，但 ADR-0019 通用规则 1"以运行时解析代码为唯一依据……找不到运行时读取的字段
不登记"——全仓库搜索 `item.affix` 只有 `ItemSchemas.cs` 自身的 schema 声明，没有任何运行时解析代码
读取过 `effects`。T-N2-2 起该字段已明确标记废弃（由 `stat_mix`/`grants` 取代），维持不登记子结构的
结论，`toolchain/schema_audit_allowlist.json` 现有条目原样保留（reason 文案未变，字段仍无运行时解析
代码，只是从"留位"变成"废弃留位"，不影响该判断记录成立与否）。

## `item.slot_definition`

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `id` | Id | 是 | — | `item.slot.<name>` |
| `name_key` | TextKey | 是 | — | 显示名文本键 |
| `sort_weight` | Int | 否 | `0` | 排序权重 |
| `is_weapon` | Bool | 否 | `false` | 实现期补录：该槽位是否为武器槽 |
| `accepts` | IdList | 否 | `[]` | 允许放入本槽位的物品 `slot` 取值列表（跨槽兼容）；未提供时只接受与本槽位 id 完全相同的 `item.template.slot` |
| `is_equipment` | Bool | 否 | `true` | 阶段 3 整理补录：该槽位是否为真正的装备位；`false` 表示分类桶（消耗品/材料一类，仅用于满足 `item.template.slot` 的引用完整性），不可经 `EquipmentHost.Equip` 装备（返回 `SlotMismatch`），也不受 `ItemStackSizeRule` 的"装备类 stack_size 必须为 1"约束 |
| `budget_coefficient` | Number | 否 | `1` | T-N2-1 新增（ADR-0032 决策 1/3）：槽位预算系数，预算上限 = 预算曲线(item_level) × 品质预算倍率 × 本系数；范围 `> 0`；消费实现见下 `item.budget_curve`（T-N2-3，`ItemBudgetValidationRule` 已接入）；武器槽位系数同一个字段（T-N2-6，`EquipmentHost.GetWeaponDps`：曲线(item_level) × 品质预算倍率 × 本系数，`is_weapon` 槽位读取） |
| `price_coefficient` | Number | 否 | `1` | T-N2-1 新增（ADR-0032 决策 1；ADR-0034）：槽位价格系数，买价 = 基准价值 × 品质价格倍率 × 本系数；范围 `> 0`；消费实现随 ADR-0034 落地任务接入 |
| `has_armor` | Bool | 否 | `false` | T-N2-6 新增（设计层裁定，取代 T-N2-5 的"非武器位且真正装备位"推断）：该槽位的装备是否提供护甲值（ADR-0032 决策 4 的护甲位）；`EquipmentHost.IsArmorSlot` 只看本字段，不再从 `is_weapon`/`is_equipment` 推断——游戏层需要给每个防具位（头/胸/腿/手/脚等）显式登记 `true`，戒指/饰品/武器位缺省 `false` 即不写护甲 |

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
语义一致）。属实现期按同类字段既有口径类推的判断，非契约条文明文规定——**设计层裁定（2026-09-15）：采纳**。

## `item.budget_curve`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `item.budget.<name>` |
| `entries` | Array | 是 | 通用断点表 `[{x:Int, y:Number}]`（`x` = 物品等级，`y` = 预算上限；04 第 3.6 节 `CurveSchema.BreakpointsField`，横轴 `ItemLevel`），按 `x` 线性插值（`ItemBudgetCurve.Interpolate` → `PiecewiseCurve.Evaluate`），越界夹取到端点；`curve_monotonic_finite` 规则要求非空、有限、`x` 无重复、`y` 不递减。schema 版本 2（T-N0-4）：v1 的 `{item_level, budget}` 由 1→2 迁移环节改名，旧数据文件无需手改即可加载 |
| `exponent` | Number | 否 | T-N2-3 新增（ADR-0032 决策 3）：消耗公式 `(Σ(值×权重)^k)^(1/k)` 的指数 `k`，缺省 `1.5`（`ItemBudgetCurve.DefaultExponent`）；范围 `> 0`。**判断记录（登记位置）**：ADR-0032/07/数值总纲三处原文只给"k 默认 1.5，数据配置"，未指明具体字段位置——设计层裁定（2026-09-15）：采纳，登记在预算曲线记录自身（同一游戏若有多条预算曲线服务不同物品档位，可各自配置不同 k） |

预算上限公式（07 第 1.2 节修订段；ADR-0032 决策 3）：
`预算曲线(item_level) × quality.budget_multiplier × slot_definition.budget_coefficient`。

消耗公式（T-N2-3 起，取代旧式 `Σ|stats[].value|`（`pct`/`mult` ×100 折算）——旧式仍以
`ItemBudgetCurve.SumConsumed` 保留、不再被本规则调用，见该方法判断记录）：
`(Σ(属性值_i × 权重_i)^k)^(1/k)`，权重来自 `stat.weight`（没有对应记录时按缺省权重 1 回退，非 0，
见 `ItemBudgetCurve.BuildStatBudgetInfo` 判断记录；不展开 `class_overrides`，校验期没有职业上下文）；
`category == percent` 的属性，`op=pct`/`mult` 的值先经该属性引用的 `stat.rating_conversion` 换算曲线
反函数折回点数（`op=flat` 的值本就是点数，恒等操作；换算所需的"单位等级"取该模板的 `item_level`）；
非 `percent` 属性沿用既有 `pct`/`mult` ×100 折算。曲线 id 由 `ItemOptions.BudgetCurveId` 指定（构造
`ItemBudgetValidationRule` 时传入）；公式的详细 op/category 组合判断记录、"折回点数"方向的推导依据
见 `ItemBudgetCurve.ComputeConsumed` 类型注释——**设计层裁定（2026-09-15）：采纳**（契约原文未展开到这一操作化
层面）。

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
| `variance` | Number | 否 | T-N2-6 新增（ADR-0032 决策 4"伤害范围 = 武器秒伤 × 初始攻速 × (1 ± 浮动)"；拍板 6"一拍常数与浮动比例为数据项"）：伤害范围浮动比例，缺省 `0.1`。**判断记录（登记位置——设计层裁定（2026-09-15）：采纳）**：落地改动点清单第 10 节第 6 条候选"一拍常数与浮动比例放 `skill.budget_rule` 与 `item.weapon_dps_curve` 旁"——一拍常数登记在 `skill.budget_rule`（最小骨架随 T-N3-3 提前创建，完整字段留给 T-N3-9，见 `core/rules/skill/schema/README.md`"`skill.budget_rule`"一节），浮动比例按同一候选落在本表；本任务只登记字段，尚无消费者（"手填偏离秒伤曲线"警告只比较均值，不展开到 `(1±浮动)` 的上下界，见 `ItemWeaponDamageDeviatesDpsCurveRule` 判断记录）。范围 `[0,1)` |

武器秒伤 = `item.weapon_dps_curve(item_level) × 品质预算倍率 × 武器槽位系数`（T-N2-6，
`EquipmentHost.GetWeaponDps` 新方法；武器槽位系数即 `item.slot_definition.budget_coefficient`）；
`EquipmentHost.GetWeaponBaseDamage` 仍保留 `(min+max)/2` 语义不变（硬性规则"禁止改既有签名"）。
**更新（T-N3-3 已落地）**：`weapon_damage_pct` 原语已改接 `GetWeaponDps × 一拍常数`（见
`core/rules/skill/schema/README.md`"weapon_damage_pct：秒伤 × 一拍常数"一节），
`GetWeaponBaseDamage` 不再是该原语的现行实现——方法本身保留供其它消费方，签名/语义未变。
伤害范围 = 武器秒伤 ×
`weapon_profile.speed` × (1 ± 浮动)——`damage_min`/`damage_max` 保留手填，`ItemWeaponDamageDeviatesDpsCurveRule`
（check `item_weapon_damage_deviates_dps_curve`，Warning，不可提升）核对手填均值与"秒伤 × speed"的
偏离比例，超阈值（构造参数，缺省 `±20%`——契约未给阈值，**设计层裁定（2026-09-15）：采纳**）报警告；曲线找不到、
或任一模板未同时填 `damage_min`/`damage_max` 时该模板不报。

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

T-N2-2（ADR-0032 决策 7；07 第 1.6 节修订段"随机词缀由留位转正"）：由留位转正为预算份额包正式表。
判断记录（是否升 schema 版本）：留位期字段（`id`/`name_key`/`effects`）从未被任何运行时代码解析、
`data/_sample/item/item.affix.json` 旧版三条样例只有 `id`/`name_key`、`games/_template` 无该表数据——
三项前提俱在，按任务书"实现要点"给出的判定条件，本表 `currentSchemaVersion` 保持 1，不新增迁移链。

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `item.affix.<name>` |
| `name_key` | TextKey | 是 | 显示名文本键 |
| `budget_share` | Number | 是 | T-N2-2 新增（ADR-0032 决策 7）：占该件预算的比例；具体数值掉落时按该件预算 × 本比例经预算反解算出，一条词缀适用于全部等级；消费实现（预算反解、"模板加词缀最大份额超预算"阻断校验）随 T-N2-3/T-N2-4 落地。范围 `[0,1]` |
| `stat_mix` | Array | 是 | T-N2-2 新增：`[{stat:Reference(stat.definition), ratio:Number(0,1]}]`，属性组合与内部分配比例；同一条词缀内部 `ratio` 之和不超过一（阻断，`item_affix_stat_mix_ratio_sum`，本任务落地，见下）；禁止出现任何绝对数值字段 |
| `quality_pool` | Reference→`item.quality_definition` | 是 | T-N2-2 新增：所属品质池；掉落时先掷品质骰，再从该品质对应的词缀池里抽词缀骰（消费实现随 T-N2-8 落地） |
| `weight` | Number | 是 | T-N2-2 新增：池内权重；范围 `>= 0`（同 `loot.table.weighted_pick_one` 既有 `weight_or_chance` 登记口径"相对权重"）；消费实现（加权抽取）随 T-N2-8 落地 |
| `grants` | Object | 否 | T-N2-2 新增（07 第 1.6 节修订段"可选 `grants`（仅高品质池）"）：`{skills:List<Id>, auras:List<Id>}`，复用 `item.template.grants` 同一 `GrantsSchema` 子结构；本任务只登记字段，不校验"仅高品质池"这条业务规则，消费实现随 T-N2-6 落地 |
| `effects` | Array | 否 | **已废弃**（T-N2-2 起）：由 `stat_mix`/`grants` 取代，保留一个版本周期仅作历史数据读取兼容，本版起不再解析、新数据不应再填写，一个版本周期后随后续词缀相关任务物理删除（硬性规则 5：ABI 只允许新增，不删除留位字段） |

`stat_mix[]` 子结构（ADR-0019 F1c）：`stat` 登记为 `Reference(stat.definition)`（L3 依赖 L1 合法，同
`item.template.stats[].stat` 判断记录）；`ratio` 范围 `(0,1]`——0 没有意义（应从数组删掉该条而非写
0），"同一条词缀 `stat_mix[]` 之和不超过一"是跨元素约束，登记表达不了，见下方 `ItemAffixStatMixRatioSumRule`。

## 校验规则清单

| 规则 | 检查项名 | 说明 |
|---|---|---|
| `item_budget_exceeded` | `ItemBudgetValidationRule` | 预算超标（消耗公式见上）|
| `item_budget_utilization_low` | `ItemBudgetValidationRule`（Warning，不可提升，T-N2-3，ADR-0032 决策 10） | 预算利用率（消耗 / 上限）低于阈值（默认 `0.7`，构造重载 `ItemBudgetValidationRule(Id, double)` 可配置，`CarriersSchemaCatalog.RegisterAll` 新增重载同步可配置）；04 第 5 节"装备预算利用率过低"一行未给出具体检查名，同上一行口径，**设计层裁定（2026-09-15）：采纳** |
| `item_weapon_profile_missing`/`item_weapon_profile_unexpected` | `ItemWeaponProfileRule` | `weapon_profile` 当且仅当 `slot_definition.is_weapon` 为真时存在 |
| `item_set_membership_mismatch` | `ItemSetMembershipRule` | `set_id` 的 `pieces` 包含该物品 |
| `item_stack_size_min`/`item_stack_size_equipment_not_one` | `ItemStackSizeRule` | `stack_size >= 1`；装备类（`slot` 指向 `is_equipment` 不为 false 的 `slot_definition`）`stack_size == 1` |
| `item_grants_auras_duplicate` | `ItemGrantsAurasDuplicateRule`（Warning） | `grants.auras` 同一物品内重复引用同一个 `aura_def` |
| `item_quality_multiplier_order` | `ItemQualityMultiplierOrderRule`（T-N2-1，ADR-0032 决策 2） | `item.quality_definition` 的 `budget_multiplier`/`price_multiplier` 大小顺序须与 `sort_weight` 一致（按 `sort_weight` 升序分组，组内并列不比较，跨组不递减）；检查名按任务书"04 §5 或 item_quality_\* 前缀"取值——**04 第 5 节该行未给出具体检查名，设计层裁定（2026-09-15）：采纳** |
| `item_affix_stat_mix_ratio_sum` | `ItemAffixStatMixRatioSumRule`（T-N2-2，ADR-0032 决策 7） | 单条 `item.affix.stat_mix` 内部 `ratio` 之和不超过一（超出 1e-9 浮点容差报错）；校验对象为"单条词缀内部"，非"同一品质池跨词缀"（见 07 第 1.6 节修订段与 04 第 5 节"词缀份额之和"行原文，二者均紧跟在 `stat_mix` 后描述）；检查名同上一行口径——**04 第 5 节该行同样未给出具体检查名，设计层裁定（2026-09-15）：采纳** |
| `item_weapon_damage_deviates_dps_curve` | `ItemWeaponDamageDeviatesDpsCurveRule`（Warning，不可提升，T-N2-6，ADR-0032 决策 4；拍板 6） | 武器槽模板手填 `weapon_profile.damage_min`/`damage_max` 均值与"秒伤(`item.weapon_dps_curve(item_level) × 品质预算倍率 × 武器槽位系数`) × `weapon_profile.speed`"的偏离比例超过阈值（构造参数，缺省 `±20%`，`CarriersSchemaCatalog.RegisterAll` 新增重载同步可配置）；曲线找不到、或 `damage_min`/`damage_max` 任一未填、或期望值为零（`speed` 未填）时不报；检查名与阈值契约均未给出，同上两行口径，**设计层裁定（2026-09-15）：采纳** |
| `item_template_affix_share_exceeds_budget` | `ItemTemplateAffixShareExceedsBudgetRule`（T-N2-11，ADR-0032 决策 7/10） | 模板自身 `stats` 消耗 + 可抽词缀池最大份额（候选 = `quality_pool` 匹配模板品质且与模板 `affixes` 白名单取交集，按 `budget_share` 降序取前 `affix_count`——未登记视为不限、取全部候选） × 预算，不得超过预算上限；检查名 04 第 5 节该行未给出，同上两行口径——**设计层裁定（2026-09-15）：采纳**，登记为 `item_template_affix_share_exceeds_budget` |
| （内置）`reference_integrity` | `data_registry` | `item.template.slot`/`quality`/`set_id`/`item.affix.quality_pool`/`stat_mix[].stat` 等 `Reference` 字段的存在性 |
| （T-N0-3 通用规则）`curve_monotonic_finite` | `CurveMonotonicFiniteRule` | 自动覆盖 `item.budget_curve`/`item.armor_curve`/`item.weapon_dps_curve`/`item.req_level_curve` 四张断点表曲线的 `entries` |

八条本模块 `IValidationRule` 均需调用方显式 `registry.RegisterValidationRule(...)` 才会生效，本模块不
自动注册（同 `progression`/`stat_block`/`power_set` 惯例）；`curve_monotonic_finite` 是 T-N0-3 的
通用规则，随 `PresentationSchemaCatalog.RegisterAll` 全局注册一次即覆盖全部登记为曲线形态的字段，
不需要本模块重复注册。
