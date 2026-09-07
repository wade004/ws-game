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

        /// <summary>
        /// RC-05 收边补齐：(unitId, skillId) → 当前正在授予它的来源 id 集合——<see cref="_knownSkills"/>
        /// 只是这个集合"是否非空"的缓存视图（见 <see cref="LearnSkill(Id,Id,Id)"/>/
        /// <see cref="ForgetSkill(Id,Id,Id)"/> 维护逻辑），只有集合归零才真正从
        /// <see cref="_knownSkills"/> 移除。判断记录：原实现 <see cref="LearnSkill(Id,Id)"/>/
        /// <see cref="ForgetSkill(Id,Id)"/> 只是一个不计来源的 HashSet 加/删，装备联动（见
        /// <c>core/carriers/item.EquipmentHost</c>/<c>SkillGranter</c>）借用这两个方法时，两件都
        /// 授予同一技能的装备卸下一件会把技能整体遗忘（另一件还穿戴着），永久学习（天赋/任务/
        /// <see cref="KnownSkillsPersistable"/> 读档）与装备授予也无法区分——卸装备会连永久学到的
        /// 技能一起遗忘（见外部审计 RC-05）。<see cref="PermanentGrantSource"/> 是无来源调用（原有
        /// 全部调用方，见 <see cref="LearnSkill(Id,Id)"/> 文档）统一归属的哨兵来源；
        /// <c>core/carriers/item</c> 装备联动改传各自的装备实例 id 作为来源（见 <see cref="SkillGranter"/>
        /// 委托签名改动）。
        /// </summary>
        private readonly Dictionary<(Id UnitId, Id SkillId), HashSet<Id>> _skillGrantSources =
            new Dictionary<(Id, Id), HashSet<Id>>();

        /// <summary>无显式来源的 <see cref="LearnSkill(Id,Id)"/>/<see cref="ForgetSkill(Id,Id)"/>
        /// 调用（天赋/任务奖励/技能书/读档等"永久学习"路径，见 <see cref="_skillGrantSources"/>
        /// 判断记录）统一归属的哨兵来源 id——不是真实技能 id，只用作字典 key，不会与任何真实
        /// <c>skill.*</c>/装备实例 id 冲突（后者恒以 <c>item.inst_</c> 前缀命名，见
        /// <c>core/carriers/item</c> 实例 id 生成惯例）。</summary>
        private static readonly Id PermanentGrantSource = new Id("skill.grant_source.permanent");

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
            IExprSchema? exprSchema = null,
            IStaticImmunityProvider? staticImmunity = null,
            IProjectileSpawner? projectileSpawner = null,
            IWeaponDamageQuery? weaponDamageQuery = null)
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
            _auraHost = new AuraHost(_defs, statHost, eventBus, options1, _diagnostics, staticImmunity);

            // ProcHost 的触发回调用方法组转换绑定 TriggerCastInternal——该方法内部读取 _pipeline
            // 字段，而 _pipeline 要到本构造函数末尾才赋值；C# 闭包/方法组按调用时刻求值字段，
            // 只要真正触发发生在构造完成之后（游戏运行期间），这里提前绑定是安全的。
            _procHost = new ProcHost(eventBus, rngHost, exprHostFactory, options1, _diagnostics, TriggerCastInternal);
            _auraHost.ProcHost = _procHost;

            _spellMods = new SpellModResolver(_defs, _auraHost);
            // W1 收边补齐：CooldownTracker 早于 SpellModResolver 构造（避免循环依赖，见构造顺序
            // 类注释），回填后 SpellModDimension.Charges 才真正生效（见 CooldownTracker.SpellMods）。
            _cooldowns.SpellMods = _spellMods;

            _effectDispatcher = new EffectDispatcher(
                _auraHost, _cooldowns, _defs, powerHost, _units, combatHost, statHost, _spellMods,
                effectExtension, _diagnostics, TriggerCastInternal, InterruptInternal, LearnSkill,
                projectileSpawner, weaponDamageQuery);
            _auraHost.EffectSink = _effectDispatcher;

            _pipeline = new CastPipeline(
                _defs, _cooldowns, _auraHost, _effectDispatcher, targetHost, _units, spatialQuery,
                powerHost, _spellMods, eventBus, options1, _diagnostics);

            // R05 收边补齐（外部审计 5e779c6，P2；见 Core.Rules.Common.TimeModelRescaledEvent
            // 类型判断记录）：本类型是 CooldownTracker/AuraHost 的组合根，在这里订阅一次、原子
            // 换算两者名下全部倒计时，不需要 core/gameplay/assembly.TimeModelSwitch 直接持有
            // 二者的具体类型引用（跨层直接引用会越过 00 架构总则的分层依赖方向）。
            //
            // 第五轮外部审核相邻缺口根治：CastPipeline（本类型同批组合的第三个持有倒计时状态的
            // 组件，见其 _currentFactor 判断记录）此前未接入这一广播，同一批一并换算。
            eventBus.Subscribe<TimeModelRescaledEvent>(RulesEventKeys.TimeModelRescaled, OnTimeModelRescaled);
        }

        private void OnTimeModelRescaled(TimeModelRescaledEvent evt)
        {
            _cooldowns.RescaleAll(evt.Factor);
            _auraHost.RescaleAll(evt.Factor);
            _pipeline.RescaleAll(evt.Factor);
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

        /// <summary>不带来源的学习——归属 <see cref="PermanentGrantSource"/> 哨兵来源（天赋/任务
        /// 奖励/技能书/读档等"永久学习"路径全部经由本重载，见 <see cref="_skillGrantSources"/>
        /// 判断记录）。多次调用幂等（哨兵来源在集合里只占一个位置）。</summary>
        public void LearnSkill(Id unitId, Id skillId) => LearnSkill(unitId, skillId, PermanentGrantSource);

        /// <summary>
        /// RC-05 收边补齐：带来源的学习——<paramref name="sourceId"/> 加入 (unitId, skillId) 的授予
        /// 来源集合（见 <see cref="_skillGrantSources"/> 判断记录）；集合此前为空时才真正把技能
        /// 加入 <see cref="_knownSkills"/>（"从无到有"才是真正的学会，重复来源/追加来源不重复触发）。
        /// 装备联动（<c>core/carriers/item.EquipmentHost</c>）经 <see cref="SkillGranter"/> 委托、
        /// 以各自装备实例 id 作为 <paramref name="sourceId"/> 调用本重载。
        /// </summary>
        public void LearnSkill(Id unitId, Id skillId, Id sourceId)
        {
            var key = (unitId, skillId);
            if (!_skillGrantSources.TryGetValue(key, out var sources))
            {
                sources = new HashSet<Id>();
                _skillGrantSources[key] = sources;
            }

            sources.Add(sourceId);

            if (!_knownSkills.TryGetValue(unitId, out var known))
            {
                known = new HashSet<Id>();
                _knownSkills[unitId] = known;
            }

            known.Add(skillId);
        }

        /// <summary>阶段 3 整理"事项四"补齐：<see cref="LearnSkill(Id,Id)"/> 的对称操作。不带来源——
        /// 归属 <see cref="PermanentGrantSource"/> 哨兵来源，与 <see cref="LearnSkill(Id,Id)"/> 配对
        /// （见 <see cref="ForgetSkill(Id,Id,Id)"/> 判断记录"来源引用计数"）。单位未注册或技能本不在
        /// 已知集合中均视为幂等成功，不抛异常。</summary>
        public void ForgetSkill(Id unitId, Id skillId) => ForgetSkill(unitId, skillId, PermanentGrantSource);

        /// <summary>
        /// RC-05 收边补齐：带来源的遗忘——只把 <paramref name="sourceId"/> 从 (unitId, skillId) 的
        /// 授予来源集合里摘除；只有摘除后集合归零，才真正从 <see cref="_knownSkills"/> 移除（见
        /// <see cref="_skillGrantSources"/> 判断记录）——卸下一件装备只撤销"这件装备"这一个来源，
        /// 若同一技能仍有其它来源（另一件装备、永久学习）在授予，技能保持已知。
        /// <paramref name="sourceId"/> 本不在来源集合中（如对同一 sourceId 重复 Forget、或该技能
        /// 从未由这个来源授予过）是安全幂等的 no-op，不抛异常。
        /// </summary>
        public void ForgetSkill(Id unitId, Id skillId, Id sourceId)
        {
            var key = (unitId, skillId);
            if (!_skillGrantSources.TryGetValue(key, out var sources))
            {
                return;
            }

            if (!sources.Remove(sourceId) || sources.Count > 0)
            {
                return;
            }

            _skillGrantSources.Remove(key);

            if (_knownSkills.TryGetValue(unitId, out var known))
            {
                known.Remove(skillId);
            }
        }

        public bool Knows(Id unitId, Id skillId) =>
            _knownSkills.TryGetValue(unitId, out var set) && set.Contains(skillId);

        public IReadOnlyList<Id> GetKnownSkills(Id unitId) =>
            _knownSkills.TryGetValue(unitId, out var set) ? set.OrderBy(id => id.Value, StringComparer.Ordinal).ToList() : Array.Empty<Id>();

        /// <summary>
        /// N07 收边补齐（外部审计 68c9bed，P2）：只返回当前由 <see cref="PermanentGrantSource"/>
        /// 哨兵来源授予的已知技能——供 <see cref="KnownSkillsPersistable.Save"/> 使用，取代此前的
        /// <see cref="GetKnownSkills"/>（返回全部来源的并集，不分"永久学习"与"装备/临时授予"）。
        /// <para>
        /// 判断记录：装备授予的临时技能（<c>core/carriers/item.EquipmentHost.Equip</c> 经
        /// <see cref="SkillGranter"/> 以装备实例 id 为来源调用 <see cref="LearnSkill(Id,Id,Id)"/>）
        /// 此前被 <c>KnownSkillsPersistable.Save</c> 一并写入 <c>player.known_skills</c> 段，
        /// <c>Load</c> 再经不带来源的 <see cref="LearnSkill(Id,Id)"/> 把它们当成永久学习重新授予——
        /// 读档后卸下装备只撤销装备来源这一份引用计数，永久来源那一份继续把技能算作已知
        /// （见外部审计 N07）。修复后存档只快照"确实是永久学习"的技能；装备授予的临时技能改由
        /// <c>ItemPersistable.Load</c> 恢复装备时经 <see cref="EquipmentHost"/> 重新走一遍
        /// <see cref="SkillGranter"/> 授予（与初次装备同一条路径，不经本方法/存档快照）。一个技能
        /// 若同时被永久来源与装备来源授予，仍然计入本方法结果（与 <see cref="Knows"/> 的"任一来源
        /// 即已知"语义一致，只是把枚举范围限定为"包含永久来源"）。
        /// </para>
        /// </summary>
        public IReadOnlyList<Id> GetPermanentlyKnownSkills(Id unitId)
        {
            var result = new List<Id>();
            foreach (var pair in _skillGrantSources)
            {
                if (!pair.Key.UnitId.Equals(unitId)) continue;
                if (pair.Value.Contains(PermanentGrantSource))
                {
                    result.Add(pair.Key.SkillId);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            return result;
        }

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
            AdvanceRoundTimers(dt);
        }

        /// <summary>
        /// H4 补齐（离散模式"统一推进"，见 <c>SkillTickHandler</c> 判断记录）：只推进冷却/充能/
        /// 公共冷却/光环/Proc 内部冷却/学派锁定，<b>不</b>推进读条/引导管线（<see cref="CastPipeline"/>
        /// 的读条剩余时间）——后者按"施法者自己的离散步"单独推进（见 <see cref="AdvanceCastForActor"/>），
        /// 二者混在一起会导致读条在轮结束时被全局统一推进一次、又在施法者自己回合内被推进一次，
        /// 双重计数。由 <c>SkillTickHandler</c> 构造期订阅 <c>sim.round_ended</c> 时以 <c>dt=1.0</c>
        /// （一轮）调用，与 <c>core/rules/combat.CombatTickHandler</c> 的既有惯例一致（见该类型注释）；
        /// <see cref="Update"/>（连续模式每 tick 调用）内部转调本方法 + <c>_pipeline.Update</c>，
        /// 连续模式行为不变。
        /// <para>
        /// RC-07 收边勘误：学派锁定（<see cref="CastPipeline.AdvanceSchoolLocks"/>）原本只在
        /// <see cref="CastPipeline.Update"/>（连续模式）内部推进——离散模式完全不调用
        /// <see cref="CastPipeline.Update"/>，学派锁定因此永远不会衰减（见外部审计 RC-07）。现在
        /// 移到本方法统一推进：<see cref="CastPipeline.Update"/> 不再自己推进（见该方法判断记录），
        /// 本方法是连续/离散两种模式唯一共同经过的推进点（连续模式经 <see cref="Update"/> 每 tick
        /// 调用本方法一次，离散模式经 <c>sim.round_ended</c> 每轮调用本方法一次），不会重复推进。
        /// </para>
        /// </summary>
        public void AdvanceRoundTimers(double dt)
        {
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
            _pipeline.AdvanceSchoolLocks(dt);
        }

        /// <summary>
        /// H4 补齐（离散模式"读条跨回合"）：只推进 <paramref name="actorId"/> 自己的读条/引导剩余
        /// 时间（见 <see cref="CastPipeline.AdvanceOne"/>），供 <c>SkillTickHandler</c> 在该行动者
        /// 自己的 Discrete 步内调用，<c>dt</c> 固定传 1（一步 = 该行动者的一个回合，见 04 第 3.1 节
        /// "以数据集声明的时间单位计"——离散作用域下 <c>cast_time</c>/<c>channel_time</c> 已是整数
        /// 回合）。跨回合读条见 <c>SkillTickHandler</c>/<c>TurnScheduler</c> 判断记录（该行动者忙于
        /// 读条时，即便是玩家也不等待新意图，自动继续）。
        /// </summary>
        public void AdvanceCastForActor(Id actorId, double dt) => _pipeline.AdvanceOne(actorId, dt);

        // -----------------------------------------------------------------
        // 内部回调（绑定给 EffectDispatcher/ProcHost，见构造函数注释）
        // -----------------------------------------------------------------

        private bool TriggerCastInternal(Id casterId, Id skillId, IReadOnlyList<Id> targets, int chainDepth) =>
            _pipeline.TriggerCast(casterId, skillId, targets, chainDepth);

        private void InterruptInternal(Id targetId, Id interrupterId, Id? lockSchool, double lockDuration) =>
            _pipeline.Interrupt(targetId, interrupterId, lockSchool, lockDuration);
    }
}
