# L2 规则层 · common 共享契约

职责：为 L2 规则层四个模块——技能 `core/rules/skill`、战斗 `core/rules/combat`、目标选择
`core/rules/targeting`、AI `core/rules/ai`——提供共用的契约接口与数据类型（见
[06_规则层_属性技能战斗AI.md](../../../architecture/06_规则层_属性技能战斗AI.md)、
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) 第 3 节"同层模块之间只经契约接口与事件
总线交互，不得互相持有对方内部状态"）。**本目录只定义类型与接口，禁止任何业务逻辑**（结算怎么算、
施法管线怎么走、AI 怎么决策一律不在这里，属于各自模块的职责）；四个模块随后可以并行实现，互相之间
只依赖本目录的类型，不直接引用对方模块内部的类型。

依赖：仅 `Core.Rules.csproj` 已声明的 `Core.Numbers`（及其传递引用的 `Core.Foundation`）；不引用
`Core.Carriers`/`Core.Gameplay`，不使用 `UnityEngine`、`System.Threading`、`DateTime`、
`System.Random`、`System.Reflection`（与 01 第 6 节禁止事项、11 第 2～4 节工程规范一致）。

## 目录

```
common/
  README.md
  contracts/
    IUnitAccess.cs        单位状态门面（存在性/位置/阵营/等级/朝向/存活/模板/标签）
    WellKnownPowers.cs     生命值资源类型 id 约定（arch.power.health）
    CastFailureReason.cs   施法管线失败原因码
    CastResult.cs          施法结果（Ok/Fail 工厂）
    SkillCastRequest.cs    一次施法请求（AI/玩家辅助施法共用）
    EffectKind.cs           Effect 原语枚举 + snake_case 互转
    SettlementEffectKinds.cs 结算类原语集合常量（T-N3-1，06 第 3.2 节修订段）
    AuraEffectKind.cs       AuraEffect 类型枚举 + snake_case 互转
    ControlFlags.cs         控制标志位组合
    EffectRef.cs             技能定义里的一条效果项（Kind + Params）
    EffectContext.cs         结算管线输入（不可变）
    SourceKind.cs             来源类别枚举（T-N1-6，Unknown/Player/Creature）
    HitResult.cs             命中判定结果
    ResolveResult.cs         结算管线输出（不可变）
    AuraInstanceRef.cs       光环实例句柄
    SpellModDimension.cs     SpellMod 维度 + 运算类型
    SkillFilter.cs           SpellMod.affects 过滤条件
    UnitFilter.cs             目标过滤条件（含 RelationFilter）
    ISkillHost.cs             技能模块契约
    IEffectSink.cs            效果落地出口
    IAuraQuery.cs             光环状态只读查询
    IStaticImmunityProvider.cs 内容驱动的静态免疫查询（阶段 3 整理，见 combat/skill README）
    ICombatHost.cs            战斗模块契约
    IThreatTable.cs           仇恨表契约
    ITargetHost.cs             目标选择模块契约
    TargetResolution.cs        T-N3-8：TargetOverflowPolicy 枚举 + ResolveWithCoefficients 返回值
    BehaviorState.cs          AI 行为外壳状态机状态
    IAiHost.cs                 AI 模块契约
    IExprHostFactory.cs        Expr 求值宿主工厂
    Events.cs                  RulesEventKeys + 06 第 8 节强类型事件
  tests/
    ...（见下方"谁实现、谁调用"矩阵后的测试清单）
```

## 谁实现、谁调用

