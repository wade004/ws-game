# `encounter.*` 数据表字段

对应 [`EncounterSchemas.cs`](EncounterSchemas.cs) 的 `TableSchema` 声明；字段语义详见
[08_玩法层_掉落任务对话关卡.md](../../../../architecture/08_玩法层_掉落任务对话关卡.md) 第 4.1/4.2/4.3
节。宿主在构造 `IDataRegistry` 后需要 `RegisterSchema(EncounterSchemas.Def)`/
`RegisterSchema(EncounterSchemas.Level)` 才能加载对应数据（本模块不自动注册，见
`GameplaySchemaCatalog.RegisterEncounterSchemas`）。

## `encounter.def`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `encounter.<name>` |
| `units` | Array | 是 | `[{spawn_ref?: Id, template_ref?: Reference(creature.template), position?: Vec2}, ...]`，`spawn_ref`/`template_ref` 二选一；ADR-0019 起元素结构登记为 `EncounterSchemas.UnitItemSchema`，见下"子结构登记表" |
| `waves` | Array | 否 | `[{trigger_condition: Expr, spawn_refs?: [Id, ...]}, ...]`；元素结构登记为 `EncounterSchemas.WaveItemSchema` |
| `phases` | Array | 否 | `[{enter_condition: Expr, ai_rotation_override?: Map<Id,Id>, on_enter_hook?: Id}, ...]`；元素结构登记为 `EncounterSchemas.PhaseItemSchema` |
| `arena_rules` | Object | 否 | `{bounds_shape, reset_if_leave?: Bool}`；ADR-0019 起登记 `Fields`，`bounds_shape` 是按 `kind` 分派的 `Variants`，见下"`bounds_shape` 变体参数表" |
| `victory_condition` | Expr | 是 | 胜利条件 |
| `defeat_condition` | Expr | 是 | 失败条件 |
| `rewards` | Object | 否 | 见 `RewardSchemaFields.Rewards()`（`core/gameplay/common`，不属本模块，未改动） |
| `combat_mode_override` | Enum(`continuous`\|`discrete`) | 否 | 覆盖场景默认战斗时间模型，由 `GameplayAssembly`/`TimeModelSwitch` 在离散模式下真实执行 |
| `initiative_override` | Object | 否 | `{policy?, params?}`，仅 `combat_mode_override=discrete` 时有意义；ADR-0019 起登记 `Fields`，见下"`initiative_override` 子字段表" |

## `encounter.level`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `encounter.level.<name>` |
| `map_ref` | Id | 是 | 指向 `world.map`（该表不在本任务数据集范围内，退回 Id，见 `EncounterSchemas` 判断记录） |
| `encounter_sequence` | List\<Id\> | 是 | 有序 `encounter.def` 引用；未升级为 `Array`+`Item=Reference`，见下"判断记录" |
| `entry_difficulty_options` | List\<Id\> | 否 | 可选难度档位（`diff.tier` 引用），处理惯例同 `encounter_sequence` |

## 子结构登记表（ADR-0019 / F1b）

以运行时解析代码（`EncounterDefinition.cs`/`EncounterHost.cs`/`EncounterShapeJson.cs`/
`TimeModelSwitch.SetPendingOverride`）为唯一依据逐字段核对，缺省值/必填性均以这些代码的实际读取
行为为准（构造函数/DTO 字段可能比运行时实际使用的更宽，已按 08 第 4.1/4.3 节与运行时代码交叉核实）。

