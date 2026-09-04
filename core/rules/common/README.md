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
    AuraEffectKind.cs       AuraEffect 类型枚举 + snake_case 互转
    ControlFlags.cs         控制标志位组合
    EffectRef.cs             技能定义里的一条效果项（Kind + Params）
    EffectContext.cs         结算管线输入（不可变）
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
   语义上仍是 06 §7 `findUnits(shape, filter)` 的展开，见 `ISkillHost.cs` 内注释。

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

## 不负责什么

- 不实现施法管线、结算管线、仇恨表、行为外壳状态机等任何具体算法——那些是 skill/combat/ai 各自
  模块在 `core/rules/<module>/core/` 下的职责。
- 不解析任何数据表（`skill.def`/`skill.aura_def`/`ai.rotation` 等）——数据到强类型对象的解析由
  各模块自己完成，本目录只提供解析结果落地时使用的目标类型（如 `EffectRef`/`SkillFilter`）。
- 不提供 `IExprHostFactory` 的默认实现——本任务只声明接口，具体把 self/target/combat/enemies
  分组接到 `IUnitAccess`/`IStatHost`/`IPowerHost` 等契约上的工作留给集成任务。
