# `found.event_catalog` 字段表

对应 `01_分层与依赖.md` L0 模块表 `event_bus` 行的主要数据表、`04_数据与内容管线.md`
第 1.1 节表清单里的 `found.event_catalog`（"事件词汇登记表：事件名、携带字段、所属
domain"）。数据文件示例：`data/_sample/found/found.event_catalog.json`。

## 字段

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `key` | string | 是 | 事件 key，格式为 `04` 第 2.1 节 id 规范 `^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$`，例如 `combat.damage_dealt`。**字段名是 `key` 而不是通用表的 `id`**，理由见下方"与通用 id 规则的偏差"。 |
| `domain` | string | 是 | 事件所属 domain。`06_规则层_属性技能战斗AI.md` 第 8 节事件词汇表已给出 domain 列的事件照抄该文档的值；未给出的事件取 `key` 的第一段。**不保证等于 `key` 的第一段**——例如 `proc.triggered` 的 domain 是 `skill`（06 原文如此），本表以 06 为准照抄。 |
| `fields` | string[] | 是 | 事件携带的字段名列表，只登记名字，不登记类型（类型由发布方各自的强类型事件类定义，见 `IEvent`）。可以是空数组，例如 `presentation.playback_finished`、`display_info.reloaded` 不携带任何字段。 |
| `description` | string \| null | 否 | 触发时机说明。对 01/03/09 只给出事件名、未明确给出携带字段的事件，在此注明"字段为建议值"，供后续文档修订时核对/收紧。 |

## 与通用 id 规则的偏差（判断记录）

- `data/README.md` 与 `toolchain/validate_data.py` 对"一般表"的通用规则是：记录主键字段名
  固定为 `id`，且其 domain 前缀必须等于表名首段（本表是 `found.event_catalog`，表名首段是
  `found`；校验器实现见 `toolchain/validate_data.py` 的 `_check_id_value`）。
- 但事件词汇表天然是跨 domain 的登记表：它登记的是分属 `skill.*`、`combat.*`、`sim.*`、
  `entity.*` 等**各自业务 domain**的事件 key，不是 `found.*` 自己的内容——对照 06 第 8 节
  `sim.turn_started` 一行的原文说明："domain 为 sim，登记在 found.event_catalog"，被登记的
  事件属于 `sim` domain，登记它的这张表属于 `found` 模块，两者是两回事。若沿用字段名 `id`
  并让通用校验器按"id 首段必须等于 found"校验，全部非 `found.*` 事件都会报错（例如
  `skill.cast_start`、`combat.damage_dealt`、`entity.created` 等 40 余条中的绝大多数）。
- 处理：本表的主键字段命名为 `key`，不叫 `id`。`toolchain/validate_data.py` 的骨架级
  `check_rows` 只在字段名为 `id` 时才做 domain 前缀比对（`elif "id" in row:`），字段名为
  `key` 不会触发该比对，因此当前骨架校验器可以对本表 0 错误通过——这与 `l10n.text` 表因为
  同样是"复合/跨 domain 语义"而对主键做特殊处理（`key` + `locale` 复合键，而不是 `id`）是
  同一类考虑，只是本表没有 `locale` 维度，不需要复合键，单独一个 `key` 字段即可。
- 这是"阶段 0 骨架校验器尚未支持跨 domain 登记表"的规避写法，不是长期方案；后续给校验器
  加入"登记表白名单"或"逐行独立 domain 前缀"能力时，应把本表 `key` 字段的格式与合法性
  校验补齐到与 `id` 同等严格（当前 `key` 值的 id 格式合法性只在运行时由
  `EventDefinition`/`Id` 构造期校验，数据文件校验器本阶段暂不覆盖）。
- `core/foundation/event_bus` 侧不因为这个数据文件的字段改名而放松运行时校验：
  `EventDefinition.Key` 的类型是 `Core.Foundation.Common.Id`，构造期仍然做完整的
  `^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$` 格式校验。

## 示例

```json
{
  "key": "combat.damage_dealt",
  "domain": "combat",
  "fields": ["sourceId", "targetId", "school", "amount", "isCrit", "hitResult"],
  "description": "结算管线\"落地\"步骤，伤害类效果（见 06 第 8 节）"
}
```

## 新增事件

新增事件属于 `12_扩展与变更流程.md` 第 2 节"新增原语"之一（拍板决策 10 列出的最小扩展开口
包含"新事件"），必须先写 ADR 说明背景与语义，ADR 未获通过前不得直接改代码/私自扩充本表；
获批后按该文档第 2 节流程"先改注册表（登记本表新增一行）、再写代码、再补文档"。

## 本模块不做什么

`core/foundation/event_bus` 只提供 `EventCatalog.FromDefinitions(IEnumerable<EventDefinition>)`
供内存构造登记表；从 `data/_sample/found/found.event_catalog.json`（或具体游戏的对应数据文件）
读取 JSON 并转换成 `EventDefinition` 列表，是数据注册表（`core/foundation/data_registry`，
T1-4）的职责，本模块不实现 JSON 反序列化，也不在本阶段把这张表接进 `EventCatalog`。
