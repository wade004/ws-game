using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Numbers.StatBlock;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// 一个已施加光环实例的运行期状态（内部可变类型，外部只经 <see cref="AuraInstanceRef"/> 与
    /// <see cref="IAuraQuery"/> 只读访问，见落地方案 T2-5 禁止事项"禁止光环状态直接被表现层写入"）。
    /// </summary>
    internal sealed class AuraInstanceState
    {
        public Id InstanceId;
        public Id DefId;
        public Id TargetId;
        public Id SourceId;
        public int Stacks;

        /// <summary>W1 收边补齐（A3 审计 #9）：施加本光环实例时的调用方标签集合（通常来自
        /// 触发 <c>apply_aura</c> 效果原语的 <c>skill.def.tags</c>，见
        /// <see cref="EffectDispatcher.ApplyAuraEffectPrimitive"/>），供 <see cref="AuraHost.FirePeriodic"/>
        /// 构造 <see cref="EffectContext"/> 时透传，让周期性光环效果的 effect_value/crit_chance
        /// 维度 SpellMod 也能按标签过滤（此前恒空列表，只有学派/技能 id 两维度生效）。未提供
        /// （如种族被动光环——见 <c>Core.Numbers.Archetype.AuraApplier</c>）时为空列表，行为与
        /// 本次改动之前一致。</summary>
        public IReadOnlyList<Id> Tags = Array.Empty<Id>();

        /// <summary>剩余持续时间；null 表示永久（见 06 第 3.3 节 <c>duration</c> 可空语义）。</summary>
        public double? Remaining;

        /// <summary>创建顺序号，供确定性遍历（见 06 第 3.8 节"周期 tick 顺序：按实例创建顺序"）。</summary>
        public int SeqNo;

        /// <summary>周期效果累加器，键为该效果在 <c>AuraDef.Effects</c> 中的下标。</summary>
        public Dictionary<int, double> PeriodicAccumulators = new Dictionary<int, double>();

        public double AbsorbRemaining;
        public Id? AbsorbSchool;

        public ControlFlags ControlFlags;
        public readonly HashSet<Id> ImmuneSchools = new HashSet<Id>();
        public readonly HashSet<EffectKind> ImmuneEffectKinds = new HashSet<EffectKind>();

        public Id? ProcDefRef;
        public readonly List<Id> SpellModRefs = new List<Id>();
        public (Id From, Id To)? OverrideSkill;
    }

    /// <summary>
    /// 光环系统：施加/移除/叠加/刷新/周期结算/驱散/免疫吸收查询（见 06 第 3.3/3.8 节、落地方案
    /// T2-5 行）。实现 <see cref="IAuraQuery"/>（供 combat/ai 只读查询）；<see cref="ApplyAura"/>/
    /// <see cref="RemoveAura"/> 由 <see cref="EffectDispatcher"/>（实现 <see cref="IEffectSink"/>）
    /// 转调，本类不直接实现 <see cref="IEffectSink"/>（避免同一个类同时暴露"结算入口"与"光环状态
    /// 管理"两套职责，见 <see cref="SkillHost"/> 组合方式）。
    /// </summary>
    public sealed class AuraHost : IAuraQuery
    {
        private readonly SkillDefCache _defs;
        private readonly IStatHost _statHost;
        private readonly IEventBus _bus;
        private readonly SkillOptions _options;
        private readonly ISkillDiagnostics _diagnostics;

        /// <summary>阶段 3 整理"事项三"：生物模板/tier 一类内容驱动的静态免疫，在光环免疫/控制之外
        /// 叠加查询（见 <see cref="IsImmune"/>、<see cref="ApplyStaticEffects"/> 对 <c>control</c>
        /// 类效果的处理）。可选构造参数，缺省 <see cref="NullStaticImmunityProvider.Instance"/>
        /// （一律不免疫，不改变既有行为）。</summary>
        private readonly IStaticImmunityProvider _staticImmunity;

        private readonly Dictionary<Id, AuraInstanceState> _instances = new Dictionary<Id, AuraInstanceState>();

        /// <summary>运行时叠加槽位表，键为 <c>(target, defId, sourceKey)</c>——<c>skill.aura_def.
        /// stack_category</c> 不参与本键（架构 ADR-0023"光环叠加类别为静态校验分组"：<c>stack_category</c>
        /// 只供 <c>StackCategoryConflictRule</c> 在加载期做静态内容校验，不是运行时槽位维度）。
        /// <see cref="SkillOptions.StackOverflowPolicy"/> 在 <see cref="ReapplyExisting"/> 中只处理
        /// 同一个槽位（同一 <c>defId</c>、同一 <c>sourceKey</c>）的重复应用；不同 <c>defId</c> 即使
        /// <c>stack_category</c> 相同，也是本字典里两个独立的键，互不叠加、互不触发对方的溢出策略。
        /// <c>sourceKey</c> 取值见 <see cref="ApplyAura"/>：<see cref="SkillOptions.AllowMultiSourceTiming"/>
        /// 为 <c>false</c>（默认）时恒为 <c>null</c>，为 <c>true</c> 时取实际来源 id。</summary>
        private readonly Dictionary<(Id Target, Id DefId, Id? SourceKey), Id> _slots =
            new Dictionary<(Id, Id, Id?), Id>();

        private int _seq;

        /// <summary>判断记录（"相邻缺口"根治，第五轮外部审核 audit-5e779c6-20260907 WA 报告"需要
        /// 说明的取舍"第 1/2 条；与 <see cref="CooldownTracker._currentFactor"/> 同批语义、同一套
        /// 系数含义，见该字段判断记录）：当前模式 1 个计时单位相当于连续模式（<c>aura_def</c> 数据
        /// authoring 的规范单位）多少秒，初始 1.0（游戏总是从连续模式起步），随
        /// <see cref="RescaleAll"/> 每次模式切换累乘更新。<see cref="ApplyAura"/> 施放当下把原始
        /// <c>duration</c> 折算成当前模式单位（R05 只解决了"切换时刻既有实例的剩余时长换算"，未处理
        /// "施放当下新建实例该按哪个单位解释原始数据"这一层，见任务判断记录）；<see cref="Update"/>
        /// 同样用它把 <c>periodic_damage</c>/<c>periodic_heal</c> 的原始 <c>interval</c> 折算成当前
        /// 模式单位（此前每次 <see cref="Update"/> 直接读原始值，不随模式换算，是 R05 判断记录明确
        /// 记录未处理的"另一个更深的既有缺口"）。</summary>
        private double _currentFactor = 1.0;

        /// <summary>供 <see cref="ProcHost.Attach"/>/<see cref="ProcHost.Detach"/> 挂载/摘除
        /// <c>proc_trigger</c> 光环效果绑定的触发器；由 <see cref="SkillHost"/> 在两者都构造完成后
        /// 设置（打破构造期循环依赖，见模块 README"组合根"一节）。</summary>
        public ProcHost? ProcHost { get; set; }

        /// <summary>供周期性效果（<c>periodic_damage</c>/<c>periodic_heal</c>）结算，由
        /// <see cref="SkillHost"/> 在 <see cref="EffectDispatcher"/> 构造完成后设置。</summary>
        public IEffectSink? EffectSink { get; set; }

        /// <summary>C08 收口：见 <see cref="IAuraQuery.InstanceReplaced"/> 判断记录。</summary>
        public event Action<Id, Id, Id, Id>? InstanceReplaced;

        public AuraHost(
            SkillDefCache defs,
            IStatHost statHost,
            IEventBus bus,
            SkillOptions options,
            ISkillDiagnostics diagnostics,
            IStaticImmunityProvider? staticImmunity = null)
        {
            _defs = defs ?? throw new ArgumentNullException(nameof(defs));
            _statHost = statHost ?? throw new ArgumentNullException(nameof(statHost));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _staticImmunity = staticImmunity ?? NullStaticImmunityProvider.Instance;

            // 收边任务补齐（自愈，见 OnEntityDestroyed 判断记录）：本类型此前不订阅任何事件，
            // 光环实例的移除完全依赖 ApplyAura/RemoveAura/Dispel/Update 到期四条主动路径——目标单位
            // 从 IWorldSim 移除（entity.destroyed，如 World.ClearAll、正常实体销毁）不会让这里的
            // 光环实例表跟着清空。周期性效果（periodic_damage/periodic_heal）仍会在下一次 Update
            // 命中该实例并对着已经消失的目标结算，命中 WorldUnitAccess.Require 抛异常——该缺口此前
            // 已被 adapters/unity 侧的测试判断记录明确记录并靠"停止 tick"规避（见
            // VerticalSliceTests.TearDown 判断记录"AuraHost 自愈...不在本任务允许改动的 core/data
            // 范围内"），本任务写入范围包含 core/，在根源补齐。
            _bus.Subscribe<EntityDestroyedEvent>(SimEventKeys.EntityDestroyed, OnEntityDestroyed);
        }

        /// <summary>目标单位被销毁时立即移除其名下全部光环实例（不等到该光环自然到期/下一次
        /// <see cref="Update"/> 才发现目标已经不存在），防止 <see cref="FirePeriodic"/> 对着已经从
        /// <c>IWorldSim</c> 移除的目标结算。只处理"作为目标（<see cref="AuraInstanceState.TargetId"/>）"
        /// 的光环——来源单位（<see cref="AuraInstanceState.SourceId"/>）被销毁不代表已施加到其他目标
        /// 身上的光环应当消失，06 未规定这种情形，稳妥起见只收窄到"目标已消失"这一确定安全的情形。</summary>
        private void OnEntityDestroyed(EntityDestroyedEvent evt)
        {
            var targetId = evt.EntityId;
            List<AuraInstanceState>? toRemove = null;
            foreach (var instance in _instances.Values)
            {
                if (instance.TargetId.Equals(targetId))
                {
                    (toRemove ??= new List<AuraInstanceState>()).Add(instance);
                }
            }

            if (toRemove == null)
            {
                return;
            }

            foreach (var instance in toRemove)
            {
                RemoveInstanceInternal(instance, "target_destroyed", targetMayBeUnregistered: true);
            }
        }

        // -----------------------------------------------------------------
        // 施加 / 移除 / 驱散
        // -----------------------------------------------------------------

        /// <summary><paramref name="triggerChainDepth"/>：见 <see cref="ITriggerChainEvent"/>/
        /// <see cref="EffectContext.TriggerChainDepth"/> 判断记录（RC-01）——只有
        /// <see cref="EffectDispatcher.ApplyAuraEffectPrimitive"/>（<c>apply_aura</c> 效果原语）
        /// 会传入非零值；<see cref="IEffectSink.ApplyAura"/>（装备 grants、种族被动等不经过
        /// <see cref="EffectContext"/> 的直接施加）恒为默认值 0。</summary>
        public AuraInstanceRef ApplyAura(Id targetId, Id defId, Id sourceId, double? durationOverride, IReadOnlyList<Id>? tags = null, int triggerChainDepth = 0)
        {
            var def = _defs.GetAuraDef(defId);
            var sourceKey = _options.AllowMultiSourceTiming ? (Id?)sourceId : null;
            var slotKey = (targetId, defId, sourceKey);
            var safeTags = tags ?? Array.Empty<Id>();

            if (_slots.TryGetValue(slotKey, out var existingId) && _instances.TryGetValue(existingId, out var existing))
            {
                return ReapplyExisting(existing, def, sourceId, durationOverride, safeTags, triggerChainDepth);
            }

            return CreateInstance(targetId, def, sourceId, durationOverride, safeTags, triggerChainDepth);
        }

        /// <summary>判断记录"相邻缺口根治"：把 <c>durationOverride ?? def.Duration</c>（原始/规范
        /// 单位的持续时间，<c>null</c> 表示永久光环）折算成当前模式的计时单位，供
        /// <see cref="CreateInstance"/>/<see cref="ReapplyExisting"/> 三处赋值 <c>Remaining</c> 的地方
        /// 共用同一份逻辑（同 <see cref="CooldownTracker.StartCooldown"/> 判断记录，_currentFactor
        /// 恒为正数，null 不受影响）。</summary>
        private double? ScaleDuration(double? raw) => raw.HasValue ? raw.Value * _currentFactor : (double?)null;

        private AuraInstanceRef ReapplyExisting(AuraInstanceState existing, AuraDef def, Id sourceId, double? durationOverride, IReadOnlyList<Id> tags, int triggerChainDepth = 0)
        {
            var newStacks = existing.Stacks + 1;
            if (newStacks <= def.MaxStacks)
            {
                existing.Remaining = ScaleDuration(durationOverride ?? def.Duration);
                existing.SourceId = sourceId;
                existing.Tags = tags;
                var old = existing.Stacks;
                existing.Stacks = newStacks;
                ReapplyStatMods(existing, def);
                existing.AbsorbRemaining += AbsorbPerStack(def, out _);
                _bus.Enqueue(new AuraStackChangedEvent(existing.TargetId, def.Id, old, existing.Stacks, triggerChainDepth));
                return new AuraInstanceRef(existing.InstanceId);
            }

            switch (_options.StackOverflowPolicy)
            {
                case StackOverflowPolicy.Ignore:
                    return new AuraInstanceRef(existing.InstanceId);

                case StackOverflowPolicy.RefreshOnly:
                    existing.Remaining = ScaleDuration(durationOverride ?? def.Duration);
                    return new AuraInstanceRef(existing.InstanceId);

                case StackOverflowPolicy.Replace:
                {
                    // C08 收口：先记下即将失效的旧句柄/目标/定义 id（RemoveInstanceInternal 只是把
                    // existing 从内部字典摘除，不会清空这个对象自身的字段，但这里显式先取值更清楚，
                    // 也不依赖"摘除后对象字段仍可读"这一实现细节）。
                    var oldInstanceId = existing.InstanceId;
                    var targetId = existing.TargetId;
                    var defId = def.Id;

                    RemoveInstanceInternal(existing, "overwritten", triggerChainDepth: triggerChainDepth);
                    var replacement = CreateInstance(targetId, def, sourceId, durationOverride, tags, triggerChainDepth);

                    // 新旧句柄都已经确定（旧实例摘除、新实例创建，两步都已完成）——同步通知订阅者
                    // （如 EquipmentHost）原子迁移自己记录里对旧句柄的引用，见 IAuraQuery.
                    // InstanceReplaced 判断记录。必须晚于 CreateInstance（订阅者需要拿到真正的新
                    // 句柄），且仍在本次 ApplyAura 调用返回之前触发（同步事件，不经 IEventBus）。
                    InstanceReplaced?.Invoke(targetId, defId, oldInstanceId, replacement.AuraInstanceId);

                    return replacement;
                }

                default:
                    throw new InvalidOperationException($"未知的 StackOverflowPolicy：{_options.StackOverflowPolicy}");
            }
        }

        private AuraInstanceRef CreateInstance(Id targetId, AuraDef def, Id sourceId, double? durationOverride, IReadOnlyList<Id>? tags = null, int triggerChainDepth = 0)
        {
            var instance = new AuraInstanceState
            {
                InstanceId = new Id($"skill.aura_inst_{++_seq}"),
                DefId = def.Id,
                TargetId = targetId,
                SourceId = sourceId,
                Stacks = 1,
                Remaining = ScaleDuration(durationOverride ?? def.Duration),
                SeqNo = _seq,
                Tags = tags ?? Array.Empty<Id>(),
            };

            ApplyStaticEffects(instance, def);
            ReapplyStatMods(instance, def);

            _instances[instance.InstanceId] = instance;
            var sourceKey = _options.AllowMultiSourceTiming ? (Id?)sourceId : null;
            _slots[(targetId, def.Id, sourceKey)] = instance.InstanceId;

            if (instance.ProcDefRef.HasValue && ProcHost != null)
            {
                var procDef = _defs.GetProcDef(instance.ProcDefRef.Value);
                ProcHost.Attach(instance.InstanceId, targetId, procDef);
            }

            _bus.Enqueue(new AuraAppliedEvent(targetId, def.Id, sourceId, instance.Stacks, triggerChainDepth));
            return new AuraInstanceRef(instance.InstanceId);
        }

        /// <summary><paramref name="triggerChainDepth"/>：见 <see cref="AuraRemovedEvent"/> 类型
        /// 注释"N04 收边补齐"。经 <see cref="IEffectSink.RemoveAura"/>（接口签名不带深度参数，见
        /// 该接口判断记录，本参数不扩大公开契约）调用时恒为默认值 0；
        /// <see cref="EffectDispatcher"/> 直接持有具体类型 <see cref="AuraHost"/>，可在 dispel 一类
        /// 内部调用点显式传入。</summary>
        public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef, int triggerChainDepth = 0)
        {
            if (_instances.TryGetValue(auraInstanceRef.AuraInstanceId, out var instance) && instance.TargetId.Equals(targetId))
            {
                RemoveInstanceInternal(instance, "removed", triggerChainDepth: triggerChainDepth);
            }
        }

        /// <summary><paramref name="targetMayBeUnregistered"/>（默认 false，惟一 true 调用方是
        /// <see cref="OnEntityDestroyed"/>）：正常移除路径（<see cref="ApplyAura"/> 叠加溢出、
        /// <see cref="RemoveAura"/>、<see cref="Dispel"/>、<see cref="Update"/> 到期）全部假定目标
        /// 仍在 <see cref="IStatHost"/> 注册——这是既有不变量，本参数不改变这些路径的行为。
        /// <c>entity.destroyed</c> 触发的移除不能沿用这条假设：<c>IStatHost</c> 的单位注册状态与
        /// <c>IWorldSim</c> 实体生命周期相互独立（没有任何耦合机制保证同步），<c>true</c> 时先查
        /// <see cref="IStatHost.IsRegistered"/> 再决定是否调用
        /// <see cref="IStatHost.RemoveModifiersBySource"/>，避免把"目标已从世界移除"这一件事变成
        /// 一次新的未注册单位异常。<paramref name="triggerChainDepth"/> 见 <see cref="AuraRemovedEvent"/>
        /// 类型注释"N04 收边补齐"：未显式传入（<see cref="Update"/> 到期、<see cref="OnEntityDestroyed"/>、
        /// <see cref="ConsumeAbsorb"/>）时为 0，视为根事件。</summary>
        private void RemoveInstanceInternal(AuraInstanceState instance, string reason, bool targetMayBeUnregistered = false, int triggerChainDepth = 0)
        {
            if (!targetMayBeUnregistered || _statHost.IsRegistered(instance.TargetId))
            {
                _statHost.RemoveModifiersBySource(instance.TargetId, instance.InstanceId);
            }

            _instances.Remove(instance.InstanceId);
            var sourceKey = _options.AllowMultiSourceTiming ? (Id?)instance.SourceId : null;
            _slots.Remove((instance.TargetId, instance.DefId, sourceKey));

            if (instance.ProcDefRef.HasValue)
            {
                ProcHost?.Detach(instance.InstanceId);
            }

            _bus.Enqueue(new AuraRemovedEvent(instance.TargetId, instance.DefId, reason, triggerChainDepth));
        }

        /// <summary><c>dispel</c> 效果原语落地（见 06 第 3.2 节"按类别、数量"）：按创建顺序移除
        /// 最多 <paramref name="count"/> 条 <c>dispel_type == dispelType</c> 的实例。
        /// <paramref name="triggerChainDepth"/> 见 <see cref="AuraRemovedEvent"/> 类型注释
        /// "N04 收边补齐"——<see cref="EffectDispatcher.ApplyDispel"/> 传入
        /// <see cref="EffectContext.TriggerChainDepth"/>，使 Proc 循环经由 <c>aura.removed</c>
        /// 触发时也受 <see cref="SkillOptions.MaxTriggerDepth"/> 约束。</summary>
        public int Dispel(Id targetId, Id dispelType, int count, int triggerChainDepth = 0)
        {
            var candidates = _instances.Values
                .Where(i => i.TargetId.Equals(targetId))
                .Where(i => _defs.GetAuraDef(i.DefId).DispelType.HasValue && _defs.GetAuraDef(i.DefId).DispelType!.Value.Equals(dispelType))
                .OrderBy(i => i.SeqNo)
                .Take(Math.Max(0, count))
                .ToList();

            foreach (var instance in candidates)
            {
                RemoveInstanceInternal(instance, "dispelled", triggerChainDepth: triggerChainDepth);
            }

            return candidates.Count;
        }

        // -----------------------------------------------------------------
        // Tick
        // -----------------------------------------------------------------

        /// <summary>按 <paramref name="dt"/> 推进全部实例的持续时间与周期效果（见 06 第 3.3 节
        /// <c>periodic_damage</c>/<c>periodic_heal</c>、第 3.8 节"周期 tick 顺序：按实例创建顺序"）。
        /// <para>
        /// R07 收边补齐（外部审计 5e779c6，P2）：一次 <paramref name="dt"/> 大于某实例剩余持续时间时
        /// （典型场景：主循环追帧/大步长一次 tick 跨越了光环的到期点），周期效果的累加器只用"到期前
        /// 那一段"（<c>Math.Min(dt, Remaining)</c>）推进，不是整个 <paramref name="dt"/>——修复前把
        /// 全部 <paramref name="dt"/>（含到期之后、光环本不该再存在的那一段"时间余量"）都计入周期
        /// 累加器，会多结算出本不该发生的周期次数（外部审计复现：duration=1、interval=0.3 的光环，
        /// 一次 dt=5 的大步推进错误地按 <c>floor(5/0.3)=16</c> 次结算，而不是到期前应有的
        /// <c>floor(1/0.3)=3</c> 次）。永久光环（<see cref="AuraInstanceState.Remaining"/> 为
        /// <c>null</c>）没有到期点，仍用完整 <paramref name="dt"/>，行为不变。
        /// </para>
        /// </summary>
        public void Update(double dt)
        {
            var ordered = _instances.Values.OrderBy(i => i.SeqNo).ToList();

            foreach (var instance in ordered)
            {
                // R07：到期前的有效时长——非永久光环夹到 [0, Remaining]，不让本次 tick 里"到期之后"
                // 的那一段时间余量参与周期效果结算（见本方法判断记录）。
                var periodicDt = instance.Remaining.HasValue ? Math.Max(0, Math.Min(dt, instance.Remaining.Value)) : dt;

                var def = _defs.GetAuraDef(instance.DefId);
                for (var i = 0; i < def.Effects.Count; i++)
                {
                    var entry = def.Effects[i];
                    if (entry.Kind != AuraEffectKind.PeriodicDamage && entry.Kind != AuraEffectKind.PeriodicHeal)
                    {
                        continue;
                    }

                    // 判断记录"相邻缺口根治"：原始 interval 同样是规范单位的 authoring 数值，乘
                    // _currentFactor 折算成当前模式的计时单位——_currentFactor 每次 Update 都取最新
                    // 值，模式切换后立即用新系数解释同一份原始数据，不需要额外缓存。
                    var interval = ParamsX.GetNumber(entry.Params, "interval") * _currentFactor;
                    if (interval <= 0)
                    {
                        // ADR-0021 落地（消费方反馈 2026-09-10：技能效果参数数值范围校验改进建议）：
                        // interval 现已在 skill.def 登记表登记 field_range（> 0），正常内容不可能
                        // 加载出这个分支——本诊断只覆盖登记表校验之外的路径（如直接构造 AuraDef
                        // 走内存数据源、绕过 DataRegistry.LoadAll 的单元测试/工具场景）。此前这里是
                        // 纯静默 continue，把内容错误（interval<=0 本应在加载期被拦下）悄悄降级成
                        // "光环没有周期效果"的运行期沉默，问题报告方从探针复跑里完全看不到任何提示。
                        // 保留防御性跳过（不让一个非法 interval 崩溃整条 Update 循环），但先记一条
                        // 诊断，行为语义不变（该周期效果依旧不生效）。
                        _diagnostics.Warn(
                            $"光环实例 \"{instance.InstanceId}\"（def \"{instance.DefId}\"）的周期效果 interval={interval.ToString(System.Globalization.CultureInfo.InvariantCulture)} <= 0，已跳过本次周期结算");
                        continue;
                    }

                    instance.PeriodicAccumulators.TryGetValue(i, out var acc);
                    acc += periodicDt;
                    while (acc >= interval)
                    {
                        acc -= interval;
                        FirePeriodic(instance, entry);
                    }

                    instance.PeriodicAccumulators[i] = acc;
                }

                if (instance.Remaining.HasValue)
                {
                    instance.Remaining -= dt;
                }
            }

            foreach (var instance in ordered)
            {
                if (instance.Remaining.HasValue && instance.Remaining.Value <= 0 && _instances.ContainsKey(instance.InstanceId))
                {
                    RemoveInstanceInternal(instance, "expired");
                }
            }
        }

        private void FirePeriodic(AuraInstanceState instance, AuraEffectEntry entry)
        {
            if (EffectSink == null)
            {
                _diagnostics.Warn($"光环实例 \"{instance.InstanceId}\" 触发周期效果时 EffectSink 尚未就绪，已跳过");
                return;
            }

            var kind = entry.Kind == AuraEffectKind.PeriodicDamage ? EffectKind.SchoolDamage : EffectKind.Heal;
            var school = ParamsX.GetId(entry.Params, "school", default);
            var baseValue = ParamsX.GetNumber(entry.Params, "base_value");
            var coefficient = ParamsX.GetNumber(entry.Params, "coefficient");

            var context = new EffectContext(
                sourceId: instance.SourceId,
                targetId: instance.TargetId,
                skillId: instance.DefId,
                kind: kind,
                school: school,
                baseValue: baseValue,
                coefficient: coefficient,
                @params: entry.Params,
                auraInstanceId: instance.InstanceId,
                isPeriodic: true,
                canCrit: true,
                canMiss: false,
                tags: instance.Tags);

            EffectSink.ApplyEffect(context);
        }

        // -----------------------------------------------------------------
        // 静态效果解析（施加/替换时执行一次）
        // -----------------------------------------------------------------

        private void ApplyStaticEffects(AuraInstanceState instance, AuraDef def)
        {
            double absorbTotal = 0;
            Id? absorbSchool = null;

            foreach (var entry in def.Effects)
            {
                switch (entry.Kind)
                {
                    case AuraEffectKind.Absorb:
                        absorbTotal += ParamsX.GetNumber(entry.Params, "amount");
                        absorbSchool = ParamsX.GetIdOpt(entry.Params, "school") ?? absorbSchool;
                        break;

                    case AuraEffectKind.Immunity:
                        foreach (var s in ParamsX.GetIdArray(entry.Params, "schools")) instance.ImmuneSchools.Add(s);
                        foreach (var k in ParamsX.GetStringArray(entry.Params, "effect_kinds"))
                        {
                            if (EffectKindNames.TryParse(k, out var kind)) instance.ImmuneEffectKinds.Add(kind);
                        }
                        break;

                    case AuraEffectKind.ProcTrigger:
                        instance.ProcDefRef = ParamsX.GetIdOpt(entry.Params, "proc_ref");
                        break;

                    case AuraEffectKind.SpellMod:
                        var modRef = ParamsX.GetIdOpt(entry.Params, "spell_mod_ref");
                        if (modRef.HasValue) instance.SpellModRefs.Add(modRef.Value);
                        break;

                    case AuraEffectKind.OverrideSkill:
                        var from = ParamsX.GetIdOpt(entry.Params, "from");
                        var to = ParamsX.GetIdOpt(entry.Params, "to");
                        if (from.HasValue && to.HasValue) instance.OverrideSkill = (from.Value, to.Value);
                        break;

                    case AuraEffectKind.Control:
                        // 阶段 3 整理"事项三"：静态控制免疫的标志位从本次施加的控制标志里剔除
                        // （见 IStaticImmunityProvider.GetControlImmunity 顶部判断记录"控制类光环
                        // 对该单位一律不生效"）——control_immune 生物身上不会真的置位这些标志，
                        // GetControlFlags 查询结果与"完全没吃到这个光环的控制效果"等价。
                        var flags = ParseControlFlags(ParamsX.GetStringArray(entry.Params, "flags"));
                        flags &= ~_staticImmunity.GetControlImmunity(instance.TargetId);
                        instance.ControlFlags |= flags;
                        break;
                }
            }

            instance.AbsorbRemaining = absorbTotal * instance.Stacks;
            instance.AbsorbSchool = absorbSchool;
        }

        private static double AbsorbPerStack(AuraDef def, out Id? school)
        {
            double total = 0;
            Id? s = null;
            foreach (var entry in def.Effects)
            {
                if (entry.Kind != AuraEffectKind.Absorb) continue;
                total += ParamsX.GetNumber(entry.Params, "amount");
                s = ParamsX.GetIdOpt(entry.Params, "school") ?? s;
            }

            school = s;
            return total;
        }

        private static ControlFlags ParseControlFlags(IReadOnlyList<string> flags)
        {
            var result = ControlFlags.None;
            foreach (var f in flags)
            {
                result |= f switch
                {
                    "no_move" => ControlFlags.NoMove,
                    "no_cast" => ControlFlags.NoCast,
                    "no_attack" => ControlFlags.NoAttack,
                    "no_interact" => ControlFlags.NoInteract,
                    _ => ControlFlags.None,
                };
            }

            return result;
        }

        private void ReapplyStatMods(AuraInstanceState instance, AuraDef def)
        {
            _statHost.RemoveModifiersBySource(instance.TargetId, instance.InstanceId);
            foreach (var entry in def.Effects)
            {
                if (entry.Kind != AuraEffectKind.ModStat) continue;

                var stat = ParamsX.GetId(entry.Params, "stat", default);
                var opText = ParamsX.GetString(entry.Params, "op", "flat");
                var op = opText switch
                {
                    "pct" => StatModifierOp.Pct,
                    "mult" => StatModifierOp.Mult,
                    _ => StatModifierOp.Flat,
                };
                var value = ParamsX.GetNumber(entry.Params, "value") * instance.Stacks;
                _statHost.AddModifier(instance.TargetId, new StatModifier(stat, op, value, instance.InstanceId));
            }
        }

        // -----------------------------------------------------------------
        // IAuraQuery
        // -----------------------------------------------------------------

        public bool HasAura(Id unitId, Id auraDefId) =>
            _instances.Values.Any(i => i.TargetId.Equals(unitId) && i.DefId.Equals(auraDefId));

        public int GetStacks(Id unitId, Id auraDefId)
        {
            var found = _instances.Values.FirstOrDefault(i => i.TargetId.Equals(unitId) && i.DefId.Equals(auraDefId));
            return found?.Stacks ?? 0;
        }

        /// <summary>CORE-170-01 根治：见 <see cref="IAuraQuery.TryGetInstanceRef"/> 判断记录。</summary>
        public AuraInstanceRef? TryGetInstanceRef(Id unitId, Id auraDefId)
        {
            var found = _instances.Values.FirstOrDefault(i => i.TargetId.Equals(unitId) && i.DefId.Equals(auraDefId));
            return found != null ? new AuraInstanceRef(found.InstanceId) : (AuraInstanceRef?)null;
        }

        public ControlFlags GetControlFlags(Id unitId)
        {
            var result = ControlFlags.None;
            foreach (var i in _instances.Values)
            {
                if (i.TargetId.Equals(unitId)) result |= i.ControlFlags;
            }

            return result;
        }

        public bool IsImmune(Id unitId, Id school, EffectKind kind)
        {
            if (_staticImmunity.IsImmune(unitId, school, kind))
            {
                return true;
            }

            foreach (var i in _instances.Values)
            {
                if (!i.TargetId.Equals(unitId)) continue;
                var schoolMatches = i.ImmuneSchools.Count == 0 || i.ImmuneSchools.Contains(school);
                var kindMatches = i.ImmuneEffectKinds.Count == 0 || i.ImmuneEffectKinds.Contains(kind);
                if ((i.ImmuneSchools.Count > 0 || i.ImmuneEffectKinds.Count > 0) && schoolMatches && kindMatches)
                {
                    return true;
                }
            }

            return false;
        }

        public double ConsumeAbsorb(Id unitId, Id school, double amount) =>
            ConsumeAbsorb(unitId, school, amount, triggerChainDepth: 0);

        /// <summary>C03 收口（外部审计 7e63d66 第四轮）：见 <see cref="IAuraQuery.ConsumeAbsorb(Id, Id, double, int)"/>
        /// 判断记录。吸收耗尽移除实例时把 <paramref name="triggerChainDepth"/> 传给
        /// <see cref="RemoveInstanceInternal"/>，使 <c>aura.removed</c> 事件携带产生这次结算的真实
        /// 触发链深度，纳入 <see cref="SkillOptions.MaxTriggerDepth"/> 收敛预算——与 <see cref="Dispel"/>
        /// 已经做到的深度传播保持一致，不再恒为 0（根事件）。</summary>
        public double ConsumeAbsorb(Id unitId, Id school, double amount, int triggerChainDepth)
        {
            double consumed = 0;
            var candidates = _instances.Values
                .Where(i => i.TargetId.Equals(unitId) && i.AbsorbRemaining > 0)
                .Where(i => !i.AbsorbSchool.HasValue || i.AbsorbSchool.Value.Equals(school))
                .OrderBy(i => i.SeqNo)
                .ToList();

            foreach (var instance in candidates)
            {
                if (consumed >= amount) break;

                var take = Math.Min(instance.AbsorbRemaining, amount - consumed);
                instance.AbsorbRemaining -= take;
                consumed += take;

                if (instance.AbsorbRemaining <= 0)
                {
                    RemoveInstanceInternal(instance, "absorb_depleted", triggerChainDepth: triggerChainDepth);
                }
            }

            return consumed;
        }

        /// <summary>
        /// R05 收边补齐（外部审计 5e779c6，P2；见 <see cref="Core.Rules.Common.TimeModelRescaledEvent"/>
        /// 类型判断记录）：连续/离散模式切换时把全部光环实例的剩余持续时间按同一系数换算——
        /// <see cref="AuraInstanceState.Remaining"/> 与 <see cref="CooldownTracker"/> 的倒计时同样
        /// "以数据集声明的时间单位计"，连续模式是秒、离散模式是轮（见 <see cref="Update"/> 的调用
        /// 时机判断记录），不换算会在切换后被新模式的 tick 单位重新解读，导致光环剩余时长突然
        /// 变短或变长。永久光环（<see cref="AuraInstanceState.Remaining"/> 为 <c>null</c>）不受影响
        /// （没有"剩余时长"可换算）。<paramref name="factor"/> 语义同
        /// <see cref="Core.Foundation.SimLoop.SimTimers.RescaleAll"/>。
        /// <para>
        /// 判断记录（"相邻缺口"根治后改为同时换算 <see cref="AuraInstanceState.PeriodicAccumulators"/>，
        /// 推翻本判断记录的历史结论——第五轮外部审核 audit-5e779c6-20260907 WA 报告"需要说明的取舍"
        /// 第 2 条）：原判断记录"不换算累加器，因为 interval 本身也不换算"的前提已经不成立——
        /// <see cref="Update"/> 现在每次都用 <see cref="_currentFactor"/> 把原始 <c>interval</c>
        /// 折算成当前模式单位（见该方法判断记录），若累加器仍留在切换前的模式单位不换算，"累加器 /
        /// interval"这个决定"还差多久触发下一跳"的比例关系会在切换瞬间被打破（新 interval 已经是新
        /// 单位，旧 acc 还是旧单位，两者不可比）。累加器现与 <see cref="AuraInstanceState.Remaining"/>
        /// 同样乘 <paramref name="factor"/>，保持该比例关系在切换前后连续。
        /// </para>
        /// </summary>
        public void RescaleAll(double factor)
        {
            if (factor <= 0)
            {
                throw new ArgumentException("factor 必须为正数", nameof(factor));
            }

            // 判断记录同 CooldownTracker.RescaleAll：累乘更新，供本次切换之后 ApplyAura 施加的新
            // 实例、Update 读取的 interval 按当前模式正确折算原始 authoring 数值。
            _currentFactor *= factor;

            foreach (var instance in _instances.Values)
            {
                if (instance.Remaining.HasValue)
                {
                    instance.Remaining = instance.Remaining.Value * factor;
                }

                if (instance.PeriodicAccumulators.Count > 0)
                {
                    var keys = new List<int>(instance.PeriodicAccumulators.Keys);
                    foreach (var key in keys)
                    {
                        instance.PeriodicAccumulators[key] = instance.PeriodicAccumulators[key] * factor;
                    }
                }
            }
        }

        public IReadOnlyList<Id> GetActiveAuraDefs(Id unitId) =>
            _instances.Values.Where(i => i.TargetId.Equals(unitId)).OrderBy(i => i.SeqNo).Select(i => i.DefId).ToList();

        /// <summary>该单位当前生效的全部 <c>spell_mod</c> 引用（供 <see cref="SpellModResolver"/>
        /// 收集，见 06 第 3.5 节"通过 apply_aura 附带 spell_mod 类型的 AuraEffect 生效"）。</summary>
        public IReadOnlyList<Id> GetActiveSpellModRefs(Id unitId) =>
            _instances.Values.Where(i => i.TargetId.Equals(unitId)).OrderBy(i => i.SeqNo)
                .SelectMany(i => i.SpellModRefs).ToList();

        /// <summary>该单位身上是否存在把 <paramref name="skillId"/> 重定向到另一个技能的
        /// <c>override_skill</c> 光环效果（见 06 第 3.3 节）；多条命中时取最近施加的一条。
        /// 集成任务改名（原名 <c>ResolveOverride</c>）以匹配提升到共享契约
        /// <see cref="IAuraQuery.ResolveSkillOverride"/> 后的方法名。</summary>
        public Id? ResolveSkillOverride(Id unitId, Id skillId)
        {
            return _instances.Values
                .Where(i => i.TargetId.Equals(unitId) && i.OverrideSkill.HasValue && i.OverrideSkill.Value.From.Equals(skillId))
                .OrderByDescending(i => i.SeqNo)
                .Select(i => (Id?)i.OverrideSkill!.Value.To)
                .FirstOrDefault();
        }
    }
}
