# `spawn.table` 数据表字段

对应 [`SpawnSchemas.cs`](../contracts/SpawnSchemas.cs) 的 `TableSchema` 声明；字段语义详见
[05_对象模型与世界.md](../../../../architecture/05_对象模型与世界.md) 第 5、5.1、5.2 节。宿主在构造
`IDataRegistry` 后需要 `RegisterSchema(SpawnSchemas.Table)` 才能加载对应数据（本模块不自动注册，见
`GameplaySchemaCatalog.RegisterSpawnSchemas`）。

## `spawn.table`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `spawn.<name>` |
| `map_id` | Id | 是 | 所属地图，概念上指向 `world.map`；`world.map` 暂存于 `core/foundation/scene_router`，本模块不跨目录依赖其加载时机，退回 Id 只做格式校验 |
| `content_ref` | Id | 是 | 指向 `creature.template` 或 `gobj.template`（二选一，域名与目标存在性由 `SpawnContentRefRule` 校验，登记层的 `ReferenceTable`/`ReferenceDomain` 至多设置一个、表达不了"二选一"） |
| `position` | Vec2 | 是 | 刷新点坐标 |
| `facing` | Number | 否 | 初始朝向，缺省 0（`SpawnTableDef.FromRecord`：`TryGetNumber` 失败按 0 处理） |
| `condition` | Expr | 否 | 刷新条件（可空） |
| `respawn_policy` | Enum(`on_map_enter`\|`once`\|`never`\|`timer`) | 是 | 四种重生策略之一（见下"子结构登记表"一节说明为何不是 `Variants`） |
| `respawn_timer` | Number | 否 | `respawn_policy=timer` 时必填，见 `SpawnRespawnPolicyFieldGroupRule` |

`spawn.table` 全部字段均为标量（`Id`/`Vec2`/`Number`/`Expr`/`Enum`），**不含任何
`FieldKind.Object`/`FieldKind.Array` 复合字段**——本轮（ADR-0019 / F1b）任务书点名核实的
"`spawn.table` 条目与重生策略"没有可登记 `Fields`/`Item`/`Variants` 的对象，见下"子结构登记表"一节
的判断记录。

## 子结构登记表（ADR-0019 / F1b）

**本节为空**：以运行时解析代码（`SpawnTableDef.FromRecord`、`SpawnHost.cs` 各方法）为唯一依据核对，
`spawn.table` 没有任何 `Object`/`Array` 字段，因此没有可登记 `Fields`/`Item`/`Variants` 的复合字段
子结构。详见下"判断记录 1"。

## 变体参数表

不适用——本模块没有任何字段登记 `Variants`（见"判断记录 1"）。

## 校验规则（`SpawnValidationRules.cs`）

| 规则/检查项 | 检查项名 | 说明 |
|---|---|---|
| `respawn_policy=timer` 时 `respawn_timer` 必填且为正数 | `spawn_respawn_policy_field_group` | `SpawnRespawnPolicyFieldGroupRule`；跨字段一致性，登记层表达不了（见"判断记录 1"），**未退役** |
| `content_ref` 域名合法（`creature`\|`gobj`）且目标行存在 | `spawn_content_ref` | `SpawnContentRefRule`；域名"二选一"+ 跨表存在性，登记层的 `ReferenceTable`/`ReferenceDomain` 表达不了"二选一"，**未退役** |
| `summon_only` 生物不得出现在 `spawn.table` | `spawn_summon_only_creature` | `SpawnSummonOnlyCreatureRule`；依赖 `ICreatureTemplateQuery` 的跨表业务判断，登记层无法表达，**未退役** |

以上规则通过 `IDataRegistry.RegisterValidationRule` 注册；`GameplaySchemaCatalog.RegisterSpawnSchemas`
里三处 `registry.RegisterValidationRule(...)` 调用**均不需要改动**（详见下"退役规则"一节结论）。

### 退役规则（ADR-0019 / F1b）