### `units[]`（`EncounterSchemas.UnitItemSchema`，对照 `EncounterDefinition.ParseUnit`）

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `spawn_ref` | Id | 否 | `spawn.table` 条目引用，与 `template_ref` 二选一（`EncounterContentValidationRule` 校验，登记层表达不了跨字段"二选一"）。domain 须为 `spawn`，但退回 `Id` 而非 `Reference(ReferenceDomain: "spawn")`——本模块与 `core/gameplay/spawn` 刻意只经 `SpawnRequester` 委托解耦（见 `../README.md` 判断记录 2），`Reference` 的跨表存在性检查会与这一决耦意图冲突；domain 校验继续留在 `EncounterContentValidationRule` |
| `template_ref` | Reference(`creature.template`) | 否 | 内联生成的生物模板，与 `spawn_ref` 二选一。`creature.template` 属 L3，`encounter`（L4）经既有 `Core.Gameplay.csproj`→`Core.Carriers` 项目引用可 `Reference`（04 §5.1 口径）；`CreatureFactory.Spawn`（`RequireTemplate`）对未知模板硬抛 `ArgumentException`，登记为 `Reference` 与运行时语义一致 |
| `position` | Vec2 | 否 | 内联模板缺省时退化为 `Vec2.Zero`（见 `EncounterUnitSpec` 判断记录）；`spawn_ref` 来源忽略本字段 |

### `waves[]`（`EncounterSchemas.WaveItemSchema`，对照 `EncounterDefinition.ParseWave`）

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `trigger_condition` | Expr | 是 | 波次触发条件；无缺省（`EncounterDefinition.ParseWave` 缺失即报 `DataFieldException`） |
| `spawn_refs` | Array\<Id\> | 否 | `spawn.table` 条目引用列表，缺省空列表；判断记录同 `units[].spawn_ref`，退回 `Id`，domain 校验保留在 `EncounterContentValidationRule` |

### `phases[]`（`EncounterSchemas.PhaseItemSchema`，对照 `EncounterDefinition.ParsePhase`）

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `enter_condition` | Expr | 是 | 阶段进入条件；无缺省 |
| `ai_rotation_override` | Object（Map，未登记 `Fields`） | 否 | `Map<Id, Id>`，键为具体参战单位 id 或模板 id（见 `EncounterHost.ApplyPhase` 判断记录）。**判断记录（Map 型待后续契约扩展，ADR-0019 首批范围外）**：现有 `FieldSchema.Fields` 只表达固定键清单，无法表达任意键的 Map；本轮不为此扩展 `FieldKind`，本字段维持"存在且是对象"的向后兼容校验（`AiRotationOverride_ArbitraryKeys_NoUnknownSubfieldReported` 锁定这一行为） |
| `on_enter_hook` | Id | 否 | `found.hook` 钩子 id。判断记录（分层边界，同 `skill.script.hook_id`）：`found.hook` 当前无实现级 schema 登记，`Reference` 到未加载表恒判定引用失效，暂退回 `Id`，待补齐登记后再升级 |

### `arena_rules`（`Fields`：`bounds_shape` + `reset_if_leave`）

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `bounds_shape` | Object（`Variants`，判别字段 `kind`） | 是 | 见下"`bounds_shape` 变体参数表"；`EncounterDefinition.FromRecord` 对 `arena_rules` 存在但 `bounds_shape` 缺失/非对象直接抛 `DataFieldException` |
| `reset_if_leave` | Bool | 否 | 缺省 `false`（`EncounterDefinition.FromRecord`：`TryGetValue` 失败或非 `true` 均按 `false` 处理） |

### `bounds_shape` 变体参数表（`EncounterSchemas.BoundsShapeSchema`，对照 `EncounterShapeJson.Parse`）

`EncounterShapeJson.RequireNumber`/`ParseVec2` 对每种形状的全部子字段均无缺省值（缺失即抛
`FormatException`），四个分支下的全部子字段均登记为必填。

| `kind` | 参数（名/类型/必填） | 对照 `Shape` 工厂方法 |
|---|---|---|
| `circle` | `origin`:Vec2/是、`radius`:Number/是 | `Shape.Circle(center, radius)` |
| `cone` | `origin`:Vec2/是、`direction`:Number/是、`angle`:Number/是、`radius`:Number/是 | `Shape.Cone(origin, direction, angle, radius)` |
| `line` | `origin`:Vec2/是、`direction`:Number/是、`length`:Number/是、`width`:Number/是 | `Shape.Line(origin, direction, length, width)` |
| `rect` | `origin`:Vec2/是、`half_extents`:Vec2/是、`rotation`:Number/是 | `Shape.Rect(origin, halfExtents, rotation)` |

