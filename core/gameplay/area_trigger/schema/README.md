# `area.trigger_def` 数据表字段

对应 [`AreaTriggerSchemas.cs`](../contracts/AreaTriggerSchemas.cs) 的 `TableSchema` 声明；字段语义
详见 [05_对象模型与世界.md](../../../../architecture/05_对象模型与世界.md) 第 1.5、3.5、7、7.1 节。
宿主在构造 `IDataRegistry` 后需要 `RegisterSchema(AreaTriggerSchemas.TriggerDef)` 才能加载对应数据
（本模块不自动注册，见 `GameplaySchemaCatalog.RegisterAreaTriggerSchemas`）。

## `area.trigger_def`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `area.<name>` |
| `map_id` | Id | 是 | 所属地图，概念上指向 `world.map`；`world.map` 暂存于 `core/foundation/scene_router`，退回 Id 只做格式校验 |
| `shape` | Object | 是 | 触发范围；ADR-0019 起登记为按 `kind` 分派的 `Variants`，见下"`shape` 变体参数表" |
| `trigger_type` | Enum(`map_transition`\|`quest_explore`\|`encounter_start`\|`script`) | 是 | 四种类型之一 |
| `condition` | Expr | 否 | 附加触发条件（可空） |
| `one_shot` | Bool | 否 | 是否只触发一次，缺省 `false` |
| `params` | Object | 是 | 按 `trigger_type` 解释；ADR-0019 起登记为 `Fields`（非 `Variants`，见下"`params` 子字段表"判断记录） |
| `name_key` | TextKey | 否 | 显示名文本键（可选） |

## 子结构登记表（ADR-0019 / F1b）

以运行时解析代码（`AreaTriggerDef.FromRecord`/`AreaTriggerShapeJson.Parse`）为唯一依据逐字段核对，
缺省值/必填性均以这些代码的实际读取行为为准。

### `shape` 变体参数表（`AreaTriggerSchemas.ShapeSchema`，对照 `AreaTriggerShapeJson.Parse`）

判别字段 `kind`，键集合 `circle`|`cone`|`line`|`rect`（`AreaTriggerSchemas.ShapeKindValues`）。
**判断记录**：`AreaTriggerShapeJson.GetNumber`/`ParseCenter` 对缺失或类型不符的字段一律静默兜底为
`0`/`Vec2.Zero`，从不抛异常——与 `core/gameplay/encounter` 的 `EncounterShapeJson.RequireNumber`
（缺失即抛 `FormatException`，见 `encounter/schema/README.md`"`bounds_shape` 变体参数表"）形成对照，
两个模块各自有独立的 shape JSON 解析器、严格程度不同。因此本表四个分支下的全部数值/`center` 子字段
均登记为**非必填**，只有判别字段 `kind` 仍强制"存在且合法"（由 `Variants` 机制内置的
`variant_discriminator` 检查覆盖，等价于原 `AreaTriggerShapeKindRule`，见下"退役规则"）。

| `kind` | 公共字段（`CommonFields`） | 分支字段 | 对照 `Shape` 工厂方法 |
|---|---|---|---|
| `circle` | `center`:Vec2/否（缺省 `Vec2.Zero`） | `radius`:Number/否（缺省 0） | `Shape.Circle(center, radius)` |
| `cone` | 同上 | `rotation`:Number/否（即 `Direction`，缺省 0）、`angle`:Number/否（缺省 0）、`radius`:Number/否（缺省 0） | `Shape.Cone(center, rotation, angle, radius)` |
| `line` | 同上 | `rotation`:Number/否（即 `Direction`，缺省 0）、`length`:Number/否（缺省 0）、`width`:Number/否（缺省 0） | `Shape.Line(center, rotation, length, width)` |
| `rect` | 同上 | `rotation`:Number/否（缺省 0）、`length`:Number/否（换算 `HalfExtents.X` 时除 2，缺省 0）、`width`:Number/否（换算 `HalfExtents.Y` 时除 2，缺省 0） | `Shape.Rect(center, halfExtents, rotation)` |

四个键与 `AreaTriggerSchemas.ShapeKindValues` 全集一致（本来就是同一个常量数组），
`AreaTriggerSchemaCoverageTests.ShapeVariantKeys_MatchShapeKindValuesFullSet` 用测试锁死这条一致性。

