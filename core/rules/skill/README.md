# L2 规则层 · skill 技能系统

职责：施法管线（读条/引导/冷却/充能/公共冷却/法术队列/打断）、效果落地（`IEffectSink`）、光环
（叠加/刷新/周期/吸收/免疫/控制/驱散）、SpellMod 修正聚合、Proc 触发链——见
[06_规则层_属性技能战斗AI.md](../../../architecture/06_规则层_属性技能战斗AI.md) 第 3 节全部、第 7、8
节；落地计划 T2-4/T2-5/T2-6 三行。

依赖：`core/rules/common`（契约与事件类型）、`Core.Numbers`（`IStatHost`/`IPowerHost`）、
`Core.Foundation`（`IDataRegistryView`/`IEventBus`/`IRngHost`/`IExprHost`/`ISpatialQuery`/
`ITickPhaseHandler`）。不引用 `core/rules/combat`/`core/rules/targeting`/`core/rules/ai` 的任何具体
类型，只经 `common` 的接口交互（见 01 第 3 节"同层模块之间只经契约接口与事件总线交互"）。

## 目录

```
skill/
  README.md
  contracts/
    SkillOptions.cs        构造期口味配置 + StackOverflowPolicy 枚举
    IEffectExtension.cs    六类委托效果原语的扩展点
    ISkillDiagnostics.cs   诊断出口
  schema/
    README.md              skill.* 五张表字段 + 校验规则说明
    SkillSchemas.cs         TableSchema 声明
    SkillValidationRules.cs IValidationRule 实现
  core/
    Defs.cs                 解析后的强类型定义（SkillDef/AuraDef/ProcDef/SpellModDefRecord/SkillBookDef）
    SkillDefCache.cs        DataRecord → 强类型定义，懒解析 + 缓存
    ParamsX.cs               EffectRef/AuraEffect Params 读取帮助方法
    （阶段 3 整理：本模块原自带的临时 PermissiveExprSchema 已删除，默认改用
     core/rules/expr_host.RulesExprSchema.Base，见 SkillDefCache 构造函数注释）
    EventCorrelation.cs      Proc"事件与持有者相关性"判定
    CooldownTracker.cs       冷却/公共冷却/充能
    AuraHost.cs              IAuraQuery 实现 + 光环施加/移除/驱散/Tick
    SpellModResolver.cs      SpellMod 聚合（flat 后 pct）
    ProcHost.cs              Proc 订阅/触发/内部冷却
    EffectDispatcher.cs      IEffectSink 实现，19 个 EffectKind 分派
    CastPipeline.cs          九步施法管线 + 打断 + 触发链
    SkillHost.cs             ISkillHost 实现，组合根
    SkillTickHandler.cs      ITickPhaseHandler，挂 TickPhase.SkillPipeline
    InMemorySkillDiagnostics.cs
  tests/
    ...
```

## 组合根：构造顺序与循环依赖处理

`SkillHost` 构造函数按以下顺序组装内部组件，避免 `AuraHost`/`ProcHost`/`EffectDispatcher` 三者出现
构造期循环依赖：

1. `AuraHost` 先构造——它不需要 `ProcHost`/`EffectSink`，两者改用可写属性 `AuraHost.ProcHost`/
   `AuraHost.EffectSink` 事后回填。
2. `ProcHost` 构造时注入一个绑定到 `SkillHost.TriggerCastInternal`（方法组）的回调；该方法内部读取
   `SkillHost._pipeline` 字段，而 `_pipeline`要到构造函数末尾才赋值——C# 方法组/闭包按**调用时刻**
   求值捕获的字段，只要真正触发发生在整个构造完成之后（游戏运行期间必然如此，任何 Proc/
   `trigger_spell` 都不会在构造期触发），这个"提前绑定、稍后才有效"的写法是安全的。
3. `AuraHost.ProcHost = procHost` 回填。
4. `SpellModResolver` 构造（依赖 `AuraHost`）。
5. `EffectDispatcher` 构造（依赖 `AuraHost`/`CooldownTracker`/`SpellModResolver`/回调）。
6. `AuraHost.EffectSink = effectDispatcher` 回填（供光环周期效果结算调用）。
7. `CastPipeline` 构造（依赖以上全部）。

## 设计要点与判断记录

