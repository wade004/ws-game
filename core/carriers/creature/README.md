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
    CreatureContentValidationRule.cs  模块专属校验扩展点（npc_flags 取值合法性已收口进 CreatureSchemas 的 FieldSchema.WithAllowedValues，见消费方反馈第 28 条）
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
`control_immune`（06 第 3.9 节"Boss/精英级免疫标志"的落地位）、`control_immune_categories`
（T-N3-6 新增，与 `control_immune` 并存的按类别声明，见判断记录 8）、`sort_weight`（仅供内容
管线排序，运行期不读取）。

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
   `IStatHost.RegisterUnit` + `SetBase`（只写 `base_stats × tier.stat_multiplier`，不再叠加曲线
   成长，见判断记录 5）→ `IProgressionHost.RegisterUnit` + `ApplyGrowthToCurrentLevel`（有
   `stat_growth_ref` 时，后者把"2 级到出生等级"的曲线成长以修正形式写入）→ `IPowerHost.RegisterUnit`
   （此时属性已就绪——基础值 + 成长修正都已写完，`max_source: stat` 的资源类型才能正确算出上限）→
   `AiRegistrar`（有 `ai_behavior_ref` 时）→ 发 `creature.spawned`。这个顺序不是随意的：属性必须
   先于资源池注册，资源池上限依赖属性。

5. **成长统一由 `Core.Numbers.Progression.ProgressionHost` 的修正承载，本模块不再重复解析
   `prog.level_curve`（消费方反馈第 36 条根治）**：此前 `CreatureFactory.ApplyStats` 独立解析
   `prog.level_curve` 并把"2 级到出生等级"的累计成长直接叠进 `SetBase` 写的基础值；该单位一旦
   经 `IProgressionHost.AddXp` 真实升级，`ProgressionHost.ApplyGrowth` 又会把"2 级到新等级"整段
   成长重算并整体覆盖写入修正——`[2..出生等级]` 这一段因此被基础值与修正各计了一次（真实探针：
   出生等级 2、出生 strength 7，升到 3 级实测 11，应为 9）。根治为"成长统一只由修正承载，单一
   来源"：`ApplyStats` 只写 `base_stats × tier.stat_multiplier`；`Spawn` 在
   `IProgressionHost.RegisterUnit` 之后紧接着调用新增的
   `IProgressionHost.ApplyGrowthToCurrentLevel`——与升级（`AddXp`）、读档（`RestoreState`）共用
   `ProgressionHost` 内部同一份聚合实现（先移除旧的 `prog.growth` 修正、再按当前等级重新算出整段
   累计值写入），不是另外维护一份公式，出生等级 1（既有示例数据现状）时这一步是空操作
   （`[2..1]` 区间不存在），行为不变。

6. **`summon_only` 标志不在本模块拦截刷新表生成**：07 第 2.2 节"`summon_only` 只能由 `summon`
   效果生成，不进入常规刷新表"——这一约束的执行方是 `core/gameplay/spawn`（L4 刷新表，不在本
   任务范围），`CreatureFactory.Spawn` 本身对任何调用方一视同仁（`ISummonHost` 与未来的刷新表都
   经同一个 `Spawn` 方法生成实体），"不得出现在刷新表"是数据/刷新策略层面的约束，不是
   `Spawn` 方法参数校验的职责。

7. **`creature.tier_definition.sort_weight` 只声明 schema 字段，不落地为运行期存储**：该字段仅
   供内容管线/编辑器按权重排序展示，`CreatureFactory` 解析 tier 记录时不读取它——声明一个永远
   不被读取的私有字段会触发"字段赋值后从未使用"的编译警告（本仓库
   `TreatWarningsAsErrors=true`），因此干脆不为它保留运行期存储位。

