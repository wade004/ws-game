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
    IEffectExtension.cs    五类委托效果原语的扩展点
    ISkillDiagnostics.cs   诊断出口
  schema/
    README.md              skill.* 五张表字段 + 校验规则说明
    SkillSchemas.cs         TableSchema 声明
    SkillValidationRules.cs IValidationRule 实现
  core/
    Defs.cs                 解析后的强类型定义（SkillDef/AuraDef/ProcDef/SpellModDefRecord/SkillBookDef）
    SkillDefCache.cs        DataRecord → 强类型定义，懒解析 + 缓存；P2-05 根治（外部审计
                             audit-c9ff301-20260909）：补 InvalidateAll()，SkillHost 订阅
                             DataLoadCompletedEvent 后调用，开发期 DataHotReload 场景下
                             resident host 下一次 cast 能看到 reload 后的新定义
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

1. **施法管线步骤 6 的 `target_shape_ref` 语义**：06 第 3.1 节已统一为"字段只引用
   `target.chain_def`，链内 `shape` 字段按需引用 05 的 `Shape` 定义"；任务书拍板"为空则
   `ITargetHost.Resolve(target_shape_ref 指向的链, caster)`"，本模块据此把 `target_shape_ref`
   统一当作 `target.chain_def` 的 id 直接传给 `ITargetHost.Resolve`，与文档口径一致（`Shape` 本身
   只在链内按需被引用，不会被 `target_shape_ref` 直接指向）——`ISkillHost.FindUnits` 的
   `origin`+`Shape` 组合用法是另一条独立入口，供技能内部效果的范围查找，不经过
   `target_shape_ref`/施法管线步骤 6。

2. **视线检查依赖注入的 `ISpatialQuery` 可为 null**：任务书"经 `ISpatialQuery.HasLineOfSight`
   （若接口有）否则跳过"——本模块构造函数把 `ISpatialQuery` 声明为可空参数，为 null 时步骤 7 只做
   距离检查、跳过视线检查，不抛异常。

3. **`ISkillHost.FindUnits` 已实现查询/筛选/排序**（DOC-116-02 根治，外部审计 audit-24a11fe-20260910；
   取代此前"当前恒返回空列表"的过时表述）：委托注入的 `ISpatialQuery.QueryShape` 做真正的形状
   判定（`shape.WithOrigin(origin)` 换锚点，其余字段原样保留），按 `UnitFilter` 的 `AliveOnly`/
   `Exclude`/`RequiredTags`/`ExcludedTags`/`Relation` 过滤（`Relation` 为 `Hostile`/`Friendly`/
   `Neutral` 时经可选注入的 `IFactionMatrix` 判定，未注入时这三种关系一律判不通过，宁可漏收不
   误纳），登记的触发体（`trigger_only` 标签）一律排除，结果按 `Id` 序数升序排序保证确定性——
   完整判断记录见 `SkillHost.FindUnits`（`core/rules/skill/core/SkillHost.cs`）方法文档。生产装配
   （`core/rules/assembly/RulesAssembly.cs`）默认把 `Spatial` 传给 `SkillHost` 构造函数，因此正常
   装配下 `FindUnits` 直接可用；只有在**未注入 `ISpatialQuery`** 的场景（例如自定义、不带空间索引
   的宿主）才降级——此时无条件记一条诊断警告并返回空列表（不抛异常，与判断记录 2 的射程/视线检查
   降级惯例一致），不区分 `UnitFilter` 其余字段。

4. **法术队列窗口外的再次施法请求**：06 第 3.6 节只描述了"窗口内入队"的行为，未规定窗口外再次
   `CastSkill` 的处理方式。本模块拍板：窗口外一律拒绝（每单位只有一个队列槽，不支持排更多队）。
   契约缺口已补齐：原实现在这一分支复用 `CastFailureReason.OnCooldown` 作为最接近的失败语义，
   但施法者此刻并非真的处于技能冷却中，只是"忙"——读条/引导占用中；`CastFailureReason` 已补上
   专门的 `Busy` 原因码（见该枚举注释），本分支现在返回 `Busy`，`OnCooldown` 恢复只表示步骤 3
   冷却/充能未就绪这一单一语义。

5. **公共冷却/冷却的实际起算时点按技能类型三分，不是统一写在步骤 8**（文档代码一致性审计对齐，
   外部审计 68c9bed 判断记录）：`CastPipeline.StartCooldownAndGcd` 的调用点——瞬发（`cast_time<=0`
   且非引导）在步骤 8 立即完成时调用（此时步骤 8/9 实质上同一时刻，见 `EnterCastOrChannel`）；
   引导（channel）类技能在步骤 8 引导开始时调用（见判断记录 6，与资源扣除同一时点）；**唯独
   普通读条类技能（`cast_time>0` 且非引导）在步骤 9 读条真正完成时（`FinishCast`）才调用**，不是
   步骤 8 开始时。三者共用同一个 `SpellModDimension.Cooldown`/`GcdEnabled`/`RespectsGcd` 判定逻辑，
   只是调用时点不同；多数同类系统里 GCD 从施法瞬间开始计时（本模块瞬发/引导两种确实如此），普通
   读条类技能这里是"完成时才起算"，是否应当统一为"开始时起算"未在既有拍板中明确定论，本 README
   如实记录当前实现，不代表已确认为最终产品决策。`SkillOptions.GcdDuration` 的默认值（1.5）只是
   一个占位式合理起点，不代表任何产品决策，真实数值应由游戏口味配置清单给出。

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

20. **`IEffectExtension` 五类委托效果原语**：`summon`/`open_lock`/`create_item`/
    `set_world_flag`/`script` 均需要 L3 载体层（召唤物/物品）或 L4 玩法层（GameObject 开锁/
    世界标志）或脚本钩子才能真正落地，不在本模块依赖范围内（`projectile` 已按架构演进拆出独立的
    `IProjectileSpawner` 依赖倒置接口，不再经 `IEffectExtension`，故本项从此前的六类改为五类，见
    `IEffectExtension.cs` 类型注释）。未注入 `IEffectExtension`，或注入后
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

22. **RC-01 收口（第四方深度审核）：触发链深度随事件传播，不止覆盖同步调用**：`CastPipeline`
    原来的 `_triggerDepth` 只在同步方法调用栈内累计，Combat 侧的效果结算经 `IEventBus` 异步
    `Enqueue`（`combat.heal_done` 等）后深度归零；无 ICD 的 `heal_done → trigger_skill(heal)`
    可以跨越多个 `EventBus` 派发轮次持续循环，只靠 `EventBus.MaxDispatchPasses` 记诊断并暂停，
    待处理事件仍会留存。`core/rules/common/contracts/ITriggerChainEvent.cs`（新增契约）让触发链
    深度随事件本身传播——`ProcHost`/`CastPipeline` 产生的触发事件携带上一层深度 +1，达到
    `MaxTriggerDepth` 上限即丢弃自循环，不依赖全局 pass 截断，不影响下一 tick 正常事件。见
    `CastPipeline.cs`、`ProcHost.cs`、`ProcTests.cs`。
23. **RC-03 收口（第四方深度审核）：`CastPipeline` 订阅死亡/销毁事件取消读条/引导/队列**：原实现
    只处理控制/受伤中断，不订阅死亡/销毁——死亡者继续扣资源并结算，销毁后 `FinishCast` 访问已
    注销的 `IPowerHost`/`IUnitAccess` 直接抛异常。现在订阅 `unit.died`/`entity.destroyed`，收到
    后立即取消该单位当前读条/引导与排队请求、清冷却/锁定；`AdvanceOne`/`FinishCast` 完成前重验
    施法者与目标仍然存在/存活。见 `CastPipelineDeathDestroyTests.cs`（新增）。
24. **RC-04 收口（第四方深度审核）：AP 消费点后移到全部检查通过之后**：原实现在目标/射程/视线
    检查之前就调用 `TryConsumeActionPoints`，AI 连续尝试失败技能会白白耗尽预算。现在扣点挪到
    步骤 3～7 全部检查通过、真正开始读条/执行之前，无目标/超距/无视线三种失败各自不再消耗 AP。
    见 `CastPipeline.cs`、`CastPipelineFailureTests.cs`。
25. **RC-07 收口（第四方深度审核）：离散模式下学派锁也会衰减**：`AdvanceSchoolLocks` 原来只被
    连续模式的 `CastPipeline.Update` 按秒调用，离散施法与轮末推进都不会调用它，学派锁在离散模式
    下永久存在。现在离散轮末（`SkillHost` 收到轮结束通知时）同样推进一次，且与连续模式的按秒
    推进互斥（不会同一场战斗被双重扣减）。见 `CastPipeline.cs`/`SkillHost.cs`、
    `DiscreteModeCastPipelineTests.cs`（新增）。