1. **施法管线步骤 6 的 `target_shape_ref` 语义**：06 第 3.1 节原文"指向 `target.chain_def` 或直接
   指向 `Shape` 定义"给了两种可能；任务书拍板"为空则 `ITargetHost.Resolve(target_shape_ref 指向的
   链, caster)`"，本模块据此把 `target_shape_ref` 统一当作 `target.chain_def` 的 id 直接传给
   `ITargetHost.Resolve`，不支持"直接指向 Shape"这一分支（该分支属于 `ISkillHost.FindUnits` 的
   `origin`+`Shape` 组合用法，供技能内部效果的范围查找，不是施法管线步骤 6 的目标解析入口）。

2. **视线检查依赖注入的 `ISpatialQuery` 可为 null**：任务书"经 `ISpatialQuery.HasLineOfSight`
   （若接口有）否则跳过"——本模块构造函数把 `ISpatialQuery` 声明为可空参数，为 null 时步骤 7 只做
   距离检查、跳过视线检查，不抛异常。

3. **`ISkillHost.FindUnits` 在未注入 `ISpatialQuery` 时返回空列表**：06 第 7 节该方法本身要求一个
   空间查询能力，构造期允许不注入（见判断记录 2），此时没有可委托的实现，返回空列表并记一条
   诊断警告（不是完全静默，也不抛异常，呼应第 5 节"运行时不做静默降级"精神但为可选依赖留出口）。

4. **法术队列窗口外的再次施法请求**：06 第 3.6 节只描述了"窗口内入队"的行为，未规定窗口外再次
   `CastSkill` 的处理方式。本模块拍板：窗口外一律拒绝（每单位只有一个队列槽，不支持排更多队），
   复用 `CastFailureReason.OnCooldown` 作为最接近的失败语义（施法者当前"不可用"，只是原因是仍在
   读条/引导而非真正冷却）——`CastFailureReason` 枚举由 `common` 定义，本模块不得新增值，这是现有
   取值集合下最贴近的选择。

5. **公共冷却在读条开始（步骤 8）而非完成（步骤 9）时启动**：多数同类系统里 GCD 从施法瞬间开始
   计时而不是等技能结算完才开始，本模块按此常见语义实现；`SkillOptions.GcdDuration` 的默认值
   （1.5）只是一个占位式合理起点，不代表任何产品决策，真实数值应由游戏口味配置清单给出。

6. **引导（channel）类技能的资源/冷却在引导开始时一次性扣除**：06 第 3.6 节步骤 9 只描述"读条/
   引导完成后……扣资源、进冷却"这一笼统语句，未单独规定引导类技能的扣减时点，也未规定引导中途
   被打断是否找回部分资源。本模块拍板在引导开始（步骤 8）扣除，中途打断不找回——避免"打断退还
   多少资源"这一更复杂的分摊语义，且与"打断"小节原文只提"中止步骤 8"、未提资源找回一致。

7. **引导周期触发间隔 `tick_interval` 的取值来源**：任务书"channel_time 内均匀触发……拍板：
   `params.tick_interval` 缺省时整个引导期触发一次效果"。06 第 3.1 节 `SkillDef` 字段表没有
   `tick_interval` 字段，本模块解释为"`effects[]` 任一项 `params.tick_interval`"——扫描
   `skill.def.effects`，取第一个声明了 `tick_interval` 参数的效果项的值；全部效果都未声明时，
   `tick_interval` 退化为 `channel_time` 本身（等价于"整个引导期只触发一次"，因为累加器在最后
   一步 `dt` 恰好达到 `channel_time` 时触发一次后引导随即结束）。

8. **打断三类触发方式的具体接线**：`interrupt_flags: movement` 由调用方（移动系统）显式调用
   `CastPipeline.NotifyMoved(unitId)` 驱动（本模块不知道"单位是否正在移动"，只提供通知入口）；
   `damage_taken` 订阅 `combat.damage_dealt` 事件，命中目标即为读条者时打断；`control` 订阅
   `aura.applied` 事件，光环施加后若聚合控制标志含 `NoCast` 则打断——06 原文"施加 NoCast 类控制时
   中断"未细化"如何知道施加了 NoCast"，本模块选用事件驱动而非轮询。

9. **`Stunned`/`Silenced` 与 `ControlFlags` 的映射**：`common.ControlFlags` 只有
   `NoMove`/`NoCast`/`NoAttack`/`NoInteract` 四个标志位，没有直接对应"眩晕"/"沉默"两个语义更粗的
   概念。本模块拍板：`NoCast`+`NoMove`+`NoAttack` 三者同时置位视为 `Stunned`（完全失能）；仅
   `NoCast`（不要求同时 `NoMove`/`NoAttack`）视为 `Silenced`（仅禁施法）。