四个键与 `Core.Foundation.EngineAdapter.ShapeKind` 枚举全集一致，`EncounterSchemaCoverageTests.
BoundsShapeVariantKeys_MatchShapeKindNamesFullSet` 用测试锁死这一致性（同源于
`AreaTriggerSchemas.ShapeKindValues`，但本模块不跨目录复用该常量，避免引入
`encounter`↔`area_trigger` 的横向耦合）。

### `initiative_override` 子字段表（`EncounterSchemas.InitiativeOverrideSchema`，对照 `TimeModelSwitch.SetPendingOverride`）

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `policy` | Enum(`initiative_stat`\|`action_points`\|`fixed_order`) | 否 | 缺省不覆盖先攻策略，仍按 `CombatModel` 默认判断。`SetPendingOverride` 对未知取值直接抛 `InvalidOperationException`（不同于 `combat_mode_override` 的宽容降级），登记为 `Enum` 把拼写错误提前到数据加载期拦下 |
| `params` | Object | 否 | `{initiative_stat?, action_points_per_turn?}` |
| `params.initiative_stat` | Reference(`stat.definition`) | 否 | 覆盖离散模式先攻属性（`TimeModelSwitch.EffectiveInitiativeStat`）；`stat.definition` 属 L1，`encounter`（L4）可 `Reference` |
| `params.action_points_per_turn` | Number | 否 | 覆盖离散模式每回合行动点上限 |

## 校验规则（`EncounterContentValidationRule.cs`）

| 规则/检查项 | 检查项名 | 说明 |
|---|---|---|
| `units[]` 二选一 | `encounter_content` | `spawn_ref`/`template_ref` 必须二选一（跨字段约束，`FieldSchema.Fields` 无法表达） |
| `spawn_ref`/`spawn_refs[]` domain 校验 | `encounter_content` | domain 必须是 `spawn`（本模块与 `spawn` 模块决耦考虑，见上"子结构登记表"判断记录） |

以上规则通过 `IDataRegistry.RegisterValidationRule` 注册；`GameplaySchemaCatalog.
RegisterEncounterSchemas` 里 `new EncounterContentValidationRule(exprSchema)` 这一行**不需要改动**
——构造签名未变（仍接受一个非空 `IExprSchema`，`exprSchema` 为 `null` 时抛
`ArgumentNullException`），调用方无需修改；`exprSchema` 参数不再被存成字段（详见下"退役规则"）。

### 退役规则（ADR-0019 / F1b）

`EncounterContentValidationRule` 原本对 `waves[].trigger_condition`/`phases[].enter_condition`
两处手写了一份 `ExprParser.Parse` 尝试（因为它们嵌套在 `FieldKind.Array` 内，`DataRegistry` 内置的
`expr_parsable` 校验项此前只覆盖顶层 `FieldKind.Expr` 字段）。ADR-0019 起
`EncounterSchemas.WaveItemSchema`/`PhaseItemSchema` 把这两处显式登记为 `FieldKind.Expr` 子字段，
`DataRegistry.ValidateExprField` 递归覆盖到子结构后已完整取代（且比旧版更严格：旧版只
`ExprParser.Parse`，新版额外跑 `ExprValidator.Validate` 静态校验）——**两处手写检查已整条删除**，
不再对同一缺陷双重报告（`EncounterSchemaCoverageTests.WaveTriggerCondition_Unparsable_
ReportsExactlyOneExprParsableIssue`/`PhaseEnterCondition_Unparsable_ReportsExactlyOneExprParsableIssue`
用测试锁死"恰好一条 `expr_parsable`"）。`units[]` 二选一、`spawn_ref`/`spawn_refs[]` domain 校验属
登记表达不了的跨字段/跨表业务判断，继续保留（见上表）。

### `phases[].enter_condition` 专项结论