### `params` 子字段表（`AreaTriggerSchemas.ParamsSchema`，对照 `AreaTriggerDef.FromRecord`）

**判断记录（为什么是 `Fields` 不是 `Variants`）**：`params` 按 `trigger_type` 分派子字段，形式上很像
`shape` 按 `kind` 分派——但判别字段 `trigger_type` 是 `area.trigger_def` **行内与 `params` 平级**的
顶层字段，不在 `params` 对象内部；`VariantSchema.Discriminator` 要求判别字段与被判别的子字段同处
**同一个** `JsonObject`（`DataRegistry.ValidateVariantObject` 是在 `params` 自身的 JSON 对象里查找
`Discriminator`）。若把 `Variants.Discriminator` 设为 `"trigger_type"`，由于 `params` 对象内部永远
不会有这个键，**每一条记录都会恒报 `variant_discriminator` 缺失**——这不是登记疏漏，是 ADR-0019
`Variants` 机制当前只覆盖"判别字段与子结构同级"这一种形状，`trigger_type`/`params` 属于"判别字段与
被判别对象不同级"，机制设计上覆盖不到（与 `core/gameplay/spawn` 的 `respawn_policy`/`respawn_timer`
同属一类结构边界，两处判断记录互相印证，见 `spawn/schema/README.md`"判断记录 1"）。因此改用 `Fields`
登记四种 `trigger_type` 分别用到的子字段并集，**全部标记为非必填**（哪些字段按 `trigger_type` 必填
这条业务判断，登记层表达不了，继续由 `AreaTriggerParamsFieldGroupRule` 独家负责，见下"校验规则"/
"退役规则"两节——**该规则未退役**）；本次登记新增的是此前完全没有的"存在时类型必须是合法 Id"校验，
与 `AreaTriggerParamsFieldGroupRule` 的必填性检查互不重叠，不会对同一缺陷双报。

| 字段 | 类型 | 必填 | 用于哪个 `trigger_type` | 说明 |
|---|---|---|---|---|
| `target_map` | Id | 否（业务必填性见 `AreaTriggerParamsFieldGroupRule`） | `map_transition` | `world.map` 未随本模块登记加载，判断记录同顶层 `map_id`，退回 Id，不做引用完整性检查 |
| `spawn_point` | Id | 否（`map_transition` 下本身也是可选字段，见 `MapTransitionParams.SpawnPoint` 判断记录：缺省由 `ISceneRouter` 落在目标地图默认出生点） | `map_transition` | 同上，退回 Id |
| `encounter_ref` | Id | 否（业务必填性见 `AreaTriggerParamsFieldGroupRule`） | `encounter_start` | 经 `AreaTriggerOptions.EncounterStartRequested` 委托分发，本模块不直接依赖 `core/gameplay/encounter`（判断记录同 `EncounterSchemas.UnitItemSchema.spawn_ref` 的决耦惯例，见 `encounter/schema/README.md` 判断记录 2），退回 Id |
| `hook_id` | Id | 否（业务必填性见 `AreaTriggerParamsFieldGroupRule`） | `script` | `found.hook` 当前无实现级 schema 登记，判断记录同 `EncounterSchemas.PhaseItemSchema.on_enter_hook`，退回 Id |

`quest_explore` 无任何子字段（`params` 允许为空对象 `{}`，`AreaTriggerDef.FromRecord` 的
`switch` 分支对该类型不读取任何 `params` 键）。

## 校验规则（`AreaTriggerValidationRules.cs`）

| 规则/检查项 | 检查项名 | 说明 |
|---|---|---|
| ~~`shape.kind` 合法~~ | ~~`area_trigger_shape_kind`~~ | **已退役**，见下"退役规则" |
| `trigger_type` 对应 `params` 必填字段齐全 | `area_trigger_params_field_group` | `AreaTriggerParamsFieldGroupRule`；判别字段 `trigger_type` 与 `params` 不同级，`Variants` 机制表达不了，**未退役**（见上"`params` 子字段表"判断记录） |

`GameplaySchemaCatalog.RegisterAreaTriggerSchemas` 需要的改动（本模块不自行编辑该文件，见任务边界）：

```diff
 private static void RegisterAreaTriggerSchemas(IDataRegistry registry)
 {
     registry.RegisterSchema(AreaTriggerSchemas.TriggerDef);
-    registry.RegisterValidationRule(new AreaTriggerShapeKindRule());
     registry.RegisterValidationRule(new AreaTriggerParamsFieldGroupRule());
 }
```