| 契约 | 由谁实现 | 谁调用 |
|---|---|---|
| `IUnitAccess` | L3 载体层（把 `Unit` 运行期实例接入）或测试假实现 | skill、combat、targeting、ai 四个模块 |
| `ISkillHost`、`IEffectSink`、`IAuraQuery` | `core/rules/skill` | combat（结算触发、免疫/吸收/控制判定）、ai（Rotation 条件判断光环）、targeting（`current_target` 等来源可能需要读取施法状态） |
| `ICombatHost`、`IThreatTable` | `core/rules/combat` | skill（`school_damage`/`heal` 等效果原语的落地结算入口）、ai（进出战斗判定、Rotation 条件） |
| `ITargetHost` | `core/rules/targeting` | skill（施法管线步骤 6 目标解析）、ai（Rotation 条件里的 `target` 分组求值对象） |
| `IAiHost` | `core/rules/ai` | 主循环（连续模式按 tick、离散模式由 `TurnScheduler` 按行动者调用） |
| `IExprHostFactory` | 集成任务（不在本任务范围内） | skill（Proc/SpellMod 条件）、ai（Rotation 条件）、targeting（`filters` 条件表达式） |
| `IStaticImmunityProvider` | `core/carriers/creature`（`CreatureImmunityProvider`），可选依赖，未注入时 `NullStaticImmunityProvider` | combat（`Resolver` 免疫判定叠加）、skill（`AuraHost.IsImmune`/`ApplyAura` 对 `control` 类效果叠加） |

`WellKnownPowers`、`EffectKind`/`AuraEffectKind`/`ControlFlags`/`HitResult`/`BehaviorState`/
`CastFailureReason` 等纯数据类型/枚举不属于上表——它们是四个模块共用的"词汇"，不由谁"实现"，
按需直接引用。

## 设计要点与判断记录

1. **`ISkillHost.ApplyStatMod` 的 `op` 参数复用 `Core.Numbers.StatBlock.StatModifierOp`**：任务书
   拍板明确要求，不在本层重新定义一套字符串常量或平行枚举，避免同一概念两份定义漂移。

2. **`ISkillHost.FindUnits` 比 06 第 7 节原始签名多一个 `origin` 参数**：05 第 3.5 节的 `Shape`
   本身已带绝对坐标，但 `skill.def.target_shape_ref` 指向的是可被多个技能/多个施法者复用的形状
   模板，模板本身不预先绑定坐标；`origin` 是调用方（通常是当前施法者位置）用来锚定模板的补充参数，
   语义上仍是 06 §7 `findUnits(shape, filter)` 的展开，见 `ISkillHost.cs` 内注释。P2-01 根治
   （外部审计 audit-c9ff301-20260909）：`SkillHost.FindUnits` 此前恒返回空列表，现委托注入的
   `ISpatialQuery.QueryShape` 做形状判定（`shape.WithOrigin(origin)` 只平移锚点，不重算朝向）、
   过滤存活/`Exclude`/标签，`Relation`（Hostile/Friendly/Neutral/Self/NotSelf）以
   `UnitFilter.Exclude` 兼任判定参照单位（签名本身没有 casterId 参数），需要额外注入
   `Core.Numbers.Faction.IFactionMatrix`（可选，未注入时 Hostile/Friendly/Neutral 判不通过）；
   未注入 `ISpatialQuery` 时行为不变（记诊断、返回空），见 `SkillHost.FindUnits`/`PassesRelation`
   判断记录。

3. **`SkillFilter.Matches` 三维度取 OR 而非 AND**：06 第 3.5 节只给出三个过滤维度是什么，没有给出
   跨维度的布尔组合方式；本模块按"影响范围是并集式声明"的常见 SpellMod 用法取 OR，判断记录见
   `SkillFilter.cs` 内注释。

4. **`targeting.resolved`/`ai.decision_made` 两个"建议"事件与 06 第 5 节"目标选择本身不发事件"
   存在冲突，仍予登记**：任务书"必读"清单明确要求读取 `found.event_catalog.json` 中
   `targeting.resolved` 行字段，且 targeting 模块要与 skill/ai 协作离不开某种通知手段；本文件
   选择"两边都留着"——按建议行的字段登记强类型事件类型，供后续模块视需要选用，不强制发布，若
   设计层最终确认 06 第 5 节为准，直接删除这两个类型不影响其余契约。

5. **`unit.died` 的 `killerId` 放宽为可空**：06 事件表字段列未标注是否可空，但环境死亡（跌落、
   脚本赐死）场景不存在"击杀者"，放宽为 `Id?` 覆盖这类场景，有明确攻击者时正常传入。