8. **`control_immune_categories`（T-N3-6，[ADR-0031](../../../architecture/adr/0031-技能数值契约与预算.md)
   决策 8；06 第 3.3 节 2026-09-14 修订段）：与 `control_immune` 并存的按类别声明，不改写同一
   字段**：`creature.tier_definition` 新增可选 `control_immune_categories`
   （`Array<Enum>`，取值固定六值 `stun|root|silence|disarm|fear|polymorph`，登记为
   `Core.Rules.Common.ControlCategoryValues.All`——与 `core/rules/skill` 的
   `skill.aura_def.effects[kind=control].params.category` 共用同一份取值集合，见该模块 README
   判断记录 53，避免两处漂移）。**判断记录（并存而非改写，同 `control_immune` 既有语义不变的
   硬性规则）**：07 原文未在本次修订段直接改写 `creature.tier_definition`（该表本就是 04/任务书
   补录，不在 07 正文），06 原文只说"`creature.template` 的控制免疫标志按类别声明"（散文表述，
   未点名是改写既有布尔字段还是新增字段），任务书据"优先新增并存字段+兼容读取（不升版本、零
   迁移风险），除非 07 原文明确改写同一字段"裁定采纳并存策略——不升 `currentSchemaVersion`、
   不需要迁移函数，旧数据/旧存档不受影响。
   <br/><br/>
   **写入路径**：`CreatureFactory.LoadTiers` 解析该数组字段（遍历 `JsonArray` 取字符串元素，
   惯例同 `SkillDefCache` 解析 `interrupt_flags` 的既有写法）存入 `TierInfo.ControlImmuneCategories`；
   `Spawn` 在既有"整体标记"写入逻辑（`tier.ControlImmune && _options.ImmunityTagPrefix.HasValue`）
   之后新增一段独立循环，逐条按 `CreatureImmunityProvider.ControlCategoryPrefix`
   （`control_category.<category>`，`internal const`，供两个类型共用避免字符串字面量漂移）写入
   `CreatureUnit.Immunities`——**不受 `_options.ImmunityTagPrefix` 是否配置影响**：该选项只是
   "整体控制免疫"这个便捷标记的开关，本字段是内容直接声明的具体类别子集，两者是正交维度、各自
   独立写入。
   <br/><br/>
   **`CreatureImmunityProvider.IsControlCategoryImmune`（`IStaticImmunityProvider` 新增默认接口
   成员的显式覆盖，过 `InterfaceDefaultMemberForwardingTests` 通用门禁）判定顺序**：①命中
   `_controlImmuneMarker`（tier 整体标记）→对任意类别都返回 `true`（旧布尔迁移等价：
   `control_immune=true` → 全部类别免疫，不需要在数据里逐一枚举六值）；②存在
   `control_category.<category>` 条目且精确匹配（`Ordinal`）→`true`；③其余（含未登记/拼写错误的
   类别文本、或该单位完全没有声明）→`false`（"未登记类别的控制视为不免疫"，从严处理，避免拼写
   错误被静默放大为意外的全面免疫）。`control_category.<category>` 前缀与既有 `control.<flag>`
   前缀是两个正交维度（不复用同一前缀，避免 `control.stun` 这类写法在既有
   `GetControlImmunity`/`ParseControlFlag` 里被当成未知标志位静默吃掉），`IsImmune`（学派/效果
   原语免疫）同步补一处跳过分支，两者互不参与彼此的判定。

9. **`creature.tier_definition.xp_multiplier`（T-N4-4，[ADR-0033](../../../architecture/adr/0033-等级经验模块正文与当量来源.md)
   决策 4）**：该分档的经验倍率，缺省 1（无加成），只在击杀经验（`kind=kill`）分支生效——
   `CreatureFactory.LoadTiers` 解析进 `TierInfo.XpMultiplier`，新增公开查询方法
   `TryGetXpMultiplier(Id tierId, out double multiplier)`：已登记返回 `(true, 该值)`，未登记
   返回 `(false, 1.0)`——不抛异常（同判断记录 5"成长统一由 ProgressionHost 承载"一类"本模块只
   登记/暴露数据，不反向依赖 progression 模块具体类型"的分层原则：`CreatureFactory` 不知道、也
   不引用 `Core.Numbers.Progression.ProgressionOptions`，真正把该查询结果接进
   `ProgressionOptions.ExtraXpMultiplierProvider`（与 `diff.tier.xp_multiplier` 相乘）的是
   `core/gameplay/assembly.GameplayAssembly`，见该文件判断记录）。未登记分档/`tierId` 为 `null`
   时的"退化为无分档加成"是调用方（`ExtraXpMultiplierProvider` 钩子）的职责，不是本方法的职责
   ——本方法只负责"查得到就给真实值，查不到就说明查不到"。

## 不负责什么

- 不实现刷新表（`SpawnHost`，L4）——本模块只提供 `ICreatureFactory` 供其调用，`summon_only`
  约束的强制执行留给刷新表自己。
- 不解析 `ai.rotation`/`ai.behavior_profile`/`loot.table`/`display.map`——这些表结构由各自模块
  负责，本模块只把引用 Id 原样传递（`ai_rotation_ref`/`ai_behavior_ref`/`loot_table_ref` 经
  `AiRegistrar`/`CreatureUnit.LootTableId` 传出）。
