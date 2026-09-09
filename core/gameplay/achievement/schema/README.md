# `achv.def` 子结构登记表（ADR-0019 / F1b）

对应 `01_分层与依赖.md` L4 模块表 `achievement` 行（目录 `core/gameplay/achievement`，契约
`AchievementHost`）、`08_玩法层_掉落任务对话关卡.md` 第 6.1 节。字段名与结构逐一核对
`AchievementSchemas.cs`（`TableSchema` 声明）+ `core/gameplay/achievement/contracts/AchievementCriterion.cs`
（`criteria` 数组内部结构解析）+ `core/gameplay/achievement/contracts/CriterionType.cs`（六种类型）+
`core/gameplay/achievement/core/AchievementHost.cs`（`TryMatch`，`target_ref` 按类型的运行期匹配语义
唯一依据）。

`criteria`/`rewards` 两处复合字段自本轮起登记为机器可读 `FieldSchema.Fields`/`Item`/`Variants`
（代码：`AchievementSchemas.cs` 的 `CriterionItemSchema`），`DataRegistry` 加载期据此逐字段递归校验
必填/类型/枚举/表达式可解析；`AchievementContentValidationRule` 相应收窄，只保留登记表达不了的业务
判断。

## 顶层字段

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `achv.<name>` |
| `name_key` | TextKey | 是 | 显示名文本键 |
| `criteria` | Array\<Criterion\> | 是 | 达成条件数组，结构见下；至少一条（业务判断，见判断记录 1） |
| `rewards` | Object | 否 | 直接复用 `Core.Gameplay.Quest.QuestSchemas.RewardsFields`，见判断记录 4 |

## `Criterion`（`criteria[]`，按 `type` 分派）

全部六种类型共有字段：

| 字段 | 类型 | 必填 | 说明 | 运行时代码位置 |
|---|---|---|---|---|
| `type` | Enum（判别字段） | 是 | 六种类型之一，见 `CriterionTypeIds.AllValues` | `AchievementCriterion.FromRecord` |
| `observe_event` | Id | 是 | 要观察的具体事件 key；已登记事件 key 的成员资格检查退回手写规则，见判断记录 2 | `AchievementCriterion.FromRecord` |
| `count` | Int | 是 | 达成所需累计次数；`>= 1` 的数值范围是业务判断，见判断记录 3 | `AchievementCriterion.FromRecord` |
| `filter` | Expr | 否 | 补充匹配条件；六种类型均可选携带 | `AchievementCriterion.FromRecord` |

各 `type` 取值专属字段：

| `type`（wire 值） | `target_ref` 语义 | `target_ref` 登记 | 运行时代码位置 |
|---|---|---|---|
| `kill_count` | `creature.template`（被击杀单位模板） | 退回 Id（不做存在性检查，见判断记录 5） | `AchievementHost.TryMatch` |
| `collect_count` | `item.template`（拾取物品模板） | 退回 Id，理由同上 | `AchievementHost.TryMatch` |
| `quest_complete` | `quest.def`（交付的任务） | 退回 Id，理由同上 | `AchievementHost.TryMatch` |
| `reach_area` | `area.trigger_def`（进入的区域触发器） | 退回 Id，理由同上 | `AchievementHost.TryMatch` |
| `cast_count` | `skill.def`（施放的技能） | 退回 Id，理由同上 | `AchievementHost.TryMatch` |
| `custom_event` | 不使用 `target_ref` | 不登记该字段（可省略；若提供视作未登记子字段，`unknown_subfield` 警告不阻断加载） | `AchievementHost.TryMatch` |

## `rewards`（直接复用 `QuestSchemas.RewardsFields`）

`items`/`xp`/`currency`/`skills`/`world_flags`/`talent_points` 六个子字段，完整定义与判断记录见
`core/gameplay/quest/schema/quest.def.md`"子结构登记表"一节；`achv.def.rewards` 与 `quest.def.rewards`
由同一份 `Core.Gameplay.Common.RewardBundle.FromRecord` 解析，字段清单与类型逐一相同，故直接复用同一
个静态只读实例（`AchievementSchemaCoverageTests.RewardsFields_ReusesQuestSchemasRewardsFieldsInstance`
锁死这条"同一实例"关系）。

## Map 型字段

本表无 Map 型字段（动态键对象），不适用；`rewards.world_flags[].value` 是联合类型而非 Map 型，
判断记录见下。

## 判断记录

1. **`criteria` 数组最小长度（`achv_criteria_min_count`）**：`AchievementDefinition.FromRecord` 构造
   期硬约束（"至少需要一条达成条件"），`FieldKind.Array` 不表达最小长度，此前**完全未被任何校验
   覆盖**（`AchievementContentValidationRule` 旧实现从不检查这一点，只有 `AchievementHost` 真正构造
   时才会抛异常崩溃）——ADR-0019 起补为新的手写业务检查，属于本次任务发现的既有覆盖缺口，不是"退役
   旧检查"，而是新增覆盖。