10. **`immunity` 的 schools/effect_kinds 两个维度取 AND**：06 第 3.3 节 `immunity` 行只说"对指定
    学派/效果类型免疫"，未规定两个维度如何组合。本模块拍板：两个维度分别"空列表 = 不限制该维度"，
    非空则必须命中；整体判定 = 两个维度各自判定的 AND（与 `common.SkillFilter.Matches` 的 OR 语义
    刻意不同——那里过滤的是"影响范围的并集声明"，这里判定的是"更具体地免疫某种组合"，语义不同，
    不应类比套用同一种布尔组合）。

11. **`absorb` 吸收池随叠加层数线性增长，但简单刷新（层数不变）不重置已消耗部分**：光环创建时
    `AbsorbRemaining = perStackAmount × 初始层数`；层数增长（未达 `max_stacks` 的正常叠加）时按
    `perStackAmount` 累加到剩余池（不覆盖，保留已消耗部分）；`StackOverflowPolicy.Replace` 才会
    整体重置为一份新的吸收池。多条 `absorb` 效果项存在于同一 `aura_def` 时，金额相加、school 限定
    取最后一条声明的值（该组合场景较少见，简化处理，见 `AuraHost.AbsorbPerStack`）。

12. **`school_damage`/`heal` 的最终数值计算发生在 `EffectDispatcher`，不发生在 `CastPipeline`/
    `AuraHost`**：`CastPipeline.ExecuteEffectsOnly`/`AuraHost.FirePeriodic` 都只是把
    `effects[].params` 里的 `base_value`/`coefficient`/`school` 原样塞进 `EffectContext` 交给
    `EffectDispatcher.ApplyEffect`，真正"`value = base + coefficient × GetStat(scaling_stat)`"
    （任务书拍板公式）与 SpellMod `effect_value`/`crit_chance` 维度的修正统一在 `EffectDispatcher`
    内一次性完成——保证无论效果来自技能直接释放还是光环周期触发，数值计算逻辑只有一份。

13. **`EffectDispatcher` 内的 SpellMod 应用不带技能标签集合**：`common.EffectContext` 不携带
    `skill.def.tags`（不可修改 `common`），`EffectDispatcher.ApplyDamageOrHeal` 调用
    `SpellModResolver.Apply` 时对 `effect_value`/`crit_chance` 两个维度统一传空标签列表——意味着
    这两个维度按标签过滤的 `SpellMod` 在经由 `ApplyEffect` 落地的效果上不会命中（学派/技能 id 两个
    过滤维度仍正常工作）。`CastPipeline` 自身对 `cast_time`/`cost`/`cooldown` 三个维度的 SpellMod
    应用**不受此限制**——那三处调用点在 `CastPipeline` 内部，可以直接传 `def.Tags`。这是本模块已知
    的一处轻微不一致，测试 `SpellModResolver` 的标签过滤时直接调用 `SpellModResolver.Apply`（见
    tests 对应用例），不经过 `EffectContext` 这条链路。

14. **`trigger_spell`/Proc 触发的 `TriggerCast` 完全绕过施法管线九步检查**：任务书"绕过读条与
    GCD、不入队列"，本模块进一步拍板为"绕过全部九步检查"（存活/控制/学派锁定/冷却/资源/目标/
    射程一律不检查），直接对（提供的目标，或退化为施法者自身）执行 `def.Effects`——因为
    "触发效果"（如"造成伤害时 10% 概率额外触发一个技能"）语义上是效果链的延伸，不是玩家/AI 发起
    的新一次独立施法请求，不应该被读条中的另一场施法或资源不足等状态挡下。

15. **触发链递归深度用 ambient 计数器而非把 depth 编码进 `ProcHost`/`EffectDispatcher` 的回调
    签名**：`CastPipeline.TriggerCast` 内部维护一个 `_triggerDepth` 字段，进入自增、退出（`finally`）
    自减；`ProcHost`/`EffectDispatcher` 的触发回调统一是 `Func<Id,Id,IReadOnlyList<Id>,bool>`（无
    depth 参数）。因为引擎单线程同步执行，"当前处于第几层触发"这一信息完全可以用一个实例字段
    表达，不需要把它塞进 `EffectContext.Params` 这类不该携带控制流状态的数据结构，也不需要在每个
    调用点手工传递、手工 +1。超过 `SkillOptions.MaxTriggerDepth`（默认 8）时 `TriggerCast` 直接
    返回 `false` 并记一条诊断错误，不再执行任何效果（防无限递归，见落地方案 T2-6 禁止事项）。

