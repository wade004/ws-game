using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 施法管线（见 06 第 3.6 节九步固定顺序、落地方案 T2-4 行）。每单位同一时刻至多一个
    /// <see cref="CastState"/>（读条或引导）；法术队列复用同一个 <see cref="CastState"/> 实例的
    /// <see cref="CastState.Queued"/> 槽位（每单位一个槽，见 06 第 3.6 节"法术队列"）。
    /// <para>
    /// 触发链递归深度（<see cref="TriggerCast"/>，供 <see cref="ProcHost"/> 与
    /// <c>trigger_spell</c> 效果原语共用）超过 <see cref="SkillOptions.MaxTriggerDepth"/> 直接拒绝
    /// （见落地方案 T2-6 禁止事项"禁止触发链无限递归"）。
    /// </para>
    /// <para>
    /// RC-01 收边勘误：深度不再用一个 ambient 计数器（进入 <c>TriggerCast</c> 自增、退出自减）
    /// 维护——原实现的注释曾声称"本引擎单线程同步执行，ambient 计数器不需要额外的调用上下文对象
    /// 即可正确工作"，但这个假设只对"同一次 <see cref="TriggerCast"/> 调用栈内的同步嵌套"成立：
    /// <c>trigger_spell</c> 效果原语确实是同步嵌套调用（<see cref="EffectDispatcher.ApplyTriggerSpell"/>
    /// 在 <see cref="ExecuteEffectsOnly"/> 执行到一半时直接方法调用 <see cref="TriggerCast"/>），但
    /// <see cref="ProcHost"/> 由 <c>combat.damage_dealt</c>/<c>combat.heal_done</c> 一类事件触发时，
    /// 这些事件是经 <see cref="IEventBus.Enqueue"/> 入队、在后续某个
    /// <see cref="IEventBus.DispatchPending"/> pass 才被派发的（见 <c>core/foundation/event_bus</c>
    /// README"同步派发 + tick 末批处理"）——产生该事件的那次 <see cref="TriggerCast"/> 调用早已
    /// 返回、ambient 计数器已经归零，深度预算对这条路径完全失效，只能靠与技能触发链语义无关的
    /// <see cref="EventBusOptions.MaxDispatchPasses"/> 全局熔断兜底（审计 RC-01）。现在深度改为随
    /// 数据显式传播：<see cref="TriggerCast"/> 的调用方（<see cref="ProcHost"/>/
    /// <see cref="EffectDispatcher.ApplyTriggerSpell"/>）传入"触发本次调用的深度"
    /// （分别读自触发事件的 <see cref="EffectContext.TriggerChainDepth"/>／当前
    /// <see cref="EffectContext"/> 自身的深度），本方法校验后 +1 传给
    /// <see cref="ExecuteEffectsOnly"/> 构造的 <see cref="EffectContext"/>，效果落地事件
    /// （<c>combat.damage_dealt</c>/<c>combat.heal_done</c> 等，见 <see cref="ITriggerChainEvent"/>）
    /// 把这个深度戳到事件上——不管事件是同步 <c>PublishImmediate</c> 还是异步 <c>Enqueue</c>
    /// 派发，深度都随事件本身传播，不再依赖调用栈是否还"活着"。
    /// </para>
    /// </summary>
    public sealed class CastPipeline
    {
        /// <summary>
        /// 第五轮外部审核相邻缺口根治（architecture/落地计划/audit-5e779c6-20260907，同
        /// <see cref="CooldownTracker._currentFactor"/>/<see cref="AuraHost._currentFactor"/>
        /// 同批语义、同一套推导——当前模式 1 个计时单位相当于连续模式（数据 authoring 的规范单位）
        /// 多少秒；初始 1.0，随 <see cref="RescaleAll"/> 每次模式切换累乘更新）：<see
        /// cref="EnterCastOrChannel"/> 施放当下从 <see cref="SkillDef"/> 读到的原始
        /// <c>cast_time</c>/<c>channel_time</c>（经 <see cref="ComputeCastTime"/>/<see
        /// cref="ComputeChannelTickInterval"/>）此前直接原样喂给 <see cref="CastState.Remaining"/>/
        /// <see cref="CastState.TickInterval"/>，不管施放当下究竟处于连续还是离散模式——技能若是在
        /// 离散战斗<b>进行中</b>才第一次被读条/引导（不是"连续模式下已有读条、切换时刻被换算"这条
        /// R05 已解决的路径），按连续模式秒数 authoring 的原始时长会被离散模式按轮推进的 <see
        /// cref="AdvanceOne"/> 直接当成"轮数"消耗，与 <see cref="CooldownTracker.StartCooldown"/>
        /// 判断记录描述的是同一类缺口。本类型现持有 <see cref="_currentFactor"/>，<see
        /// cref="EnterCastOrChannel"/> 在把原始 <c>cast_time</c>/<c>channel_time</c>/引导周期
        /// <c>tick_interval</c> 写入 <see cref="CastState"/> 前先乘以该系数；<see cref="RescaleAll"/>
        /// 同时把已经在读条/引导中的既有 <see cref="CastState"/>（<c>Remaining</c>/
        /// <c>TickInterval</c>/<c>TickAccumulator</c>）按 <paramref name="factor"/> 换算——这是
        /// <see cref="AuraHost.RescaleAll"/> 同步换算 <c>PeriodicAccumulators</c> 判断记录的直接
        /// 类比：既有读条/引导若不随切换换算，"已读条时长/总时长""累加器/tick_interval 还差多久触发
        /// 下一跳"这些比例关系会在切换瞬间被打破。
        /// </summary>
        private double _currentFactor = 1.0;

        private sealed class CastState
        {
            public Id SkillId;
            public SkillDef Def = default!;
            public IReadOnlyList<Id> Targets = Array.Empty<Id>();

            /// <summary>T-N3-8（ADR-0031 决策 6、拍板 7；06 第 3.7 节 2026-09-14 修订段）：本次读条/
            /// 引导开始时（<see cref="EnterCastOrChannel"/>）经 <c>ITargetHost.ResolveWithCoefficients</c>
            /// 解析出的目标分配系数（键为 <see cref="Targets"/> 中的目标 Id）——引导型技能的每一次
            /// 周期跳（<see cref="AdvanceOne"/>）与完成时结算（<see cref="FinishCast"/>）复用同一份
            /// 系数，不逐跳重新解析目标链（同 <see cref="Targets"/> 本身"读条/引导开始时解析一次、
            /// 全程复用"的既有惯例，保证同一次读条/引导内的多次结算对同一批目标给出同一份系数）。
            /// 显式目标/地面坐标施法路径恒为 <c>null</c>（<see cref="Core.Rules.Skill.CastPipeline.ExecuteEffectsOnly"/>
            /// 内退化为逐目标 1.0，见该方法判断记录）。</summary>
            public IReadOnlyDictionary<Id, double>? TargetCoefficients;

            public bool IsChannel;
            public double Remaining;
            public double TickInterval;
            public double TickAccumulator;
            public IReadOnlyList<(Id PowerType, double Amount)> ModifiedCost = Array.Empty<(Id, double)>();

            /// <summary>消费方反馈 2026-09-10（施法生命周期事件缺少实例关联标识建议）根治：排队槽位
            /// 现同时携带排队时分配的实例 id（见 <see cref="CastInstanceId"/>/<c>CastPipeline.CastSkill</c>
            /// 判断记录"身份规则"）——排队接受本身就是一次"分配 id 并随 CastResult 返回"的时刻，
            /// 这个 id 要一路带到排队请求真正开始（<see cref="FinishCast"/> 的 Queued 分支）或者
            /// 中途被覆盖/清空（<c>CastSkill</c>/<see cref="Interrupt"/>）产生的
            /// <see cref="SkillCastFailedEvent"/>。</summary>
            public (Id SkillId, IReadOnlyList<Id> Targets, Id CastInstanceId)? Queued;

            /// <summary>
            /// ADR-0027《地面坐标施法请求》补充：非空表示本次读条/引导来自
            /// <see cref="CastSkillAtGround"/>（地面坐标施法请求），<see cref="Targets"/> 恒为空列表，
            /// 效果落地时改按本字段解析（见 <see cref="ApplyGroundEffectsIfValid"/> 判断记录）；为 null
            /// 表示既有单位目标路径（<see cref="CastSkill"/>），与此前完全一致。两条路径互斥——同一个
            /// <see cref="CastState"/> 不会同时有非空 <see cref="Targets"/> 又非空本字段。
            /// </summary>
            public GroundCastRequest? GroundRequest;

            /// <summary>N19 收边补齐：本次读条/引导开始时的原始时长（引导为 <c>channel_time</c>、
            /// 读条为 <c>cast_time</c>，均已按当时 SpellMod 修正），与本次施法开始时发布的
            /// <see cref="SkillCastStartEvent.CastTime"/> 同一个值——<see cref="FinishCast"/> 完成时
            /// 原样戳到 <see cref="SkillCastSuccessEvent.CastTimeSeconds"/> 上，供表现层区分"这是
            /// 一次真正花了时间的读条/引导完成"（见 SkillCastSuccessEvent.IsInstant 判断记录）。</summary>
            public double CastTimeSeconds;

            /// <summary>消费方反馈 2026-09-10 根治：本次读条/引导开始（<see cref="EnterCastOrChannel"/>）
            /// 时分配的施法实例 id，与本次 <see cref="SkillCastStartEvent"/> 携带的同一个值——
            /// <see cref="FinishCast"/>/<see cref="Interrupt"/> 原样戳到各自发出的
            /// <see cref="SkillCastSuccessEvent"/>/<see cref="SkillCastInterruptedEvent"/> 上，供消费方
            /// 关联"这一次请求"从开始到最终结果的完整生命周期。</summary>
            public Id CastInstanceId;
        }

        private readonly SkillDefCache _defs;
        private readonly CooldownTracker _cooldowns;
        private readonly AuraHost _auraHost;
        private readonly EffectDispatcher _effects;
        private readonly ITargetHost _targetHost;
        private readonly IUnitAccess _units;
        private readonly ISpatialQuery? _spatialQuery;
        private readonly IPowerHost _powerHost;
        private readonly SpellModResolver _spellMods;
        private readonly IEventBus _bus;
        private readonly SkillOptions _options;
        private readonly ISkillDiagnostics _diagnostics;

        /// <summary>
        /// ADR-0027《地面坐标施法请求》补充：地面坐标可行走判定（见
        /// <see cref="ValidateGroundPoint"/> 判断记录）。可选依赖（同 <see cref="_spatialQuery"/>
        /// 既有惯例——02 第 1.8 节 <see cref="Core.Foundation.EngineAdapter.INavigation2D"/> 本身是
        /// "可选接口"）：未注入时地面坐标施法请求跳过可行走校验（宁可漏判，不误判）。
        /// </summary>
        private readonly INavigation2D? _navigation;

        /// <summary>
        /// T-N3-4（ADR-0031 决策 9，06 第 3.6 节 2026-09-14 修订）：施法管线步骤 1.5"使用条件"求值用
        /// 的宿主工厂——复用 <see cref="SkillHost"/> 已经持有并传给 <see cref="ProcHost"/> 的同一个
        /// <see cref="IExprHostFactory"/> 实例（见 <see cref="SkillHost"/> 构造函数判断记录），不是
        /// 本模块新引入的依赖。可选依赖（同 <see cref="_spatialQuery"/>/<see cref="_navigation"/> 既有
        /// 惯例）：只有经下方新增的十四参数构造函数才会被注入；旧的十二/十三参数 <c>[Obsolete]</c>
        /// 兼容构造函数不设置本字段，保持 <c>null</c>——未注入时 <see cref="EvaluateUseCondition"/>
        /// 按"宁可漏判，不误判"既有惯例放行并记一条诊断（见该方法判断记录），不是新的破坏性行为：
        /// 这些旧构造函数在 T-N3-4 之前本就不认识 <c>use_condition</c> 字段。
        /// </summary>
        private readonly IExprHostFactory? _exprHostFactory;

        /// <summary>
        /// T-N3-5（ADR-0031 决策 10；06 第 3.1 节 2026-09-14 修订段"急速缩短动作时长"）：
        /// <see cref="ComputeCastTime"/> 折算急速用的属性宿主，可选（新增十五参数重载参数，见该重载
        /// 判断记录）。为 <c>null</c>（旧调用方经十二/十三/十四参数构造函数构造，未传本参数）时
        /// <see cref="ComputeCastTime"/> 恒不做急速折算，与 <see cref="SkillOptions.HasteAffectsActionTime"/>
        /// 默认关闭时的行为等价（双重保险，不只依赖 <see cref="SkillOptions"/> 一侧的开关）。
        /// </summary>
        private readonly IStatHost? _statHost;

        private readonly Dictionary<Id, CastState> _casting = new Dictionary<Id, CastState>();
        private readonly Dictionary<(Id Unit, Id School), double> _schoolLocks = new Dictionary<(Id, Id), double>();

        private int _castInstanceSeq;

        public CastPipeline(
            SkillDefCache defs,
            CooldownTracker cooldowns,
            AuraHost auraHost,
            EffectDispatcher effects,
            ITargetHost targetHost,
            IUnitAccess units,
            ISpatialQuery? spatialQuery,
            IPowerHost powerHost,
            SpellModResolver spellMods,
            IEventBus bus,
            SkillOptions options,
            ISkillDiagnostics diagnostics,
            INavigation2D? navigation = null)
        {
            _defs = defs ?? throw new ArgumentNullException(nameof(defs));
            _cooldowns = cooldowns ?? throw new ArgumentNullException(nameof(cooldowns));
            _auraHost = auraHost ?? throw new ArgumentNullException(nameof(auraHost));
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
            _targetHost = targetHost ?? throw new ArgumentNullException(nameof(targetHost));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _spatialQuery = spatialQuery;
            _powerHost = powerHost ?? throw new ArgumentNullException(nameof(powerHost));
            _spellMods = spellMods ?? throw new ArgumentNullException(nameof(spellMods));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _navigation = navigation;

            _bus.Subscribe(RulesEventKeys.AuraApplied, OnAuraApplied);
            _bus.Subscribe(RulesEventKeys.CombatDamageDealt, OnDamageDealt);

            // RC-03 收边补齐：施法者死亡/被销毁取消读条/引导/队列（见 OnCasterDiedOrDestroyed
            // 判断记录）——此前只处理控制类打断（OnAuraApplied）与受伤打断（OnDamageDealt），完全
            // 不订阅死亡/销毁，死亡者会继续在 AdvanceOne/FinishCast 里扣资源并结算，销毁后更会因为
            // 访问已注销的 Powers/Unit 资源而抛异常（审计 RC-03）。
            _bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, evt => OnCasterDiedOrDestroyed(evt.UnitId));
            _bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, evt => OnCasterDiedOrDestroyed(evt.EntityId));
        }

        /// <summary>
        /// T-N3-4 新增重载：携带 <see cref="_exprHostFactory"/>（步骤 1.5"使用条件"求值用，见该字段
        /// 判断记录）。判断记录（不是给上方构造函数的 <c>navigation</c> 之后再加一个可选参数）：同
        /// <c>Core.Rules.Skill.SkillDef</c> 十九参数重载判断记录同一套 ABI 兼容惯例——既有构造函数
        /// 追加参数会改变其物理 IL 签名；本重载十四个参数全部不带默认值，与上方主构造函数（12 个
        /// 必填 + <c>navigation</c> 最多 13 个）、下方 ADR-0027 之前的十二参数 <c>[Obsolete]</c> façade
        /// 参数个数均不重叠，互不冲突——恰好传 14 个参数时精确匹配本重载。<see cref="SkillHost"/> 是
        /// 本模块唯一的生产组装点，已改为调用本重载（见其构造函数判断记录），旧的 12/13 参数签名继续
        /// 保留供其余既有调用方（含测试内的直接构造，若存在）二进制/源码兼容，不强制它们迁移。
        /// </summary>
        public CastPipeline(
            SkillDefCache defs,
            CooldownTracker cooldowns,
            AuraHost auraHost,
            EffectDispatcher effects,
            ITargetHost targetHost,
            IUnitAccess units,
            ISpatialQuery? spatialQuery,
            IPowerHost powerHost,
            SpellModResolver spellMods,
            IEventBus bus,
            SkillOptions options,
            ISkillDiagnostics diagnostics,
            INavigation2D? navigation,
            IExprHostFactory? exprHostFactory)
            : this(defs, cooldowns, auraHost, effects, targetHost, units, spatialQuery, powerHost, spellMods,
                bus, options, diagnostics, navigation)
        {
            _exprHostFactory = exprHostFactory;
        }

        /// <summary>
        /// T-N3-5 新增重载：携带 <see cref="_statHost"/>（<see cref="ComputeCastTime"/> 折算急速用，见
        /// 该字段判断记录）。判断记录（不是给上方十四参数重载再加一个可选参数，同该重载"不是给主
        /// 构造函数追加可选参数"同一套 ABI 兼容惯例）：本重载十五个参数全部不带默认值，与既有
        /// 十二/十三/十四参数构造函数参数个数均不重叠，互不冲突——恰好传 15 个参数时精确匹配本重载。
        /// <see cref="SkillHost"/> 是本模块唯一的生产组装点，已改为调用本重载（见其构造函数判断
        /// 记录），旧的 12/13/14 参数签名继续保留供其余既有调用方（含测试内的直接构造，若存在）
        /// 源码/二进制兼容，不强制它们迁移——这些旧构造函数在 T-N3-5 之前本就不认识急速折算这件事，
        /// <see cref="_statHost"/> 恒为 <c>null</c>、<see cref="ComputeCastTime"/> 按未开启处理，是
        /// 既有降级路径的延伸，不是新的破坏性行为。
        /// </summary>
        public CastPipeline(
            SkillDefCache defs,
            CooldownTracker cooldowns,
            AuraHost auraHost,
            EffectDispatcher effects,
            ITargetHost targetHost,
            IUnitAccess units,
            ISpatialQuery? spatialQuery,
            IPowerHost powerHost,
            SpellModResolver spellMods,
            IEventBus bus,
            SkillOptions options,
            ISkillDiagnostics diagnostics,
            INavigation2D? navigation,
            IExprHostFactory? exprHostFactory,
            IStatHost? statHost)
            : this(defs, cooldowns, auraHost, effects, targetHost, units, spatialQuery, powerHost, spellMods,
                bus, options, diagnostics, navigation, exprHostFactory)
        {
            _statHost = statHost;
        }

        /// <summary>
        /// ABI 兼容 façade（ADR-0027 补充 <see cref="_navigation"/> 之前的物理十二参数构造签名，
        /// 同 <c>Core.Rules.Skill.SkillHost</c> 十七/十八参数构造函数判断记录同一套推导）：本重载
        /// 十二个参数全部不带默认值，与上方主构造函数（12 个必填 + <c>navigation</c> 最多 13 个）
        /// 参数个数不重叠时精确匹配本重载，恰好传 13 个参数时精确匹配主构造函数，互不冲突，保证
        /// 已编译好、以"省略 navigation"方式调用本构造函数的既有二进制消费方不需要重新编译。
        /// <c>navigation</c> 固定传 <c>null</c>——旧调用方不会得到地面坐标可行走校验，
        /// <see cref="ValidateGroundPoint"/> 对这类实例恒跳过可行走分支（同未注入
        /// <see cref="_spatialQuery"/> 的既有降级惯例），其余行为与本重载补充之前完全一致。
        /// </summary>
        [Obsolete("ADR-0027 之前的十二参数构造签名，仅为源码/二进制兼容保留；新代码请使用带 navigation 的十三参数构造函数。")]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public CastPipeline(
            SkillDefCache defs,
            CooldownTracker cooldowns,
            AuraHost auraHost,
            EffectDispatcher effects,
            ITargetHost targetHost,
            IUnitAccess units,
            ISpatialQuery? spatialQuery,
            IPowerHost powerHost,
            SpellModResolver spellMods,
            IEventBus bus,
            SkillOptions options,
            ISkillDiagnostics diagnostics)
            : this(defs, cooldowns, auraHost, effects, targetHost, units, spatialQuery, powerHost, spellMods, bus,
                options, diagnostics, navigation: null)
        {
        }

        public bool IsCasting(Id unitId) => _casting.ContainsKey(unitId);

        // -----------------------------------------------------------------
        // 施法请求入口
        // -----------------------------------------------------------------

        public CastResult CastSkill(Id casterId, Id skillId, IReadOnlyList<Id> targets)
        {
            var safeTargets = targets ?? Array.Empty<Id>();

            if (_casting.TryGetValue(casterId, out var activeState))
            {
                // 节拍锁泛化（ADR-0031 决策 10，06 第 3.1/3.6 节 2026-09-14 修订）："respects_gcd=false
                // 的反应类技能可在他技能动作时长内插入"——只在 GcdEnabled=false 时判定（GcdEnabled=true
                // 时本分支整体不生效，见下方 Fail 分支同一条判断记录，T-N3-4 硬性规则"禁止改
                // GcdEnabled=true 的行为"）。ClassifyReactiveInsert 只对respects_gcd=false 且瞬发（无
                // 读条/引导）的技能给出"安全插入"结论——见该方法判断记录"反应类插入的槽位保护"：
                // 非瞬发的反应类技能不满足安全插入条件，落回下面的排队/Fail 分支，不静默覆盖仍在读条/
                // 引导中的原技能状态。
                var insertOutcome = _options.GcdEnabled
                    ? ReactiveInsertOutcome.NotReactive
                    : ClassifyReactiveInsert(casterId, skillId);

                if (insertOutcome == ReactiveInsertOutcome.SafeInstantInsert)
                {
                    // 不占用/不清空现有 CastState 槽位——直接当作施法者当前不忙一样跑完整条
                    // TryStartCast 管线；瞬发（cast_time/channel_time 均为 0）保证 EnterCastOrChannel
                    // 不会写入 _casting[casterId]（见该方法判断记录"瞬发"分支），因此不打断/不覆盖
                    // activeState。
                    return TryStartCast(casterId, skillId, safeTargets);
                }

                if (activeState.Remaining <= _options.QueueWindow)
                {
                    // 消费方反馈 2026-09-10（施法生命周期事件缺少实例关联标识建议）根治：排队接受
                    // 本身就是"校验通过、进入排队"的时刻（见 CastState.Queued 判断记录"身份规则"），
                    // 在这里分配 id 并随 CastResult 返回——若该队列槽位此前已经排了另一个尚未执行的
                    // 请求，本次调用会直接覆盖它（原实现对此完全静默，旧排队请求既不会执行也不会
                    // 收到任何通知），现在改为先给被覆盖的旧请求发一条 QueueCleared 的
                    // SkillCastFailedEvent（携带它自己的实例 id），再覆盖。
                    var queuedInstanceId = NextCastInstanceId();
                    if (activeState.Queued.HasValue)
                    {
                        var overwritten = activeState.Queued.Value;
                        _bus.Enqueue(new SkillCastFailedEvent(
                            casterId, overwritten.SkillId, CastFailureReason.QueueCleared, overwritten.CastInstanceId));
                    }

                    activeState.Queued = (skillId, safeTargets, queuedInstanceId);
                    return CastResult.Ok(queuedInstanceId);
                }

                // 判断记录：06 第 3.6 节只描述了"窗口内入队"的行为，未规定窗口外再次施法请求的
                // 处理方式；本模块拍板窗口外一律拒绝（呼应"每单位一个队列槽"——不支持排更多队）。
                // 契约缺口已补齐：原实现这里复用 OnCooldown 作为最接近的失败语义，集成任务已给
                // CastFailureReason 补上专门的 Busy（施法者当前"不可用"，原因是仍在读条/引导而非
                // 真正的冷却），见该原因码注释；本处改用 Busy，OnCooldown 恢复只表示步骤 3 冷却/
                // 充能未就绪。窗口外的拒绝本身未曾分配 id（见 Fail 判断记录"校验阶段失败不分配"）。
                //
                // T-N3-4 节拍锁泛化：GcdEnabled=true 时保持 Busy 不变（ABI 保护，硬性规则"禁止改
                // GcdEnabled=true 的行为"）。GcdEnabled=false 时，走到这里的请求要么是
                // respects_gcd=true（06 §3.6 修订"respects_gcd 为真的新施法在他技能动作中返回
                // ActionLocked"），要么是未登记/被动技能（ClassifyReactiveInsert 同样归为
                // NotReactive，沿用既有 Busy 语义——这两类技能本就会在 TryStartCast 内部再报出
                // UnknownSkill/PassiveSkill，此处的粗粒度原因码不是最终裁决），要么是"respects_gcd=
                // false 但非瞬发、无法安全插入"的边缘情形（UnsafeNonInstantReactive——用 Busy 而不是
                // ActionLocked，因为它不是被节拍锁挡下，是本模块结构上无法在不丢失原读条/引导状态的
                // 前提下容纳第二个并发的非瞬发 CastState，见 ClassifyReactiveInsert 判断记录）。
                return Fail(casterId, skillId, _options.GcdEnabled || insertOutcome == ReactiveInsertOutcome.UnsafeNonInstantReactive
                    ? CastFailureReason.Busy
                    : CastFailureReason.ActionLocked);
            }

            return TryStartCast(casterId, skillId, safeTargets);
        }

        /// <summary>见 <see cref="CastSkill"/> 判断记录"节拍锁泛化"与 <see cref="ClassifyReactiveInsert"/>。</summary>
        private enum ReactiveInsertOutcome
        {
            /// <summary>技能未登记、是被动技能、或 <c>respects_gcd=true</c>——不满足反应类插入条件，
            /// 落回既有排队/Busy/ActionLocked 分支（<see cref="CastSkill"/> 未知/被动技能延后到
            /// <see cref="TryStartCast"/> 内部报出各自的精确原因码，这里不重复判定）。</summary>
            NotReactive,

            /// <summary><c>respects_gcd=false</c> 且瞬发（<c>channel_time&lt;=0</c> 且折算后
            /// <c>cast_time&lt;=0</c>）——可以安全插入，不占用现有 <see cref="CastState"/> 槽位。</summary>
            SafeInstantInsert,

            /// <summary><c>respects_gcd=false</c> 但需要非瞬发的读条/引导——本模块的 <see
            /// cref="_casting"/> 每个施法者只有一个槽位，插入会覆盖/丢失仍在读条/引导中的原技能状态
            /// （且不会像 <see cref="Interrupt"/> 那样补发 <see cref="SkillCastInterruptedEvent"/>），
            /// 保守回退到既有排队/Busy 分支，不静默覆盖（见 <c>core/rules/skill/README.md</c> 判断
            /// 记录"反应类插入的槽位保护"）。</summary>
            UnsafeNonInstantReactive,
        }

        /// <summary>
        /// 判定 <paramref name="skillId"/>（在 <paramref name="casterId"/> 当前的 <c>override_skill</c>
        /// 重定向之后）是否满足"节拍锁泛化"下的反应类插入条件（见 <see cref="ReactiveInsertOutcome"/>
        /// 各成员注释）。只在调用方已确认 <c>GcdEnabled=false</c> 时调用——本方法自身不检查
        /// <see cref="SkillOptions.GcdEnabled"/>。
        /// </summary>
        private ReactiveInsertOutcome ClassifyReactiveInsert(Id casterId, Id skillId)
        {
            if (!_defs.TryGetSkillDef(skillId, out var def))
            {
                return ReactiveInsertOutcome.NotReactive;
            }

            var overridden = _auraHost.ResolveSkillOverride(casterId, skillId);
            if (overridden.HasValue && _defs.TryGetSkillDef(overridden.Value, out var overriddenDef))
            {
                def = overriddenDef;
            }

            if (def.IsPassive || def.RespectsGcd)
            {
                return ReactiveInsertOutcome.NotReactive;
            }

            var wouldBeInstant = def.ChannelTime <= 0 && ComputeCastTime(casterId, def) <= 0;
            return wouldBeInstant
                ? ReactiveInsertOutcome.SafeInstantInsert
                : ReactiveInsertOutcome.UnsafeNonInstantReactive;
        }

        /// <summary>
        /// ADR-0027《地面坐标施法请求》：见 <see cref="Core.Rules.Common.ISkillHost.CastSkillAtGround"/>
        /// 判断记录。裁决口径与 <see cref="CastSkill"/> 共用步骤 1～5，随后换成地面坐标专属校验
        /// （<see cref="TryStartCastAtGround"/>）。
        /// <para>
        /// 判断记录（不支持法术队列）：<see cref="CastSkill"/> 的"读条即将结束前的窗口内可预先提交
        /// 下一个施法请求"（06 第 3.6 节法术队列）依赖 <see cref="CastState.Queued"/> 这一
        /// <c>(SkillId, IReadOnlyList&lt;Id&gt; Targets, Id CastInstanceId)</c> 三元组，形状是单位目标
        /// 专属的；把地面坐标请求也塞进同一个队列槽位需要扩出第二套"排队的是地面请求还是单位目标
        /// 请求"的分支语义，超出消费方反馈"动态地面坐标施法请求"的最小场景与验证条件范围（见
        /// architecture/落地计划/消费方反馈-2026-09-11-地面坐标施法.md）。本方法在施法者已经处于
        /// 读条/引导中时一律直接拒绝（<see cref="CastFailureReason.Busy"/>），不区分是否落在
        /// <see cref="SkillOptions.QueueWindow"/> 窗口内——与 <see cref="CastSkill"/> 窗口外拒绝复用
        /// 同一个失败原因码，但地面坐标请求没有"窗口内则入队"这一分支。地面坐标施法排队留待后续
        /// 有真实场景需求时再补齐，不在本次范围内过度设计。
        /// </para>
        /// </summary>
        public CastResult CastSkillAtGround(Id casterId, Id skillId, GroundCastRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (_casting.ContainsKey(casterId))
            {
                return Fail(casterId, skillId, CastFailureReason.Busy);
            }

            return TryStartCastAtGround(casterId, skillId, request);
        }

        /// <summary>见 <see cref="CastState.Queued"/>/<see cref="CastSkill"/> 判断记录"身份规则"：
        /// <paramref name="presetCastInstanceId"/> 非空时表示本次调用是 <see cref="FinishCast"/> 续跑
        /// 排队请求（排队接受时已经分配过 id，见 <see cref="CastSkill"/>），本方法内全部失败分支与
        /// 最终 <see cref="EnterCastOrChannel"/> 都改用这个既有的 id，不再重新分配；为 <c>null</c>
        /// （默认，顶层 <see cref="CastSkill"/> 直接调用）时保持原有语义——校验阶段任何一步失败都不
        /// 分配 id（<see cref="Fail(Id,Id,CastFailureReason,Id?)"/> 收到 <c>null</c> 原样发出不带
        /// 实例 id 的 <see cref="SkillCastFailedEvent"/>），只有真正走到步骤 8
        /// <see cref="EnterCastOrChannel"/> 才第一次分配。</summary>
        private CastResult TryStartCast(Id casterId, Id skillId, IReadOnlyList<Id> targets, Id? presetCastInstanceId = null)
        {
            if (!_defs.TryGetSkillDef(skillId, out var def))
            {
                return Fail(casterId, skillId, CastFailureReason.UnknownSkill, presetCastInstanceId);
            }

            var overridden = _auraHost.ResolveSkillOverride(casterId, skillId);
            if (overridden.HasValue && _defs.TryGetSkillDef(overridden.Value, out var overriddenDef))
            {
                skillId = overridden.Value;
                def = overriddenDef;
            }

            if (def.IsPassive)
            {
                return Fail(casterId, skillId, CastFailureReason.PassiveSkill, presetCastInstanceId);
            }

            // 步骤 1：存活与状态
            if (!_units.IsAlive(casterId))
            {
                return Fail(casterId, skillId, CastFailureReason.Dead, presetCastInstanceId);
            }

            var control = _auraHost.GetControlFlags(casterId);
            const ControlFlags fullyIncapacitated = ControlFlags.NoCast | ControlFlags.NoMove | ControlFlags.NoAttack;
            if ((control & fullyIncapacitated) == fullyIncapacitated)
            {
                return Fail(casterId, skillId, CastFailureReason.Stunned, presetCastInstanceId);
            }

            if ((control & ControlFlags.NoCast) != 0)
            {
                return Fail(casterId, skillId, CastFailureReason.Silenced, presetCastInstanceId);
            }

            // 步骤 1.5：使用条件（ADR-0031 决策 9，06 第 3.1/3.6 节 2026-09-14 修订）——插入在"存活与
            // 状态"之后、"学派锁定"之前，其余步骤编号不变。
            if (!EvaluateUseCondition(casterId, def, targets))
            {
                return Fail(casterId, skillId, CastFailureReason.ConditionNotMet, presetCastInstanceId);
            }

            // 步骤 2：学派锁定
            if (GetSchoolLockRemaining(casterId, def.School) > 0)
            {
                return Fail(casterId, skillId, CastFailureReason.SchoolLocked, presetCastInstanceId);
            }

            // 步骤 3：冷却/充能
            if (!_cooldowns.IsSkillReady(casterId, def))
            {
                var reason = def.HasCharges && _cooldowns.GetCharges(casterId, def) <= 0
                    ? CastFailureReason.NoCharges
                    : CastFailureReason.OnCooldown;
                return Fail(casterId, skillId, reason, presetCastInstanceId);
            }

            // 步骤 4：节拍锁（ADR-0031 决策 10，06 第 3.1/3.6 节 2026-09-14 修订"公共冷却泛化为节拍
            // 锁"）——GcdEnabled=true 分支逐字节不变（ABI 保护，T-N3-4 硬性规则）；GcdEnabled=false
            // 分支是防御性收口：本方法在 CastSkill 的"反应类插入"分支下会在 _casting[casterId] 仍
            // 持有其它技能状态时被直接调用（见该分支判断记录），但届时 def.RespectsGcd 恒为 false
            // （ClassifyReactiveInsert 已经筛过），下面这个 respects_gcd=true 分支实际上不会被这条
            // 路径触发——保留它是为了不让 TryStartCast 未来任何新增调用路径悄悄绕过节拍锁语义（同一
            // 判定条件即便重复判断一次也是零成本的布尔短路）。离散模式（isDiscreteStep）不受影响，
            // 同既有 GcdActive 分支惯例（06 §3.6 修订"离散模式不受影响"）。
            var isDiscreteStep = _options.IsDiscreteStep?.Invoke() ?? false;
            if (_options.GcdEnabled)
            {
                if (def.RespectsGcd && !isDiscreteStep && !_cooldowns.IsGcdReady(casterId))
                {
                    return Fail(casterId, skillId, CastFailureReason.GcdActive, presetCastInstanceId);
                }
            }
            else if (def.RespectsGcd && !isDiscreteStep && _casting.ContainsKey(casterId))
            {
                return Fail(casterId, skillId, CastFailureReason.ActionLocked, presetCastInstanceId);
            }

            // 步骤 5：资源（06 第 3.6 节表格：本步只检查"cost 是否够；离散模式下另检查
            // action_cost 行动点是否够"——是"是否够"的校验，不是扣除；真正扣除见步骤 9）
            var modifiedCost = ComputeCost(casterId, def);
            foreach (var (powerType, amount) in modifiedCost)
            {
                if (_powerHost.GetPower(casterId, powerType) < amount)
                {
                    return Fail(casterId, skillId, CastFailureReason.InsufficientPower, presetCastInstanceId);
                }
            }

            // 步骤 6：目标合法性
            // N10 收边补齐（外部审计 68c9bed，P2）：调用方显式传入 targets 时，此前直接跳过
            // ITargetHost.Resolve 整条"来源收集 → 过滤 → 排序 → 截断 → 回退"管线，连带把目标链
            // 声明的 filters（tag/expr 等额外目标条件，见 06 第 5 节）也一并绕过——配置要求
            // "目标必须是 undead"的技能，显式指定一个非 undead 单体目标仍会成功。显式目标不需要
            // 来源收集/排序/截断/回退（调用方已经给定具体目标），但额外目标条件必须继续生效，改用
            // ITargetHost.FilterExplicitTargets 只跑"过滤"这一步（关系类过滤的 AI 侧场景已在
            // ai 模块单独处理，见该接口方法判断记录，不在本步骤重复）。
            //
            // T-N3-8（ADR-0031 决策 6、拍板 7；06 第 3.7 节 2026-09-14 修订段）：链自行收集目标
            // 时改调 ITargetHost.ResolveWithCoefficients（旧 Resolve 保留、行为不变，见该方法判断
            // 记录），额外取得每个目标的分配系数，一路带到步骤 9 的 ExecuteEffectsOnly 按系数缩放
            // 群体效果值。显式目标（targets 非空）经 FilterExplicitTargets 不跑来源收集/排序/
            // 截断，超出策略不适用（该方法判断记录"不按 max_targets 截断"），系数恒为 1，
            // targetCoefficients 传 null（ExecuteEffectsOnly 内退化为逐目标 1.0，见该方法判断
            // 记录），不额外分配字典。
            IReadOnlyList<Id> resolvedTargets;
            IReadOnlyDictionary<Id, double>? targetCoefficients;
            if (targets.Count > 0)
            {
                resolvedTargets = _targetHost.FilterExplicitTargets(def.TargetShapeRef, casterId, targets);
                targetCoefficients = null;
            }
            else
            {
                var resolution = _targetHost.ResolveWithCoefficients(def.TargetShapeRef, casterId);
                var ids = new List<Id>(resolution.Targets.Count);
                Dictionary<Id, double>? coefficients = null;
                foreach (var (target, coefficient) in resolution.Targets)
                {
                    ids.Add(target);
                    if (coefficient != 1.0)
                    {
                        coefficients ??= new Dictionary<Id, double>();
                        coefficients[target] = coefficient;
                    }
                }

                resolvedTargets = ids;
                targetCoefficients = coefficients;
            }

            if (resolvedTargets.Count == 0)
            {
                return Fail(casterId, skillId, CastFailureReason.NoValidTarget, presetCastInstanceId);
            }

            // 步骤 7：距离与视线（Range == 0 表示无限制/作用于自身，见 06 第 3.1 节）
            if (def.Range > 0)
            {
                var casterPos = _units.GetPosition(casterId);
                foreach (var targetId in resolvedTargets)
                {
                    if (Vec2.Distance(casterPos, _units.GetPosition(targetId)) > def.Range)
                    {
                        return Fail(casterId, skillId, CastFailureReason.OutOfRange, presetCastInstanceId);
                    }
                }

                if (_spatialQuery != null)
                {
                    foreach (var targetId in resolvedTargets)
                    {
                        if (!_spatialQuery.HasLineOfSight(casterPos, _units.GetPosition(targetId)))
                        {
                            return Fail(casterId, skillId, CastFailureReason.LineOfSight, presetCastInstanceId);
                        }
                    }
                }
            }

            // RC-04 收边勘误：行动点消耗（离散模式补充，06 第 3.1 节 action_cost）原来插在步骤 5
            // 与步骤 6 之间——TryConsumeActionPoints 是"检查是否够 + 原子扣除"合一的委托（不同于
            // 上面的资源检查：Power 类资源在这一步只探测余量，真正扣除延后到步骤 9 的
            // DeductResources；行动点没有对应的"仅探测"接口，只能整体挪动调用时机），若挪到目标/
            // 射程/视线检查之前，会出现"目标不存在/超距/无视线导致本次施法必然失败"时行动点已经
            // 被扣掉的缺陷（见外部审计 RC-04、validation-repros.txt R6：超距仍产生 1 次消费）。
            // 现在移到全部校验通过、即将进入步骤 8 读条/引导之前——本方法从这里往下不再有会失败
            // 的校验分支，行动点与后续步骤 9 的资源/冷却扣除一样，只在"确定会真正开始读条/引导"
            // 时才真正发生。
            if (isDiscreteStep && def.ActionCost > 0)
            {
                if (_options.TryConsumeActionPoints == null || !_options.TryConsumeActionPoints(casterId, def.ActionCost))
                {
                    return Fail(casterId, skillId, CastFailureReason.InsufficientActionPoints, presetCastInstanceId);
                }
            }

            // 步骤 8：读条/引导
            return EnterCastOrChannel(
                casterId, skillId, def, resolvedTargets, modifiedCost, presetCastInstanceId, targetCoefficients);
        }

        private CastResult EnterCastOrChannel(
            Id casterId, Id skillId, SkillDef def, IReadOnlyList<Id> targets, IReadOnlyList<(Id, double)> modifiedCost,
            Id? presetCastInstanceId = null, IReadOnlyDictionary<Id, double>? targetCoefficients = null)
        {
            // 见 TryStartCast 判断记录：非空表示续跑排队请求，复用排队接受时已经分配的 id，不重新
            // 分配（同一次请求从排队到真正开始只有一个 id）。
            var castInstanceId = presetCastInstanceId ?? NextCastInstanceId();
            // 判断记录见类型顶部"_currentFactor"：cast_time/channel_time 都是从 SkillDef 原始数据
            // 读出的一次性初始值，乘 _currentFactor 折算成当前生效模式的计时单位。isChannel 的判定
            // 用未折算的 def.ChannelTime——_currentFactor 恒为正数，乘法不改变 > 0 判定结果。
            var castTime = ComputeCastTime(casterId, def) * _currentFactor;
            var channelTime = def.ChannelTime * _currentFactor;
            var isChannel = def.ChannelTime > 0;

            _bus.Enqueue(new SkillCastStartEvent(casterId, skillId, isChannel ? channelTime : castTime, castInstanceId));

            if (!isChannel && castTime <= 0)
            {
                // 瞬发：步骤 8 立即完成，直接执行步骤 9。
                DeductResources(casterId, def.Id, modifiedCost);
                StartCooldownAndGcd(casterId, def);
                ExecuteEffectsOnly(casterId, def, targets, targetCoefficients: targetCoefficients);
                // N19 收边补齐：瞬发——IsInstant=true，CastTimeSeconds=0（见 SkillCastSuccessEvent
                // 判断记录）。消费方反馈 2026-09-10：携带与本次 SkillCastStartEvent 同一个 castInstanceId。
                _bus.Enqueue(new SkillCastSuccessEvent(casterId, skillId, targets, isInstant: true, castTimeSeconds: 0, castInstanceId: castInstanceId));
                return CastResult.Ok(castInstanceId);
            }

            if (isChannel)
            {
                // 判断记录：06 第 3.6 节步骤 9 只描述了"读条/引导完成后"统一扣资源进冷却，未单独
                // 规定引导类技能资源/冷却的扣减时点；本模块拍板在引导开始（步骤 8）时一次性扣除，
                // 避免"引导中途打断是否退还部分资源"这一更复杂的分摊语义，与 06 第 3.6 节"打断"
                // 小节只提到"中止步骤 8"、未提资源找回一致（不找回）。
                DeductResources(casterId, def.Id, modifiedCost);
                StartCooldownAndGcd(casterId, def);
            }

            var state = new CastState
            {
                SkillId = skillId,
                Def = def,
                Targets = targets,
                TargetCoefficients = targetCoefficients,
                IsChannel = isChannel,
                Remaining = isChannel ? channelTime : castTime,
                TickInterval = isChannel ? ComputeChannelTickInterval(def) * _currentFactor : 0,
                ModifiedCost = modifiedCost,
                // N19 收边补齐：与本次 SkillCastStartEvent.CastTime 同一个值，见 CastState.CastTimeSeconds
                // 判断记录。
                CastTimeSeconds = isChannel ? channelTime : castTime,
                // 消费方反馈 2026-09-10：与本次 SkillCastStartEvent.CastInstanceId 同一个值，见
                // CastState.CastInstanceId 判断记录。
                CastInstanceId = castInstanceId,
            };

            _casting[casterId] = state;
            return CastResult.Ok(castInstanceId);
        }

        // -----------------------------------------------------------------
        // ADR-0027《地面坐标施法请求》：地面坐标专属校验/进入读条/效果落地
        // -----------------------------------------------------------------

        /// <summary>
        /// 见 <see cref="CastSkillAtGround"/> 判断记录：步骤 1～5 与 <see cref="TryStartCast"/> 逐字节
        /// 相同的检查内容（存活/控制、学派锁定、冷却/充能、公共冷却、资源），换成地面坐标专属的
        /// 步骤 6'（技能是否声明允许地面目标）/7'（射程/视线/可行走，见 <see cref="ValidateGroundPoint"/>）
        /// 取代单位目标步骤 6（目标合法性）/7（距离与视线）。判断记录（为什么不重构成共享一份步骤
        /// 1～5 的私有方法）：<see cref="TryStartCast"/> 是全部既有单位目标施法测试覆盖的核心路径，
        /// 抽出共享辅助方法会让这条已被大量测试锁定的路径多一层间接调用——本方法独立复制这五步，
        /// 用少量重复代码换取"改动地面坐标路径时不可能影响单位目标路径的字节级行为"这一更强的
        /// 保证，与任务书"既有单位目标施法语义与事件序列逐字节不变"的硬约束直接对应。
        /// </summary>
        private CastResult TryStartCastAtGround(Id casterId, Id skillId, GroundCastRequest request)
        {
            if (!_defs.TryGetSkillDef(skillId, out var def))
            {
                return Fail(casterId, skillId, CastFailureReason.UnknownSkill);
            }

            var overridden = _auraHost.ResolveSkillOverride(casterId, skillId);
            if (overridden.HasValue && _defs.TryGetSkillDef(overridden.Value, out var overriddenDef))
            {
                skillId = overridden.Value;
                def = overriddenDef;
            }

            if (def.IsPassive)
            {
                return Fail(casterId, skillId, CastFailureReason.PassiveSkill);
            }

            // 步骤 6'（技能定义门禁）：未声明 ground_target 的技能一律拒绝，保持既有技能行为不变
            // （见 SkillDef.AllowGroundTarget 判断记录）。放在步骤 1～5 之前——这是"这条技能定义是否
            // 支持本入口"这一静态问题，不依赖施法者当前状态，与 PassiveSkill/UnknownSkill 同属
            // "技能定义级别的前置校验"，早失败早返回。
            if (!def.AllowGroundTarget)
            {
                return Fail(casterId, skillId, CastFailureReason.GroundTargetUnsupported);
            }

            // 步骤 1：存活与状态
            if (!_units.IsAlive(casterId))
            {
                return Fail(casterId, skillId, CastFailureReason.Dead);
            }

            var control = _auraHost.GetControlFlags(casterId);
            const ControlFlags fullyIncapacitated = ControlFlags.NoCast | ControlFlags.NoMove | ControlFlags.NoAttack;
            if ((control & fullyIncapacitated) == fullyIncapacitated)
            {
                return Fail(casterId, skillId, CastFailureReason.Stunned);
            }

            if ((control & ControlFlags.NoCast) != 0)
            {
                return Fail(casterId, skillId, CastFailureReason.Silenced);
            }

            // 步骤 1.5：使用条件（ADR-0031 决策 9，06 第 3.1/3.6 节 2026-09-14 修订，S4 要求两条入口
            // 均补齐）——地面坐标请求没有单位目标列表，EvaluateUseCondition 传空列表，
            // use_condition 引用 target.* 时不绑定目标（同 EvaluateUseCondition 判断记录"目标绑定"，
            // 落回 IExprHostFactory 既有"没有绑定目标"分支）。
            if (!EvaluateUseCondition(casterId, def, Array.Empty<Id>()))
            {
                return Fail(casterId, skillId, CastFailureReason.ConditionNotMet);
            }

            // 步骤 2：学派锁定
            if (GetSchoolLockRemaining(casterId, def.School) > 0)
            {
                return Fail(casterId, skillId, CastFailureReason.SchoolLocked);
            }

            // 步骤 3：冷却/充能
            if (!_cooldowns.IsSkillReady(casterId, def))
            {
                var reason = def.HasCharges && _cooldowns.GetCharges(casterId, def) <= 0
                    ? CastFailureReason.NoCharges
                    : CastFailureReason.OnCooldown;
                return Fail(casterId, skillId, reason);
            }

            // 步骤 4：公共冷却（地面坐标入口不支持法术队列，CastSkillAtGround 的外层 Busy 门禁已经
            // 排除了 _casting.ContainsKey(casterId) 的情形——本方法从不会在施法者仍处于读条/引导中
            // 时被调用，见 CastSkillAtGround 判断记录"本方法一律直接拒绝"；节拍锁泛化的
            // ActionLocked/反应类插入分支因此不适用于本入口，T-N3-4 范围之外，保持逐字节不变）。
            var isDiscreteStep = _options.IsDiscreteStep?.Invoke() ?? false;
            if (_options.GcdEnabled && def.RespectsGcd && !isDiscreteStep && !_cooldowns.IsGcdReady(casterId))
            {
                return Fail(casterId, skillId, CastFailureReason.GcdActive);
            }

            // 步骤 5：资源
            var modifiedCost = ComputeCost(casterId, def);
            foreach (var (powerType, amount) in modifiedCost)
            {
                if (_powerHost.GetPower(casterId, powerType) < amount)
                {
                    return Fail(casterId, skillId, CastFailureReason.InsufficientPower);
                }
            }

            // 步骤 6'/7'：地面坐标射程/视线/可行走。
            var invalidReason = ValidateGroundPoint(casterId, def, request.Point);
            if (invalidReason.HasValue)
            {
                return Fail(casterId, skillId, invalidReason.Value);
            }

            // RC-04 同款时机：全部会失败的校验都已通过，行动点消耗放在进入读条/引导之前（同
            // TryStartCast 判断记录）。
            if (isDiscreteStep && def.ActionCost > 0)
            {
                if (_options.TryConsumeActionPoints == null || !_options.TryConsumeActionPoints(casterId, def.ActionCost))
                {
                    return Fail(casterId, skillId, CastFailureReason.InsufficientActionPoints);
                }
            }

            return EnterGroundCastOrChannel(casterId, skillId, def, request, modifiedCost);
        }

        /// <summary>
        /// 地面坐标专属校验：射程/视线仅当 <c>def.Range &gt; 0</c> 时生效（<c>0</c> 表示无限制，见
        /// 06 第 3.1 节、<see cref="TryStartCast"/> 步骤 7 同一惯例）；可行走校验与射程无关，只要
        /// 注入了 <see cref="_navigation"/> 且该施法者能取得地图 id（<see cref="IUnitAccess.GetMapId"/>）
        /// 就无条件生效——一个"无限射程"的地面坐标技能仍然不应该落在墙内。返回 <c>null</c> 表示通过；
        /// 非 null 时是具体的拒绝原因，供 <see cref="TryStartCastAtGround"/>（请求时）与
        /// <see cref="ApplyGroundEffectsIfValid"/>（释放时再校验一次，见该方法判断记录）共用同一套
        /// 判定逻辑，不允许两处出现不一致的裁决口径。
        /// </summary>
        private CastFailureReason? ValidateGroundPoint(Id casterId, SkillDef def, Vec2 point)
        {
            if (def.Range > 0)
            {
                var casterPos = _units.GetPosition(casterId);
                if (Vec2.Distance(casterPos, point) > def.Range)
                {
                    return CastFailureReason.OutOfRange;
                }

                if (_spatialQuery != null && !_spatialQuery.HasLineOfSight(casterPos, point))
                {
                    return CastFailureReason.GroundTargetNoLineOfSight;
                }
            }

            if (_navigation != null)
            {
                var mapId = _units.GetMapId(casterId);
                if (mapId.HasValue && !_navigation.IsWalkable(mapId.Value, point))
                {
                    return CastFailureReason.GroundTargetUnreachable;
                }
            }

            return null;
        }

        private CastResult EnterGroundCastOrChannel(
            Id casterId, Id skillId, SkillDef def, GroundCastRequest request, IReadOnlyList<(Id, double)> modifiedCost)
        {
            var castInstanceId = NextCastInstanceId();
            var castTime = ComputeCastTime(casterId, def) * _currentFactor;
            var channelTime = def.ChannelTime * _currentFactor;
            var isChannel = def.ChannelTime > 0;

            // 事件携带请求时快照坐标（诊断用途）——AtRelease 策略下效果落地那一刻可能重采样出不同
            // 坐标（见 SkillCastSuccessEvent.GroundPoint 携带的才是"实际使用"的坐标），本事件只标记
            // "这次请求当初落在哪"。
            _bus.Enqueue(new SkillCastStartEvent(
                casterId, skillId, isChannel ? channelTime : castTime, castInstanceId, request.Point));

            if (!isChannel && castTime <= 0)
            {
                // 瞬发：步骤 8 立即完成，直接执行步骤 9。
                DeductResources(casterId, def.Id, modifiedCost);
                StartCooldownAndGcd(casterId, def);
                var hitTargets = ApplyGroundEffectsIfValid(casterId, def, request, out var appliedPoint);
                _bus.Enqueue(new SkillCastSuccessEvent(
                    casterId, skillId, hitTargets, isInstant: true, castTimeSeconds: 0,
                    castInstanceId: castInstanceId, groundPoint: appliedPoint ?? request.Point));
                return CastResult.Ok(castInstanceId);
            }

            if (isChannel)
            {
                // 判断记录同 EnterCastOrChannel：引导开始时一次性扣资源进冷却。
                DeductResources(casterId, def.Id, modifiedCost);
                StartCooldownAndGcd(casterId, def);
            }

            var state = new CastState
            {
                SkillId = skillId,
                Def = def,
                Targets = Array.Empty<Id>(),
                GroundRequest = request,
                IsChannel = isChannel,
                Remaining = isChannel ? channelTime : castTime,
                TickInterval = isChannel ? ComputeChannelTickInterval(def) * _currentFactor : 0,
                ModifiedCost = modifiedCost,
                CastTimeSeconds = isChannel ? channelTime : castTime,
                CastInstanceId = castInstanceId,
            };

            _casting[casterId] = state;
            return CastResult.Ok(castInstanceId);
        }

        /// <summary>
        /// 效果落地那一刻（瞬发本身、非引导读条完成、引导每一次周期跳）解析实际坐标并再校验一次
        /// （消费方反馈验证条件"施法期间移动点"）：按 <see cref="GroundCastRequest.SnapshotPolicy"/>
        /// 决定用请求时快照坐标（<see cref="GroundCastSnapshotPolicy.AtRequest"/>）还是重新采样
        /// （<see cref="GroundCastSnapshotPolicy.AtRelease"/>，<see cref="GroundCastRequest.Sampler"/>
        /// 未提供时退化为快照坐标），再用与请求时相同的 <see cref="ValidateGroundPoint"/> 重新判一次
        /// （施法者自己在读条/引导期间也可能移动，射程/视线的判定基准——施法者当前坐标——同样需要
        /// 重新取值，不是只有点会变）。校验未通过时不应用任何效果（返回空列表，<paramref
        /// name="appliedPoint"/> 为 null）——判断记录：不是把整次施法判失败（<see cref="CastResult"/>
        /// 早在请求时就已经同步返回成功，读条期间没有第二次机会改写它），而是与既有
        /// <see cref="FilterDestroyedTargets"/>"目标在读条期间被销毁则从结算列表里剔除，不影响施法
        /// 本身完成"同一惯例——静默跳过这一次效果落地，只记一条诊断，cast 生命周期事件仍然正常收尾。
        /// </summary>
        private IReadOnlyList<Id> ApplyGroundEffectsIfValid(
            Id casterId, SkillDef def, GroundCastRequest request, out Vec2? appliedPoint)
        {
            var point = request.SnapshotPolicy == GroundCastSnapshotPolicy.AtRelease && request.Sampler != null
                ? request.Sampler()
                : request.Point;

            if (ValidateGroundPoint(casterId, def, point).HasValue)
            {
                appliedPoint = null;
                _diagnostics.Warn(
                    $"地面坐标施法 \"{def.Id}\" 效果落地时坐标校验未通过（casterId=\"{casterId}\"），跳过本次效果落地");
                return Array.Empty<Id>();
            }

            appliedPoint = point;
            var hitTargets = _targetHost.ResolveAtPoint(def.TargetShapeRef, casterId, point);
            ExecuteEffectsOnly(casterId, def, hitTargets, groundPoint: point);
            return hitTargets;
        }

        // -----------------------------------------------------------------
        // Update：推进读条/引导
        // -----------------------------------------------------------------

        public void Update(double dt)
        {
            // RC-07 收边勘误：学派锁定推进已经挪到 SkillHost.AdvanceRoundTimers（见
            // AdvanceSchoolLocks 判断记录）——本方法（连续模式每 tick 调用）不再在这里重复推进，
            // 避免 SkillHost.Update 内 "_pipeline.Update(dt) + AdvanceRoundTimers(dt)" 两次调用对
            // 同一个 dt 各推进一次、变成双倍衰减速度。

            var casterIds = new List<Id>(_casting.Keys);
            foreach (var casterId in casterIds)
            {
                AdvanceOne(casterId, dt);
            }
        }

        /// <summary>
        /// H4 补齐（离散模式"读条跨回合"，见 <c>SkillHost.AdvanceCastForActor</c>/
        /// <c>SkillTickHandler</c> 判断记录）：只推进 <paramref name="casterId"/> 自己的读条/引导
        /// 剩余时间与周期效果（逻辑与 <see cref="Update"/> 对单个施法者所做的完全一致，
        /// <see cref="Update"/> 现改为对 <see cref="_casting"/> 里每个施法者各调用一次本方法，
        /// 行为不变），但只处理这一个施法者，不遍历全部在场单位——离散模式下"我方读条是否推进"
        /// 应当只在该行动者自己的离散步内发生（一步 = 该行动者的一回合），不能像连续模式那样
        /// 每个 tick 对全体施法者统一推进（离散步是"以行动者为粒度"，见 03 第 4.2 节步骤 1、
        /// <c>Core.Foundation.SimLoop.WorldSim</c> 判断记录）。该单位当前未在读条/引导时空操作。不推进
        /// <see cref="AdvanceSchoolLocks"/>——学派锁定与"世界时钟"一样按轮统一推进（见
        /// <c>SkillHost.AdvanceRoundTimers</c>），不属于"该行动者自己的时间"。
        /// </summary>
        public void AdvanceOne(Id casterId, double dt)
        {
            if (!_casting.TryGetValue(casterId, out var state))
            {
                return;
            }

            // RC-03 收边补齐：完成前重验施法者仍然存活/存在——构造函数订阅的死亡/销毁事件是主要
            // 清理路径（见 OnCasterDiedOrDestroyed），本处是防御性兜底：例如死亡结算与本次推进
            // 恰好落在同一批 DispatchPending 内、事件尚未先于本次调用被处理的边界情况，或调用方
            // 绕过事件总线直接推进的场景（见类型注释判断记录）。命中则静默丢弃整个 CastState——
            // 不推进周期效果、不结算、不进冷却、不发任何事件（死亡/销毁本身没有"这次读条被谁打断"
            // 这个语义，交给死亡/销毁事件驱动的 Interrupt 路径发 SkillCastInterruptedEvent；这里
            // 命中纯属兜底，理论上不会在正常事件顺序下触发）。
            if (!IsCasterStillValid(casterId))
            {
                // CORE-118-CAST 根治（外部审计 audit-d6fda65-20260911）：此前这里只 Remove、不发任何
                // 事件——若施法者死亡/销毁事件本身已经 Enqueue、但要等本次 SkillHost.Update 之后的
                // DispatchPending 才真正派发到 OnCasterDiedOrDestroyed（见类型注释"RC-03 收边补齐"
                // 与本方法顶部判断记录），本兜底会抢先摘掉 CastState，等死亡事件真正派发时
                // Interrupt 已经在 _casting 里找不到状态而直接 no-op——当前读条与排队请求都拿不到
                // Interrupted/QueueCleared 通知（真实探针复现：先 Update 后 Flush 时
                // interrupted=0,queueFailed=0；先 Flush 后 Update 时正常为 1,1）。改为与
                // FinishCast 的同类兜底、以及 Interrupt 本身共用同一个终结方法 TerminateCast——
                // 摘除 CastState 与发送终结事件收拢成同一次操作，不会再出现"摘了但没发"的中间态；
                // 幂等性由 _casting 的移除时机保证（见 TerminateCast 判断记录），真正的死亡/销毁事件
                // 到达时 Interrupt 发现 _casting 已空会自然 no-op，不会重复发送。
                _casting.Remove(casterId);
                TerminateCast(casterId, state, casterId, null, 0);
                return;
            }

            if (state.IsChannel)
            {
                // CR130-04 根治（外部审计 audit-5c444f1-20260908）：本次推进的 dt 可能已经超出引导
                // 剩余时间（引导会在这次 Update/AdvanceOne 内结束）——只把"引导仍然有效"的那一段时间
                // （Min(dt, Remaining)）计入周期累加器，不能把引导已经结束之后的那段 dt 也当作还在
                // 引导中继续结算周期效果，否则会多算一跳（复现：channel_time=0.5、tick_interval=1、
                // Update(1) 期望 0 次实际 1 次）。与 AuraHost R07 对周期效果尾跳的 Min(dt,Remaining)
                // 处理同一惯例。
                var channelDt = dt < state.Remaining ? dt : Math.Max(0, state.Remaining);
                state.TickAccumulator += channelDt;
                while (state.TickInterval > 0 && state.TickAccumulator >= state.TickInterval && _casting.ContainsKey(casterId))
                {
                    state.TickAccumulator -= state.TickInterval;

                    // ADR-0027：引导型地面坐标施法请求每一次周期跳都是一次独立的"效果落地"——见
                    // ApplyGroundEffectsIfValid 判断记录（AtRelease 策略下每一跳各自重新采样/校验一次，
                    // 不是只在引导开始或结束时各算一次）。
                    if (state.GroundRequest != null)
                    {
                        ApplyGroundEffectsIfValid(casterId, state.Def, state.GroundRequest, out _);
                    }
                    else
                    {
                        var tickTargets = FilterDestroyedTargets(state.Targets);
                        if (tickTargets.Count > 0)
                        {
                            ExecuteEffectsOnly(casterId, state.Def, tickTargets, targetCoefficients: state.TargetCoefficients);
                        }
                    }
                }
            }

            if (!_casting.ContainsKey(casterId))
            {
                // 引导期间的效果触发了自我打断（例如控制类效果，或本次周期效果本身导致施法者/
                // 目标死亡进而经 RC-03 死亡订阅自我打断）。
                return;
            }

            state.Remaining -= dt;
            if (state.Remaining <= 0)
            {
                FinishCast(casterId, state);
            }
        }

        private void FinishCast(Id casterId, CastState state)
        {
            _casting.Remove(casterId);

            // RC-03 收边补齐：同 AdvanceOne 顶部的判断记录——完成前重验施法者仍然有效，命中则
            // 不再走正常成功路径（不结算、不扣资源、不进冷却、不发成功事件、不启动排队的下一个
            // 技能）。CORE-118-CAST 根治（外部审计 audit-d6fda65-20260911）：此前命中即直接
            // return、不发任何终结事件——与 AdvanceOne 顶部兜底同一类缺口（死亡事件已入队但尚未
            // 派发到 OnCasterDiedOrDestroyed 时，本次 Update 恰好推进到 Remaining<=0 触发
            // FinishCast，同样会抢在 Interrupt 之前静默吞掉 CastState）。改为同样经
            // TerminateCast 补发 Interrupted/QueueCleared，与 AdvanceOne/Interrupt 共用同一条
            // 幂等终结路径。
            if (!IsCasterStillValid(casterId))
            {
                TerminateCast(casterId, state, casterId, null, 0);
                return;
            }

            // ADR-0027：地面坐标施法请求与既有单位目标路径在这里彻底分叉——见 CastState.GroundRequest
            // 判断记录"两条路径互斥"。地面分支是全新代码，单位目标分支（else）与本次改动之前逐字节
            // 相同，只是从"方法主体"缩进进了 else 块。
            if (state.GroundRequest != null)
            {
                Vec2? appliedPoint = null;
                var hitTargets = (IReadOnlyList<Id>)Array.Empty<Id>();

                if (!state.IsChannel)
                {
                    DeductResources(casterId, state.Def.Id, state.ModifiedCost);
                    StartCooldownAndGcd(casterId, state.Def);
                    hitTargets = ApplyGroundEffectsIfValid(casterId, state.Def, state.GroundRequest, out appliedPoint);
                }

                // N19/消费方反馈 2026-09-10 同款惯例：见下方 else 分支同一段注释。ADR-0027：Targets
                // 改用"效果落地那一刻实际命中的单位"（引导型在完成时不重复结算，见上方 IsChannel
                // 判断，恒为空列表——每一次周期跳已经在 AdvanceOne 各自结算过），GroundPoint 取实际
                // 使用的坐标（校验未通过时退回请求时快照坐标，仅作诊断标注，不代表真的应用过效果）。
                _bus.Enqueue(new SkillCastSuccessEvent(
                    casterId, state.SkillId, hitTargets, isInstant: false, castTimeSeconds: state.CastTimeSeconds,
                    castInstanceId: state.CastInstanceId, groundPoint: appliedPoint ?? state.GroundRequest.Point));
            }
            else
            {
                var targets = FilterDestroyedTargets(state.Targets);

                if (!state.IsChannel)
                {
                    DeductResources(casterId, state.Def.Id, state.ModifiedCost);
                    StartCooldownAndGcd(casterId, state.Def);
                    if (targets.Count > 0)
                    {
                        ExecuteEffectsOnly(casterId, state.Def, targets, targetCoefficients: state.TargetCoefficients);
                    }
                }

                // N19 收边补齐：非瞬发（真正经历过读条/引导才走到这里）——IsInstant=false，
                // CastTimeSeconds 取本次开始时记录的原始时长（见 CastState.CastTimeSeconds 判断记录）。
                // 消费方反馈 2026-09-10：携带与本次 SkillCastStartEvent 同一个 state.CastInstanceId。
                _bus.Enqueue(new SkillCastSuccessEvent(
                    casterId, state.SkillId, state.Targets, isInstant: false, castTimeSeconds: state.CastTimeSeconds,
                    castInstanceId: state.CastInstanceId));
            }

            if (state.Queued.HasValue)
            {
                // 消费方反馈 2026-09-10：排队请求续跑——传入它排队时已经分配的 CastInstanceId
                // （见 CastState.Queued/TryStartCast 判断记录），不重新分配。
                var (queuedSkill, queuedTargets, queuedInstanceId) = state.Queued.Value;
                TryStartCast(casterId, queuedSkill, queuedTargets, queuedInstanceId);
            }
        }

        /// <summary>RC-03 收边补齐：施法者是否仍然存在且存活（见 <see cref="AdvanceOne"/>/
        /// <see cref="FinishCast"/> 判断记录）。</summary>
        private bool IsCasterStillValid(Id casterId) => _units.Exists(casterId) && _units.IsAlive(casterId);

        /// <summary>RC-03 收边补齐：从目标列表里剔除已不存在（销毁）的单位——存在但已死亡的目标
        /// 仍保留在列表中交给下游各效果自行处理（伤害/治疗经 <see cref="Combat.Resolver.Resolve"/>
        /// 对死亡目标短路为 Miss，见该方法"对已死亡目标再结算"分支；其它效果原语按各自语义处理，
        /// 06 未要求施法管线本身对"目标已死但仍存在"做统一拦截）。全部目标仍存在时返回原列表，
        /// 不额外分配。</summary>
        private IReadOnlyList<Id> FilterDestroyedTargets(IReadOnlyList<Id> targets)
        {
            var anyDestroyed = false;
            for (var i = 0; i < targets.Count; i++)
            {
                if (!_units.Exists(targets[i]))
                {
                    anyDestroyed = true;
                    break;
                }
            }

            if (!anyDestroyed)
            {
                return targets;
            }

            var result = new List<Id>(targets.Count);
            foreach (var targetId in targets)
            {
                if (_units.Exists(targetId))
                {
                    result.Add(targetId);
                }
            }

            return result;
        }

        /// <summary>RC-03 收边补齐：施法者死亡（<c>unit.died</c>）或被销毁（<c>entity.destroyed</c>）
        /// 时取消其读条/引导/队列——两个事件都订阅是因为死亡与实体销毁未必同时发生（死亡后可能
        /// 留一段时间的尸体才真正销毁，见 <see cref="UnitDiedEvent"/>/<see cref="EntityDestroyedEvent"/>
        /// 判断记录），任一先到达都应立即取消，不等另一个。复用 <see cref="Interrupt"/>（interrupterId
        /// 传自身，不加学派锁）——与 <see cref="NotifyMoved"/> 的"自我打断"是同一惯例：死亡/销毁没有
        /// 引入新事件词汇表词条的必要，<c>skill.cast_interrupted</c> 已经能准确表达"这次读条/引导
        /// 没有正常完成"。<see cref="Interrupt"/> 本身对未在读条/引导中的单位是安全 no-op，本方法
        /// 因此天然幂等，不需要额外的 <c>_casting.ContainsKey</c> 前置判断。</summary>
        private void OnCasterDiedOrDestroyed(Id unitId) => Interrupt(unitId, unitId, null, 0);

        // -----------------------------------------------------------------
        // 打断
        // -----------------------------------------------------------------

        public void Interrupt(Id unitId, Id interrupterId, Id? lockSchool, double lockDuration)
        {
            if (!_casting.TryGetValue(unitId, out var state))
            {
                return;
            }

            _casting.Remove(unitId);
            TerminateCast(unitId, state, interrupterId, lockSchool, lockDuration);
        }

        /// <summary>
        /// CORE-118-CAST 根治（外部审计 audit-d6fda65-20260911）：把此前只存在于 <see
        /// cref="Interrupt"/> 里的"发终结事件"逻辑收敛成一个独立方法，供 <see cref="Interrupt"/>
        /// 本身、<see cref="AdvanceOne"/> 与 <see cref="FinishCast"/> 的"施法者已失效"防御性兜底
        /// 三处共用——这三处都会把某个施法者的 <see cref="CastState"/> 从 <see cref="_casting"/>
        /// 里摘除，此前只有 <see cref="Interrupt"/> 会同时发送 <see cref="SkillCastInterruptedEvent"/>
        /// （当前读条/引导）与排队请求的 <see cref="SkillCastFailedEvent"/>(QueueCleared)，另两处
        /// 静默 Remove——当施法者死亡/销毁事件已经 <see cref="IEventBus.Enqueue"/> 入队、但要等本次
        /// <c>SkillHost.Update</c> 结束后的 DispatchPending 才真正派发到订阅的
        /// <see cref="OnCasterDiedOrDestroyed"/>（见类型注释）时，<see cref="AdvanceOne"/>/<see
        /// cref="FinishCast"/> 的防御性重验会抢先摘掉状态，等死亡事件真正派发时 <see
        /// cref="Interrupt"/> 已经在 <see cref="_casting"/> 里找不到对应 state 而直接 no-op——当前
        /// 读条与排队请求都拿不到任何终结通知（真实探针复现：<c>death_dispatch_before_update=False</c>
        /// 时 <c>interrupted=0,queueFailed=0</c>，而先派发时正常为 <c>1,1</c>）。
        /// <para>
        /// 幂等性：本方法不做任何 <see cref="_casting"/> 查找/移除，只负责"已经从字典摘出来的
        /// <paramref name="state"/> 该怎么收尾"——三个调用点都是各自先用 <c>TryGetValue</c> +
        /// <c>Remove</c> 拿到唯一一次这个施法者的 <see cref="CastState"/> 之后才调用本方法，
        /// 而 <see cref="Dictionary{TKey,TValue}"/> 的同一个 key 只能被这三处调用点中的一处
        /// 先摘到——不管是死亡事件先派发（<see cref="Interrupt"/> 先摘）还是 <see
        /// cref="AdvanceOne"/>/<see cref="FinishCast"/> 的兜底先摘，后到达的另一条路径都会因为
        /// <c>TryGetValue</c> 失败而直接返回，不会对同一次施法重复调用本方法、不会重复发送任何
        /// 事件——覆盖"死亡已入队但尚未 Dispatch""实体销毁""队列替换后仍在读条的当前施法被
        /// 打断""正常打断（控制/受伤/位移）""FinishCast 完成前失效"等全部会摘除 CastState 的
        /// 路径，不需要为其中任何一条单独判重。
        /// </para>
        /// </summary>
        private void TerminateCast(Id casterId, CastState state, Id interrupterId, Id? lockSchool, double lockDuration)
        {
            if (lockSchool.HasValue)
            {
                // CR130-03 根治（外部审计 audit-5c444f1-20260908）：lockDuration 是调用方按 authoring
                // 规范秒数传入的原始值（与 cast_time/cooldown_duration 同一口径），与
                // <see cref="EnterCastOrChannel"/> 写入 <c>Remaining</c>/<see
                // cref="CooldownTracker.StartCooldown"/> 写入冷却同款处理——写入前先乘
                // <see cref="_currentFactor"/> 折算成当前模式的计时单位，否则连续模式 authoring 的
                // "3 秒沉默"在离散模式下会被当成"3 轮沉默"。
                _schoolLocks[(casterId, lockSchool.Value)] = lockDuration * _currentFactor;
            }

            // 消费方反馈 2026-09-10：携带被打断的这次施法自己的 CastInstanceId（见
            // CastState.CastInstanceId 判断记录）。
            _bus.Enqueue(new SkillCastInterruptedEvent(casterId, state.SkillId, interrupterId, state.CastInstanceId));

            if (state.Queued.HasValue)
            {
                // 消费方反馈 2026-09-10（施法生命周期事件缺少实例关联标识建议）根治："施法中打断
                // 并清队列"——被打断的这次施法若还排着下一个技能，队列随打断一起清空、永远不会
                // 执行。原实现对此完全静默；现在补发一条携带被清空的排队请求自己 CastInstanceId
                // 的 QueueCleared SkillCastFailedEvent（与被打断的当前施法各自携带自己的 id，
                // 两个事件不共用同一个值）。
                var queued = state.Queued.Value;
                _bus.Enqueue(new SkillCastFailedEvent(casterId, queued.SkillId, CastFailureReason.QueueCleared, queued.CastInstanceId));
            }
        }

        /// <summary>供调用方（移动系统）在单位位移时通知（见 06 第 3.1 节 <c>interrupt_flags</c>
        /// 的 <c>movement</c> 项）：若正在读条/引导且该技能声明了 <see cref="InterruptFlags.Movement"/>，
        /// 视为自我打断。</summary>
        public void NotifyMoved(Id unitId)
        {
            if (_casting.TryGetValue(unitId, out var state) && (state.Def.InterruptFlags & InterruptFlags.Movement) != 0)
            {
                Interrupt(unitId, unitId, null, 0);
            }
        }

        private void OnAuraApplied(IEvent evt)
        {
            if (evt is AuraAppliedEvent applied
                && _casting.TryGetValue(applied.TargetId, out var state)
                && (state.Def.InterruptFlags & InterruptFlags.Control) != 0
                && (_auraHost.GetControlFlags(applied.TargetId) & ControlFlags.NoCast) != 0)
            {
                Interrupt(applied.TargetId, applied.SourceId, null, 0);
            }
        }

        private void OnDamageDealt(IEvent evt)
        {
            if (evt is CombatDamageDealtEvent dmg
                && _casting.TryGetValue(dmg.TargetId, out var state)
                && (state.Def.InterruptFlags & InterruptFlags.DamageTaken) != 0)
            {
                Interrupt(dmg.TargetId, dmg.SourceId, null, 0);
            }
        }

        // -----------------------------------------------------------------
        // 触发链（Proc / trigger_spell 共用，见类型注释）
        // -----------------------------------------------------------------

        /// <summary>
        /// <paramref name="chainDepth"/>：触发本次调用的触发链深度（0 = 由一次未经触发的根结算/
        /// 根效果发起，见类型注释"RC-01 收边勘误"）——调用方传入的是"触发它的那一层"的深度，
        /// 本方法校验通过后统一 +1 作为"本次触发实际执行"的深度，向下传播。
        /// </summary>
        internal bool TriggerCast(Id casterId, Id skillId, IReadOnlyList<Id> targets, int chainDepth)
        {
            if (chainDepth >= _options.MaxTriggerDepth)
            {
                _diagnostics.Error(
                    $"触发链深度达到上限 {_options.MaxTriggerDepth}（casterId=\"{casterId}\", skillId=\"{skillId}\"），" +
                    "已拒绝本次触发，防止无限递归（见落地方案 T2-6 禁止事项；RC-01 收边后深度随事件/" +
                    "效果上下文显式传播，覆盖跨 EventBus 异步派发 pass 的场景，不再依赖调用栈是否" +
                    "仍在同一次同步调用中）");
                return false;
            }

            if (!_defs.TryGetSkillDef(skillId, out var def))
            {
                _diagnostics.Warn($"TriggerCast 引用的技能 \"{skillId}\" 不存在，已忽略");
                return false;
            }

            var resolvedTargets = targets != null && targets.Count > 0 ? targets : new[] { casterId };

            ExecuteEffectsOnly(casterId, def, resolvedTargets, chainDepth + 1);
            return true;
        }

        // -----------------------------------------------------------------
        // 帮助方法
        // -----------------------------------------------------------------

        /// <summary><paramref name="chainDepth"/>：见类型注释"RC-01 收边勘误"，默认 0（正常施法
        /// 管线步骤 8/9 完成后的根结算，即 <see cref="EnterCastOrChannel"/>/<see cref="AdvanceOne"/>/
        /// <see cref="FinishCast"/> 三个调用点，均不显式传参）；<see cref="TriggerCast"/> 是唯一显式
        /// 传入非零值的调用点。
        /// <para>
        /// 攻击实例 id 遗留根治（<c>architecture/落地计划/audit-3224ca1-20260908/AUDIT_REPORT.md</c>
        /// "攻击实例 id"，取代 PR140-04 遗留的"同一攻击者未释放窗口"时序代理合批）：本方法每次调用
        /// 固定分配一个全新的 <see cref="Id"/>（<see cref="NextCastInstanceId"/>，与
        /// <see cref="EnterCastOrChannel"/> 对外返回的 <see cref="CastResult.CastInstanceId"/> 共用
        /// 同一个序列生成器，含义相同——"这一次结算"），本次调用内 <c>def.Effects</c> × <paramref name="targets"/>
        /// 的笛卡尔积产生的全部 <see cref="EffectContext"/> 共享这一个值，作为
        /// <see cref="EffectContext.AttackInstanceId"/>。判断记录（为什么是"每次调用"而不是"每次
        /// 读条/引导"）：<see cref="EnterCastOrChannel"/> 瞬发分支、<see cref="FinishCast"/> 完成分支、
        /// <see cref="AdvanceOne"/> 的引导周期跳、<see cref="TriggerCast"/> 各自独立调用一次本方法——
        /// 任务书原文"施法实例/攻击实例，来自 CastPipeline/CombatHost 的一次结算"里的"一次结算"字面
        /// 就是本方法的一次调用（一批目标在同一时刻各自应用同一组效果）：引导技能的多次周期跳是同一次
        /// 引导发起的、但发生在不同时刻的多次独立结算，各自的目标批次理应各自独立释放命中帧同步，不能
        /// 因为"同属一次引导"就把跨越多个 tick 的批次强行合并成一批（那正是审计要根治的"同一窗口内
        /// 不同攻击被误合批"的另一种表现形式，只是触发场景从"两次独立技能"换成了"同一引导的两个
        /// tick"）；反过来，同一次调用内命中的多个目标（如一次范围攻击命中三个目标）理应共享同一个
        /// 值，使它们的命中帧同步动作原子性地一起释放（PR140-04 原始诉求"AoE 命中批次"）——本设计
        /// 用同一个粒度天然同时满足这两条互相制约的要求，不需要额外的"同一引导跨 tick 但不同批次"
        /// 特判。</para>
        /// </summary>
        /// <summary>
        /// ADR-0027 补充 <paramref name="groundPoint"/>：地面坐标施法请求效果落地时实际使用的坐标，
        /// 原样戳到每一份 <see cref="EffectContext.GroundPoint"/> 上（见该属性判断记录）；默认 null
        /// 表示既有单位目标路径，全部既有调用点（<see cref="EnterCastOrChannel"/>/<see cref="AdvanceOne"/>/
        /// <see cref="FinishCast"/>/<see cref="TriggerCast"/>）均不传本参数——只有
        /// <see cref="ApplyGroundEffectsIfValid"/> 这一个新增调用点会传入非 null 值。
        /// <para>
        /// 勘误（T-N1-6，构造 <see cref="EffectContext"/> 补 <see cref="EffectContext.SourceKind"/>
        /// 之后）：本方法此前按 <c>groundPoint.HasValue</c> 分两个分支分别调用原十五参数/十六参数
        /// 构造函数（"未新增 groundPoint 之前的同一条代码路径逐字节不变"），是为了在 ADR-0027 落地
        /// 当时把改动面收紧到"只新增一个调用点"；T-N1-6 要求全部生产构造点显式传入 sourceKind，
        /// 十五参数构造函数已经没有承载它的余地——统一改经新增的十七参数构造函数（<c>groundPoint</c>
        /// 直接传入本方法的同名可空参数，为 null 与两个分支各自省略该参数的既有效果等价），不再
        /// 区分两个分支；<see cref="EffectContext.GroundPoint"/>/其余字段的取值与改动前逐一对应，
        /// 不变，只是不再由"调用哪个构造函数重载"来决定是否携带 <c>groundPoint</c>。
        /// </para>
        /// </summary>
        private void ExecuteEffectsOnly(
            Id casterId, SkillDef def, IReadOnlyList<Id> targets, int chainDepth = 0, Vec2? groundPoint = null,
            IReadOnlyDictionary<Id, double>? targetCoefficients = null)
        {
            var attackInstanceId = NextCastInstanceId();

            // T-N1-6（ADR-0030 决策 5；06 第 4.1 节）：本次结算所属施法者的来源类别，经
            // IUnitAccess.GetSourceKind 查询一次、本方法内全部效果 × 目标的笛卡尔积共享同一个值
            // （与 attackInstanceId 同一粒度惯例——"这一次结算"）。见本方法 XML 文档"勘误"段。
            var sourceKind = _units.GetSourceKind(casterId);

            foreach (var effect in def.Effects)
            {
                foreach (var targetId in targets)
                {
                    var school = ParamsX.GetIdOpt(effect.Params, "school") ?? def.School;
                    var baseValue = ParamsX.GetNumber(effect.Params, "base_value");
                    var coefficient = ParamsX.GetNumber(effect.Params, "coefficient");
                    var canMiss = effect.Kind != EffectKind.Heal;
                    // T-N3-8（ADR-0031 决策 6、拍板 7；06 第 3.7 节 2026-09-14 修订段）：群体目标
                    // 超出 max_targets 时该目标分得的分配系数——只有步骤 6 经
                    // ITargetHost.ResolveWithCoefficients 解析出的链目标才会有非默认值（见
                    // TryStartCast 判断记录），其余调用点（显式目标、地面坐标施法、TriggerCast 触发
                    // 链）传入 null，此处退化为 1.0（未超出策略参与，逐位不变）。
                    var targetCoefficient = targetCoefficients != null && targetCoefficients.TryGetValue(targetId, out var tc)
                        ? tc
                        : 1.0;

                    var context = new EffectContext(
                        casterId, targetId, def.Id, effect.Kind, school, baseValue, coefficient,
                        effect.Params, auraInstanceId: null, isPeriodic: false, canCrit: true, canMiss: canMiss,
                        tags: def.Tags, triggerChainDepth: chainDepth, attackInstanceId: attackInstanceId,
                        groundPoint: groundPoint, sourceKind: sourceKind, targetCoefficient: targetCoefficient);

                    _effects.ApplyEffect(context);
                }
            }
        }

        /// <summary>
        /// T-N3-5（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 10；06
        /// 第 3.1 节 2026-09-14 修订段）：动作时长（<c>cast_time</c>）先按既有
        /// <see cref="SpellModDimension.CastTime"/> 维度聚合 SpellMod（惯例不变，T-N3-5 之前的唯一
        /// 逻辑），随后新增急速折算。<see cref="SkillOptions.HasteAffectsActionTime"/> 为
        /// <c>false</c>（默认，硬性规则"禁止默认开启急速缩短"）、<see cref="SkillOptions.HasteStat"/>
        /// 未声明、或本实例未接到 <see cref="_statHost"/>（旧构造函数调用方）三者任一成立，直接返回
        /// SpellMod 聚合后的值，与 T-N3-5 之前逐位一致（回归）。
        /// <para>
        /// 开启时：<see cref="ReadHastePercent"/> 读取 <see cref="SkillOptions.HasteStat"/> 的最终值
        /// 并夹到 <c>[0, SkillOptions.MaxHastePct]</c>（百分比数值，如 20 表示 20%，见该字段判断
        /// 记录），折算公式 <c>castTime / (1 + haste% / 100)</c>。
        /// </para>
        /// <para>
        /// 下限（<see cref="SkillOptions.MinActionSeconds"/>）判断记录：只夹住急速造成的缩短——
        /// <c>haste &lt;= 0</c>（未取得任何有效急速值：属性值为 0、施法者未注册、或
        /// <see cref="SkillOptions.HasteStat"/> 未登记，均按 0 处理）时直接返回未折算的原始值，不
        /// 套用下限。理由：06 原文"下限"语境是"急速能把动作时长压多低"，不是"全部技能的最短动作
        /// 时长"——若把下限当作无条件的全局地板，会让 authoring 本就低于下限的瞬发技能在开启本
        /// 策略项后被意外拉长，与"默认不受影响"的最小惊讶原则冲突（落地方案 T-N3-5 契约疑点，上报
        /// 待设计层确认，此为临时判断）。
        /// </para>
        /// </summary>
        private double ComputeCastTime(Id casterId, SkillDef def)
        {
            var baseCastTime = Math.Max(
                0, _spellMods.Apply(casterId, SpellModDimension.CastTime, def.Id, def.School, def.Tags, def.CastTime));

            if (!_options.HasteAffectsActionTime || !_options.HasteStat.HasValue || _statHost == null)
            {
                return baseCastTime;
            }

            var hastePct = ReadHastePercent(casterId, _options.HasteStat.Value);
            if (hastePct <= 0)
            {
                return baseCastTime;
            }

            var shortened = baseCastTime / (1.0 + hastePct / 100.0);
            return Math.Max(shortened, _options.MinActionSeconds);
        }

        /// <summary>
        /// 读取 <paramref name="hasteStat"/> 的最终值并夹到 <c>[0, SkillOptions.MaxHastePct]</c>
        /// （见 <see cref="SkillOptions.HasteStat"/>/<see cref="SkillOptions.MaxHastePct"/> 判断
        /// 记录）。属性未在 <c>stat.definition</c> 登记（<see cref="ArgumentException"/>）或施法者
        /// 未在 <see cref="_statHost"/> 注册（<see cref="InvalidOperationException"/>）均按 0（不
        /// 折算）处理，不阻断施法——同 <c>Core.Rules.Combat.Resolver.GetStatSafe</c>/C02 判断记录
        /// "属性缺失或单位未注册按 0 处理，不抛异常"同一惯例；前者额外记一条诊断警告（数据配置问题，
        /// 值得提醒），后者不警告（施法者未注册属于正常的单位生命周期边缘状态，不是内容错误）。
        /// </summary>
        private double ReadHastePercent(Id casterId, Id hasteStat)
        {
            double raw;
            try
            {
                raw = _statHost!.GetStat(casterId, hasteStat);
            }
            catch (ArgumentException)
            {
                _diagnostics.Warn(
                    $"SkillOptions.HasteStat \"{hasteStat}\" 未在 stat.definition 登记，"
                    + "ComputeCastTime 急速折算按 0 处理。");
                return 0.0;
            }
            catch (InvalidOperationException)
            {
                return 0.0;
            }

            return Math.Min(Math.Max(raw, 0.0), _options.MaxHastePct);
        }

        /// <summary>
        /// 步骤 1.5"使用条件"求值（ADR-0031 决策 9，06 第 3.1/3.6 节 2026-09-14 修订）。
        /// <c>def.UseCondition == null</c>（未声明）时直接放行，零行为变化。
        /// <para>
        /// 判断记录（使用条件的目标绑定）：本步位于步骤 6（目标合法性/来源收集-过滤-排序-截断-回退）
        /// 之前，此刻还没有"解析后的目标列表"可言——06 §3.6 修订段未进一步规定条件引用 <c>target.*</c>
        /// 时该绑定哪个目标，这是需要向上汇报的契约缺口。本实现按最贴近调用方意图的口径处理：若
        /// <paramref name="targets"/>（<see cref="TryStartCast"/> 收到的调用方原始显式目标列表，未经
        /// <see cref="ITargetHost"/> 过滤/排序/截断）非空，取第一个作为候选目标绑定给 <c>target</c>
        /// 分组；显式目标为空（调用方依赖 <c>target_shape_ref</c> 自动选择，如 AI/一键智能释放常见
        /// 用法）时不绑定目标（<c>targetId: null</c>）——<c>target.*</c> 引用落回 <see
        /// cref="IExprHostFactory"/> 既有"没有绑定目标"分支（记一条警告、按类型默认值处理，见
        /// <c>RulesExprHostFactory.Host.Query</c> 判断记录），不是本方法的独立分支、不额外报错。内容
        /// 作者据此约束：<c>use_condition</c> 引用 <c>target.*</c> 的技能应配合显式目标调用（游戏层
        /// UI 通常本就是"先选目标再放技能"），或改用不依赖目标的条件（<c>self.*</c>/<c>combat.*</c>）。
        /// </para>
        /// </summary>
        private bool EvaluateUseCondition(Id casterId, SkillDef def, IReadOnlyList<Id> targets)
        {
            if (def.UseCondition == null)
            {
                return true;
            }

            if (_exprHostFactory == null)
            {
                // 未注入 IExprHostFactory（旧的 12/13 参数 [Obsolete] 构造签名，见该字段判断记录）：
                // 无法求值，按本模块既有"可选依赖未注入时宁可漏判，不误判"惯例放行（同
                // _spatialQuery/_navigation 缺省降级），只记一条诊断，不阻断施法、不抛异常。
                _diagnostics.Warn(
                    $"技能 \"{def.Id}\" 声明了 use_condition，但 CastPipeline 未注入 IExprHostFactory（ABI 兼容旧构造签名），按条件为真处理");
                return true;
            }

            var candidateTarget = targets.Count > 0 ? targets[0] : (Id?)null;
            var host = _exprHostFactory.CreateFor(casterId, candidateTarget, null);
            return ExprEvaluator.EvaluateBool(def.UseCondition, host, new ExprDiagnosticsRecorder());
        }

        private IReadOnlyList<(Id, double)> ComputeCost(Id casterId, SkillDef def)
        {
            var result = new List<(Id, double)>(def.Cost.Count);
            foreach (var (powerType, amount) in def.Cost)
            {
                result.Add((powerType, _spellMods.Apply(casterId, SpellModDimension.Cost, def.Id, def.School, def.Tags, amount)));
            }

            return result;
        }

        private void DeductResources(Id casterId, Id skillId, IReadOnlyList<(Id PowerType, double Amount)> cost)
        {
            foreach (var (powerType, amount) in cost)
            {
                _powerHost.ModifyPower(casterId, powerType, -amount, skillId);
            }
        }

        private void StartCooldownAndGcd(Id casterId, SkillDef def)
        {
            var modifiedCooldown = _spellMods.Apply(casterId, SpellModDimension.Cooldown, def.Id, def.School, def.Tags, def.CooldownDuration);
            _cooldowns.StartCooldown(casterId, def, modifiedCooldown);

            var isDiscreteStep = _options.IsDiscreteStep?.Invoke() ?? false;
            if (_options.GcdEnabled && def.RespectsGcd && !isDiscreteStep)
            {
                _cooldowns.StartGcd(casterId, _options.GcdDuration);
            }
        }

        private static double ComputeChannelTickInterval(SkillDef def)
        {
            foreach (var effect in def.Effects)
            {
                if (effect.Params.ContainsKey("tick_interval"))
                {
                    return ParamsX.GetNumber(effect.Params, "tick_interval", def.ChannelTime);
                }
            }

            return def.ChannelTime;
        }

        private double GetSchoolLockRemaining(Id unitId, Id school) =>
            _schoolLocks.TryGetValue((unitId, school), out var v) ? Math.Max(0, v) : 0;

        /// <summary>
        /// RC-07 收边勘误：原为 <see cref="Update"/>（连续模式每 tick 调用）内部私有步骤，只在
        /// 连续模式被推进——离散模式完全不调用 <see cref="Update"/>（见 <c>SkillTickHandler.Execute</c>
        /// 判断记录："连续步调用 SkillHost.Update；离散步只推进当前行动者自己的读条/引导 +
        /// 由 sim.round_ended 驱动 SkillHost.AdvanceRoundTimers"），学派锁定因此在离散模式下永远
        /// 不会衰减（见外部审计 RC-07）。现在改为 internal，由 <see cref="SkillHost.AdvanceRoundTimers"/>
        /// 统一调用——该方法本身在连续模式下经 <see cref="SkillHost.Update"/> 每 tick 调用一次
        /// （<c>dt=每 tick 的秒数</c>），在离散模式下经 <c>sim.round_ended</c> 每轮调用一次
        /// （<c>dt=1.0</c>），两种模式各自只有唯一一条推进路径，不会重复推进（见 <see cref="Update"/>
        /// 判断记录）。
        /// </summary>
        internal void AdvanceSchoolLocks(double dt)
        {
            var keys = new List<(Id, Id)>(_schoolLocks.Keys);
            foreach (var key in keys)
            {
                _schoolLocks[key] = Math.Max(0, _schoolLocks[key] - dt);
            }
        }

        /// <summary>
        /// 第五轮外部审核相邻缺口根治：见类型顶部 <see cref="_currentFactor"/> 判断记录。由
        /// <c>SkillHost.OnTimeModelRescaled</c> 与 <see cref="CooldownTracker.RescaleAll"/>/
        /// <see cref="AuraHost.RescaleAll"/> 同一批调用（同一次模式切换广播的
        /// <see cref="Core.Rules.Common.TimeModelRescaledEvent"/>）。<paramref name="factor"/>
        /// 语义同 <see cref="CooldownTracker.RescaleAll"/>：新单位下 1 个单位对应旧单位下
        /// <paramref name="factor"/> 个单位。
        /// </summary>
        public void RescaleAll(double factor)
        {
            if (factor <= 0)
            {
                throw new ArgumentException("factor 必须为正数", nameof(factor));
            }

            _currentFactor *= factor;

            foreach (var state in _casting.Values)
            {
                state.Remaining *= factor;
                state.TickInterval *= factor;
                state.TickAccumulator *= factor;
            }

            // CR130-03 根治：既有学派锁定倒计时（同 CooldownTracker.RescaleAll 换算既有冷却存量的
            // 判断记录）此前从未随模式切换换算——切换前后同一份剩余时间被两种模式的
            // AdvanceSchoolLocks(dt) 用不同单位重新解读，"锁 3 秒"在切换后可能变成"锁 3 轮"或反过来。
            var schoolLockKeys = new List<(Id, Id)>(_schoolLocks.Keys);
            foreach (var key in schoolLockKeys)
            {
                _schoolLocks[key] *= factor;
            }
        }

        private Id NextCastInstanceId() => new Id($"skill.cast_inst_{++_castInstanceSeq}");

        /// <summary>顶层 <see cref="CastSkill"/> 直接调用的校验失败——从未排过队、从未分配过实例 id，
        /// 见 <see cref="SkillCastFailedEvent.CastInstanceId"/> 判断记录"校验阶段失败不分配"。</summary>
        private CastResult Fail(Id casterId, Id skillId, CastFailureReason reason) =>
            Fail(casterId, skillId, reason, castInstanceId: null);

        /// <summary>
        /// 消费方反馈 2026-09-10（施法生命周期事件缺少实例关联标识建议）根治：<paramref name="castInstanceId"/>
        /// 非空时（<see cref="TryStartCast"/> 续跑排队请求失败）原样戳到
        /// <see cref="SkillCastFailedEvent.CastInstanceId"/> 上——这次失败针对的是一个已经在排队时
        /// 拿到过 id、被调用方持有的请求，让消费方能用那个 id 关联到"最终失败"这个结果；为空
        /// （顶层直接调用）时事件不带实例 id，与之前完全一致。<see cref="CastResult"/> 本身的
        /// <c>CastInstanceId</c> 契约不变——失败结果恒为 <c>null</c>（见该类型既有文档），只有事件
        /// 补充了这个字段，不改变 <see cref="CastResult"/> 已发布的既有行为。
        /// </summary>
        private CastResult Fail(Id casterId, Id skillId, CastFailureReason reason, Id? castInstanceId)
        {
            _bus.Enqueue(new SkillCastFailedEvent(casterId, skillId, reason, castInstanceId));
            return CastResult.Fail(reason);
        }
    }
}