26. **RC-08 收口（第四方深度审核）：移动中断改为生产环境真的会触发，不再只是一个未接线的入口**：
    `interrupt_flags: movement` 此前只有 `CastPipeline.NotifyMoved` 这个方法定义，没有任何生产
    代码调用它——真实移动只发布 `unit.moved` 事件，本模块并未订阅，声明了 movement
    interrupt 的读条实际上可以边移动边完成。**订阅位置勘误（外部审计 68c9bed 收边，"旧文案纠正"）：
    实际订阅方是 `core/rules/assembly/RulesAssembly`（见该文件 `Bus.Subscribe(EventKeys.UnitMoved,
    ...)`，转调 `Skill.NotifyMoved`），不是 `SkillHost` 自身构造期订阅**——`core/rules` 不依赖
    `core/carriers`（L3），无法直接引用 `core/carriers/common` 定义的强类型 `UnitMovedEvent`，
    只能由持有跨层依赖的装配根（`RulesAssembly`）用弱类型 `Id` 事件 key 订阅后转调本模块的
    `NotifyMoved` 公开方法；`SkillHost.NotifyMoved`（见 `SkillHost.cs`）本身只是一个可供外部调用
    的窄契约方法，不含任何 `Subscribe` 调用。成功位移时同步调用 `NotifyMoved`；被阻挡（未真正
    发生位移）不触发。见 `RulesAssembly.cs`、`SkillHost.cs`、`MovementInterruptWiringTests.cs`
    （新增）。

27. **C02 收口（外部审计 7e63d66 第四轮，P1，成立，跨模块——本模块负责的一半）：来源真实
    `Despawn` 后周期效果不再抛异常，缩放贡献降级为 0**：`EffectDispatcher.ApplyDamageOrHeal`
    此前在效果声明了 `scaling_stat` 时无条件调用 `IStatHost.GetStat(context.SourceId, ...)`——
    周期性效果（`periodic_damage`/`periodic_heal`）的 `EffectContext.SourceId` 恒是施加光环时的
    施法者（`AuraHost.FirePeriodic` 每次都用 `instance.SourceId` 重建上下文），而光环只在"目标"
    被销毁时才由 `AuraHost.OnEntityDestroyed` 摘除（来源销毁不代表已施加到其他目标身上的光环
    应当消失，见本文件条目 9/`OnEntityDestroyed` 判断记录）——真实 `Core.Carriers.Creature.
    CreatureFactory.Despawn` 会同步注销来源的 `IStatHost` 注册，来源销毁后光环仍按周期结算，
    命中未注册单位直接抛 `InvalidOperationException`。现在先查 `IStatHost.IsRegistered
    (context.SourceId)`，未注册时缩放贡献按 0 处理（只保留 `base_value`），不中断周期结算、不抛
    异常；另一半（`Core.Rules.Combat.CombatHost.NotifyCombatEvent` 对不存在单位静默跳过进战
    通知）见 `core/rules/combat/README.md` 同编号条目。验收（真实 `CreatureFactory`+`AuraHost`+
    `CombatHost` 全链路集成测试，不是 fake unit access）：
    `Tests.Carriers.Assembly.CreatureDespawnPeriodicEffectTests.
    PeriodicDotWithScalingStat_SourceDespawnedThenMultipleTicksElapse_DoesNotThrow_
    AndKeepsLandingDamage`（`core/carriers/assembly/tests/`）。06/05 文档同步补充语义说明，见两者
    变更记录。
28. **C03 收口（外部审计 7e63d66 第四轮，P1，成立）：`ConsumeAbsorb` 吸收耗尽移除实例传递触发
    链深度**：`AuraHost.ConsumeAbsorb` 吸收池耗尽时走 `RemoveInstanceInternal(instance,
    "absorb_depleted")`，此前没有 `triggerChainDepth` 参数，恒以深度 0（根事件）发布
    `aura.removed`，绕开了 `SkillOptions.MaxTriggerDepth` 的收敛预算——与本文件条目 22（RC-01）
    已经覆盖的"触发链深度随事件传播"是同一类问题，但 `absorb_depleted` 是与 `dispel`（RC-01/N04
    已覆盖）完全独立的另一条 `aura.removed` 发布入口，此前遗漏。永久 proc 光环监听
    `aura.removed`、造成恰好耗尽自身吸收池的自伤，可以借此反复触发自己，不受 `MaxTriggerDepth`
    限制。现在新增 `ConsumeAbsorb(Id, Id, double, int triggerChainDepth)` 重载（`IAuraQuery` 用
    C#8 默认接口方法转发到三参数重载，等价于此前恒 0 的行为，避免强制其它 combat/expr_host/
    carriers 测试假实现连带改动），`AuraHost` 提供真正实现，把深度透传给 `aura.removed`；
    `core/rules/combat/core/Resolver.cs` 步骤 7"免疫吸收"传入 `context.TriggerChainDepth`；
    `RulesAssembly.DeferredAuraQuery` 代理显式转发（不能依赖默认接口方法的隐式转发，否则会绕开
    代理把深度悄悄丢回 0）。验收：`Tests.Rules.Skill.ProcTests.
    TriggerChain_ViaAuraRemoved_AbsorbDepletedLoop_IsBoundedByMaxTriggerDepth`（永久 proc + 吸收
    耗尽自伤循环，有限 pass 内收敛，惯例同条目 22 的 `TriggerChain_ViaAuraRemoved_
    ApplyThenDispelLoop_IsBoundedByMaxTriggerDepth`）。
29. **C08 收口（外部审计 7e63d66 第四轮，P2，成立，跨模块——本模块提供机制，`core/carriers/item`
    消费）：`AuraHost` 新增 `InstanceReplaced` 事件，`StackOverflowPolicy.Replace` 换句柄时同步
    通知订阅者**：`ReapplyExisting` 的 `Replace` 分支摘除旧实例、创建新实例（新 `AuraInstanceRef`）
    时，旧句柄从此在光环系统内部彻底失效；此前没有任何机制把这次换句柄告知外部按句柄记账的
    调用方（如 `core/carriers/item.EquipmentHost` 按装备实例记录"自己授予了哪个光环句柄"），
    导致这类调用方的账本与真实存活的实例发生偏差。`IAuraQuery` 新增
    `event Action<Id targetId, Id defId, Id oldInstanceId, Id newInstanceId> InstanceReplaced`
    （C#8 默认接口成员，`add`/`remove` 均空实现——对不会触发 `Replace` 的测试假实现是安全的
    等价降级），`AuraHost` 在旧实例摘除、新实例创建完成的同一步同步触发（不经 `IEventBus`
    异步队列）；`RulesAssembly.DeferredAuraQuery` 显式转发 `add`/`remove`（同条目 28 的惯例，
    不能依赖默认接口成员的隐式转发）。具体消费方与验收见 `core/carriers/item/README.md`/
    `core/carriers/assembly/README.md` 同编号条目。
30. **C09 收口（外部审计 7e63d66 第四轮，P2，成立，跨模块——本模块提供的原语，
    `KnownSkillsPersistable` 消费）**：见本文件"不负责什么"之外无新增原语——`SkillHost.
    LearnSkill(Id,Id)`/`ForgetSkill(Id,Id)`（不带来源，归属内部 `PermanentGrantSource` 哨兵来源）
    与 `GetPermanentlyKnownSkills(Id)` 三个既有方法已经足够支撑"先撤销当前有、快照没有的永久
    技能，再补上快照里的全部技能"这套替换语义，本次收口不需要改动 `SkillHost` 本身，只改动
    `KnownSkillsPersistable.Load`（存档段实现，`core/rules/skill/core/KnownSkillsPersistable.cs`）。
    具体判断记录见该文件类型注释；验收见 `Tests.Rules.Skill.KnownSkillsPersistableTests.
    Load_SameHost_ReplacesCurrentPermanentSkillSet_RemovingSkillsLearnedAfterSnapshot`/
    `Load_SameHost_ReplacePermanentSet_DoesNotBreakIndependentEquipmentGrantLifecycle`。