2. **`observe_event` 未登记为 `Reference(found.event_catalog)`**：语义上指向
   `Core.Foundation.DataRegistry.BuiltinSchemas.FoundEventCatalog`（主键 `key`），但该表不由
   `GameplaySchemaCatalog.RegisterAll` 注册；既有测试 `AchievementHostTests` 用只注册 `achv.def`
   一张表的最小 `DataRegistry`（`TestSupport.MakeRegistry`）驱动全部用例，登记为 Reference 会让
   `found.event_catalog` 未加载时的每条记录都报 `reference_integrity` 错误，与既有测试冲突（理由同
   `QuestSchemas.ObjectiveItemSchema` "kill"/"escort" 判断记录）。退回 Id（只查格式），"已登记事件
   key"这条比 Reference 弱一档的成员资格检查改由 `AchievementContentValidationRule`
   （`achv_observe_event_unregistered`）对 `Core.Foundation.EventBus.EventKeys.All` 编译期常量集合
   做成员测试，不依赖 `found.event_catalog` 表是否加载。
3. **`count >= 1`（`achv_criterion_count_positive`）**：`AchievementCriterion` 构造函数硬约束，
   `FieldKind.Int` 不区分正负，此前同判断记录 1，完全未被任何校验覆盖，本轮补为新增业务检查。
4. **`rewards` 直接复用 `QuestSchemas.RewardsFields`，但既有测试驱动了一处基础设施补丁**：
   `AchievementHostTests.Unlock_RewardGrantFails_StaysLocked_RetryPendingRewardsGrantsExactlyOnceAfterRoomFreed`
   用 `TestSupport.MakeRegistry`（此前只注册 `achv.def` 一张表）构造的 `DataRegistry` 校验一条
   `rewards.items` 含 `item.sample_reward` 的记录；`QuestSchemas.RewardsFields.items[].itemId` 是
   `Reference(item.template)`，若 `item.template` 未注册/未加载会报 `reference_integrity` 错误。
   本轮扩展 `TestSupport.MakeRegistry` 同时注册 `item.template`/`item.slot_definition`/
   `item.quality_definition` 三张表（新增可选 `extraItemIds` 参数登记引用到的 id，缺省不影响原有
   调用点），并把该测试的调用点改为传入 `"item.sample_reward"`，而不是把 `RewardsFields` 退化成
   本模块自己一份"结构相同但 items/currency/skills 退回 Id"的拷贝——直接复用同一份登记，避免
   `achv.def`/`quest.def`/`encounter.def` 三张共用 `RewardBundle` 结构的表在 Reference 严格度上
   出现漂移。
5. **`target_ref` 五处（`kill_count`/`collect_count`/`quest_complete`/`reach_area`/`cast_count`）
   均退回 Id，不登记为 Reference**：`AchievementCriterion.FromRecord` 对全部六种类型统一按
   "存在则必须是合法 Id 字符串，不存在则为 null"解析，不因 `type` 强制必填——`target_ref` 缺失时
   该条 criterion 在 `AchievementHost.TryMatch` 里静默永不命中（不是解析期报错），因此 FieldSchema
   层面忠实保持"可选"，不额外收紧为 `required: true`（收紧属于新增业务规则的范畴，超出"以运行时
   解析代码为唯一依据"的登记口径）。域名上虽然可以指向 `creature.template`/`item.template`（L3，
   低于本模块 L4，允许 Reference）与 `quest.def`/`area.trigger_def`（L4 同层，`GameplaySchemaCatalog.
   RegisterAll` 同一次调用内与 `achv.def` 一并注册）、`skill.def`（L2），分层边界本身不构成阻碍；
   但既有测试 `AchievementHostTests`（`TestSupport.MakeRegistry` 不加载 `creature.template`/
   `item.template`/`quest.def`/`area.trigger_def`/`skill.def` 中的绝大多数）大量用例的 `target_ref`
   指向从未注册进对应表的 id（如 `creature.sample_monster`），登记为 Reference 会让这些既有用例
   改判为 `reference_integrity` 错误，理由同 `observe_event` 判断记录，五处统一退回 Id。
6. **`type` 合法性 / `observe_event`、`count` 缺失格式检查已退役**：`VariantSchema` 自动校验判别
   字段落在 `Cases` 键集合内（等价原 `type` 合法性检查）；`observe_event`/`count` 的缺失/类型错误
   已由 `required_field`/`field_type` 覆盖。`AchievementContentValidationRule` 相应收窄，不再重复
   报告同一缺陷（测试见 `AchievementSchemaCoverageTests`）。
