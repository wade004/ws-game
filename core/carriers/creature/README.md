# L3 载体层 · creature（生物）

职责：落地 [07_载体层_物品生物物件.md](../../../architecture/07_载体层_物品生物物件.md) 第 2 节
Creature——`creature.template`/`creature.tier_definition` 两张数据表的 schema 与解析、
`ICreatureFactory` 的默认实现 `CreatureFactory`（按模板生成/移除 `CreatureUnit` 实例，串联
L1 StatBlock/PowerSet/Progression 与 L2 AI）。供 `core/carriers/summon`、未来的
`core/gameplay/spawn`（L4，不在本任务范围）复用生成生物实体。

依赖：`Core.Rules.csproj`（及其传递引用的 `Core.Numbers`/`Core.Foundation`）、同程序集的
`Core.Carriers.Common`（`core/carriers/common`）、`Core.Carriers.Unit`（`core/carriers/unit`，
提供 `CreatureUnit`/`IUnitAccess` 的真实实现）。不引用 `Core.Gameplay`、`Core.Carriers.Item`、
`Core.Carriers.Gobj`，不使用 `UnityEngine`、`System.Threading`、`DateTime`、`System.Random`、
`System.Reflection`。

## 目录

```
creature/
  README.md
  contracts/
    NpcFlag.cs               NpcFlag 枚举（固定六值）+ NpcFlagIds（数据 Id/标签 互转）
    AiRegistrar.cs             把新生物接入 AI 行为外壳的具名委托（契约缺口桥接，见下）
    ICreatureTemplateQuery.cs  模板只读查询契约
    CreatureTemplate.cs        creature.template 记录的强类型视图 + FromRecord
  core/
    CreatureSchemas.cs         creature.template / creature.tier_definition 的 TableSchema
    CreatureContentValidationRule.cs  npc_flags 取值合法性校验
    CreatureOptions.cs         口味配置项（默认资源类型、control_immune 标记 Id）
    CreatureFactory.cs         ICreatureFactory + ICreatureTemplateQuery 的默认实现
    CreatureImmunityProvider.cs  IStaticImmunityProvider 的默认实现（阶段 3 整理，见下）
  tests/
    ...
```

## 数据表

### `creature.template`

字段见 07 第 2.1 节全部字段 + 本模块补录的 `name_key`（TextKey，07 原文遗漏但供显示名使用，
惯例同 `Core.Numbers.PowerSet.PowerTypeDefinition` 对 `name_key` 的处理）。`tier`/`stat_growth_ref`
用 `FieldKind.Reference`（目标表在本任务数据集内可加载）；`ai_rotation_ref`/`ai_behavior_ref`/
`loot_table_ref`/`display_ref` 用 `FieldKind.Id`（目标表——`ai.rotation`/`ai.behavior_profile`/
`loot.table`/`display.map`——不在本任务数据集范围内，声明为 `Reference` 会让 `reference_integrity`
校验恒报错，见 `CreatureSchemas` 判断记录）。

ADR-0024 第二批登记（04 第 3.3 节"映射登记"，取代下方已废止的 ADR-0019 F1c 判断记录）：`base_stats`
是 `Map<StatKey, Number>`（键为 `stat.definition` 的 id，动态键、值同构），现登记为
`MapSchema.ReferenceKeyTable("stat.definition", ...)`——键按 `reference_integrity` 检查其在
`stat.definition` 中存在，值登记为 `FieldKind.Number`，与 `CreatureTemplate.FromRecord` 逐键
`Id.TryParse` + `JsonNumber` 解析的形状核对一致。

### `creature.tier_definition`（补录）

07 第 2.1 节 `tier` 字段只描述"强度分档，由数据定义具体分档集合"，未给出该表字段结构；本模块
按任务书拍板补录：`id`（`creature.tier.<name>`）、`name_key`、`stat_multiplier`（默认 1）、
`control_immune`（06 第 3.9 节"Boss/精英级免疫标志"的落地位）、`sort_weight`（仅供内容管线排序，
运行期不读取）。

## 设计要点与判断记录

1. **`npc_flags`/`immunities` 数据侧写法是本模块选定的约定，不是 07 明文规定**：07 第 2.1 节只
   给出 `List<Id>` 记法，未规定具体 Id 取值格式。本模块选定 `npc_flag.<name>`（如
   `npc_flag.vendor`）作为 `npc_flags` 的数据表侧写法，并映射为单位标签 `tag.npc.<name>`（见
   `NpcFlagIds`）；`control_immune` 为 true 时另写入一个可配置的标记 Id（默认
   `immunity.control_immune`，见 `CreatureOptions.ImmunityTagPrefix`）到 `CreatureUnit.Immunities`。

