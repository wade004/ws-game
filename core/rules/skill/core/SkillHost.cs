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

        /// <summary>见 <see cref="FindUnits"/> 判断记录：本模块构造未强制要求注入
        /// <see cref="ISpatialQuery"/>，为 null 时 <see cref="FindUnits"/> 记一条诊断并返回空列表。</summary>
        private readonly ISpatialQuery? _spatialQuery;

        /// <summary>见 <see cref="FindUnits"/> 判断记录：<see cref="UnitFilter.Relation"/> 为
        /// Hostile/Friendly/Neutral 时需要按阵营矩阵判定，可选注入（同 <see cref="_spatialQuery"/>
        /// 惯例，未注入时这三种 relation 一律判不通过，见该方法判断记录），不强加新的必填依赖。</summary>
        private readonly Core.Numbers.Faction.IFactionMatrix? _factions;

        /// <summary>见 <see cref="FindUnits"/> 判断记录（格子吸附落地新增字段）：构造期传入
        /// <see cref="AuraHost"/>/<see cref="ProcHost"/>/<see cref="CastPipeline"/> 的同一份
        /// <see cref="SkillOptions"/> 实例，本类型此前没有为自己保留一份引用——<see cref="FindUnits"/>
        /// 需要读取 <see cref="SkillOptions.IsDiscreteStep"/>/<see cref="SkillOptions.GridSnapCellSize"/>/
        /// <see cref="SkillOptions.GridSnapPolicy"/> 判定是否启用格子中心采样，因此补上本字段。</summary>
        private readonly SkillOptions _options;

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
        /// <para>
        /// CR130-02 根治（外部审计 audit-5c444f1-20260908）：value 从 <c>HashSet&lt;Id&gt;</c> 改为
        /// <c>Dictionary&lt;Id, bool&gt;</c>——记录每个来源各自的"是否永久"分类（见
        /// <see cref="LearnSkill(Id,Id,Id,bool)"/> 判断记录），不再只靠"是否等于
        /// <see cref="PermanentGrantSource"/> 这一个哨兵值"判断永久性。根因：一次性任务/成就/遭遇
        /// 奖励经 <c>RewardDispatcher.GrantSkills</c> 透传自己的奖励来源 id（不是哨兵）调用
        /// <see cref="LearnSkill(Id,Id,Id)"/>，此前唯一的"永久"判据是"来源 == 哨兵"，这类奖励技能
        /// 因此被 <see cref="GetPermanentlyKnownSkills"/>/<see cref="KnownSkillsPersistable.Save"/>
        /// 排除在存档快照之外——新宿主读档会丢失这个本应长期保留的技能，同宿主读档也不会清理它
        /// （因为它压根没被当作"永久"处理过，见 <see cref="KnownSkillsPersistable"/> 替换语义），
        /// 结果依宿主生命周期分叉（外部审计复现）。
        /// </para>
        /// </summary>
        private readonly Dictionary<(Id UnitId, Id SkillId), Dictionary<Id, bool>> _skillGrantSources =
            new Dictionary<(Id, Id), Dictionary<Id, bool>>();

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
            IWeaponDamageQuery? weaponDamageQuery = null,
            Core.Numbers.Faction.IFactionMatrix? factions = null)
        {
            _registry = dataRegistry ?? throw new ArgumentNullException(nameof(dataRegistry));
            _units = unitAccess ?? throw new ArgumentNullException(nameof(unitAccess));
            _statHost = statHost ?? throw new ArgumentNullException(nameof(statHost));
            _spatialQuery = spatialQuery;
            _factions = factions;
            if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
            if (powerHost == null) throw new ArgumentNullException(nameof(powerHost));
            if (rngHost == null) throw new ArgumentNullException(nameof(rngHost));
            if (combatHost == null) throw new ArgumentNullException(nameof(combatHost));
            if (targetHost == null) throw new ArgumentNullException(nameof(targetHost));
            if (exprHostFactory == null) throw new ArgumentNullException(nameof(exprHostFactory));

            var options1 = options ?? new SkillOptions();
            _options = options1;
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

            // P2-05 根治（外部审计 audit-c9ff301-20260909）：开发期 DataHotReload 契约要求"成功
            // reload 通知后，既有 resident host 的下一次 cast 能看到新定义"（见 games/_template
            // README 热重载一节、SkillDefCache.InvalidateAll 判断记录）。此前 SkillDefCache 只懒解析
            // 一次、永久常驻，从不订阅 DataLoadCompletedEvent，resident host 因此在 reload 之后继续
            // 使用旧的 base_value/cooldown_duration 等字段，直到进程重建全新 host 才会看到新值。这里
            // 订阅一次，任何一次数据加载完成（含非 skill.* 表的加载，见该事件不携带表名判断记录）都
            // 整体清空五张 skill.* 缓存表——过度失效（下一次访问重新解析）比"选择性失效但漏判某张表"
            // 更安全，且本模块只在懒解析路径（TryGet*/Get*）重新读 registry，不做任何异步/预取，
            // 清空本身是零成本的。
            eventBus.Subscribe<DataLoadCompletedEvent>(DataRegistryEventKeys.LoadCompleted, OnDataLoadCompleted);
        }

        /// <summary>
        /// ABI/API 兼容 façade（PJ114-01 根治，外部审计 audit-76d16a5-20260910）：1.13.0（同时也是
        /// 1.12.0，两者物理签名相同，见本重载判断记录复现命令）的唯一构造函数物理 IL 签名恰好是
        /// 十七个参数、没有 <c>factions</c> 这个 1.14.0 新增的第十八个可选参数。C# 的可选参数是
        /// 编译期特性——上面主构造函数虽然 <c>factions</c> 带默认值 <c>null</c>，但物理 IL 签名仍是
        /// 完整的十八个参数；1.13.0/1.12.0 编译产物里对十七参数构造函数的调用（省略末尾的可选实参时，
        /// 编译器把当时能看到的全部默认值一起固化进调用点 IL），在只替换正式 DLL、不重新编译的情况下，
        /// 找不到匹配的物理方法而 <c>MissingMethodException</c>（见 project-findings.md PJ114-01
        /// "实际"：1.13.0 正式 consumer 对 1.13.0 正式 DLL 运行 exit 0；只替换五个 1.14.0 正式 Core
        /// DLL、不重编译，运行 exit 11，`MissingMethodException` 指向本重载缺失的十七参数构造器）。
        /// CHANGELOG 把 1.14.0 定义为 MINOR 并声明旧 consumer 替换正式 DLL 无需重编译，本重载补回
        /// 这个物理十七参数签名、转发到主构造函数，<c>factions</c> 固定传 <c>null</c>——旧调用方不会
        /// 得到阵营矩阵注入（<see cref="UnitFilter.Relation"/> 为 Hostile/Friendly/Neutral 时判不
        /// 通过，同未注入 <see cref="ISpatialQuery"/> 的既有降级惯例），只保证不再
        /// <c>MissingMethodException</c>，其余行为与 1.13.0/1.12.0 完全一致。
        /// <para>
        /// 判断记录（不是可选参数）：本重载的十七个参数全部不带默认值——若也写成可选参数（例如给
        /// <c>weaponDamageQuery</c> 之后再加一个可选 <c>factions</c>），会与主构造函数在"只传
        /// 10～16 个参数"的调用点产生重载二义性（两者都可以匹配、都需要为末尾参数代入默认值，C#
        /// 编译器无法确定唯一最佳候选）。全部十七个参数都必填后，C# 重载决议规则（"不需要为可选
        /// 参数代入默认值的候选更优"）保证恰好传 17 个位置/具名参数时精确匹配本重载、不需要为
        /// <c>factions</c> 代入默认值；传少于 17 个参数的调用只能匹配主构造函数（十八参数版本，
        /// <c>factions</c> 及更早的可选参数依次代入默认值）。两条重载因此对源码侧（重新编译旧
        /// 调用点）与二进制侧（旧调用点固化的 IL 恰好 17 个实参）都不产生歧义、都能兼容。
        /// </para>
        /// </summary>
        [Obsolete("1.12.0/1.13.0 的十七参数构造签名，仅为源码/二进制兼容保留；新代码请使用带 factions 的十八参数构造函数。")]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
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
            SkillOptions? options,
            IEffectExtension? effectExtension,
            ISkillDiagnostics? diagnostics,
            IExprSchema? exprSchema,
            IStaticImmunityProvider? staticImmunity,
            IProjectileSpawner? projectileSpawner,
            IWeaponDamageQuery? weaponDamageQuery)
            : this(dataRegistry, eventBus, unitAccess, statHost, powerHost, rngHost, combatHost,
                targetHost, exprHostFactory, spatialQuery, options, effectExtension, diagnostics,
                exprSchema, staticImmunity, projectileSpawner, weaponDamageQuery, factions: null)
        {
        }

        private void OnDataLoadCompleted(DataLoadCompletedEvent evt) => _defs.InvalidateAll();

        private void OnTimeModelRescaled(TimeModelRescaledEvent evt)
        {
            _cooldowns.RescaleAll(evt.Factor);
            _auraHost.RescaleAll(evt.Factor);
            _pipeline.RescaleAll(evt.Factor);
            // CR130-03 根治（外部审计 audit-5c444f1-20260908）：ProcHost 的内部冷却（ICD）此前未接入
            // 这一批广播，与冷却/光环/施法管线各用各的时间基准，同批一并换算。
            _procHost.RescaleAll(evt.Factor);
        }

        // -----------------------------------------------------------------
        // ISkillHost
        // -----------------------------------------------------------------

        public Vec2 GetPosition(Id unitId) => _units.GetPosition(unitId);

        /// <summary>
        /// 静态差距根治（外部审计 audit-c9ff301-20260909）：此前恒返回空列表。实现按 06 第 7 节
        /// <c>findUnits(shape, filter)</c> 契约，委托注入的 <see cref="ISpatialQuery.QueryShape"/>
        /// 做真正的形状判定（复用引擎适配层既有的圆/扇形/线段/矩形判定算法，不在本模块重新实现一遍
        /// 几何计算——同 <see cref="Core.Rules.Targeting.BuiltinTargetStrategies"/> 的既有惯例）：
        /// <list type="bullet">
        /// <item><paramref name="shape"/> 只用 <see cref="Shape.WithOrigin"/> 把锚点换成
        /// <paramref name="origin"/>，其余字段（<c>Direction</c>/<c>Angle</c>/<c>Radius</c> 等）原样
        /// 保留——与 <see cref="Core.Rules.Targeting.TargetHost"/> 的 <c>RebaseShape</c> 不同（那里
        /// 额外用施法者当前朝向覆盖模板的 <c>Direction</c>），本方法签名没有朝向参数，调用方若需要
        /// 按朝向重建 cone/line/rect，应在传入前自行构造好 <paramref name="shape"/> 的方向字段。</item>
        /// <item>登记进空间索引的触发体（<c>trigger_only</c> 标签，见
        /// <see cref="Core.Foundation.EngineAdapter.CollisionLayers.TriggerOnly"/> 判断记录）一律
        /// 排除，避免下游把触发体 id 当单位 id 处理时崩溃（同 <c>BuiltinTargetStrategies</c> 的
        /// <c>ExcludeTriggerOnly</c> 判断记录，这里复用同一个标签常量，不是独立发明的过滤规则）。</item>
        /// <item><see cref="UnitFilter.AliveOnly"/>/<see cref="UnitFilter.Exclude"/>/
        /// <see cref="UnitFilter.RequiredTags"/>/<see cref="UnitFilter.ExcludedTags"/> 按字面语义
        /// 过滤。</item>
        /// <item><see cref="UnitFilter.Relation"/>：签名没有 casterId 参数，<see cref="UnitFilter.Exclude"/>
        /// 兼任"关系判定的参照单位"——该字段文档"通常是施法者自身"，<c>Self</c>/<c>NotSelf</c> 直接
        /// 按候选是否等于该参照单位判断；<c>Hostile</c>/<c>Friendly</c>/<c>Neutral</c> 用
        /// <see cref="_factions"/>（可选注入）查参照单位与候选单位阵营的反应。<c>Exclude</c> 未提供
        /// 或 <see cref="_factions"/> 未注入时，这三种 relation 判不通过（宁可漏收，不误纳——没有
        /// 参照单位/阵营矩阵时无法做出正确判定，不应该悄悄退化为"不过滤"而把不相关单位纳入结果）。</item>
        /// </list>
        /// 结果按 Id 序数升序排序，保证确定性（同 <c>BuiltinTargetStrategies.AllInShapeStrategy</c>
        /// 惯例）。
        /// </summary>
        public IReadOnlyList<Id> FindUnits(Shape shape, Vec2 origin, UnitFilter filter)
        {
            // 判断记录：本模块构造未强制要求注入 ISpatialQuery（射程/视线检查允许在无空间索引
            // 时跳过，见 CastPipeline 步骤 7 注释），FindUnits 若在未注入 ISpatialQuery 的场景下
            // 被调用，没有可委托的空间查询实现，返回空列表并记一条诊断（不抛异常，呼应"运行时
            // 不做静默降级"以外——这里明确记警告，不是完全静默）。
            if (_spatialQuery == null)
            {
                _diagnostics.Warn("ISkillHost.FindUnits 被调用，但本实例未注入 ISpatialQuery，返回空列表");
                return Array.Empty<Id>();
            }

            var anchored = shape.WithOrigin(origin);
            var excludedTags = new List<string>(filter.ExcludedTags.Count + 1);
            foreach (var tag in filter.ExcludedTags)
            {
                excludedTags.Add(tag.Value);
            }
            excludedTags.Add(Core.Foundation.EngineAdapter.CollisionLayers.TriggerOnly);

            var requiredTags = filter.RequiredTags.Count == 0
                ? null
                : filter.RequiredTags.Select(id => id.Value).ToList();

            var queryFilter = new QueryFilter(requiredTags: requiredTags, excludedTags: excludedTags);

            // ADR-0013 决策 6、04 第 3.1 节 grid_snap 落地：离散步内声明了格子吸附时，候选按其所属
            // 格子中心点判定是否落在 anchored 形状内，而不是按候选的原始坐标（见 SkillOptions.
            // GridSnapCellSize 判断记录）；未装配/连续模式下行为与格子吸附落地之前逐字节一致。
            var isGridSnapActive = (_options.IsDiscreteStep?.Invoke() ?? false) && _options.GridSnapCellSize.HasValue;
            var raw = isGridSnapActive
                ? Core.Foundation.EngineAdapter.GridSnapShapeQuery.QueryShapeAtCellCenters(
                    _spatialQuery, anchored, queryFilter, _units.GetPosition,
                    _options.GridSnapPolicy, _options.GridSnapCellSize!.Value)
                : _spatialQuery.QueryShape(anchored, queryFilter);

            var result = new List<Id>(raw.Count);
            foreach (var candidateId in raw)
            {
                if (filter.AliveOnly && !_units.IsAlive(candidateId))
                {
                    continue;
                }

                // 判断记录：Exclude 兼任"关系判定的参照单位"（见本方法类型文档）。Relation.Self
                // 的语义恰恰是"只保留等于参照单位的候选"，与"无条件排除 Exclude"直接矛盾——因此
                // 无条件排除只在非 Self 时生效；其余 relation（含 Any/NotSelf/Hostile/Friendly/
                // Neutral）继续无条件排除参照单位自己（对 Hostile/Friendly/Neutral 而言，
                // IFactionMatrix.GetReaction(reference, reference) 恒为 Friendly，参照单位若落在
                // 查询范围内会通过 Friendly 判定，这里排除它是"目标查询不应该把施法者自己算进去"
                // 这一常见期望）。
                if (filter.Relation != RelationFilter.Self &&
                    filter.Exclude.HasValue && candidateId == filter.Exclude.Value)
                {
                    continue;
                }

                if (!PassesRelation(filter.Relation, filter.Exclude, candidateId))
                {
                    continue;
                }

                result.Add(candidateId);
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            return result;
        }

        private bool PassesRelation(RelationFilter relation, Id? reference, Id candidateId)
        {
            switch (relation)
            {
                case RelationFilter.Any:
                    return true;

                case RelationFilter.Self:
                    return reference.HasValue && candidateId == reference.Value;

                case RelationFilter.NotSelf:
                    return !reference.HasValue || candidateId != reference.Value;

                case RelationFilter.Hostile:
                case RelationFilter.Friendly:
                case RelationFilter.Neutral:
                    // 判断记录：见本方法调用处 FindUnits 类型文档"Exclude 未提供或 _factions 未注入
                    // 时判不通过"——没有参照单位/阵营矩阵时无法正确判定，不悄悄退化成"全部放行"。
                    if (!reference.HasValue || _factions == null)
                    {
                        return false;
                    }

                    var reaction = _factions.GetReaction(_units.GetFaction(reference.Value), _units.GetFaction(candidateId));
                    return relation switch
                    {
                        RelationFilter.Hostile => reaction == Core.Numbers.Faction.Reaction.Hostile,
                        RelationFilter.Friendly => reaction == Core.Numbers.Faction.Reaction.Friendly,
                        RelationFilter.Neutral => reaction == Core.Numbers.Faction.Reaction.Neutral,
                        _ => false,
                    };

                default:
                    throw new ArgumentOutOfRangeException(nameof(relation), relation, "未知的 RelationFilter");
            }
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
        /// 判断记录）。多次调用幂等（哨兵来源在集合里只占一个位置）。永久（<see
        /// cref="LearnSkill(Id,Id,Id,bool)"/> 判断记录）。</summary>
        public void LearnSkill(Id unitId, Id skillId) => LearnSkill(unitId, skillId, PermanentGrantSource, permanent: true);

        /// <summary>
        /// RC-05 收边补齐、CR130-02 根治：带来源但未显式声明是否永久的学习——转发到
        /// <see cref="LearnSkill(Id,Id,Id,bool)"/> 并按 <c>permanent: true</c> 处理（见该重载判断
        /// 记录"默认按永久语义处理"）。天赋/任务奖励/成就/遭遇一类"一次性、长期保留"的授予路径
        /// （<c>RewardDispatcher.GrantSkills</c> 经 <c>GameplayAssembly</c> 的 <c>SkillGranter</c>
        /// 闭包透传各自的奖励来源 id）全部经由本重载，默认永久与直觉一致，不需要每个调用方都显式
        /// 传 <c>permanent</c>。只有装备/光环一类"跟随宿主生命周期、卸下即失效"的临时授予需要显式
        /// 调用四参重载传 <c>permanent: false</c>（<c>core/carriers/assembly.CarriersAssembly</c> 的
        /// 装备 <see cref="SkillGranter"/> 接线已改用四参重载，见其构造处判断记录）。
        /// </summary>
        public void LearnSkill(Id unitId, Id skillId, Id sourceId) => LearnSkill(unitId, skillId, sourceId, permanent: true);

        /// <summary>
        /// CR130-02 根治（外部审计 audit-5c444f1-20260908）：带来源、显式声明是否永久的学习——
        /// <paramref name="sourceId"/> 连同 <paramref name="permanent"/> 一并记入 (unitId, skillId)
        /// 的授予来源集合（见 <see cref="_skillGrantSources"/> 判断记录）；集合此前为空时才真正把
        /// 技能加入 <see cref="_knownSkills"/>（"从无到有"才是真正的学会，重复来源/追加来源不重复
        /// 触发）。同一来源重复调用按最新一次的 <paramref name="permanent"/> 覆盖（同一 sourceId
        /// 理论上只应由同一条授予路径使用同一个永久性分类调用，不存在合法的"同一来源时而永久时而
        /// 临时"场景）。<see cref="GetPermanentlyKnownSkills"/> 只要该 (unit, skill) 的来源集合里
        /// 存在任意一个 <c>permanent: true</c> 的来源即计入——与 <see cref="Knows"/> 的"任一来源即
        /// 已知"是同一种"任一"语义，只是把枚举范围限定为"其中至少一个是永久来源"。
        /// </summary>
        public void LearnSkill(Id unitId, Id skillId, Id sourceId, bool permanent)
        {
            var key = (unitId, skillId);
            if (!_skillGrantSources.TryGetValue(key, out var sources))
            {
                sources = new Dictionary<Id, bool>();
                _skillGrantSources[key] = sources;
            }

            sources[sourceId] = permanent;

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

        /// <summary>
        /// CR130-02 根治（外部审计 audit-5c444f1-20260908）：一次性撤销 (unitId, skillId) 当前全部
        /// <c>permanent: true</c> 的来源（含无来源调用归属的 <see cref="PermanentGrantSource"/> 哨兵，
        /// 也包括奖励/任务/成就一类显式来源 id），不触碰任何 <c>permanent: false</c> 的临时来源
        /// （装备/光环）。供 <see cref="KnownSkillsPersistable.Load"/> 的替换语义（C09）使用——
        /// "读档 = 恢复到那个时间点的永久技能状态"要求把当前全部永久来源一次性清空，而不只是清空
        /// 哨兵来源那一份（旧实现假设"永久"只可能是哨兵来源，<see
        /// cref="LearnSkill(Id,Id,Id,bool)"/> 收口后这个假设不再成立：<see
        /// cref="ForgetSkill(Id,Id)"/> 只撤销哨兵来源，对以奖励/任务来源 id 授予的永久技能是
        /// no-op——那个 sourceId 从未出现在哨兵来源的位置上，会导致 C09 替换语义对这类技能失效，
        /// 读一份更早的快照反而不会让它们变少）。单位/技能未登记视为幂等成功，不抛异常。
        /// </summary>
        public void ForgetAllPermanentGrants(Id unitId, Id skillId)
        {
            var key = (unitId, skillId);
            if (!_skillGrantSources.TryGetValue(key, out var sources))
            {
                return;
            }

            // 先收集一份快照再逐个 ForgetSkill——ForgetSkill 会就地修改/移除 sources 及其所属的
            // _skillGrantSources[key]，不能在遍历 sources 本身的同时修改它。
            var permanentSourceIds = new List<Id>();
            foreach (var pair in sources)
            {
                if (pair.Value)
                {
                    permanentSourceIds.Add(pair.Key);
                }
            }

            foreach (var sourceId in permanentSourceIds)
            {
                ForgetSkill(unitId, skillId, sourceId);
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
                // CR130-02 根治：不再只判"是否存在 PermanentGrantSource 这个哨兵来源"——任何被显式
                // 标记为 permanent: true 的来源（见 LearnSkill(Id,Id,Id,bool) 判断记录，覆盖不带
                // 哨兵、直接以奖励/任务/成就来源 id 调用的永久授予）都应计入。
                foreach (var isPermanent in pair.Value.Values)
                {
                    if (isPermanent)
                    {
                        result.Add(pair.Key.SkillId);
                        break;
                    }
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

        /// <summary>
        /// 推进读条/引导、冷却/充能/公共冷却、光环（含周期效果）、Proc 内部冷却
        /// （见 <see cref="SkillTickHandler"/> 调用时机）。
        /// <para>
        /// 消费方反馈 2026-09-10（读条完成当帧新冷却被提前推进问题，证据 c08-new-cooldown，见
        /// architecture/落地计划/消费方反馈-2026-09-10-施法时序与实例标识.md）根治：本方法此前先
        /// 调 <c>_pipeline.Update(dt)</c> 再调 <see cref="AdvanceRoundTimers"/>——读条/引导恰好在本次
        /// <paramref name="dt"/> 内完成时，<c>_pipeline.Update</c> 内部经 <c>CastPipeline.FinishCast</c>
        /// 新开启的冷却/公共冷却/充能恢复窗口，会被同一次调用里紧接着执行的
        /// <see cref="AdvanceRoundTimers"/> 用同一个 <paramref name="dt"/> 再扣一遍——新计时状态从
        /// "尚不存在"到"存在"的那个瞬间被当成已经存在了整个 <paramref name="dt"/>，多扣的量随
        /// <paramref name="dt"/> 如何被拆成多个子步而变化（外部复现：<c>cast_time=0.5、
        /// cooldown_duration=1</c>，单步 <c>Update(0.5)</c> 剩 0.5、两步
        /// <c>[0.25,0.25]</c> 剩 0.75、三步 <c>[0.25,0.125,0.125]</c> 剩 0.875，预期均为 1——同一个
        /// 完成时刻因为分段方式不同产生不同结果，是本缺陷的核心症状）。<see cref="AuraHost"/> 的
        /// 光环持续时间/周期累加器、<see cref="ProcHost"/> 的内部冷却与本方法共用同一条
        /// <see cref="AdvanceRoundTimers"/> 推进路径，属于同一类"本 tick 内新创建的计时状态被本
        /// tick 自己的 <paramref name="dt"/> 二次扣减"缺口（见判断记录"相邻计时状态排查"）。
        /// </para>
        /// <para>
        /// 判断记录"相邻计时状态排查"：
        /// <list type="bullet">
        /// <item>GCD——<c>CastPipeline.StartCooldownAndGcd</c> 与技能自身冷却同一调用点写入
        /// <see cref="CooldownTracker"/>，同一批被 <see cref="AdvanceRoundTimers"/> 的
        /// <c>_cooldowns.Update(dt)</c> 推进，同类缺口，随本次改动一并根治。</item>
        /// <item>充能恢复——<c>CastPipeline.StartCooldownAndGcd</c>→<c>CooldownTracker.StartCooldown</c>
        /// 在充能耗尽的那一刻写入 <c>RechargeRemaining</c>，与技能自身冷却共用
        /// <see cref="AdvanceRoundTimers"/> 内 <c>AdvanceCharges</c> 那一批推进，同类缺口，随本次
        /// 改动一并根治。</item>
        /// <item>光环持续时间/周期——<c>CastPipeline.ExecuteEffectsOnly</c>（读条/引导完成时结算的
        /// 效果之一可以是 <c>apply_aura</c>）与 <see cref="AuraHost.Update"/> 同样共用本方法内的调用
        /// 次序，同类缺口，随本次改动一并根治。</item>
        /// <item>Proc 内部冷却——<see cref="ProcHost"/> 只在 <see cref="ProcHost.OnEvent"/>（经
        /// <see cref="Core.Foundation.EventBus.IEventBus.Subscribe"/> 订阅、<c>DispatchPending</c>
        /// 批处理时才派发，见 <see cref="CastPipeline"/> 类型注释 RC-01 判断记录"经 Enqueue 入队、
        /// 下一个 DispatchPending pass 才派发"）里写入 <c>IcdRemaining</c>——本模块全部规则事件
        /// （含触发 Proc 判定的 <c>combat.damage_dealt</c>/<c>combat.heal_done</c> 等）一律
        /// <c>Enqueue</c>，不在 <see cref="Update"/> 内部同步派发，新 ICD 因此恒晚于本次
        /// <see cref="Update"/> 调用（要等调用方后续显式调 <c>DispatchPending</c>）才被写入，不与本方法
        /// 内的 <see cref="ProcHost.Update"/> 竞争同一个 <paramref name="dt"/>，不是同类缺口——
        /// 保留 <see cref="ProcTests"/>/本次新增对照用例钉住"内部冷却按调用批次正常推进、不受本次
        /// 调换顺序影响"。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 判断记录"为什么调换顺序是安全的"（选择方案 a：调换调用顺序，而非给每个计时器额外记一个
        /// "创建于本 tick"标记）：<see cref="AdvanceRoundTimers"/> 只读写既有的冷却/GCD/充能/光环/
        /// Proc 内部冷却/学派锁定状态，不读取、不依赖 <c>_pipeline</c> 内部字段；调换后它在
        /// <c>_pipeline.Update</c> 之前先把"进入本次 <paramref name="dt"/> 之前就已存在"的状态推进
        /// 完毕，随后 <c>_pipeline.Update</c> 才会因为读条/引导完成而经
        /// <c>CastPipeline.FinishCast</c>/<c>ExecuteEffectsOnly</c> 新建冷却/GCD/充能窗口/光环实例——
        /// 这些新状态自然不会被"已经执行完毕"的 <see cref="AdvanceRoundTimers"/> 碰到，要等到<b>下一次</b>
        /// <see cref="Update"/> 调用时才第一次被推进（此时它们已经真正存在了一整个 tick，用那次调用的
        /// <paramref name="dt"/> 扣减是正确的）。唯一需要核实"顺序调换是否改变其它语义"的地方是：读条/
        /// 引导完成时若有排队的下一个施法（<see cref="CastPipeline.FinishCast"/> 里的
        /// <c>state.Queued</c> 分支），<c>TryStartCast</c> 检查 GCD/冷却是否就绪时读到的现在是"已经按
        /// 本次 <paramref name="dt"/> 推进过"的最新值，而不是调换前"尚未被本次 tick 推进"的旧值——这
        /// 让排队技能的就绪判定更及时（少算一次滞后），不存在把原本不就绪判成就绪、或反过来的错误方向；
        /// 学派锁定的推进（<see cref="CastPipeline.AdvanceSchoolLocks"/>）与读条/引导完成之间没有任何
        /// 数据依赖（<c>Interrupt</c> 写入学派锁定的两条路径——<c>OnAuraApplied</c>/<c>OnDamageDealt</c>
        /// ——只在 <c>DispatchPending</c> 批处理时触发，不在本方法内部同步发生），调换顺序不影响它。
        /// 见本模块新增测试对以上判断逐条钉住。
        /// </para>
        /// </summary>
        public void Update(double dt)
        {
            AdvanceRoundTimers(dt);
            _pipeline.Update(dt);
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