### 退役规则（ADR-0019 / F1b）

`AreaTriggerShapeKindRule`（"`shape.kind` 合法"检查项，检查名 `area_trigger_shape_kind`）**整条
删除**：`AreaTriggerSchemas.ShapeSchema` 把 `shape` 登记为按判别字段 `kind` 分派的 `VariantSchema`
后，`DataRegistry` 内置的 `variant_discriminator` 检查（判别字段缺失/非字符串/不在合法取值集合内）
已完整覆盖原规则的全部报错场景——旧检查名 `area_trigger_shape_kind`、字段路径 `shape` 整体，变为新
检查名 `variant_discriminator`、字段路径精确到 `shape.kind`；两者语义完全等价（合法集合同源于
`AreaTriggerSchemas.ShapeKindValues`），不产生行为回退。原有测试
`AreaTriggerValidationRuleTests.ShapeKindRule_IllegalKind_ReportsError`/
`ShapeKindRule_LegalKind_NoIssue` 迁移到 `AreaTriggerSchemaCoverageTests`（同名保留，断言目标改为
`variant_discriminator`/`shape.kind`），不再注册 `AreaTriggerShapeKindRule`（类已删除，`schema/
AreaTriggerValidationRules.cs` 现在只剩 `AreaTriggerParamsFieldGroupRule` 一个类）。

`AreaTriggerParamsFieldGroupRule` **未退役、未收窄**——判别字段 `trigger_type` 与被判别对象 `params`
不同级，`VariantSchema` 机制设计上无法覆盖（见上"`params` 子字段表"判断记录），该规则继续是"按
`trigger_type` 决定 `params` 哪些子字段必填"这条业务判断的唯一来源；`AreaTriggerSchemas.ParamsSchema`
新增的 `Fields` 登记只新增"类型校验"这一层此前完全没有的覆盖，两者检查不同的缺陷，不会双报（见
`AreaTriggerSchemaCoverageTests.ParamsFields_TargetMapBadIdFormat_ReportsFieldTypeWithNestedPath`
锁定"类型错误只报 `field_type`，不触发 `area_trigger_params_field_group`"这条边界）。

## Map 型待后续契约扩展一览（ADR-0019 首批范围外）

本轮（F1b）核对未发现本模块内因"动态键 Map"而无法登记 `Fields` 的字段——`shape`/`params` 均是固定
键集合（按判别字段/`trigger_type` 分派后，各分支内部字段名固定），不适用本节。

## 判断记录

1. **`shape` 升级为 `Variants`，`params` 改用 `Fields`（非 `Variants`）**：两者形式上都是"按某个判别
   字段分派子结构"，处理不同的原因是判别字段与被判别对象的位置关系不同——`shape.kind` 在 `shape`
   对象内部（`Variants` 直接适用），`trigger_type` 在 `params` 对象外部、与其平级（`Variants` 机制
   要求同级，见上"`params` 子字段表"判断记录）。这不是登记疏漏，是 ADR-0019 `Variants` 机制当前的
   设计边界；与 `core/gameplay/spawn` 的 `respawn_policy`/`respawn_timer`（同属"判别字段与被判别
   字段不同级"）互相印证，见 `spawn/schema/README.md` 判断记录 1。
2. **`shape`/`params` 四个 Id 类子字段均未升级为 `Reference`**：`target_map`（判断记录同顶层
   `map_id`，`world.map` 未登记加载）、`spawn_point`（同上，且本身语义上不指向任何数据表主键，是
   `world.map.spawn_points` 数组的索引/键）、`encounter_ref`（判断记录同 `encounter` 模块
   `spawn_ref` 的决耦考虑——`AreaTriggerHost` 经 `AreaTriggerOptions.EncounterStartRequested` 委托
   分发，刻意不直接依赖 `core/gameplay/encounter`）、`hook_id`（`found.hook` 当前无实现级 schema
   登记，判断记录同 `encounter` 模块 `on_enter_hook`）——四者均退回 `Id`，只做格式校验。
3. **未使用 `itemFactory`/自引用递归**：`shape`/`params` 均是"叶子"结构（子字段全是标量），不存在
   `SkillSchemas.EffectsItemSchema`/`SubstructureValidationTests` 那种自引用场景，本模块无需惰性
   求值。