**本轮（F1b）在 `spawn` 模块没有任何规则被退役或收窄**。任务书原句"`spawn.table` 条目与重生策略
（`SpawnRespawnPolicyFieldGroupRule` 若为纯结构则退役为变体登记）——请核实并处理"，核实结论见下
"判断记录 1"：`SpawnRespawnPolicyFieldGroupRule` 检查的确是"纯结构"（`respawn_policy=timer` ⇒
`respawn_timer` 必填且为正数），但它是**两个平级标量字段之间**的一致性，不是"某个复合字段的内部
子结构"，ADR-0019 的 `Fields`/`Item`/`Variants` 机制无法覆盖这类形状，因此**不满足**"可退役"的前提
（"若为纯结构则退役"的"若"不成立——纯结构≠登记层能表达）。`SpawnContentRefRule`/
`SpawnSummonOnlyCreatureRule` 同样因跨字段"二选一"/跨表业务查询而无法登记覆盖，本轮均原样保留。

## 判断记录

1. **`SpawnRespawnPolicyFieldGroupRule` 不能改写为 `Variants` 登记**：ADR-0019 的
   `FieldSchema.Variants`（`VariantSchema`）要求判别字段与被判别的子字段**同处一个 `JsonObject`**——
   `DataRegistry.ValidateVariantObject` 是在某个 `FieldKind.Object` 字段自身的 JSON 对象内查找
   `Discriminator`，再按命中的取值校验该对象内部的其余子字段。`spawn.table` 的 `respawn_policy`
   （`Enum`）与 `respawn_timer`（`Number`）是这张表**行内两个平级的顶层标量字段**，二者之间没有一个
   共同的父 `Object` 字段可以挂载 `Variants`（也没有 `Fields`——同样要求先有一个 `Object` 字段）。把
   `respawn_policy` 当作 `Variants.Discriminator`、`respawn_timer` 当作某个分支的子字段，要求
   `respawn_timer` 存在于 `respawn_policy` 所在的那个 JSON 对象内部——但 `respawn_policy` 本身就是
   顶层字段，不存在"`respawn_policy` 所在的对象"这个概念上更外层的容器可供挂载。ADR-0019 的
   `Fields`/`Item`/`Variants` 三者面向的是"复合字段的子结构登记"（04 第 3.2 节标题原文），不是
   "表内任意两个平级标量字段之间的取值一致性"——后者与 `data_registry` 已有的顶层字段级校验（04
   第 5 节 `required_field`/`field_type`/`reference_integrity` 等）属于同一量级的"跨字段"约束，
   `data_registry` 现有设计里这类约束一律走 `IValidationRule`（`04 第 5 节"跨字段/跨记录一致性由
   IValidationRule 负责"`），`SpawnRespawnPolicyFieldGroupRule` 正是这类规则的标准形态，因此原样
   保留，不构成"能登记但没登记"的遗漏。
   <br><br>
   与 `area_trigger` 模块的 `area.trigger_def.params`（判别字段 `trigger_type` 与 `params` 平级，
   同属这一类"判别字段与被判别对象不同级"）结构完全同构，两处判断记录互相印证，见
   `core/gameplay/area_trigger/schema/README.md`"子结构登记表"一节对 `params` 的判断记录。
2. **未新建/修改任何 `Object`/`Array` 字段登记**：`SpawnSchemas.cs`（`contracts/SpawnSchemas.cs`）
   本轮未改动一行——`spawn.table` 的 8 个字段本来就全是标量，没有登记空间。`SpawnSchemaCoverageTests.
   SpawnTable_HasNoObjectOrArrayFields_NoSubstructureToRegister` 用测试锁定这一结论：若后续任务给
   `spawn.table` 新增了 `Object`/`Array` 字段，本测试会失败，提醒作者重新评估是否需要补登记子结构。
3. **Map 型对象**：不适用——`spawn.table` 没有任何 `Object` 字段（含"键值对不固定"的 Map 型），本节
   无内容可列，与 `area_trigger`/`encounter` 等模块的同名一览表形成对照（那些模块确有 `Object` 字段
   但因动态键而未登记 `Fields`，见各自 README 的"Map 型待后续契约扩展一览"一节）。

## Map 型待后续契约扩展一览（ADR-0019 首批范围外）

不适用（见"判断记录 3"）——`spawn.table` 没有任何 `Object` 字段。
