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

10. **T-N6-3b（[ADR-0035](../../../architecture/adr/0035-数值仿真骨架为框架交付物.md) 决策 3
    "生物模板按指定等级出生"）：`ICreatureFactory`/`CreatureFactory.Spawn` 6 参重载 + 新增契约
    `ICreatureLevelScaler`**：`ICreatureFactory` 新增默认接口成员
    `Spawn(templateId, mapId, position, facing, ownerId, int level)`（ABI 硬性规则"新增走默认
    接口成员/新重载"，不改既有 5 参成员）——默认实现直接委托 5 参重载、忽略 `level`（未显式覆盖
    本成员的旧实现类/测试假类经接口引用调用时行为不变）；`CreatureFactory` 显式实现本成员，真正
    按 `level` 覆盖 `CreatureUnit.Level`（不再取 `template.Level`），使 `IUnitAccess.GetLevel`/
    经验发放/等级差计算等消费方自然读到新等级。
    <br/><br/>
    **基础属性换算**：新增窄接口 `ICreatureLevelScaler.ScaleBaseStats(template, templateLevel,
    targetLevel, baseStats)`（放本模块，因为真正的锚点表数据登记在更上层的 `core/sim`，
    `core/carriers/creature`（L3）不能反向依赖它，只能按依赖倒置声明这个薄接口、由 `core/sim`
    装配期实现并反向注入——架构 01 第 8 节"窄接口反向注入"）；`CreatureFactory` 新增 10 参构造
    重载接受可选 `ICreatureLevelScaler? levelScaler`（未注入或 `spawnLevel == template.Level`
    时原样返回 `template.BaseStats`，即"等级改、属性不变"）。分档 `stat_multiplier` 仍在换算之后
    按既有顺序相乘（`ApplyStats` 唯一改动是把 `template.BaseStats` 换成 `ResolveBaseStats` 的
    结果，倍率相乘顺序不变）。
    <br/><br/>
    **判断记录（`ProgressionHost.RegisterUnit` 改传 `spawnLevel` 而不是固定 `template.Level`）**：
    `spawnLevel` 未被覆盖时恒等于 `template.Level`，对既有调用方零变化；被覆盖时，
    Progression 内部登记的"当前等级"同步为 `spawnLevel`（否则后续 `AddXp`/`GetXpToNext` 仍按
    模板等级计算，与 `CreatureUnit.Level` 已经写成 `spawnLevel` 相互矛盾）——曲线"2..当前登记
    等级"复利是 `ApplyGrowthToCurrentLevel` 的既有既定语义，本任务只是把"当前登记等级"的来源
    从硬编码的 `template.Level` 改成 `spawnLevel`，曲线计算方式不变；若某模板既配置了
    `stat_growth_ref`、又用 6 参 `Spawn` 指定了不同等级、且未注入 `ICreatureLevelScaler`，基础
    属性仍会经曲线"2..spawnLevel"算出与未覆盖时不同的修正值——`ICreatureLevelScaler` 判断记录
    "未注入时属性不变"特指本任务新增的缩放器这一步不生效，不改写曲线这一既有独立机制的既定行为，
    两者是否要在同一模板上组合使用留给设计层/装配层口味决策，本任务不禁止也不特殊处理。
    <br/><br/>
    测试：`core/carriers/creature/tests/CreatureFactoryTests.cs`（未注入缩放器属性不变、注入线性
    缩放器按缩放器输出再乘分档、既有 5 参 `Spawn` 行为不变）；
    `core/gameplay/progression_bridge/tests/T_N6_3b_CreatureLevelOverrideKillXpTests.cs`（按覆盖
    等级出生的怪被击杀时击杀经验按新等级算，端到端）。