任务书点名核实"`phases[].enter_condition` 的 Expr 校验若已由子层 `Expr` 登记覆盖则退役该部分"——
结论：**已退役**。`EncounterSchemas.PhaseItemSchema` 把 `enter_condition` 登记为
`FieldKind.Expr`，`DataRegistry.RunFieldValidation` 递归到 `phases[]` 数组元素时会对该子字段跑
`ValidateExprField`（与顶层 `victory_condition`/`defeat_condition` 共用同一份实现，检查名同为
`expr_parsable`），语义完全覆盖旧版手写检查，且额外跑 `ExprValidator.Validate` 静态校验更严格；
`EncounterContentValidationRule.Validate` 里原来的 `phases` 遍历分支已整段删除。

## Map 型待后续契约扩展一览（ADR-0019 首批范围外）

本轮（F1b）内发现的、因"动态键 Map"而未登记 `Fields` 的字段：

| 字段 | 说明 |
|---|---|
| `encounter.def.phases[].ai_rotation_override` | `Map<Id, Id>`，键为参战单位 id 或模板 id，值为 `ai.rotation` 引用；维持"存在且是对象"校验 |

## 判断记录

1. **`encounter.level.encounter_sequence`/`entry_difficulty_options` 未升级为 `Array`+
   `Item=Reference`**：两者都是"有序/无序 Id 引用列表"，不含嵌套 Object/Array 结构——ADR-0019 的
   `Fields`/`Item`/`Variants` 机制面向"复合字段的子结构"（04 第 3.2 节），`FieldKind.IdList`
   本身不支持挂载 `Item`（`FieldSchema` 构造期"Item 仅 Array 字段可设"检查）。把它们改造成
   `FieldKind.Array` + `Item=Reference(...)` 属于"用引用完整性替换纯格式校验"的额外升级，不是
   "给复合字段登记缺失的子结构"；核实后结论是两者均不复合（任务书原句"encounter.level 的有序
   遭遇序列若为复合"，核实结论为否），且升级会让"`encounter.level` 与 `encounter.def`/`diff.tier`
   必须同一个 `DataRegistry` 里加载"成为强约束，本轮保持现状不变。
2. **`template_ref` 升级为 `Reference(creature.template)`，`spawn_ref`/`spawn_refs[]` 保持
   `Id`**：两者形式相似（都是"生成参战单位的来源引用"），处理不同的原因是分层与决耦意图不同——
   `creature.template` 属 L3，`encounter`（L4）已有正当程序集引用路径，且 `CreatureFactory.
   RequireTemplate` 对未知模板硬抛异常，`Reference` 与运行时语义完全一致；`spawn.table` 引用则
   刻意保持"只经 `SpawnRequester` 委托、不直接依赖 `core/gameplay/spawn`"的决耦（见
   `../README.md` 判断记录 2），`Reference` 的跨表存在性检查会与这一决耦意图冲突，因此退回 `Id`，
   domain 校验继续留在 `EncounterContentValidationRule`。
3. **`initiative_override.params.initiative_stat` 登记为 `Reference(stat.definition)`**：
   `stat.definition` 属 L1，`encounter`（L4）经 `core/rules/common` 传递可见，属正当跨层引用；
   `TimeModelSwitch` 本身不检查该值存在性（只是 `Id.TryParse` 后原样存起来），但登记为 `Reference`
   在数据加载期即可拦下"覆盖到一个不存在的属性"这类拼写错误，属于比运行时更严格但方向正确的
   加固（同 `skill.apply_aura.aura_def` 一类"运行时缺目标即崩溃/无意义"场景的登记惯例）。
4. **测试装配（`TestSupport.MakeRegistry`）新增加载 `creature.template`/`creature.tier_definition`
   两张表**：`template_ref` 升级为 `Reference` 后，本模块既有测试（`EncounterHostTests`/
   `LevelHostTests`）若不提供可解析的 `creature.template` 记录，会在 `TestSupport.MakeRegistry`
   阶段就因 `reference_integrity` 阻断——补齐 `creature.sample_boss`/`creature.sample_boss2`
   两条最小记录（含其引用的 `creature.tier.sample` 一条 tier 记录）后全部既有测试保持绿色，
   不改变任何断言本身。