31. **R05 收口（外部审计 5e779c6，P2，成立，跨模块——本模块负责的一半）：`SkillHost` 订阅
    `Core.Rules.Common.TimeModelRescaledEvent`，把技能冷却/充能恢复进度/光环剩余时间随连续↔离散
    模式切换一起换算**：`core/gameplay/assembly.TimeModelSwitch` 切模式时此前只换算了
    `core/foundation/sim_loop.SimTimers` 的通用具名计时器，完全没有触及本模块内部维护的
    `CooldownTracker`/`AuraHost` 倒计时状态——两者与 `SimTimers` 上的计时器同样"以数据集声明的
    时间单位计"（连续模式秒、离散模式轮），切换不换算就会被新模式的 tick 单位重新解读（外部审计
    复现）。`CooldownTracker.RescaleAll(factor)`/`AuraHost.RescaleAll(factor)` 两个新增公开方法各自
    按同一系数换算全部倒计时（`AuraHost.RescaleAll` 最初判断记录是"周期效果累加器不换算"，后续
    相邻缺口根治时已推翻——见下方第 35 条），
    `SkillHost` 构造期订阅一次、转发给两者——不需要 `TimeModelSwitch` 直接持有本模块任何具体类型
    引用（跨越 L2/L4+ 层级边界），改经两端共享的同一个 `IEventBus` 广播。见
    `core/rules/common/contracts/Events.cs`（`TimeModelRescaledEvent` 判断记录）、
    `CooldownTracker.cs`/`AuraHost.cs`（`RescaleAll` 判断记录）、`SkillHost.cs`
    （`OnTimeModelRescaled`）、新增测试文件 `core/rules/skill/tests/TimeModelRescaleTests.cs`；
    跨模块另一半（`TimeModelSwitch.RescaleTimers` 发出事件）见
    `core/gameplay/assembly/README.md` 同编号条目。