2. **静态免疫（`immunities`/`control_immune`）经 `CreatureImmunityProvider` 接入 L2 免疫判定**
   （阶段 3 整理"事项三"，取代本条原判断记录"契约缺口/待办"）：`core/rules/common` 新增
   `IStaticImmunityProvider`（`IsImmune(unitId, school, kind)` + `GetControlImmunity(unitId)`），
   `core/rules/combat`（`Resolver.Resolve` 步骤 7）与 `core/rules/skill`（`AuraHost.IsImmune`、
   `AuraHost.ApplyAura` 对 `control` 类效果）在查询光环免疫/控制之外叠加查询该契约（可选构造参数，
   缺省 `NullStaticImmunityProvider`，一律不免疫）。本模块的 `CreatureImmunityProvider` 是该契约的
   默认实现，读 `CreatureUnit.Immunities`（经组装期注入的 `IWorldSim` 按 `unitId` 取回实例），
   非 `CreatureUnit` 一律按"不免疫"处理。`CreatureUnit.Immunities` 元素格式（07 原文未规定具体
   写法，本模块补录）：
   - `effect.<kind>`（`kind` 取 `EffectKindNames` 的 snake_case 文本，如 `effect.school_damage`）：
     免疫该效果原语类型，不区分学派。
   - `control.<flag>`（`flag` 取 `no_move`/`no_cast`/`no_attack`/`no_interact` 之一，同
     `AuraHost.ParseControlFlags` 惯例）：静态免疫该单项控制标志，只影响 `GetControlImmunity`。
   - 其余取值一律按"学派 id"处理（如 `school.fire`）：免疫该学派下全部效果原语类型。
   - `CreatureOptions.ImmunityTagPrefix` 配置的整体控制免疫标记（默认 `immunity.control_immune`，
     由 `CreatureFactory.Spawn` 按 `tier.control_immune` 写入）：命中时 `GetControlImmunity` 返回
     全部四个控制标志位（"tier `control_immune` → 全部控制免疫"）。`CreatureImmunityProvider` 构造
     参数 `controlImmuneMarker` 须与游戏层实际使用的 `CreatureOptions.ImmunityTagPrefix` 保持一致
     （两者未共享同一个配置对象，缺省值相同，改了前者的非默认值时需要同步传给后者）。

3. **`AiRegistrar` 是契约缺口的桥接委托**：`Core.Rules.Common.IAiHost` 只声明了
   `Evaluate`/`GetBehaviorState`/`SetRotation`，以及供遭遇脚本强制切换行为状态的方法，共四个
   （06 第 6.5/7 节），真正的
   `RegisterUnit(unitId, profileId, spawnPoint)` 是 `core/rules/ai` 的 `AiHost` 类在契约之外追加的
   "内部驱动版本"方法，不在 `IAiHost` 签名内；`core/carriers/creature`（L3）不能直接引用
   `core/rules/ai`（同层模块，见 01 第 3 节"同层模块之间只经契约接口与事件总线交互"）。本模块
   用具名委托 `AiRegistrar` 作为组装层的桥接点：组装期把
   `(unitId, profileId, spawnPoint, rotationId) => { aiHost.RegisterUnit(unitId, profileId, spawnPoint);
   if (rotationId.HasValue) aiHost.SetRotation(unitId, rotationId.Value); }` 适配成本委托签名注入
   `CreatureFactory`。

4. **`CreatureFactory.Spawn` 装配顺序固定**：`AllocateEntityId` → 构造 `CreatureUnit`（含
   `npc_flags`/`immunities`/`Tags` 装配）→ `world.AddEntity` → `IUnitAccess.SetPosition`（冗余但
   幂等，触发潜在的空间索引同步，见 `core/carriers/unit` README 判断记录 4）→
   `IStatHost.RegisterUnit` + `SetBase`（`base_stats × tier.stat_multiplier` + 曲线成长累加）→
   `IProgressionHost.RegisterUnit`（有 `stat_growth_ref` 时）→ `IPowerHost.RegisterUnit`（此时属性
   已就绪，`max_source: stat` 的资源类型才能正确算出上限）→ `AiRegistrar`（有 `ai_behavior_ref`
   时）→ 发 `creature.spawned`。这个顺序不是随意的：属性必须先于资源池注册，资源池上限依赖属性。

5. **成长曲线在本模块独立解析，不复用 `Core.Numbers.Progression.ProgressionHost` 内部实现**：
   `IProgressionHost` 契约不暴露"给定曲线与等级返回累计成长量"这一查询（只有
   `RegisterUnit`——且刻意不隐式写成长，见该接口注释判断记录），本模块需要在装配基础属性这一步
   （早于 `IPowerHost.RegisterUnit`）就拿到最终数值，因此 `CreatureFactory` 按
   `prog.level_curve` 的既定结构（`entries[].growth: Object<stat_id, Number>`）独立解析一份只读
   索引，成长口径与 `ProgressionHost.ApplyGrowth` 完全一致："从 2 级累加到当前等级"（1 级本身
   没有成长增量）。

6. **`summon_only` 标志不在本模块拦截刷新表生成**：07 第 2.2 节"`summon_only` 只能由 `summon`
   效果生成，不进入常规刷新表"——这一约束的执行方是 `core/gameplay/spawn`（L4 刷新表，不在本
   任务范围），`CreatureFactory.Spawn` 本身对任何调用方一视同仁（`ISummonHost` 与未来的刷新表都
   经同一个 `Spawn` 方法生成实体），"不得出现在刷新表"是数据/刷新策略层面的约束，不是
   `Spawn` 方法参数校验的职责。

7. **`creature.tier_definition.sort_weight` 只声明 schema 字段，不落地为运行期存储**：该字段仅
   供内容管线/编辑器按权重排序展示，`CreatureFactory` 解析 tier 记录时不读取它——声明一个永远
   不被读取的私有字段会触发"字段赋值后从未使用"的编译警告（本仓库
   `TreatWarningsAsErrors=true`），因此干脆不为它保留运行期存储位。

## 不负责什么

- 不实现刷新表（`SpawnHost`，L4）——本模块只提供 `ICreatureFactory` 供其调用，`summon_only`
  约束的强制执行留给刷新表自己。
- 不解析 `ai.rotation`/`ai.behavior_profile`/`loot.table`/`display.map`——这些表结构由各自模块
  负责，本模块只把引用 Id 原样传递（`ai_rotation_ref`/`ai_behavior_ref`/`loot_table_ref` 经
  `AiRegistrar`/`CreatureUnit.LootTableId` 传出）。