6. **`unit.respawned.policy` 用强类型 `RespawnPolicy` 枚举而非裸字符串**：06 第 4.6 节把死亡复活
   策略列成固定的三值表格（`respawn_point`/`reload_save`/`permadeath`），与 `CastFailureReason`/
   `HitResult` 等其它"文档给出固定有限枚举"的字段一致处理，优于事件目录建议字段表里的裸字符串。

7. **`EffectContext`/`ResolveResult` 均为不可变类型**：呼应 06 第 4.1 节结算管线"每一步只依赖
   上一步的输出...不允许跨步骤回填修改，保证同种子同输入同结果"（拍板决策 3）；集合类字段
   （如 `ResolveResult.Steps`）在构造期做防御性拷贝，构造后外部持有的原始列表被修改不影响本实例。

8. **C03/C08 收口（外部审计 7e63d66 第四轮）：`IAuraQuery` 新增两个 C#8 默认接口成员，均不强制
   既有测试假实现连带改动**：
   - `ConsumeAbsorb(Id unitId, Id school, double amount, int triggerChainDepth)` 重载——吸收池
     耗尽会移除对应光环实例并发布 `aura.removed`，这个移除本应像 `dispel`（`ITriggerChainEvent`
     机制，见本文件 `AuraRemovedEvent`/`ITriggerChainEvent` 相关判断记录）一样携带产生它的触发链
     深度、受 `MaxTriggerDepth` 收敛约束，此前恒以深度 0（根事件）发布，绕开了收敛预算（外部
     审计 C03 复现：永久 proc 光环监听 `aura.removed`、借助"造成恰好耗尽自身吸收池的伤害"反复
     触发自己）。默认转发到既有的三参数重载（等价于此前恒 0 的行为）。
   - `event Action<Id targetId, Id defId, Id oldInstanceId, Id newInstanceId> InstanceReplaced`——
     `StackOverflowPolicy.Replace` 换实例句柄时的同步通知，供按句柄记账的调用方（如
     `core/carriers/item.EquipmentHost`）原子迁移自己的记录（外部审计 C08 复现：装备 A/B 共享
     同一光环槽位，Replace 换句柄后卸下其中一件会把另一件应有的光环误清空）。默认 `add`/`remove`
     均不做任何事。
   - 两者都只有 `core/rules/skill.AuraHost` 提供真正实现；`core/rules/assembly.RulesAssembly`
     内部的 `DeferredAuraQuery` 代理必须显式转发这两个新成员，不能依赖默认接口成员的隐式转发
     （否则深度/事件订阅会在代理层悄悄失效），见 `core/rules/assembly/README.md` 同编号条目。
     本接口已有的多个 combat/expr_host/carriers 测试假实现（改动范围不允许连带修改它们）继承
     默认实现即是安全的等价降级——它们本就不模拟 `Replace` 策略或吸收耗尽的深度传播。

9. **T-N1-6（[ADR-0030](../../../architecture/adr/0030-属性系统派生换算与来源类别.md) 决策 5；
   06 第 4.1 节 2026-09-14 修订段）：`EffectContext.SourceKind` 走新增十七参构造函数、
   `IUnitAccess.GetSourceKind` 走 C#8 默认接口成员，均不强制既有测试假实现连带改动**：
   - `SourceKind`（`Unknown|Player|Creature`）新增枚举，`Unknown` 是"载体类型未知/未显式提供"
     的保守占位（同 `IAuraQuery` 前一条判断记录的"默认值不改变既有行为"惯例），不是契约新增的
     第三种玩法取值——06 §4.1 原文只登记 `player|creature` 两个取值，详见该类型注释。
   - `EffectContext` 既有十五/十六参数构造函数（ABI 规则禁止改动签名）不带 `sourceKind`，恒得到
     `SourceKind.Unknown`；新增的十七参数构造函数（与既有两个在参数个数上不重叠）携带真实值。
   - `IUnitAccess.GetSourceKind(Id) => SourceKind.Unknown` 默认返回未知，只有
     `core/carriers/unit.WorldUnitAccess`（本接口目前唯一的生产实现）覆盖为按
     `Entity.Kind`（`PlayerUnit`/`CreatureUnit` 既有判定依据，不新造标记字段）返回真实值——本接口
     十余个 combat/skill/targeting/ai/gameplay 各模块的测试假实现（`FakeUnitAccess`/
     `StubUnitAccess`/`NullUnitAccess` 等）继承默认实现即是安全的等价降级。
   - 全部生产构造点（`CastPipeline.ExecuteEffectsOnly`、`AuraHost.FirePeriodic`——经新增可写属性
     `AuraHost.Units` 查询、`core/carriers/projectile.ProjectileHost.ApplyOnHitEffects`）经
     `IUnitAccess.GetSourceKind` 查询真实值；`EffectDispatcher.ApplyDamageOrHeal` 重建 outbound
     上下文时原样转发入参 `context.SourceKind`（同该方法对 `TriggerChainDepth`/
     `AttackInstanceId` 的既有转发惯例，不重新查询）——四处均已逐一核对（grep `new EffectContext(`
     全部生产/测试构造点，任务汇报逐条列出文件:行），任一处漏填都会让 06 §4.1"目标乘区"步骤
     读到的 `sourceKind` 静默退化为 `Unknown`，是本任务表"风险"段点名的遗漏形态。

