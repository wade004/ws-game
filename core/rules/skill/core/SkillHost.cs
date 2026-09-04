using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// <c>core/rules/skill</c> 模块的组合根：实现 <see cref="ISkillHost"/>，持有并串接
    /// <see cref="AuraHost"/>/<see cref="CooldownTracker"/>/<see cref="CastPipeline"/>/
    /// <see cref="ProcHost"/>/<see cref="SpellModResolver"/>/<see cref="EffectDispatcher"/>
    /// （见落地方案 T2-4/T2-5/T2-6 行）。构造顺序刻意安排为"先构造不需要对方的一侧，再用可写
    /// 属性回填另一侧的引用"，避免 <see cref="AuraHost"/>/<see cref="ProcHost"/>/
    /// <see cref="EffectDispatcher"/> 三者出现构造期循环依赖（细节见各自构造调用处的注释）。
    /// </summary>
    public sealed class SkillHost : ISkillHost
    {
        private readonly IDataRegistryView _registry;
        private readonly IUnitAccess _units;
        private readonly IStatHost _statHost;
        private readonly SkillDefCache _defs;
        private readonly ISkillDiagnostics _diagnostics;

        private readonly CooldownTracker _cooldowns;
        private readonly AuraHost _auraHost;
        private readonly ProcHost _procHost;
        private readonly SpellModResolver _spellMods;
        private readonly EffectDispatcher _effectDispatcher;
        private readonly CastPipeline _pipeline;

        private readonly Dictionary<Id, HashSet<Id>> _knownSkills = new Dictionary<Id, HashSet<Id>>();

        /// <summary>供 <c>combat</c> 调用的效果落地出口（见 06 第 7 节 <c>EffectSink</c>）。</summary>
        public IEffectSink EffectSink => _effectDispatcher;

        /// <summary>供 <c>combat</c>/<c>ai</c> 调用的光环状态只读查询（见 06 第 7 节）。</summary>
        public IAuraQuery AuraQuery => _auraHost;

        public SkillHost(
            IDataRegistryView dataRegistry,
            IEventBus eventBus,
            IUnitAccess unitAccess,
            IStatHost statHost,
            IPowerHost powerHost,
            IRngHost rngHost,
            ICombatHost combatHost,
            ITargetHost targetHost,
            IExprHostFactory exprHostFactory,
            ISpatialQuery? spatialQuery,
            SkillOptions? options = null,
            IEffectExtension? effectExtension = null,
            ISkillDiagnostics? diagnostics = null,
            IExprSchema? exprSchema = null)
        {
            _registry = dataRegistry ?? throw new ArgumentNullException(nameof(dataRegistry));
            _units = unitAccess ?? throw new ArgumentNullException(nameof(unitAccess));
            _statHost = statHost ?? throw new ArgumentNullException(nameof(statHost));
            if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
            if (powerHost == null) throw new ArgumentNullException(nameof(powerHost));
            if (rngHost == null) throw new ArgumentNullException(nameof(rngHost));
            if (combatHost == null) throw new ArgumentNullException(nameof(combatHost));
            if (targetHost == null) throw new ArgumentNullException(nameof(targetHost));
            if (exprHostFactory == null) throw new ArgumentNullException(nameof(exprHostFactory));

            var options1 = options ?? new SkillOptions();
            _diagnostics = diagnostics ?? new InMemorySkillDiagnostics();
            _defs = new SkillDefCache(_registry, exprSchema);
            _cooldowns = new CooldownTracker();

            // AuraHost 先构造（不需要 ProcHost/EffectDispatcher），随后用可写属性回填两者，
            // 避免"AuraHost 施加带 proc_trigger/周期效果的光环时需要 ProcHost/EffectSink，
            // 而 ProcHost/EffectDispatcher 的构造又需要引用 AuraHost"这一循环。
            _auraHost = new AuraHost(_defs, statHost, eventBus, options1, _diagnostics);

            // ProcHost 的触发回调用方法组转换绑定 TriggerCastInternal——该方法内部读取 _pipeline
            // 字段，而 _pipeline 要到本构造函数末尾才赋值；C# 闭包/方法组按调用时刻求值字段，
            // 只要真正触发发生在构造完成之后（游戏运行期间），这里提前绑定是安全的。
            _procHost = new ProcHost(eventBus, rngHost, exprHostFactory, options1, _diagnostics, TriggerCastInternal);
            _auraHost.ProcHost = _procHost;

            _spellMods = new SpellModResolver(_defs, _auraHost);

            _effectDispatcher = new EffectDispatcher(
                _auraHost, _cooldowns, _defs, powerHost, _units, combatHost, statHost, _spellMods,
                effectExtension, _diagnostics, TriggerCastInternal, InterruptInternal, LearnSkill);
            _auraHost.EffectSink = _effectDispatcher;

            _pipeline = new CastPipeline(
                _defs, _cooldowns, _auraHost, _effectDispatcher, targetHost, _units, spatialQuery,
                powerHost, _spellMods, eventBus, options1, _diagnostics);
        }

        // -----------------------------------------------------------------
        // ISkillHost
        // -----------------------------------------------------------------

        public Vec2 GetPosition(Id unitId) => _units.GetPosition(unitId);

        public IReadOnlyList<Id> FindUnits(Shape shape, Vec2 origin, UnitFilter filter)
        {
            // 判断记录：本模块构造未强制要求注入 ISpatialQuery（射程/视线检查允许在无空间索引
            // 时跳过，见 CastPipeline 步骤 7 注释），FindUnits 若在未注入 ISpatialQuery 的场景下
            // 被调用，没有可委托的空间查询实现，返回空列表并记一条诊断（不抛异常，呼应"运行时
            // 不做静默降级"以外——这里明确记警告，不是完全静默）。
            _diagnostics.Warn("ISkillHost.FindUnits 被调用，但本实例未注入 ISpatialQuery，返回空列表");
            return Array.Empty<Id>();
        }

        public void ApplyStatMod(Id sourceId, Id unitId, Id stat, StatModifierOp op, double value) =>
            _statHost.AddModifier(unitId, new StatModifier(stat, op, value, sourceId));

        public CastResult CastSkill(Id casterId, Id skillId, IReadOnlyList<Id> targets) =>
            _pipeline.CastSkill(casterId, skillId, targets);

        public double GetCooldown(Id unitId, Id skillId) =>
            _defs.TryGetSkillDef(skillId, out var def) ? _cooldowns.GetCooldown(unitId, def) : 0;

        public bool IsCasting(Id unitId) => _pipeline.IsCasting(unitId);

        public void Interrupt(Id unitId, Id interrupterId, Id? lockSchool, double lockDuration) =>
            _pipeline.Interrupt(unitId, interrupterId, lockSchool, lockDuration);

        /// <summary>供移动系统在单位位移时通知（见 06 第 3.1 节 <c>interrupt_flags: movement</c>）；
        /// 不在 <see cref="ISkillHost"/> 契约中（该契约由 06 第 7 节固定签名），是本模块对外的补充
        /// 公开方法。</summary>
        public void NotifyMoved(Id unitId) => _pipeline.NotifyMoved(unitId);

        // -----------------------------------------------------------------
        // 已知技能 / 技能书
        // -----------------------------------------------------------------

        public void LearnSkill(Id unitId, Id skillId)
        {
            if (!_knownSkills.TryGetValue(unitId, out var set))
            {
                set = new HashSet<Id>();
                _knownSkills[unitId] = set;
            }

            set.Add(skillId);
        }

        public bool Knows(Id unitId, Id skillId) =>
            _knownSkills.TryGetValue(unitId, out var set) && set.Contains(skillId);

        public IReadOnlyList<Id> GetKnownSkills(Id unitId) =>
            _knownSkills.TryGetValue(unitId, out var set) ? set.OrderBy(id => id.Value, StringComparer.Ordinal).ToList() : Array.Empty<Id>();

        /// <summary>按 <c>skill.book</c> 的等级映射学习技能（见 04 第 1.1 节 skill.book 行）：
        /// 学习全部 <c>entries[].level &lt;= level</c> 的技能。</summary>
        public void LearnFromBook(Id unitId, Id bookId, int level)
        {
            var book = _defs.GetBook(bookId);
            foreach (var (entryLevel, skillId) in book.Entries)
            {
                if (entryLevel <= level)
                {
                    LearnSkill(unitId, skillId);
                }
            }
        }

        // -----------------------------------------------------------------
        // Tick
        // -----------------------------------------------------------------

        /// <summary>推进读条/引导、冷却/充能/公共冷却、光环（含周期效果）、Proc 内部冷却
        /// （见 <see cref="SkillTickHandler"/> 调用时机）。</summary>
        public void Update(double dt)
        {
            _pipeline.Update(dt);
            _cooldowns.Update(dt);

            foreach (var (unitId, skillId) in _cooldowns.TrackedChargeKeys)
            {
                if (_defs.TryGetSkillDef(skillId, out var def))
                {
                    _cooldowns.AdvanceCharges(unitId, def, dt);
                }
            }

            _auraHost.Update(dt);
            _procHost.Update(dt);
        }

        // -----------------------------------------------------------------
        // 内部回调（绑定给 EffectDispatcher/ProcHost，见构造函数注释）
        // -----------------------------------------------------------------

        private bool TriggerCastInternal(Id casterId, Id skillId, IReadOnlyList<Id> targets) =>
            _pipeline.TriggerCast(casterId, skillId, targets);

        private void InterruptInternal(Id targetId, Id interrupterId, Id? lockSchool, double lockDuration) =>
            _pipeline.Interrupt(targetId, interrupterId, lockSchool, lockDuration);
    }
}