11. **`creature.tier_definition.gold_multiplier`（T-N6-3b，N4 遗留第 7 项；ADR-0034 决策 3 延伸；
    08 第 7.4 节"怪物掉钱 = 当量 × econ.gold_base_curve(怪物等级) × 分档倍率 ×
    diff.tier.loot_multiplier"）**：该分档的金币倍率，缺省 1（无加成）——`CreatureFactory.
    LoadTiers` 解析进 `TierInfo.GoldMultiplier`，新增公开查询方法
    `TryGetGoldMultiplier(Id tierId, out double multiplier)`，取法与
    `TryGetXpMultiplier`（判断记录 9）完全一致（已登记返回 `(true, 该值)`，未登记返回
    `(false, 1.0)`，不抛异常）——`CreatureFactory` 同样不知道、也不引用
    `Core.Gameplay.Loot.LootGoldMultiplierProvider`，真正把该查询结果接进这个钩子的是
    `core/gameplay/assembly.GameplayAssembly`，见 `core/gameplay/loot/README.md` 判断记录 17
    "T-N6-3b 变更记录"。

12. **2026-09-16 深度复审 B-S2 判断记录（建议修，已采纳）**：`xp_multiplier`（判断记录 9）/
    `gold_multiplier`（判断记录 11）两个字段补 `.WithRange(FieldRange.Range(min: 0))`——此前均
    未加范围约束，内容作者误填负值时：`xp_multiplier` 负值会让 `ExtraXpMultiplierProvider` 算出
    负经验，`ProgressionHost.AddXpCore` 对负 `amount` 直接抛 `ArgumentOutOfRangeException`（运行
    期崩溃，不是校验期诊断）；`gold_multiplier` 负值不会崩溃（`LootHost.ResolveCurrencyOutcome`
    的 `amount > 0` 才产出，负值静默变成"这一条不产出货币"），但掩盖了一个明显的数据配置错误、
    且没有诊断信息帮助定位。同 `stat.weight.weight` 既有处理口径（`WithRange(min: 0)`），schema
    版本不升。新增测试：`CreatureSchemaCoverageTests
    .TierDefinition_NegativeXpMultiplier_ReportsFieldRange`/
    `.TierDefinition_NegativeGoldMultiplier_ReportsFieldRange`。

13. **ADR-0051：生物原生交互路径（消费方反馈第 2 条根治）**：新增 `CreatureTemplate.GossipMenuRef`
    （可选，指向 `dialog.gossip_menu`，`CreatureSchemas` 登记为 `WithSoftReference`——`dialog`
    属 L4，本模块 L3 不可 `Reference`，惯例同既有 `ai_rotation_ref` 等四个软引用字段）+
    `CreatureInteractionHost`（`ICreatureInteractionHost` 默认实现，构造期只依赖 `IWorldSim`/
    `IUnitAccess`/`ICreatureTemplateQuery`）+ `CreatureInteractIntentTickHandler`（挂
    `TickPhase.TriggerEvaluation`，与 `Core.Carriers.Gobj.InteractIntentTickHandler` 共用同一个
    `"interact"` 意图 Kind，按 `Args` 是否含 `creature_instance_id`/`gobj_instance_id` 分流，互不
    重复警告，见两者判断记录）。回调类型 `CreatureGossipOpenerDelegate(unitId, creatureInstanceId,
    dialogRef)` 命名对齐 ADR-0044 的 `DialogOpenerWithSourceDelegate` 形状，但如实反映携带的是
    生物实例身份。运行时不静默降级：目标生物未登记/生物没有配置 `GossipMenuRef`/已配置但未注入
    `GossipOpener` 三种情形均经 `ICreatureDiagnostics.Warn` 留痕（新增诊断出口，惯例同
    `Core.Carriers.Gobj.IGobjDiagnostics`）；交互距离不足不记诊断（惯例同 `GameObjectHost`）。
    新增测试：`Tests.Gameplay.Assembly.GameplayAssemblyCreatureDialogInteractionTests`（装配级，
    走 `"interact"` 意图 → tick → `CreatureInteractionHost` → `DialogHost.OpenGossip` 全链路，
    断言 `VendorOpenRequestedCallback` 收到的 `npcId` 等于生物实例 id 且不等于菜单 id；另一例断言
    未配置 `GossipMenuRef` 时诊断 Warnings 计数 +1）。

14. **ADR-0067：生物原生交互分流拒绝死亡目标与死亡发起者（消费方反馈第十批第 1 条）**：
    `CreatureInteractionHost.Interact` 新增两条存活核对，均用构造期已持有的 `IUnitAccess`
    （不新增构造参数）——目标生物：`Exists`×`IsAlive` 合取为假时拒绝，返回
    `InteractOutcome.TargetDead`；交互发起者：同一判定为假时拒绝，返回
    `InteractOutcome.ActorDead`（`Core.Carriers.Common.InteractResult` 新增这两个枚举成员，只
    追加不改既有取值）。判定口径与 ADR-0061/0065 一致，不看生命值资源池是否 `<= 0`。两种情形均
    不记诊断，理由同既有"交互距离不足不记诊断"惯例——玩家点中尸体是正常游玩操作，不是内容配置
    错误。`interact` 意图分流（`CreatureInteractIntentTickHandler`）本就转调同一个 `Interact`，
    不需要另写判定。这是行为变更（此前允许与死亡生物交互），详见该 ADR。新增测试：
    `Tests.Gameplay.Assembly.GameplayAssemblyCreatureInteractDeathTests`（装配级，覆盖直连生产
    入口的目标死亡/发起者死亡拒绝、经 `"interact"` 意图的目标死亡拒绝、存活双方回归成功四例）。

15. **ADR-0069：`ICreatureInteractionHost` 新增 `HasInteractableContent`（消费方反馈——游戏接入方
    第十四批）**：`CreatureInteractionHost.Interact` 抽出一个私有方法 `HasContent(CreatureTemplate)`
    （生物模板配置了 `GossipMenuRef` 且 `CreatureInteractOptions.GossipOpener` 已注入，两者同时
    成立才算"有可交互内容"——任一缺失，`Interact` 分发到对话系统都不会真正发生，对玩家而言是同一
    种"什么都不会发生"的结果），`Interact` 原有的两段"没有配置 gossip_menu_ref"/"已配置但未注入
    GossipOpener"诊断分支改为先调 `HasContent` 判定、再按具体缺失哪一项选诊断文案，不改变既有
    诊断文案/返回值。新增的 `ICreatureInteractionHost.HasInteractableContent` 用显式接口实现转发
    到同一个 `HasContent`（目标未登记为生物实例时直接返回 `false`——本查询没有诊断出口，"目标
    未知"不是它的职责）——查询与 `Interact` 因此保证不会出现"查询说能交互、分流说不能"的分歧。
    本查询不判距离/发起者/存活：即便目标已死亡，只要内容配置存在查询仍返回 `true`（存活判定是
    `IInteractionTargetRegistry` 层单独核对的另一条件，见 `core/carriers/assembly/README.md` 对应
    判断记录）。新增测试：`Tests.Carriers.Creature.CreatureInteractionHostHasInteractableContentTests`
    （六例：有内容/无内容/内容配置但未注入回调三个分支与 `Interact` 结果一致性、死亡生物内容判定
    不受存活影响、未登记生物 id 返回 `false`、接口默认实现降级为 `true`）。

## 不负责什么

- 不实现刷新表（`SpawnHost`，L4）——本模块只提供 `ICreatureFactory` 供其调用，`summon_only`
  约束的强制执行留给刷新表自己。
- 不解析 `ai.rotation`/`ai.behavior_profile`/`loot.table`/`display.map`——这些表结构由各自模块
  负责，本模块只把引用 Id 原样传递（`ai_rotation_ref`/`ai_behavior_ref`/`loot_table_ref` 经
  `AiRegistrar`/`CreatureUnit.LootTableId` 传出）。
- T-N6-3b：不实现任何具体的等级缩放算法（如按锚点表插值）——本模块只声明窄接口
  `ICreatureLevelScaler` 并在未注入时退化为"属性不变"，真正的锚点表数据与插值算法归上层模块
  `core/sim`，装配期反向注入，见判断记录 10。

## 判断记录（生物模板新增 attack_interval 挥击间隔回退字段，2026-09-21，architecture/adr/0059-普通攻击的框架原生执行机制.md）

`creature.template` 新增可选字段 `attack_interval`（`FieldKind.Number`，非必填，`min` 严格大于
0，纯新增字段，不提升该表 `currentSchemaVersion`，不需要迁移函数）——供未装备武器的生物（本模块
不给生物提供武器槽装备机制）声明一个固定的普通攻击挥击间隔（秒）。新增
`CreatureAttackIntervalProvider`（`Core.Rules.Common.IAttackIntervalFallbackProvider` 的 L3
实现）：按 `IWorldSim.GetEntity` 取 `CreatureUnit`、按其 `TemplateId` 查
`ICreatureTemplateQuery.Get(...).AttackInterval`，取不到（非生物单位、模板未声明该字段、模板 id
未登记）时返回 `null`，不抛异常。经 `Core.Rules.Assembly.DeferredAttackIntervalProvider`（延迟
绑定代理，惯例同既有 `DeferredWeaponDamageQuery`）在 `CarriersAssembly` 构造完成后换上真实实现——
`RulesAssembly` 构造期需要一份 `IAttackIntervalFallbackProvider` 才能装配 `AutoAttackHost`，但
`CreatureFactory`/`ICreatureTemplateQuery` 的真实实现要等 `CarriersAssembly` 组装完成之后才存在，
同类循环见 `IStaticImmunityProvider`/`CreatureImmunityProvider` 判断记录。详见
`core/rules/combat/README.md` 对应判断记录。

## 判断记录（`Despawn` 不再同步注销 Stats/Powers，2026-09-23，消费方反馈第二十二批，
architecture/adr/0079-销毁时序对齐与待销毁单位跳过处理.md）

根因：`Despawn` 此前同步调用 `_stats.UnregisterUnit`/`_powers.UnregisterUnit`——两者立即生效，而
`IWorldSim` 对该实体的真正移除（连同 `AiHost` 的行为外壳登记、`EntitySpatialSyncHost` 的空间索引，
两者都靠订阅 `EntityDestroyedEvent` 自清理）要等到某次 `IWorldSim.Tick` 阶段 8（生命周期清理）。
"实体何时不再存在"因此出现两个不同时刻：两次调用之间若该实体仍有生效的 AI 移动意图，下一拍
`MovementTickHandler.ResolveSpeed` 会向已注销的 `StatHost.GetStat` 要属性抛
`InvalidOperationException`，中止整个 Tick——阶段 8 这次跑不到，实体没被真正移除，下一拍同样的
路径再抛一次，永久卡死。

根治：`Despawn` 只标记待销毁 + 发 `CreatureDespawnedEvent`（时机/内容不变），并把 `entityId` 记进
本类型新增的私有集合 `_pendingDespawnUnregistration`；本类型构造函数新订阅
`Core.Foundation.SimLoop.EntityDestroyedEvent`，处理器 `OnEntityDestroyed` 只对命中这个集合的 id
才调用 Stats/Powers 的 `UnregisterUnit`（命中即从集合移除），与世界真正移除该实体在同一批
`DispatchPending` 里一起生效。

**为什么不是让 `StatHost`/`PowerHost` 直接订阅 `EntityDestroyedEvent`（本次改动踩过的坑，已被
`RacePassiveAuraCrossMapTests` 等既有跨地图用例的真实回归拦下）**：`EntityDestroyedEvent` 不只在
`IWorldSim.Tick` 阶段 8 为待销毁实体触发，`IWorldSim.ClearAll`（地图切换）也会为**当时仍存活、
从未调用过 `Despawn`** 的全部实体无差别触发同一事件——包括玩家单位。玩家的 `Stats`/`Powers`
注册代表跨地图持久的角色状态，`GameplayAssembly.EnterMap` 重新登记的只是世界实体本身，不会重新
调用 `RulesAssembly.RegisterUnit`。若让两个 L1 宿主自己无差别响应该事件注销，玩家跨地图 ClearAll
后属性宿主未注册，光环重新施加等后续操作会抛异常。`StatHost`/`PowerHost` 的 `RegisterUnit` 有多个
调用方（本类型与 `RulesAssembly.RegisterUnit`），各自的生命周期语义不同，注销责任因此留在各自的
登记方（本类型），不集中收归两个 L1 宿主自己判断——`ClearAll` 场景下未经 `Despawn` 就被整体清空的
生物，其 Stats/Powers 注册与本次改动之前一样不会被自动注销（既有遗留 gap，不属于本次修复范围）。

新增验收：`core/gameplay/assembly/tests/ADR0079_DespawnDuringAiMovementTests.cs`（脱离引擎，全程走
真实 `GameplayAssembly` 生产装配入口；四例：两拍之间 Despawn 一个正在被 AI 驱动追击移动的单位不再
抛异常且被正确移除、同一拍内 Despawn 同样不抛、没有 AI 意图的普通 Despawn 路径行为不变、属性/
能量宿主真正移除后确已注销）。