16. **`override_skill` 在 `CastSkill`/`TryStartCast` 入口解析一次，多条命中时取最近施加的一条**：
    `AuraHost.ResolveOverride` 扫描施法者身上全部生效光环实例的 `override_skill` 效果，按
    `SeqNo`（创建顺序）降序取第一条 `From == skillId` 命中的记录；解析结果只影响本次施法用哪个
    `SkillDef`（冷却/消耗/效果等全部按重定向后的技能计算），不影响 `IsCasting`/`GetCooldown` 等
    查询接口对外暴露的技能 id 语义（调用方传什么 id 查询就按什么 id 查询，不做重定向）。

17. **`weapon_damage_pct` 不解析真实武器伤害**：L2 规则层不依赖 L3 载体层的物品/装备数据（01 第 3
    节依赖矩阵），本模块把 `params.pct` 原样放进结算前的 `EffectContext.BaseValue`，真正"武器伤害
    基数从哪来"留给 combat（或更上层集成）按需解释 `EffectContext.Params["pct"]`，本模块只保证
    这个字段被正确传递。

18. **`teleport` 只支持字面坐标 `params.point`，不支持"传送点引用"**：06 第 3.2 节
    `teleport`"目标坐标或传送点引用"给了两种参数形式，传送点注册表（`world.map` 一类）属于 L4
    玩法层数据，L2 规则层不依赖，本模块只实现坐标分支；传送点引用分支需要更上层在调用
    `ISkillHost.CastSkill`/构造 `EffectRef.Params` 时提前把点位解析好再传入。

19. **`move` 效果只写最终逻辑位置，不做寻路/碰撞**：`charge`（冲向目标、在 `stop_distance` 处
    停下）/`leap`（瞬间落到 `params.point`）/`knockback`（沿来源→目标方向推开
    `params.distance`）均直接调用 `IUnitAccess.SetPosition`，06 原文本节未规定位移的插值/寻路
    细节，交给 L3 移动系统在表现层/物理层精化（呼应 05 对象模型的移动系统分工）。

20. **`IEffectExtension` 六类委托效果原语**：`projectile`/`summon`/`open_lock`/`create_item`/
    `set_world_flag`/`script` 均需要 L3 载体层（抛射物/召唤物/物品）或 L4 玩法层（GameObject 开锁/
    世界标志）或脚本钩子才能真正落地，不在本模块依赖范围内。未注入 `IEffectExtension`，或注入后
    `TryHandle` 返回 false，`EffectDispatcher` 记一条诊断警告并返回一个"未处理"的
    `ResolveResult`（`Hit=Hit, RequestedAmount=FinalAmount=Absorbed=0, Immune=false`），不抛异常、
    不阻断同一次施法里其余效果继续执行。

21. **阶段 3 整理"事项三"：`AuraHost` 叠加查询 `IStaticImmunityProvider`**（新增
    `core/rules/common` 契约）：`AuraHost.IsImmune` 在光环免疫命中之外，先查询注入的
    `IStaticImmunityProvider.IsImmune`，命中即视为免疫；`ApplyStaticEffects` 处理 `control` 类
    `AuraEffect` 时，把本次施加的控制标志与 `IStaticImmunityProvider.GetControlImmunity(targetId)`
    做按位与非（`flags &= ~immunity`）后再并入 `instance.ControlFlags`——静态控制免疫的标志位
    从未真正置位，`GetControlFlags` 查询结果与"完全没吃到这段控制"等价，不是"吃到了但立刻被
    解除"。构造参数 `staticImmunity` 可选，缺省 `NullStaticImmunityProvider`（一律不免疫/无控制
    免疫），不改变未接入方的既有行为；真实实现（`CreatureImmunityProvider`）在
    `core/carriers/creature`。

## 不负责什么

- 不实现命中判定、暴击、护甲/抗性减免、免疫吸收后的实际扣血扣蓝——06 第 4.1 节结算管线本身完全
  是 `core/rules/combat` 的职责；`EffectDispatcher` 对 `school_damage`/`weapon_damage_pct`/`heal`
  三类效果只组装 `EffectContext`（含 SpellMod `effect_value`/`crit_chance` 修正）交给
  `ICombatHost.ResolveEffect`，返回值原样透传。
- 不实现目标选择链（`target.chain_def` 的候选/过滤/排序），只在施法管线步骤 6 调用注入的
  `ITargetHost.Resolve`。
- 不实现 AI 优先级表（`ai.rotation`）求值，只被动接受 `ISkillHost.CastSkill`/`SkillTickHandler`
  消费的 `cast` 意图。