32. **R06 收口（外部审计 5e779c6，P2，成立）：`recharge_time <= 0` 统一按"即时恢复"处理，不再
    永久卡死**：`CooldownTracker.StartCooldown` 原实现把 `RechargeRemaining` 设成 0（"没有变化"），
    `AdvanceCharges` 一看到 `RechargeRemaining <= 0` 就直接判定"没有需要推进的恢复"提前返回，
    `Current` 永远不会被加回去——充能一旦耗尽就永久不可用（外部审计复现）。现在 `StartCooldown`
    在 `recharge <= 0` 时把消耗的那一点充能当场原地补满，不产生"进入一个恢复窗口却再也不会被
    推进"的中间状态。新增 `ChargesRechargeTimeZeroWarningRule`（`skill/schema/
    SkillValidationRules.cs`，Warning 级、不阻断）提醒数据作者复核显式登记 0 是否真的是本意（若想
    表达"永久不再恢复"，`charges` 不是合适字段）。见 `CooldownTracker.cs`（`StartCooldown` 判断
    记录）、新增测试文件 `core/rules/skill/tests/ChargesZeroRechargeTests.cs`。

33. **R07 收口（外部审计 5e779c6，P2，成立）：一次 `dt` 跨越光环到期点时，周期效果只按"到期前
    那一段时长"结算**：`AuraHost.Update` 原实现把完整 `dt`（含到期之后本不该存在的时间余量）都
    计入周期累加器，一次大步长（如主循环追帧）跨越到期点会多结算出本不该发生的周期次数（外部
    审计复现：`duration=1`、`interval=0.3` 的光环一次 `dt=5` 的大步推进错误按
    `floor(5/0.3)=16` 次结算，应为到期前的 `floor(1/0.3)=3` 次）。现在按
    `Math.Min(dt, Remaining)` 推进周期累加器，`Remaining` 本身仍按完整 `dt` 递减（到期判定不变）；
    永久光环（`Remaining` 为 `null`）没有到期点，不受影响。见 `AuraHost.cs`（`Update` 判断记录）、
    新增测试文件 `core/rules/skill/tests/AuraExpiryPeriodicBoundaryTests.cs`。

34. **R09 收口（外部审计 5e779c6，P2，成立）：`effects[]`/`charges`/`cost[]` 嵌套结构补齐加载期
    校验，不再"能通过校验、施法时才抛异常"**：`SkillDefCache.ParseSkillDef`/`ParseAuraDef`（懒
    解析，只在这个技能/光环第一次真正被解析——通常就是第一次被施放——时才跑）对这些嵌套结构做的
    是无防御直接类型转换，原有的 `EffectKindRegisteredRule` 判断条件里 `is JsonObject`/
    `TryGetValue`/`is JsonString` 任一环短路失败就整体跳过、不产生任何校验问题——`effects[i]`
    根本不是对象、缺 `kind` 字段、`kind` 不是字符串三种更基础的坏形状因此完全不受校验、只在
    运行期崩溃（外部审计复现："技能嵌套坏数据通过校验，使用时才抛异常"）。现在
    `EffectKindRegisteredRule` 改为逐层显式检查，三种坏形状各自产生一条校验问题；新增
    `ChargesShapeRule`（`charges.max`/`charges.recharge_time` 声明后均为必填）、
    `CostEntryShapeRule`（`cost[].power_type`/`cost[].amount` 同理）两条规则，覆盖同一类"顶层字段
    是可选对象/数组、但一旦声明其内部子字段即为必填"的嵌套结构。均为 Error 级（会真的在运行期
    崩溃，不是可以放行的告警）。见 `schema/SkillValidationRules.cs`、新增测试文件
    `core/rules/skill/tests/SkillValidationNestedGapTests.cs`（每个"未修复前会怎样"用例都先用
    `SkillDefCache` 独立证实"这份数据确实会在解析时崩溃"，再证实新规则能在加载阶段拦下）。

35. **相邻缺口根治（第五轮外部审核 audit-5e779c6-20260907，WA 报告"需要说明的取舍"第 1/2 条）：
    施放/施加<b>当下</b>（不只是第 31 条覆盖的"切换那一刻"）按当前生效模式折算冷却/光环 duration/
    周期 interval 的原始 authoring 数值**：第 31 条（R05）只解决了"已经存在的倒计时状态在模式切换
    那一刻跟着换算"；`CooldownTracker.StartCooldown`/`AuraHost.ApplyAura` 此前施放/施加当下直接把
    `SkillDef`/`AuraDef` 原始数据（authoring 时按连续模式秒数）原样写入倒计时状态，不管当下实际
    处于哪种模式——若技能是在离散战斗<b>进行中</b>才第一次被释放（不是"连续模式下已有冷却、切换
    时刻被换算"这条路径），原始秒数会被离散模式按轮推进的 `Update` 直接当成"轮数"消耗，同一份数据
    因为"碰巧先手动切了一次模式还是没切"产生完全不同倍数的实际冷却/持续时间。现在
    `CooldownTracker`/`AuraHost` 各自持有一个 `_currentFactor`（"当前模式 1 个计时单位相当于连续
    模式多少秒"，初始 1.0，随 `RescaleAll` 每次切换累乘更新——与第 31 条既有的换算系数同一个含义、
    同一个字段承担两件事）：`StartCooldown`（`cooldown_duration`/`charges.recharge_time`）、
    `ApplyAura`（`duration`）、`Update`（`periodic_damage`/`periodic_heal` 的 `interval`）在消费
    原始数据前都先乘上该系数；`AuraHost.RescaleAll` 同时改为一并换算
    `AuraInstanceState.PeriodicAccumulators`（推翻第 31 条"周期累加器不换算"的历史判断——`interval`
    换算之后，累加器若不跟着换算，"累加器/interval 还差多久触发下一跳"这个比例关系会在切换瞬间被
    打破）。判断记录（未纳入本次范围）：`CastPipeline` 消费的 `cast_time`/`channel_time`、
    `modify_cooldown`/`add_charge` 两个效果原语的运行期增量参数同属"未按当前模式折算"的相邻缺口，
    但不在本次任务书列出的"冷却/光环 duration"范围内，未改动。见 `CooldownTracker.cs`（
    `_currentFactor`/`StartCooldown`/`RescaleAll` 判断记录）、`AuraHost.cs`（`_currentFactor`/
    `ScaleDuration`/`Update`/`RescaleAll` 判断记录）、新增测试文件
    `core/rules/skill/tests/TimeModelCastTimeRescaleTests.cs`（6 条用例，覆盖冷却/充能/光环
    duration 三条施放路径 + 周期 interval + 累加器换算比例保持）。

36. **CR130-02 根治（外部审计 audit-5c444f1-20260908，P1）：`LearnSkill` 新增显式 `permanent`
    参数的四参重载，"永久 vs. 临时"改按调用方显式声明分类，不再只靠"来源是否等于
    `PermanentGrantSource` 哨兵"这一个判据**：`core/gameplay/assembly.GameplayAssembly` 的奖励/
    任务 `SkillGranter` 经三参 `LearnSkill(unitId, skillId, sourceId)` 透传奖励/任务自己的来源 id
    （不是哨兵）——修复前这类一次性奖励技能因为"来源≠哨兵"被 `GetPermanentlyKnownSkills`（供
    `KnownSkillsPersistable.Save` 使用）排除在存档快照之外，新宿主读档会丢失这个本应长期保留的
    技能；同宿主读旧档时，因为它从未被承认为"永久"，`KnownSkillsPersistable.Load` 的替换语义
    （第 30 条 C09）也无从撤销它，读一份更早的存档反而不会让它消失（外部审计复现）。现在
    `_skillGrantSources` 的 value 从 `HashSet<Id>` 改为 `Dictionary<Id, bool>`，记录每个来源各自
    的永久性：新增 `LearnSkill(Id,Id,Id,bool permanent)`（`sources[sourceId] = permanent`），
    不带 `permanent` 的三参重载默认 `permanent: true`（奖励/任务/成就一类路径的直觉默认值，不需要
    每个调用方都显式声明），`core/carriers/assembly.CarriersAssembly` 的装备 `SkillGranter` 改为
    显式传 `permanent: false`（临时来源，见该模块 README 同编号条目）。`GetPermanentlyKnownSkills`
    改为"该 (unit, skill) 的来源集合里存在任意一个 `permanent: true` 的来源即计入"，不再要求必须
    是哨兵。新增 `ForgetAllPermanentGrants(unitId, skillId)`：一次性撤销全部
    `permanent: true` 来源（含哨兵，也包括奖励/任务来源 id），不触碰任何 `permanent: false` 的
    临时来源；`KnownSkillsPersistable.ReplacePermanentlyKnownSkills` 的撤销步骤改用这个新方法
    （原来调用不带来源的 `ForgetSkill(Id,Id)`，只撤销哨兵来源那一份引用，对以奖励来源 id 授予的
    永久技能是 no-op，C09 替换语义对这类技能会失效——本条修复中途发现的相邻缺口，一并根治）。见
    `SkillHost.cs`（`_skillGrantSources`/`LearnSkill`/`ForgetAllPermanentGrants`/
    `GetPermanentlyKnownSkills`）、`KnownSkillsPersistable.cs`（`ReplacePermanentlyKnownSkills`）、
    `core/rules/skill/tests/KnownSkillsPersistableTests.cs`（`RewardSourceSkill_*` 两条新用例 +
    既有装备模拟用例改用显式 `permanent: false`）。

37. **CR130-03 根治（外部审计 audit-5c444f1-20260908，P2）：混合时间模式此前只折算了"施放/施加
    当下"与"模式切换那一刻"两个时间点（第 31/35 条），三处"后续推进/重新写入"未纳入——充能耗尽
    恢复后紧接着开始的下一个恢复窗口、`ProcHost` 内部冷却（ICD）、`CastPipeline` 施法学派锁**：
    (1) `CooldownTracker.AdvanceCharges` 在充能归零、`RechargeRemaining` 归零后立即开始下一个
    恢复窗口时，直接用未折算的原始 `EffectiveRechargeTime`（外部审计复现：`factor=0.2`、
    `recharge_time=10`，期望折算为 2，实际残留 10）——首个窗口由 `StartCooldown` 正确折算（第 35
    条），但 `AdvanceCharges` 重新开窗这一步没有跟进同一处理，现补上 `* _currentFactor`。(2)
    `ProcHost` 完全没有接入 R05 那一批时间模式广播（`SkillHost.OnTimeModelRescaled` 当时只广播给
    `CooldownTracker`/`AuraHost`/`CastPipeline` 三者）：新增 `ProcHost._currentFactor`/
    `RescaleAll(factor)`，`OnEvent` 写入 `Attachment.IcdRemaining` 前先乘该系数，
    `SkillHost.OnTimeModelRescaled` 同批调用 `_procHost.RescaleAll`。(3) `CastPipeline.Interrupt`
    写入学派锁剩余时间（`_schoolLocks`）时同样未折算，`RescaleAll` 也从未换算既有学派锁存量——
    现写入前乘 `_currentFactor`，`RescaleAll` 同时按 `factor` 换算全部既有学派锁。三处修复统一
    效果：连续↔离散往返切换后，技能自身/分类冷却、充能与下一恢复窗口、公共冷却、proc 内部冷却、
    施法学派锁全部使用同一套折算系数，不再各用各的时间基准。见 `CooldownTracker.cs`
    （`AdvanceCharges`）、`ProcHost.cs`（`_currentFactor`/`RescaleAll`）、`CastPipeline.cs`
    （`Interrupt`/`RescaleAll`）、`SkillHost.cs`（`OnTimeModelRescaled`）、
    `core/rules/skill/tests/CR130_03_TimeModelRescaleGapsTests.cs`（充能第二窗口 1 条 + ICD 写入/
    既有存量换算各 1 条 + 学派锁写入/既有存量换算各 1 条，共 5 条）；06 第 8 节事件词汇表
    `sim.time_model_rescaled` 行同步勘误（订阅方枚举补齐 `CastPipeline`/`ProcHost`）。

38. **CR130-04 根治（外部审计 audit-5c444f1-20260908，P2）：引导（channel）周期效果结算不再用
    完整 `dt`，改用 `Min(dt, Remaining)`，与 `AuraHost` 第 33 条（R07）同款处理对齐**：
    `CastPipeline.AdvanceOne`（连续模式 `Update`/离散模式 `AdvanceCastForActor` 唯一共同经过的
    推进点）此前把完整 `dt` 累加进 `state.TickAccumulator`，即便 `dt` 已经超出本次引导的剩余时间
    （引导会在这次推进内结束）——超出引导之外的那段时间仍被计入周期结算，多算一跳（外部审计复现：
    `channel_time=0.5`、`tick_interval=1`、单次 `Update(1)` 期望 0 次实际 1 次）。现在
    `channelDt = dt < state.Remaining ? dt : Math.Max(0, state.Remaining)`，只把"引导仍然有效"
    的那段时间计入累加器；`state.Remaining -= dt` 本身不变（用完整 `dt` 判定是否结束，只是
    "计入周期累加器的量"改用截断后的 `channelDt`）。见 `CastPipeline.cs`（`AdvanceOne`）、
    `core/rules/skill/tests/CR130_04_ChannelBoundaryTests.cs`（单区间越界不多算、跨多个 interval
    恰好按引导时长计数、被打断不多算、模式切换后越界不多算，共 4 条）。

39. **ADR-0019 / F1a：`effects[]`/`charges`/`cost[]` 的加载期校验改由 `SkillSchemas` 的子结构
    登记承担，条目 34（R09）新增的三条手写规则相应收窄/退役**：`SkillSchemas.Def`/`AuraDef` 现把
    `effects` 登记为按 `kind` 分派的 `FieldSchema.Variants`（19 种效果原语、10 种光环效果各自的
    `params` 形状，见 `schema/README.md`"效果原语参数表"）、`charges`/`cost[]` 登记为带 `Fields`
    的 `FieldSchema`，`DataRegistry` 的递归结构校验（`required_field`/`field_type`/
    `variant_discriminator`）覆盖了条目 34 描述的全部坏形状（条目不是对象/缺 `kind`/`kind` 非
    字符串/未登记的 `kind`/`charges`|`cost[]` 子字段缺失或类型错）——`EffectKindRegisteredRule`/
    `CostEntryShapeRule` 整条退役，`ChargesShapeRule` 收窄为只保留登记表达不了的业务判断
    （`charges.max` 必须 &gt;= 1，改名 `ChargesMaxAtLeastOneRule`），条目 34 的历史叙述保持不动
    （记录的是当时的问题与修复），本条只说明后续状态。判断记录（偏离方案第 36 行"skill_id→
    skill.def"示例，收窄为 `Id`）：`trigger_spell`/`modify_cooldown`/`add_charge`/`learn_skill`
    四处 `skill_id` 与 `summon.creature_template`/`create_item.item_template`/
    `set_world_flag.flag_key`/`script.hook_id` 未登记为强 `Reference`/未标记必填——`EffectDispatcher`
    对目标缺失分别有专门的 Warn 降级路径（不像 `apply_aura.aura_def` 那样缺目标即崩溃），且既有
    测试套件（`EffectPrimitiveDispatchTests`/`SkillBookTests` 等）已经在多处exercise"引用一个当前
    未加载的技能/无落地扩展实现"这类场景，强校验会与既有测试语义冲突；`display_ref`/
    `creature_template`/`item_template` 因跨层引用限制（04 §5.1 口径）退回 `Id`。详见
    `schema/SkillSchemas.cs`/`schema/SkillValidationRules.cs` 判断记录、
    `tests/SkillValidationNestedGapTests.cs`/`tests/SkillSchemaCoverageTests.cs`。

40. **效果免疫统一门（消费方 2026-09-10 反馈，M-C06 前置核验复现"打断免疫"缺口）：
    `EffectDispatcher.ApplyEffect` 在分发入口统一查询 `AuraHost.IsImmune(TargetId, School, Kind)`
    并短路，取代此前只有 `ApplyDamageOrHeal` 一处查询免疫的旧实现**：06 第 3.3 节 `immunity` 与 07
    `CreatureUnit.Immunities` 的 `effect.<kind>` 写法均按 (学派, 效果原语类型) 泛化定义（见 06 第
    3.3 节勘误，2026-09-10），此前 `interrupt`/`dispel`/`energize`/`teleport`/`move` 等分支从未
    查询过免疫，导致打断免疫等标记对这些效果原语完全不生效（外部消费方复现，详见
    `architecture/落地计划/消费方反馈-2026-09-10-打断免疫.md`）。免疫命中时直接返回
    `immune: true` 的 `ResolveResult`（沿用伤害/治疗分支既有的返回形状），不进入具体分支、不产生
    该效果的任何事件/状态变化——含 `interrupt` 命中免疫时不触发学派锁定（锁定是"成功打断"的连带
    后果，免疫拦截下这次打断本身没有发生）。例外：`EffectKind.ApplyAura` 不纳入本统一判定，`AuraHost.ApplyAura`
    内部对 `control` 类子效果的静态控制免疫判定（`GetControlImmunity`）保留不变、不重复判定——
    对"施加光环实例"这一操作本身的免疫是另一层语义，06/07 未就此拍板，若按 `AuraHost.IsImmune`
    "`effect_kinds` 为空则不挑 kind、只看学派"的既有规则一并拦截，会让任何只声明 `schools` 的
    免疫光环（如"火免疫"）意外连带挡住该学派下全部增益光环的施加（含友方治疗类光环），超出本次
    缺口修复范围。免疫拦截时施法本身仍算成功（消耗、冷却照旧，与伤害免疫同一惯例）；施法者自身
    内部取消（`CastPipeline.NotifyMoved`/`OnCasterDiedOrDestroyed`/`OnAuraApplied`/`OnDamageDealt`
    直接调用 `CastPipeline.Interrupt`）不经过 `EffectDispatcher`，不受本次改动影响。测试见
    `core/carriers/creature/tests/EffectImmunityGateTests.cs`（真实 `RulesAssembly` +
    `CreatureImmunityProvider` 装配，覆盖动态/静态打断免疫、控制免疫不等同打断免疫、驱散/位移/
    能量免疫、免疫时施法仍消耗资源与进冷却、`ResolveResult.Immune` 可观察、自身内部取消不受影响）。

41. **`stack_category` 契约明确为静态校验分组，不是运行时叠加槽位维度（消费方 2026-09-10 反馈
    "光环叠加类别契约一致性核对"，见 [ADR-0023](../../../architecture/adr/0023-光环叠加类别为静态校验分组.md)）**：
    消费方 Runtime 报告复现，把两个显式相同 `stack_category` 且 `max_stacks=1` 的 `aura_def` 依次
    施加到同一目标，均产生成功的 `aura.applied`、各自 `AuraInstanceRef` 不同、层数各 1——这是既有
    实现的既定行为，不是缺陷：`AuraHost._slots` 的运行时槽位键是 `(target, defId, sourceKey)`，
    `stack_category` 不参与该键；`stack_category` 只被 `schema/SkillValidationRules.cs` 的
    `StackCategoryConflictRule` 在加载期用作静态内容校验分组，检查同一分组下是否混用了不同叠加
    形态（`max_stacks>1` 与 `max_stacks==1`），不影响运行时叠加/溢出行为。`SkillOptions.
    StackOverflowPolicy`（`Ignore`/`RefreshOnly`/`Replace`）只作用于同一 `aura_def` 与同一来源
    槽位的重复应用；`sourceKey` 在默认的 `AllowMultiSourceTiming=false` 下恒为 `null`（不按来源
    区分），开启后才按 `(defId, sourceId)` 分别维护独立实例。跨定义共享叠加槽位（同类别不同
    `aura_def` 互相竞争同一份叠加计数）是未提供能力，列入
    `architecture/落地计划/落地方案与分阶段计划.md`"能力边界与未默认接入能力索引"表"未提供"分类。
    本次未改动 `AuraHost`/`StackCategoryConflictRule` 的任何判定逻辑，只对齐了 06/04/本模块与
    `AuraHost.cs`/`SkillOptions.cs`/`SkillValidationRules.cs`/`schema/README.md` 的措辞；新增
    验收测试见 `tests/ADR0023_StackCategoryStaticGroupingTests.cs`（覆盖静态校验允许同类别一致
    形态、运行时同类别不同定义各自独立叠加、溢出策略只影响命中定义、`AllowMultiSourceTiming`
    开关下 `sourceKey` 分槽/不分槽四条场景，详见 `architecture/落地计划/消费方反馈-2026-09-10-光环叠加类别.md`）。

42. **同一光环多个 `proc_trigger` 各自独立生效，此前实现按单值字段处理是缺陷（消费方 2026-09-10
    反馈"同一光环多个 Proc 触发器静默忽略问题"，见
    `architecture/落地计划/消费方反馈-2026-09-10-多Proc触发器.md`）**：消费方反馈复现，
    `skill.aura_def.effects` 登记两个 `proc_trigger` 效果条目（各自引用不同的 `skill.proc_def`）
    加载校验通过，但运行期只有 `effects` 数组里最后一条真正生效——根因是 `AuraInstanceState`
    此前只有单值 `Id? ProcDefRef`，`ApplyStaticEffects` 遍历同一光环定义的多个 `proc_trigger`
    条目时后一个覆盖前一个；`ProcHost._attachments` 同样以 `instanceId` 为单值键，第二次
    `Attach` 直接覆盖第一次（旧订阅的 `SubscriptionHandle` 从未 `Dispose`，是另一层未回收的
    订阅泄漏）。根治：`AuraInstanceState.ProcDefRefs` 改为有序集合，保存本光环定义登记的全部
    `proc_trigger` 条目；`ProcHost._attachments` 改为按 `instanceId` 分桶的多槽列表
    （`Dictionary<Id, List<Attachment>>`），同一光环实例下的多个 `Attachment` 各自持有独立的
    `IcdRemaining`（内部冷却独立计时）与独立的 `SubscriptionHandle`。`AuraHost.CreateInstance`
    对 `ProcDefRefs` 里每一个 `proc_ref` 各调一次 `ProcHost.Attach`（原有单值调用改为循环）；
    `ProcHost.Detach(instanceId)` 语义变更为"摘除该实例挂载的全部触发器"（此前单槽存储下等价于
    "摘除唯一一个"，调用方 `AuraHost.RemoveInstanceInternal` 未改，语义变更对它透明）——
    `AuraHost` 的全部实例移除路径（到期 `Update`、`RemoveAura`、`Dispel`、`ConsumeAbsorb` 吸收
    耗尽、叠加溢出 `Replace` 策略、`OnEntityDestroyed` 目标销毁）统一经
    `RemoveInstanceInternal` 收口，一次 `Detach(instanceId)` 调用即完整注销，不会有孤儿订阅
    残留。公开 API 只新增：`ProcHost` 新增重载 `Detach(Id instanceId, Id procDefId)`（按单个
    `procDef` 精细摘除，当前调用方未使用，为公开 API 补齐的新增能力，不改变既有
    `Attach(Id, Id, ProcDef)`/`Detach(Id)`/`Update(double)`/`RescaleAll(double)` 四个既有公开
    方法的签名）；`AuraInstanceState`/`Attachment` 均为内部类型，不在公开 API 表面。同一光环内
    重复引用同一个 `proc_def`（两条 `proc_trigger` 的 `proc_ref` 取值相同）改在加载期由新增的
    `SkillValidationRules.AuraProcTriggerDuplicateRule`（检查名 `aura_proc_trigger_duplicate`，
    定位到 `effects[index].params.proc_ref`）拒绝——两次独立挂载同一触发器对同一持有者没有可
    区分的运行时语义（各自独立 ICD 抢同一个触发事件），比允许其静默生效更安全，已在
    `RulesSchemaCatalog.RegisterAll` 默认登记。验收测试见
    `tests/C07_MultipleProcTriggersTests.cs`（AB/BA 两种登记顺序均各自独立触发、两个独立光环
    对照不受影响、内部冷却各自独立计时、`RemoveAura`/到期/叠加溢出替换三条移除路径均完整注销、
    重复 `proc_ref` 加载期拒绝且定位到字段、正例对照）。

43. **读条完成当帧新创建的冷却被同一 `dt` 二次扣减（消费方 2026-09-10 反馈"读条完成当帧新冷却
    被提前推进问题"，见
    `architecture/落地计划/消费方反馈-2026-09-10-施法时序与实例标识.md`）**：消费方复现
    `cast_time=0.5`、`cooldown_duration=1` 的技能，恰好在读条完成的那次 `Update(dt)` 调用内，
    新开启的冷却剩余没有显示满额 `1`，而是按当次推进被拆成几段而不同（单步 `Update(0.5)` 剩
    `0.5`；两步 `[0.25,0.25]` 剩 `0.75`；三步 `[0.25,0.125,0.125]` 剩 `0.875`；瞬发同次调用
    对照——调用后未推进时间——为 `1`，正常）。根因：`SkillHost.Update(dt)` 此前先调
    `_pipeline.Update(dt)`（读条在本次 `dt` 内完成时，内部经 `CastPipeline.FinishCast` 开启
    冷却/公共冷却/充能恢复窗口），再调 `AdvanceRoundTimers(dt)`（推进冷却/公共冷却/充能/光环/
    Proc 内部冷却），后者把刚创建的计时状态又用同一个 `dt` 扣了一遍——新状态从"不存在"到
    "存在"的那个瞬间被当成已经存在了整个 `dt`。修法（选择方案 a：调换调用顺序，而非给每个
    计时器额外记"创建于本 tick"标记）：`Update(dt)` 改为先调 `AdvanceRoundTimers(dt)`（只推进
    "进入本次 `dt` 之前就已存在"的状态）、再调 `_pipeline.Update(dt)`（读条/引导完成时新创建的
    状态自然不会被"已经执行完毕"的 `AdvanceRoundTimers` 碰到，要等下一次 `Update` 调用才第一次
    被推进，此时它已经真正存在了一整个 tick，用那次调用的 `dt` 扣减是正确的）。判断记录"为什么
    调换顺序是安全的"：`AdvanceRoundTimers` 只读写既有冷却/GCD/充能/光环/Proc 内部冷却/学派锁定
    状态，不读取 `_pipeline` 内部字段；唯一需要核实的边界是读条完成时若有排队的下一个施法
    （`FinishCast` 的 `Queued` 分支），`TryStartCast` 检查 GCD/冷却是否就绪时读到的现在是"已经
    按本次 `dt` 推进过"的最新值而不是调换前"尚未被本次 tick 推进"的旧值——这让排队技能的就绪
    判定更及时（少算一次滞后），不存在把原本不就绪判成就绪的错误方向；学派锁定推进
    （`AdvanceSchoolLocks`）与读条/引导完成之间没有数据依赖（写入学派锁定的两条路径只在事件总线
    批处理派发时触发，不在 `Update` 内部同步发生），调换顺序不影响它。相邻计时状态排查结论：

    | 计时状态 | 创建点是否与读条/引导完成同一调用 | 是否同类缺陷 | 处理 |
    |---|---|---|---|
    | 技能自身/分类冷却 | 是（`FinishCast`→`StartCooldownAndGcd`→`CooldownTracker.StartCooldown`） | 是 | 随本次改动根治 |
    | GCD（公共冷却） | 是（同一调用点 `StartCooldownAndGcd`→`CooldownTracker.StartGcd`） | 是 | 随本次改动根治 |
    | 充能恢复窗口 | 是（充能耗尽的那一刻，同一调用点写入 `RechargeRemaining`） | 是 | 随本次改动根治 |
    | 光环持续时间/周期累加器 | 是（`ExecuteEffectsOnly` 里的 `apply_aura` 效果落地新实例） | 是 | 随本次改动根治 |
    | Proc 内部冷却（ICD） | 否——只在 `ProcHost.OnEvent`（经事件总线 `DispatchPending` 批处理才派发）里写入，不在 `SkillHost.Update` 内部同步发生 | 否 | 保留对照测试钉住"不受影响" |
    | 离散模式（[ADR-0013](../../../architecture/adr/0013-时间模型可替换即时与回合制同一规则层.md)）冷却/GCD/充能/光环 | `AdvanceCastForActor`（行动者自己的步）与 `AdvanceRoundTimers`（round-end）dt 恒为 1.0（不可再分） | 不适用——没有"同一完成时刻不同分段结果不一致"这一可观测条件 | 现状行为（mid-round 新建冷却在同一 round-end 立即扣减 1 轮）保留、新增对照测试钉住；是否应改为"下一轮才开始扣减"是需要新 ADR 拍板的离散冷却起算语义问题，不在本次范围 |

    验收测试见 `tests/CastCompletionTimerBoundaryTests.cs`（原始三种分段+瞬发对照+既有冷却
    正常推进对照、GCD、充能、光环持续时间、光环周期累加器边界、Proc 内冷对照、离散模式对照，
    共 11 例，其中 7 例在修复前会失败，已核实）。公开 API 无变化（`SkillHost.Update(double)`
    签名不变，只是内部调整了两个私有/内部方法的调用顺序）。

44. **施法生命周期事件补充实例关联标识（消费方 2026-09-10 反馈"施法生命周期事件缺少实例关联
    标识建议"，见同一份反馈文档）**：`CastResult.CastInstanceId` 早已公开，但
    `SkillCastStartEvent`/`SkillCastSuccessEvent`/`SkillCastFailedEvent`/`SkillCastInterruptedEvent`
    此前没有对应字段，消费方无法单靠事件确认"哪一次请求"的开始/成功/失败/打断属于同一次施法。
    四个事件类各自新增只读属性 `CastInstanceId`（`Id?`，与 `CastResult.CastInstanceId` 同一枚
    id）与一个带该参数的新构造重载（新重载的全部参数均不带默认值，避免与旧构造在"只传前 N 个
    参数"的调用点产生重载二义性，同 `SkillHost` 十七/十八参数构造重载判断记录同一套推导）；旧
    构造原样保留，物理签名不变，`CastInstanceId` 恒为 `null`。身份规则（完整措辞见 06 第 3.6
    节勘误段）：`CastPipeline.TryStartCast`/`EnterCastOrChannel` 在校验通过、真正进入步骤 8 或
    进入法术队列时分配 id（`NextCastInstanceId`），步骤 1～7 任一步校验失败不分配（对应
    `Fail(...)` 不携带 id，`SkillCastFailedEvent.CastInstanceId` 为 `null`，`CastResult.Reason`
    仍可辨明原因）；`CastState` 新增字段 `CastInstanceId`（本次读条/引导开始时分配的 id，一路
    带到 `FinishCast`/`Interrupt` 各自发出的成功/打断事件）；`CastState.Queued` 元组新增第三个
    分量 `CastInstanceId`（排队接受时分配，随 `CastResult` 返回），`FinishCast` 续跑排队请求时
    改用 `TryStartCast(..., presetCastInstanceId: 排队时的 id)`——排队请求的整个生命周期只有一个
    id，不因为"排队接受"与"真正开始"是两次不同的方法调用就分配两个不同的 id。原实现对"排队
    请求被覆盖/被打断清空"完全静默（调用方拿着排队时返回的 id，却永远等不到任何结果通知）——
    新增 `CastFailureReason.QueueCleared`：`CastSkill` 覆盖已排队的旧请求前，先为旧请求发一条
    携带它自己 id 的 `SkillCastFailedEvent(QueueCleared)`；`Interrupt` 打断当前读条/引导时，若
    还排着下一个技能，同样为被清空的排队请求发一条携带它自己 id 的 `SkillCastFailedEvent`
    （与被打断的当前施法各自携带各自的 id，互不混用）。`trigger_spell`/Proc 直接执行效果
    （`CastPipeline.TriggerCast`）核实现状：不经过步骤 1～9 施法管线、不发本节四个事件——保持
    "不伪造主动施法事件"，其效果结算仍然各自分配 `EffectContext.AttackInstanceId`（每次
    `ExecuteEffectsOnly` 调用一个全新值），与施法实例 id 是两个不同层面、互不冒用的标识，见该
    字段既有判断记录（"为什么是每次调用而不是每次读条/引导"）。验收测试见
    `tests/CastInstanceIdTests.cs`（瞬发/读条 start-success-CastResult 三方一致、连续两次施放
    各自独立关联、校验失败不分配、排队执行/排队被覆盖/施法中断清队列三种排队身份场景、旧构造
    仍可用、Proc 触发不冒充生命周期事件，共 9 例）。公开 API 只新增：四个事件类各一个构造重载 +
    一个只读属性；`CastFailureReason` 新增一个枚举成员 `QueueCleared`（枚举新增成员是纯新增，不
    改变既有成员的底层数值，不影响 ABI）。ABI 探针（`toolchain/abi_probe.ps1`，基线 1.12.0）
    breaks=0。

45. **CORE-118-CAST 根治（外部审计 audit-d6fda65-20260911，P2）：施法者死亡/销毁事件延后派发时，
    防御性重验分支静默吞掉终结事件，本条目 23（RC-03 收口）"`AdvanceOne`/`FinishCast` 完成前
    重验施法者仍然存在/存活"这句话此前只字未提"重验命中之后要不要发事件"——原实现两处防御分支
    命中即直接 `_casting.Remove(casterId); return;`，判断记录写着"理论上不会在正常事件顺序下
    触发"，这一前提不成立：真实探针复现，施法者已死亡/销毁（`IUnitAccess.IsAlive`/`Exists`
    已反映新状态）、对应的 `unit.died`/`entity.destroyed` 已经 `IEventBus.Enqueue` 入队，但要
    等本次 `SkillHost.Update` 结束后的批处理派发才真正送达订阅的 `OnCasterDiedOrDestroyed` ——
    `AdvanceOne`/`FinishCast` 的防御分支跑在派发之前，抢先摘除 `CastState` 且不发任何事件；等
    死亡/销毁事件真正派发到 `Interrupt` 时 `_casting` 已经找不到对应状态而直接 no-op，当前读条
    与排队请求都收不到 `skill.cast_interrupted`/`skill.cast_failed(QueueCleared)`
    （`interrupted=0,queueFailed=0`，若死亡事件恰好先于本次 `Update` 派发则正常为 `1,1`）。
    现在把 `Interrupt`（正常打断/自我打断路径）与 `AdvanceOne`/`FinishCast` 两处防御分支共用同一
    个新私有方法 `TerminateCast(casterId, state, interrupterId, lockSchool, lockDuration)`：三处
    调用点各自先用 `_casting.TryGetValue` + `Remove` 唯一摘除一次某个施法者的 `CastState`，再交
    给 `TerminateCast` 统一发送终结事件——`Dictionary` 的同一个 key 只能被其中一处先摘到，后到达
    的另一条路径必然因为 `TryGetValue` 失败直接返回，天然幂等，不会对同一次施法/排队请求重复
    发送。覆盖场景：死亡事件已入队但尚未派发、实体销毁同类时序、队列替换（不涉及 `TerminateCast`，
    行为不变）、正常中断（控制/受伤/位移）、`FinishCast` 完成前重验命中。验收测试见
    `tests/CORE118_CastPipelineDeathTerminationTests.cs`（死亡事件先/后派发两种时序均得到
    `Interrupted=1, QueueCleared=1` 且各自携带正确 `CastInstanceId`、无重复事件、实体销毁、
    `FinishCast` 兜底、队列替换与正常打断回归，共 7 例）。公开 API 无变化（`TerminateCast` 为
    私有方法）。ABI 探针 breaks=0。

46. **冷却/充能/公共冷却统一只读查询接口（消费方反馈 2026-09-11"冷却充能与公共冷却缺少统一只读
    查询接口"，见 `architecture/落地计划/消费方反馈-2026-09-11-冷却充能只读查询.md`，06 第 3.1/3.6/7
    节同批勘误）：此前 `ISkillHost.GetCooldown` 是消费方唯一的间接查询出口——不区分究竟是技能自身
    冷却、分类冷却还是充能未恢复，不呈现当前充能数、下次充能恢复剩余、公共冷却是否生效、修饰后的
    完整周期，消费方只能靠反复调用 `GetCooldown` + 自行拼凑猜测。新增 `SkillReadiness`（不可变值
    类型，`core/rules/skill/contracts/SkillReadiness.cs`）与 `[Flags] SkillReadinessBlockers`
    （`None`/`SkillCooldown`/`CategoryCooldown`/`GlobalCooldown`/`NoCharges`，可按位或组合）、
    `CategoryCooldownStatus`（分类 id + 该分类剩余，不可变结构体）三个新类型，`ISkillHost` 新增
    带默认实现的 `GetSkillReadiness(Id unitId, Id skillId): SkillReadiness`——C# 8 默认接口成员，
    公开 API 只新增不删改，既有实现方零改动仍可编译。默认实现只能借 `GetCooldown` 拼一个降级快照
    （`IsReady`/`BlockingSources` 粗略推断，公共冷却完全不参与判定，其余字段一律 `null` 表示
    "未知"）；生产实现 `SkillHost.GetSkillReadiness` 显式覆盖，直接从 `CooldownTracker`/GCD/充能
    状态读取精确字段，裁决口径与 `CastSkill`（经 `CastPipeline`）步骤 3（冷却/充能）、步骤 4
    （公共冷却）逐字对齐（`def.HasCharges` 时只看充能数、不检查分类冷却，同 `CooldownTracker.
    IsSkillReady` 既有判断记录；`EffectiveCooldownDuration` 与 `StartCooldownAndGcd` 的
    `modifiedCooldown` 计算同一算式，只读不写）；不推进时间、不创建第二套计时器、不修改任何状态
    （全程不调用 `CooldownTracker` 任何写方法）。`CooldownTracker` 补充只读公开出口
    `CurrentTimeFactor`/`GetChargeRechargeRemaining`/`GetEffectiveChargesMax`/
    `GetEffectiveRechargeTimeScaled`（均为既有私有状态/私有方法的只读转发，不新增账本、不改变既有
    写路径）。**转发/豁免清单**：框架内 `ISkillHost` 的全部实现/包装——生产实现 `SkillHost`（显式
    覆盖，见上）、`RulesAssembly` 内部的 `DeferredSkillCastQuery` 延迟绑定代理（显式转发到
    `Real.GetSkillReadiness`，理由同该代理对其余全部成员的既有转发惯例：默认接口成员的"悄悄吃掉
    默认值"陷阱对组合/包装实现方同样成立，见 `presentation/assembly/tests/
    InterfaceDefaultMemberForwardingTests.cs` 类型判断记录里 `IExprSchema.KnownKeys` 那个反面
    案例）——均已按此登记；`core/rules/*/tests/**/FakeSkillHost`（`ai`/`expr_host`/`gobj`/
    `dialog` 四处测试替身）均位于各自模块的独立测试程序集（`Tests.Rules`/`Tests.Carriers`/
    `Tests.Gameplay`），不在 `InterfaceDefaultMemberForwardingTests` 反射的六个生产程序集
    （`Core.Foundation`/`Core.Numbers`/`Core.Carriers`/`Core.Rules`/`Core.Gameplay`/
    `Presentation.Common`）依赖链上，门禁本就反射不到，比照该门禁类型判断记录"本测试只反射六个
    生产程序集本身，不含各自配对的测试程序集"对 `LegacyFakeSchema` 一类测试替身的既定处理方式，
    不登记豁免、不需要改动。验收测试 `tests/C09_SkillReadinessTests.cs`（经真实
    `Core.Rules.Assembly.RulesAssembly` 装配根，非本模块其余测试惯用的最小 `SkillWorldBuilder`
    直接构造裸 `SkillHost`）：零充能、部分充能恢复中（`CurrentCharges`/`NextChargeRemaining` 同时
    正确）、仅公共冷却阻塞、冷却修饰后 `EffectiveCooldownDuration` 与刚施放完毕的剩余一致、暂停
    （未推进时间）反复查询逐字段不变与恢复（推进时间）后正确反映流逝时间、分类冷却（自身从未
    施放的姊妹技能仍被分类冷却阻塞）共 6 例，每例均断言查询本身只读（重复查询/`GetCooldown`
    结果不变）且快照与随后一次 `CastSkill` 的裁决一致。ABI 探针（`toolchain/abi_probe.ps1`，
    基线 1.12.0）breaks=0。

47. **P2 根治（消费方反馈 2026-09-11"只读就绪查询影响后续充能状态"，见
    `architecture/落地计划/消费方反馈-2026-09-11-充能查询副作用.md`，06 第 3.5 节同批勘误）：
    充能只读查询不得产生状态、`charges` 维度有效上限变化的守恒规则**：条目 46 新增的
    `GetSkillReadiness` 只读接口投入使用后，消费方复现出一处与"只读查询"契约相反的可观测副作用
    ——`CooldownTracker.GetCharges(unitId, def)`/`GetChargeRechargeRemaining` 等只读方法此前经
    `GetOrCreateChargeState` 惰性创建账本，创建时把 `Current` 写成<b>查询当下</b>的有效充能上限；
    若查询发生在一次上限变化（如施加提高充能上限的光环）<b>之前</b>，创建的账本记下旧上限，此后
    上限变化不会追认到已创建的账本——消费方 A/B 探针复现：技能配置 `charges: {max: 2}`、
    `GetSkillReadiness` 查询一次（上限仍是 2）后，再施加把上限从 2 提到 3 的 `charges` 维度
    SpellMod 光环，A（先查询过）此时当前充能仍是 2、只能连续施法成功 2 次；B（完全不查询，直接
    施法）当前充能变为 3、能连续成功 3 次——同一场景"是否提前查询过一次"这一操作本身，决定了
    后续<b>原生</b>施法能连续成功几次。深挖后发现这不只是"查询有副作用"：即便完全不经过任何只读
    查询，只要该 (unit, skill) 组合是在上限变化<b>之后</b>才第一次被任何路径（含首次施法）触达，
    同样会按变化后的上限创建账本；反过来，若组合在上限变化<b>之前</b>已经被触达过（不论是查询还
    是施法），上限变化后当前充能数从不跟着调整——本质是"有效上限变化时，当前充能数不守恒"这一更
    深的账本语义缺陷，查询只是最容易触发它的路径之一。
    <br/><br/>
    根治按两条规则收口（完整判断记录见 `core/rules/skill/core/CooldownTracker.cs` 类型注释）：
    (1) **查询纯化**——`GetCharges(Id, SkillDef)`/`GetChargeRechargeRemaining`/
    `GetEffectiveChargesMax`/`GetEffectiveRechargeTimeScaled`/`IsSkillReady`/
    `GetCooldown(Id, SkillDef)` 六个只读方法全部不再触达 `GetOrCreateChargeState`，改经新增私有
    方法 `ComputeReadOnlySnapshot` 计算只读快照（无账本时按当前有效上限给出默认快照，不创建；有
    账本时按下一条守恒规则<b>计算</b>对账后的值，不写回）；惰性创建收窄为只允许发生在写路径
    （`StartCooldown`/`AddCharge`/`AdvanceCharges`，经 `GetOrCreateChargeState`）。顺带修复一枚
    同源的独立读路径缺口：`GetCooldown(Id, SkillDef)` 此前内部调用 `GetCharges(Id, SkillDef)`
    间接触发创建，现同样改走 `ComputeReadOnlySnapshot`。(2) **充能上限变化守恒规则**——
    `ChargeState` 新增字段 `KnownEffectiveMax`（记录"上一次对账时的有效上限"）；新增纯函数
    `ReconcileForMaxChange(current, rechargeRemaining, knownMax, newMax)`：上限<b>提高</b> Δ →
    `Current += Δ`（新增充能格立即可用，正在进行中的恢复窗口不受影响、不重置）；<b>降低</b> →
    `Current` 夹取到不超过新上限，夹取后若恰好满充能则 `RechargeRemaining` 清零。写路径
    （`GetOrCreateChargeState`/`AdvanceCharges`）与只读路径（`ComputeReadOnlySnapshot`）共用同一
    个纯函数，前者对账后写回 `ChargeState`，后者只使用返回值、不写回——保证只读查询看到的数字与
    "紧接着这次查询之后立即发生一次原生写路径触达"会产生的结果完全一致，不会因为"查没查询过"而
    改变后续原生施法的实际次数；`AdvanceCharges`（`SkillHost.Update` 逐个已知充能组合调用）同样
    先对账再推进——充能上限光环生效时充能往往已经满（`RechargeRemaining<=0`），若仍先判定
    `RechargeRemaining<=0` 提前返回、后对账，会跳过这类"满充能状态下上限提高"的场景，因此对账必须
    排在提前返回判断之前。
    <br/><br/>
    测试：`tests/CooldownTrackerReadOnlyQueryTests.cs`（直接构造裸 `CooldownTracker`，验证只读
    方法调用前后 `TrackedChargeKeys`——`SkillHost.Update` 借以枚举需要推进充能恢复的组合——保持
    不变，覆盖 0/1/多次调用；充能上限提高/降低/满充能时提高三种守恒场景，共 9 例）；
    `tests/C09b_ChargeQueryConservationTests.cs`（经真实 `RulesAssembly` 装配根 + 真实 `charges`
    维度 SpellMod，同一份技能/光环定义分别驱动两个独立单位模拟"先查询"与"不查询"两条路径，逐一
    对照最终快照与连续施法成功次数：上限提高消费方原始场景、上限降低到低于当前触发夹取与恢复窗口
    清零、部分充能恢复中叠加上限提高、零充能叠加上限提高立即可用、暂停区间内反复查询叠加上限变化
    不产生任何隐性推进，共 5 例）；既有 `tests/C09_SkillReadinessTests.cs` 六例、
    `tests/ChargesSpellModTests.cs`、`tests/ChargesZeroRechargeTests.cs` 等既有充能相关测试保持
    全部通过（本次改动不改变任何原生写路径的既定行为，只收紧只读路径 + 补齐上限变化对账）。公开
    API 无变化（`GetOrCreateChargeState`/`ComputeReadOnlySnapshot`/`ReconcileForMaxChange` 均为
    私有方法，`ChargeState` 为私有嵌套类型）。ABI 探针（`toolchain/abi_probe.ps1`，基线 1.12.0）
    breaks=0。

## ADR-0026《技能位移的连续模式》：`move` 效果原语的 `motion: continuous` 分支

消费方反馈"连续技能位移"（`architecture/落地计划/消费方反馈-2026-09-11-技能位移连续模式.md`）：
`EffectDispatcher.ApplyMove` 的三种子类型（`charge`/`leap`/`knockback`）只做一次性 `SetPosition`，
没有连续路径采样/碰撞裁决。新增可选参数 `motion`（`instant`，缺省，原有语义；`continuous`）——
判断记录（命名不复用既有 `mode` 字段）：`mode` 早已表示子类型取值集合
`{charge,leap,knockback}`，"瞬移/连续"是另一个正交维度，复用同名字段会与既有取值集合冲突，改用
`motion` 避免撞名（两个字段各自独立登记进 `SkillSchemas` 的 `move` 变体枚举）。

`motion: continuous` 时，`ApplyMove` 顶部短路到独立的 `ApplyContinuousMove`——按与瞬移分支完全
独立实现（不共享代码/状态）的相同子类型几何公式算出被位移单位与目标点，取 `speed`（缺省用
`duration` 换算：`|target-origin|/duration`，`speed` 存在时优先）、`blocking`（缺省 `stop`）、
`sample_step`（缺省 0，透传"未声明"哨兵值，由 L3 `MovementOptions.DefaultDisplacementSampleStep`
兜底——本模块不持有导航网格尺寸信息，不在这里猜默认值），组装
`Core.Rules.Common.ControlledDisplacementRequest` 交给新增可写属性 `DisplacementSink`
（`IControlledDisplacementSink?`，依赖倒置接口，惯例同既有 `IProjectileSpawner`）。

判断记录（新增可写属性而非新增构造函数参数）：`EffectDispatcher`/`SkillHost` 均不改动任何既有
构造函数的物理签名——`DisplacementSink` 是纯 getter/setter 属性，`Core.Carriers.Assembly.
CarriersAssembly` 在装配期把真正实现（`Core.Carriers.Unit.MovementHost`，直接实现该接口）通过
一行属性赋值接入（`Rules.Skill.DisplacementSink = Movement;`），不需要像 `IProjectileSpawner`
那样把依赖提前到 `RulesAssembly` 构造之前传入——晚于 `RulesAssembly`/`CarriersAssembly` 双双构造
完成后再赋值同样安全，构造函数因此保持零改动，ABI 探针（`toolchain/abi_probe.ps1`）与
`toolchain/abi_surface`（全公开签名表面 diff）均验证通过。未注入时（典型场景：只装配
`core/rules` 不装配 `core/carriers` 的纯 L2 测试/集成）`ApplyContinuousMove` 退化为直接按算出的
目标点 `SetPosition`（同 `instant` 语义，跳过逐 tick 推进/裁决）并记一条警告，不抛异常（惯例同
`ApplyProjectile` 对未注入 `IProjectileSpawner` 的既有降级）。

真正的逐 tick 推进/阻挡裁决/终止条件（到达/受阻/控制打断/施法者死亡/显式 Stop）由 L3
`core/carriers/unit` 的 `MovementHost`/`MovementTickHandler` 落地，见该模块 README
"ADR-0026《技能位移的连续模式》：受控位移"一节的完整判断记录，本模块不重复实现、也不了解其内部
状态机。

测试：`core/rules/skill/tests/C10a_ContinuousMoveDispatchTests.cs`（10 例，用记录用的假
`IControlledDisplacementSink` 验证三种子类型的目标点/被位移单位判定、`speed`/`duration` 换算、
`blocking`/`sample_step` 参数透传、无效速度/零距离 no-op、未注入 sink 的降级、既有三种瞬移模式
在 `motion` 缺省/显式 `instant` 时完全不触碰 sink 的回归）；
`core/rules/skill/tests/C10a_MoveContinuousSchemaRangeTests.cs`（16 例，`SkillSchemas.cs` 新增
五个字段的加载期校验：`speed`/`duration`/`sample_step` 若声明必须 `> 0`，`motion`/`blocking`
枚举取值集合，既有 `mode` 字段取值集合不受影响的回归）；端到端真实施法链路见
`core/carriers/assembly/tests/C10a_ContinuousMoveEndToEndTests.cs`（真实 `CarriersAssembly` + 带
阻挡的 `StubNavigation2D`，复现消费方反馈原始最小场景）。

## 不负责什么

- 不实现命中判定、暴击、护甲/抗性减免、免疫吸收后的实际扣血扣蓝——06 第 4.1 节结算管线本身完全
  是 `core/rules/combat` 的职责；`EffectDispatcher` 对 `school_damage`/`weapon_damage_pct`/`heal`
  三类效果只组装 `EffectContext`（含 SpellMod `effect_value`/`crit_chance` 修正）交给
  `ICombatHost.ResolveEffect`，返回值原样透传。
- 不实现目标选择链（`target.chain_def` 的候选/过滤/排序），只在施法管线步骤 6 调用注入的
  `ITargetHost.Resolve`。
- 不实现 AI 优先级表（`ai.rotation`）求值，只被动接受 `ISkillHost.CastSkill`/`SkillTickHandler`
  消费的 `cast` 意图。