10. **T-N3-1（[ADR-0031](../../../architecture/adr/0031-技能数值契约与预算.md) 决策 2/12；06 第
    3.2 节 2026-09-14 修订段）：`SettlementEffectKinds` 按效果原语类型登记，`apply_aura` 一项无
    条件计入、不下钻解析具体引用的光环定义**：06 原文"结算类原语集合"（`school_damage`、
    `weapon_damage_pct`、`heal`、`projectile`，以及 `apply_aura` 所引光环含 `periodic_damage`/
    `periodic_heal`/`mod_stat`/`control`/`absorb` 任一效果者）不是一份能直接按
    `effects[].kind` 这一层扁平判定的清单——前四项是无条件的效果原语类型，但 `apply_aura` 是
    否算结算类取决于它引用的 `skill.aura_def` 自身的效果构成，不是 `EffectKind.ApplyAura` 本身
    的固有属性。`SettlementEffectKinds.All`/`IsSettlement(string kind)` 只按 `kind` 这一层判定，
    把 `apply_aura` 无条件计入集合（"按效果原语类型可能是结算类"），不解析 `aura_def` 引用；
    真正需要"这条 `apply_aura` 是否满足光环含五种效果之一"这一更细判定的调用方（T-N3-9
    `SkillBudgetAnalyzer`、T-N3-10 独立求值组件）需要另行解析光环定义逐条核对，不是本类型职责
    范围——**设计层裁定（2026-09-15）：采纳**，按效果原语类型这一层判定即可，精确判定（所引
    光环含结算效果）由 `SkillBudgetAnalyzer` 等消费方消费时另行解析落实，不在
    `SettlementEffectKinds` 内新开重载。

