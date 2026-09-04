using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Numbers.StatBlock;
using Core.Foundation.EventBus;
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

        private readonly Dictionary<Id, AuraInstanceState> _instances = new Dictionary<Id, AuraInstanceState>();
        private readonly Dictionary<(Id Target, Id DefId, Id? SourceKey), Id> _slots =
            new Dictionary<(Id, Id, Id?), Id>();

        private int _seq;

        /// <summary>供 <see cref="ProcHost.Attach"/>/<see cref="ProcHost.Detach"/> 挂载/摘除
        /// <c>proc_trigger</c> 光环效果绑定的触发器；由 <see cref="SkillHost"/> 在两者都构造完成后
        /// 设置（打破构造期循环依赖，见模块 README"组合根"一节）。</summary>
        public ProcHost? ProcHost { get; set; }

        /// <summary>供周期性效果（<c>periodic_damage</c>/<c>periodic_heal</c>）结算，由
        /// <see cref="SkillHost"/> 在 <see cref="EffectDispatcher"/> 构造完成后设置。</summary>
        public IEffectSink? EffectSink { get; set; }

        public AuraHost(SkillDefCache defs, IStatHost statHost, IEventBus bus, SkillOptions options, ISkillDiagnostics diagnostics)
        {
            _defs = defs ?? throw new ArgumentNullException(nameof(defs));
            _statHost = statHost ?? throw new ArgumentNullException(nameof(statHost));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        // -----------------------------------------------------------------
        // 施加 / 移除 / 驱散
        // -----------------------------------------------------------------

        public AuraInstanceRef ApplyAura(Id targetId, Id defId, Id sourceId, double? durationOverride)
        {
            var def = _defs.GetAuraDef(defId);
            var sourceKey = _options.AllowMultiSourceTiming ? (Id?)sourceId : null;
            var slotKey = (targetId, defId, sourceKey);

            if (_slots.TryGetValue(slotKey, out var existingId) && _instances.TryGetValue(existingId, out var existing))
            {
                return ReapplyExisting(existing, def, sourceId, durationOverride);
            }

            return CreateInstance(targetId, def, sourceId, durationOverride);
        }

        private AuraInstanceRef ReapplyExisting(AuraInstanceState existing, AuraDef def, Id sourceId, double? durationOverride)
        {
            var newStacks = existing.Stacks + 1;
            if (newStacks <= def.MaxStacks)
            {
                existing.Remaining = durationOverride ?? def.Duration;
                existing.SourceId = sourceId;
                var old = existing.Stacks;
                existing.Stacks = newStacks;
                ReapplyStatMods(existing, def);
                existing.AbsorbRemaining += AbsorbPerStack(def, out _);
                _bus.Enqueue(new AuraStackChangedEvent(existing.TargetId, def.Id, old, existing.Stacks));
                return new AuraInstanceRef(existing.InstanceId);
            }

            switch (_options.StackOverflowPolicy)
            {
                case StackOverflowPolicy.Ignore:
                    return new AuraInstanceRef(existing.InstanceId);

                case StackOverflowPolicy.RefreshOnly:
                    existing.Remaining = durationOverride ?? def.Duration;
                    return new AuraInstanceRef(existing.InstanceId);

                case StackOverflowPolicy.Replace:
                    RemoveInstanceInternal(existing, "overwritten");
                    return CreateInstance(existing.TargetId, def, sourceId, durationOverride);

                default:
                    throw new InvalidOperationException($"未知的 StackOverflowPolicy：{_options.StackOverflowPolicy}");
            }
        }

        private AuraInstanceRef CreateInstance(Id targetId, AuraDef def, Id sourceId, double? durationOverride)
        {
            var instance = new AuraInstanceState
            {
                InstanceId = new Id($"skill.aura_inst_{++_seq}"),
                DefId = def.Id,
                TargetId = targetId,
                SourceId = sourceId,
                Stacks = 1,
                Remaining = durationOverride ?? def.Duration,
                SeqNo = _seq,
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

            _bus.Enqueue(new AuraAppliedEvent(targetId, def.Id, sourceId, instance.Stacks));
            return new AuraInstanceRef(instance.InstanceId);
        }

        public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef)
        {
            if (_instances.TryGetValue(auraInstanceRef.AuraInstanceId, out var instance) && instance.TargetId.Equals(targetId))
            {
                RemoveInstanceInternal(instance, "removed");
            }
        }

        private void RemoveInstanceInternal(AuraInstanceState instance, string reason)
        {
            _statHost.RemoveModifiersBySource(instance.TargetId, instance.InstanceId);
            _instances.Remove(instance.InstanceId);
            var sourceKey = _options.AllowMultiSourceTiming ? (Id?)instance.SourceId : null;
            _slots.Remove((instance.TargetId, instance.DefId, sourceKey));

            if (instance.ProcDefRef.HasValue)
            {
                ProcHost?.Detach(instance.InstanceId);
            }

            _bus.Enqueue(new AuraRemovedEvent(instance.TargetId, instance.DefId, reason));
        }

        /// <summary><c>dispel</c> 效果原语落地（见 06 第 3.2 节"按类别、数量"）：按创建顺序移除
        /// 最多 <paramref name="count"/> 条 <c>dispel_type == dispelType</c> 的实例。</summary>
        public int Dispel(Id targetId, Id dispelType, int count)
        {
            var candidates = _instances.Values
                .Where(i => i.TargetId.Equals(targetId))
                .Where(i => _defs.GetAuraDef(i.DefId).DispelType.HasValue && _defs.GetAuraDef(i.DefId).DispelType!.Value.Equals(dispelType))
                .OrderBy(i => i.SeqNo)
                .Take(Math.Max(0, count))
                .ToList();

            foreach (var instance in candidates)
            {
                RemoveInstanceInternal(instance, "dispelled");
            }

            return candidates.Count;
        }

        // -----------------------------------------------------------------
        // Tick
        // -----------------------------------------------------------------

        /// <summary>按 <paramref name="dt"/> 推进全部实例的持续时间与周期效果（见 06 第 3.3 节
        /// <c>periodic_damage</c>/<c>periodic_heal</c>、第 3.8 节"周期 tick 顺序：按实例创建顺序"）。</summary>
        public void Update(double dt)
        {
            var ordered = _instances.Values.OrderBy(i => i.SeqNo).ToList();

            foreach (var instance in ordered)
            {
                var def = _defs.GetAuraDef(instance.DefId);
                for (var i = 0; i < def.Effects.Count; i++)
                {
                    var entry = def.Effects[i];
                    if (entry.Kind != AuraEffectKind.PeriodicDamage && entry.Kind != AuraEffectKind.PeriodicHeal)
                    {
                        continue;
                    }

                    var interval = ParamsX.GetNumber(entry.Params, "interval");
                    if (interval <= 0)
                    {
                        continue;
                    }

                    instance.PeriodicAccumulators.TryGetValue(i, out var acc);
                    acc += dt;
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
                canMiss: false);

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
                        instance.ControlFlags |= ParseControlFlags(ParamsX.GetStringArray(entry.Params, "flags"));
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

        public double ConsumeAbsorb(Id unitId, Id school, double amount)
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
                    RemoveInstanceInternal(instance, "absorb_depleted");
                }
            }

            return consumed;
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
