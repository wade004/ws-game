# `arch.power_type` 字段说明

对应 [06_规则层_属性技能战斗AI.md](../../../../architecture/06_规则层_属性技能战斗AI.md) 第 2.1 节
"每种资源类型声明：上限来源（固定值或引用属性）、回复规则、衰减规则、消耗与产出的触发点"、
[01_分层与依赖.md](../../../../architecture/01_分层与依赖.md) L1 模块表 `power_set` 行。

## 判断记录：表清单补录，非新原语

[04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 1.1 节总索引没有单列
"资源类型定义"这张表——06 第 2.1 节原文只说"`arch.class` 引用一组资源类型定义（属于 L1
`prog`/`arch` 域，字段见 04）"，但 04 全文搜索不到任何一张登记"资源类型定义本身"字段的表
（04 第 1.1 节 `arch` 域下只有 `arch.class`/`arch.race`/`arch.talent_tree` 三张，均是"引用"资源
类型而非"定义"资源类型）。

任务书拍板：把资源类型定义登记为 `arch` 域下的独立表 `arch.power_type`（`arch.class.resource_types`
一类字段引用本表的 `id`），域名沿用 `arch`（04 第 2.2 节域名清单已有该域，符合"资源类型定义随
职业模板登记"的分域原则，不需要新增域名走审批）。这属于"04 总索引的表清单补录"（04 现有 29 张
表里漏收了一张 06 原文已经点名要存在的表），不属于"新增数据表结构"这一类需要过 ADR 的新原语
（见 00_架构总则.md 拍板决策 10 的原语清单：新增表字段/新增数据表本身不在该清单内，只有新增
Effect 原语、事件 key 词汇、契约签名这类才走 ADR）。已在本文件记录，供设计层复核是否需要同步
更新 04 第 1.1 节总索引补上这一行。

## 字段

| 字段 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `id` | Id | 是 | — | 资源类型 id，格式 `arch.power.<name>`（如 `arch.power.mana`、`arch.power.rage`），满足 04 第 2.1 节 id 规范。 |
| `name_key` | TextKey | 是 | — | 显示名文本键。 |
| `max_source` | Object | 是 | — | 上限来源：`{"kind": "fixed", "value": Number}` 或 `{"kind": "stat", "stat": Id}`。`fixed` 时 `value` 必填；`stat` 时 `stat` 必填且必须是合法 Id 格式字符串（引用哪张属性表由使用方决定，本模块不校验其引用完整性——本模块不引用 `stat_block` 模块类型，见下）。 |
| `regen_in_combat` | Number | 否 | `0` | 战斗内每时间单位回复量（以数据集声明的时间单位计，见 04 第 3.1 节"时间字段语义"）。 |
| `regen_out_of_combat` | Number | 否 | `0` | 脱战每时间单位回复量。 |
| `decay_out_of_combat` | Number | 否 | `0` | 脱战每时间单位衰减量；只在脱战状态下生效，战斗内恒不衰减（见 06 第 4.5 节）。 |
| `refill_on_leave_combat` | Bool | 否 | `false` | 脱战瞬间是否立即回满（见 06 第 4.5 节"资源回复规则切换（如脱战自动回满）"）。 |
| `start_full` | Bool | 否 | `true` | 单位注册该资源类型时，初始值是否为上限；为 `false` 时初始值为 `min`。 |
| `allow_overflow` | Bool | 否 | `false` | 是否允许当前值超出上限（正向修改不夹取到 max；负向修改仍夹取到 min）。 |
| `min` | Number | 否 | `0` | 下限。 |

## 示例

```json
{
  "id": "arch.power.mana",
  "name_key": "l10n.power.mana.name",
  "max_source": { "kind": "stat", "stat": "stat.intellect_derived_mana" },
  "regen_in_combat": 5,
  "regen_out_of_combat": 15,
  "decay_out_of_combat": 0,
  "refill_on_leave_combat": false,
  "start_full": true,
  "allow_overflow": false,
  "min": 0
}
```

```json
{
  "id": "arch.power.rage",
  "name_key": "l10n.power.rage.name",
  "max_source": { "kind": "fixed", "value": 100 },
  "regen_in_combat": 0,
  "regen_out_of_combat": 0,
  "decay_out_of_combat": 20,
  "refill_on_leave_combat": false,
  "start_full": false,
  "allow_overflow": false,
  "min": 0
}
```

## 与 `stat_block` 模块的关系

`max_source.kind == "stat"` 时，上限来源是"某条属性的当前值"；本模块（`power_set`）与
`stat_block` 同层（L1）且并行开发，不互相引用对方的 `core/`/`contracts/` 类型（见 11 第 2 节
"不跨模块直接引用"）。本模块只声明一个具名委托 `StatLookup(Id unitId, Id stat) -> double`
（见 `contracts/StatLookup.cs`），由组装 `PowerHost` 的更上层代码把 `stat_block` 的
`IStatHost.GetStat` 适配成这个委托签名后注入，`PowerHost` 本身不知道也不关心背后到底是
`IStatHost` 还是别的什么实现。

## 本模块不做什么

- 不读取 `arch.power_type.json`；`PowerTypeDefinition(DataRecord)` 只接收已经由数据注册表
  （`core/foundation/data_registry`）加载好的 `DataRecord`，JSON 文件读取/校验是数据注册表职责。
- 不校验 `max_source.stat` 指向的属性 id 是否真的存在（跨表引用完整性校验属于数据注册表
  `reference_integrity`/`DeclareReference` 机制，本模块的字段解析只做"格式合法"检查）。