11. **ADR-0050《技能宿主契约纳入技能簿查询与学习成员》：`ISkillHost` 新增四个 C#8 默认接口成员，
    默认语义按"只读查询可显式降级、写路径不能静默降级"两分**（消费方反馈：`GetKnownSkills`/
    `LearnSkill`/`LearnFromBook`/`Knows` 此前只在具体类 `Core.Rules.Skill.SkillHost` 上，面向接口
    编程做不到）：
    - `Knows(Id,Id)`/`GetKnownSkills(Id)`（只读查询）默认分别返回 `false`/空列表——与
      `GetSkillReadiness`/`FindUnits` 既有的"查询类默认实现允许显式降级"惯例一致，语义是"该宿主
      不支持技能簿"。
    - `LearnSkill(Id,Id)`/`LearnFromBook(Id,Id,int)`（写路径）默认改为抛
      `System.NotSupportedException`，不悄悄什么也不学——AGENTS.md §3"运行时路径不静默降级"
      明确区分"只读分析类入口"与写路径，只允许前者显式标记降级；若写路径也默认 no-op，调用方会
      得到"已经学会"的错觉，但随后 `Knows`/`GetKnownSkills` 查不到，是"看似接入了、实际上悄悄
      失效"的陷阱（同 C03/C08 收口判断记录对同类陷阱的定性）。
    - 生产实现 `Core.Rules.Skill.SkillHost` 既有的同名公开方法签名/行为逐字不变，新增四个**显式
      接口实现**（`IReadOnlyList<Id> ISkillHost.GetKnownSkills(Id) => GetKnownSkills(id)` 等）转发
      到既有公开方法，不落到默认实现——判断记录（为什么不用隐式实现）：若让既有公开方法直接隐式
      满足新增接口成员，编译器会把它们的物理 IL 属性从普通实例方法改写成 `virtual sealed`（隐式
      接口实现的必然结果），`toolchain/abi_surface` 按物理签名/方法属性逐字节比对，会把这个属性
      变化误判成四处 breaking change（即便已用 `toolchain/abi_probe.ps1` 的旧编译 consumer 换新
      DLL 不重编译实测验证运行完全正常）；改用显式接口实现后，既有公开方法的物理签名逐字节不变，
      显式接口实现方法本身是 IL private 方法，不计入 `abi_surface` 的表面成员，`breaks=0`。
      `core/rules/assembly.RulesAssembly` 内部的 `DeferredSkillCastQuery` 代理必须显式转发这四个
      新成员，不能依赖默认接口成员的隐式转发，见 `core/rules/assembly/README.md` 同一判断记录。
      本接口已有的多个 combat/targeting/ai 测试假实现（改动范围不允许连带修改它们）继承默认实现
      即是安全的等价降级——它们本就不模拟技能簿账本。
    - 装备/光环一类"带来源、区分永久/临时"的学习重载（`LearnSkill(Id,Id,Id,bool)`/
      `ForgetSkill(Id,Id,Id)`）未被消费方点名，不在本次收口范围，`core/carriers/item.SkillGranter`
      委托继续作为这部分残余缺口的绕行手段（见该类型判断记录）。

12. **`GetSkillNameKey` 一并纳入 `ISkillHost` 契约**（ADR-0048/ADR-0050 合并两条并行分支时暴露）：
    `GetSkillNameKey` 原是 ADR-0048 消费方反馈第 3 条给 `Core.Rules.Skill.SkillHost` 新增的普通
    公开方法；同一时间 ADR-0050 让 `presentation/ui.SkillHostSkillBookQuery` 改为面向
    `ISkillHost` 接口装配（见判断记录 11）。两条分支各自单独编译都成立，合并后
    `SkillHostSkillBookQuery` 持有的字段类型已经是接口，却仍要调用只在具体类上的
    `GetSkillNameKey`，编译不过。裁决：不回退接口化，也不让消费方改回持有具体类，而是把
    `GetSkillNameKey` 一并提升为 `ISkillHost` 的 C# 8 默认接口成员，默认返回 `null`——与
    `Knows`/`GetKnownSkills` 同一套"只读查询允许显式降级"惯例（本方法不产生副作用，语义是
    "该宿主取不到/不支持这个技能的显示名键"，与 ADR-0048 原有的"缺省不渲染不回退占位文案"
    口径一致，不需要判断记录 11 里写路径那套"禁止静默降级"的更严格口径）。生产实现
    `Core.Rules.Skill.SkillHost` 既有同名公开方法签名/行为不变，新增一个显式接口实现转发
    （理由同判断记录 11：隐式实现会改写既有方法物理签名，被 ABI 探针误判为破坏）。
    `core/rules/assembly.RulesAssembly` 内部的 `DeferredSkillCastQuery` 代理同样补上这一个
    成员的显式转发。详见 `core/rules/skill/README.md` 判断记录 59。

## 不负责什么

- 不实现施法管线、结算管线、仇恨表、行为外壳状态机等任何具体算法——那些是 skill/combat/ai 各自
  模块在 `core/rules/<module>/core/` 下的职责。
- 不解析任何数据表（`skill.def`/`skill.aura_def`/`ai.rotation` 等）——数据到强类型对象的解析由
  各模块自己完成，本目录只提供解析结果落地时使用的目标类型（如 `EffectRef`/`SkillFilter`）。
- 不提供 `IExprHostFactory` 的默认实现——本任务只声明接口，具体把 self/target/combat/enemies
  分组接到 `IUnitAccess`/`IStatHost`/`IPowerHost` 等契约上的工作留给集成任务。
