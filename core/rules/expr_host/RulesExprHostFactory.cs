using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Rules.ExprHost
{
    /// <summary>
    /// <see cref="IExprHostFactory"/> 的组装期默认实现（见 <c>common/contracts/IExprHostFactory.cs</c>
    /// 判断记录"具体实现属于集成任务，本任务只声明接口"——本类型就是那个集成任务的落地）。把
    /// 04 第 6.2 节九个宿主引用分组接到 L1/L2 的具体契约上，供 skill（Proc/SpellMod 条件）、
    /// ai（Rotation/行为外壳转移条件）、targeting（<c>filters</c> 条件）共用同一份求值语义——
    /// 三个模块原先各自的临时 <c>IExprHostFactory</c> 占位（测试用 Fake）不受影响，本类型是它们在
    /// 生产组装（<c>core/rules/assembly</c>）里注入的真实实现。
    /// </summary>
    /// <remarks>
    /// <b>event.&lt;field&gt; 字段名映射</b>（P4-3 契约缺口最小修补）：<see cref="IExprReadableEvent"/>
    /// 登记的字段名是 camelCase（如 <c>"isCrit"</c>/<c>"sourceId"</c>），但
    /// <c>Core.Foundation.Expr.ExprLexer</c> 的标识符词法只接受全小写 <c>a-z0-9_</c>，内容侧按 04 第
    /// 2.1 节 Id 惯例只能写 <c>event.is_crit</c>/<c>event.source_id</c> 这种 snake_case 引用，两者原本
    /// 无法对上。<c>Host.QueryEvent</c> 现在按"先原样查找，查不到再转成 camelCase 重试一次"的顺序解析
    /// <c>event.&lt;field&gt;</c>：先保留原样查找是为了不破坏"字段名本就恰好不含下划线"（如
    /// <c>event.amount</c>）或调用方直接以 camelCase 传入的既有用法；转换只在原样查找失败时触发，不
    /// 改变 <see cref="IExprReadableEvent"/> 契约本身仍以 camelCase 登记字段这一事实。<b>内容作者一律按
    /// snake_case 书写 <c>event.&lt;field&gt;</c> 引用</b>（如 <c>event.is_crit</c>），这是本约定对内容
    /// 侧的唯一要求。
    /// </remarks>
    public sealed class RulesExprHostFactory : IExprHostFactory
    {
        /// <summary>缺目标/缺事件字段/未知 key 等场景下 Id 类型的占位默认值（见 04 第 6.3 节
        /// "引用对象暂缺按默认值处理"，任务书拍板"Id 类型返回 ExprValue.OfId(new Id("none.none"))
        /// 并记警告"）。</summary>
        public static readonly Id NoneId = new Id("none.none");

        /// <summary><c>enemies.nearest_distance</c> 在没有任何敌对单位时的返回值（任务书"无敌人
        /// 返回一个大数并记录约定"）：取一个足够大、但仍能安全参与四则运算与比较（不会像
        /// <see cref="double.MaxValue"/> 那样一乘就溢出为 <see cref="double.PositiveInfinity"/>）的
        /// 有限数——游戏口味配置的"感知/攻击距离"不可能达到这个量级，任何
        /// <c>enemies.nearest_distance &lt; N</c> 一类条件在无敌人时都会正确判定为假。</summary>
        public const double NoEnemyDistance = 1_000_000.0;

        /// <summary><c>enemies.*</c> 查询用的搜索半径上限：<see cref="ISpatialQuery"/> 只提供带半径
        /// 的范围查询，没有"查询全部"的方法，<c>nearest_distance</c> 需要一个近似"无限远"的半径
        /// 撒网再取最近——同样取一个远超任何真实游戏地图尺度、但不会导致下游数值运算异常的有限值。</summary>
        public const double UnboundedSearchRadius = 1_000_000.0;

        private readonly IUnitAccess _units;
        private readonly IStatHost _stats;
        private readonly IPowerHost _powers;
        private readonly IAuraQuery _auras;
        private readonly ICombatHost _combat;
        // 判断记录：IThreatTable 未在本类字段中单独持有——ICombatHost.GetThreatTable(unitId) 已经
        // 是取得某单位仇恨表的唯一入口（见 IThreatTable 契约与 CombatHost 实现，GetThreatTable
        // 忽略参数返回同一份共享表），构造参数里仍保留 threatTable 只是为了让调用方按任务书列出的
        // 依赖清单原样传入，内部统一改经 _combat.GetThreatTable 取得，不重复存一份引用造成两个
        // "威胁表来源"互相打架。
        private readonly ISpatialQuery _spatial;
        private readonly IFactionMatrix _factions;
        private readonly Func<double> _simTimeProvider;
        private readonly Func<Id, double> _combatStartTimeProvider;
        private readonly IReadOnlyDictionary<string, IExprGroupProvider> _extraGroups;
        private readonly IExprDiagnostics _diagnostics;

        // 判断记录（构造参数清单的一处偏差，任务书未列出）：self/target.is_casting、
        // combat.is_casting 语义上必须读 ISkillHost.IsCasting（06 第 3.6 节步骤 8"是否处于读条/
        // 引导"是 skill 模块的运行期状态，不属于 ICombatHost/IAuraQuery/IUnitAccess 任何一个），
        // 但任务书给出的构造注入清单（IUnitAccess/IStatHost/IPowerHost/IAuraQuery/ICombatHost/
        // IThreatTable/ISpatialQuery/IFactionMatrix/两个时间委托/extraGroups）没有列出 ISkillHost。
        // 这里作为清单之外的第 12 个可选构造参数补上：为 null 时 is_casting 一律按默认值 false
        // 处理并记一条警告（与"分组/字段缺失"同一惯例），不是编译期错误——组装根
        // （core/rules/assembly）会传入真实的 SkillHost，只有极简场景（如只需要 targeting.filters
        // 求值、不关心施法状态）才会留空。
        private readonly ISkillHost? _skillHost;

        /// <summary>解析期/默认值查找用的登记表：未注入 <c>extraSchemas</c> 时就是
        /// <see cref="RulesExprSchema.Base"/> 本身；注入时经 <see cref="RulesExprSchema.Compose"/>
        /// 与 <see cref="RulesExprSchema.Base"/> 合并（见构造函数 <c>extraSchemas</c> 参数、任务书
        /// "RulesExprHostFactory/RulesAssembly 接收可选 extraSchemas，组装成 CompositeExprSchema"）。
        /// <see cref="DefaultFor"/> 用它选取未知 key 的默认返回类型。</summary>
        private readonly IExprSchema _schema;

        private readonly HashSet<string> _warnedMissingGroups = new HashSet<string>(StringComparer.Ordinal);
        private bool _warnedMissingSkillHost;

        public RulesExprHostFactory(
            IUnitAccess units,
            IStatHost stats,
            IPowerHost powers,
            IAuraQuery auras,
            ICombatHost combat,
            IThreatTable threatTable,
            ISpatialQuery spatial,
            IFactionMatrix factions,
            Func<double> simTimeProvider,
            Func<Id, double> combatStartTimeProvider,
            IReadOnlyDictionary<string, IExprGroupProvider>? extraGroups = null,
            ISkillHost? skillHost = null,
            IExprDiagnostics? diagnostics = null,
            IReadOnlyList<IExprSchema>? extraSchemas = null)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _powers = powers ?? throw new ArgumentNullException(nameof(powers));
            _auras = auras ?? throw new ArgumentNullException(nameof(auras));
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            if (threatTable == null) throw new ArgumentNullException(nameof(threatTable));
            _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
            _factions = factions ?? throw new ArgumentNullException(nameof(factions));
            _simTimeProvider = simTimeProvider ?? throw new ArgumentNullException(nameof(simTimeProvider));
            _combatStartTimeProvider = combatStartTimeProvider ?? throw new ArgumentNullException(nameof(combatStartTimeProvider));
            _extraGroups = extraGroups ?? new Dictionary<string, IExprGroupProvider>(StringComparer.Ordinal);
            _skillHost = skillHost;
            _diagnostics = diagnostics ?? new ExprDiagnosticsRecorder();
            _schema = extraSchemas == null || extraSchemas.Count == 0
                ? RulesExprSchema.Base
                : RulesExprSchema.Compose(extraSchemas.ToArray());
        }

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) =>
            new Host(this, selfId, targetId, triggeringEvent);

        // -----------------------------------------------------------------
        // 默认值帮助方法
        // -----------------------------------------------------------------

        private static ExprValue DefaultForKind(ExprValueKind kind)
        {
            switch (kind)
            {
                case ExprValueKind.Bool: return ExprValue.OfBool(false);
                case ExprValueKind.Int: return ExprValue.OfInt(0);
                case ExprValueKind.Number: return ExprValue.OfNumber(0);
                case ExprValueKind.String: return ExprValue.OfString(string.Empty);
                case ExprValueKind.Id: return ExprValue.OfId(NoneId);
                default: return ExprValue.OfBool(false);
            }
        }

        /// <summary>按 <see cref="_schema"/>（<see cref="RulesExprSchema.Base"/> 或其与
        /// <c>extraSchemas</c> 的组合）登记的返回类型选取对应默认值；未登记的 group.key 组合落回
        /// Bool(false)——严格模式下（ADR-0015）"未登记"本就意味着这不是一个合法引用，运行期在这里
        /// 只是兜底给一个安全默认值，不代表该组合被判定为引用。</summary>
        private ExprValue DefaultFor(string group, string key)
        {
            return _schema.TryGetSignature(group, key, out var signature)
                ? DefaultForKind(signature.ReturnKind)
                : ExprValue.OfBool(false);
        }

        // -----------------------------------------------------------------
        // 绑定了具体 (selfId, targetId, triggeringEvent) 上下文的 IExprHost 实现
        // -----------------------------------------------------------------

        private sealed class Host : IExprHost
        {
            private readonly RulesExprHostFactory _f;
            private readonly Id _selfId;
            private readonly Id? _targetId;
            private readonly IEvent? _triggeringEvent;

            public Host(RulesExprHostFactory factory, Id selfId, Id? targetId, IEvent? triggeringEvent)
            {
                _f = factory;
                _selfId = selfId;
                _targetId = targetId;
                _triggeringEvent = triggeringEvent;
            }

            public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)
            {
                switch (group)
                {
                    case ExprGroups.Self:
                        return QueryUnit(_selfId, isSelfGroup: true, key, args);

                    case ExprGroups.Target:
                        if (!_targetId.HasValue)
                        {
                            _f._diagnostics.Warn(
                                $"target.{key}：当前求值上下文没有绑定目标（selfId={_selfId}），按默认值处理");
                            return _f.DefaultFor(group, key);
                        }
                        return QueryUnit(_targetId.Value, isSelfGroup: false, key, args);

                    case ExprGroups.Combat:
                        return QueryCombat(key);

                    case ExprGroups.Enemies:
                        return QueryEnemies(key, args);

                    case ExprGroups.Time:
                        return QueryTime(key);

                    case ExprGroups.Event:
                        return QueryEvent(key);

                    case ExprGroups.World:
                    case ExprGroups.Quest:
                    case ExprGroups.Player:
                        return QueryExtraGroup(group, key, args);

                    default:
                        _f._diagnostics.Warn($"未知的 Expr 宿主分组 \"{group}\"（{group}.{key}），按默认值处理");
                        return _f.DefaultFor(group, key);
                }
            }

            // -------------------------------------------------------------
            // self / target
            // -------------------------------------------------------------

            private ExprValue QueryUnit(Id unitId, bool isSelfGroup, string key, IReadOnlyList<ExprValue> args)
            {
                var group = isSelfGroup ? ExprGroups.Self : ExprGroups.Target;

                switch (key)
                {
                    case "hp":
                        return ExprValue.OfNumber(_f._powers.GetPower(unitId, WellKnownPowers.Health));

                    case "hp_max":
                        return ExprValue.OfNumber(_f._powers.GetPowerMax(unitId, WellKnownPowers.Health));

                    case "hp_pct":
                        return ExprValue.OfNumber(Pct(
                            _f._powers.GetPower(unitId, WellKnownPowers.Health),
                            _f._powers.GetPowerMax(unitId, WellKnownPowers.Health)));

                    case "power":
                    {
                        var powerType = args[0].AsId;
                        return ExprValue.OfNumber(_f._powers.GetPower(unitId, powerType));
                    }

                    case "power_pct":
                    {
                        var powerType = args[0].AsId;
                        return ExprValue.OfNumber(Pct(
                            _f._powers.GetPower(unitId, powerType),
                            _f._powers.GetPowerMax(unitId, powerType)));
                    }

                    case "level":
                        return ExprValue.OfInt(_f._units.GetLevel(unitId));

                    case "faction":
                        return ExprValue.OfId(_f._units.GetFaction(unitId));

                    case "is_alive":
                        return ExprValue.OfBool(_f._units.IsAlive(unitId));

                    case "in_combat":
                        return ExprValue.OfBool(_f._combat.IsInCombat(unitId));

                    case "is_casting":
                        return QueryIsCasting(unitId);

                    case "has_aura":
                        return ExprValue.OfBool(_f._auras.HasAura(unitId, args[0].AsId));

                    case "aura_stacks":
                        return ExprValue.OfInt(_f._auras.GetStacks(unitId, args[0].AsId));

                    case "stat":
                        return ExprValue.OfNumber(_f._stats.GetStat(unitId, args[0].AsId));

                    case "has_tag":
                        return ExprValue.OfBool(ContainsTag(_f._units.GetTags(unitId), args[0].AsId));

                    case "position_x":
                        return ExprValue.OfNumber(_f._units.GetPosition(unitId).X);

                    case "position_y":
                        return ExprValue.OfNumber(_f._units.GetPosition(unitId).Y);

                    case "distance_to_target" when isSelfGroup:
                        if (!_targetId.HasValue)
                        {
                            _f._diagnostics.Warn($"self.distance_to_target：没有绑定目标（selfId={_selfId}），按默认值处理");
                            return _f.DefaultFor(ExprGroups.Self, key);
                        }
                        return ExprValue.OfNumber(Vec2.Distance(_f._units.GetPosition(unitId), _f._units.GetPosition(_targetId.Value)));

                    case "threat_top" when isSelfGroup:
                    {
                        var top = _f._combat.GetThreatTable(unitId).GetTopThreat(unitId);
                        if (top.HasValue)
                        {
                            return ExprValue.OfId(top.Value);
                        }
                        _f._diagnostics.Warn($"self.threat_top：单位 \"{unitId}\" 当前没有仇恨记录，按默认值处理");
                        return _f.DefaultFor(ExprGroups.Self, key);
                    }

                    default:
                        // 覆盖两类情况：真正未知的 key，以及 target 分组下的 self 专用 key
                        // （distance_to_target/threat_top，见 RulesExprSchema 判断记录）。
                        _f._diagnostics.Warn($"未知的 {group}.{key} 引用，按默认值处理");
                        return _f.DefaultFor(group, key);
                }
            }

            private ExprValue QueryIsCasting(Id unitId)
            {
                if (_f._skillHost != null)
                {
                    return ExprValue.OfBool(_f._skillHost.IsCasting(unitId));
                }

                if (!_f._warnedMissingSkillHost)
                {
                    _f._diagnostics.Warn(
                        "is_casting 需要 ISkillHost，但 RulesExprHostFactory 构造时未注入，按默认值 false 处理（本条只警告一次）");
                    _f._warnedMissingSkillHost = true;
                }

                return ExprValue.OfBool(false);
            }

            // -------------------------------------------------------------
            // combat（自身，即 selfId）
            // -------------------------------------------------------------

            private ExprValue QueryCombat(string key)
            {
                switch (key)
                {
                    case "in_combat":
                        return ExprValue.OfBool(_f._combat.IsInCombat(_selfId));
                    case "is_casting":
                        return QueryIsCasting(_selfId);
                    default:
                        _f._diagnostics.Warn($"未知的 combat.{key} 引用，按默认值处理");
                        return _f.DefaultFor(ExprGroups.Combat, key);
                }
            }

            // -------------------------------------------------------------
            // enemies（相对 selfId 的敌对单位统计）
            // -------------------------------------------------------------

            private ExprValue QueryEnemies(string key, IReadOnlyList<ExprValue> args)
            {
                switch (key)
                {
                    case "count_in_range":
                    {
                        var radius = args.Count > 0 ? args[0].ToDouble() : 0.0;
                        return ExprValue.OfInt(CountHostilesInRange(radius));
                    }

                    case "nearest_distance":
                        return ExprValue.OfNumber(NearestHostileDistance());

                    default:
                        _f._diagnostics.Warn($"未知的 enemies.{key} 引用，按默认值处理");
                        return _f.DefaultFor(ExprGroups.Enemies, key);
                }
            }

            private int CountHostilesInRange(double radius)
            {
                var selfPos = _f._units.GetPosition(_selfId);
                var selfFaction = _f._units.GetFaction(_selfId);
                var candidates = _f._spatial.QueryRadius(selfPos, radius, QueryFilter.None);

                var count = 0;
                foreach (var id in candidates)
                {
                    if (IsHostileEnemy(id, selfFaction))
                    {
                        count++;
                    }
                }

                return count;
            }

            private double NearestHostileDistance()
            {
                var selfPos = _f._units.GetPosition(_selfId);
                var selfFaction = _f._units.GetFaction(_selfId);
                var candidates = _f._spatial.QueryRadius(selfPos, UnboundedSearchRadius, QueryFilter.None);

                var best = NoEnemyDistance;
                foreach (var id in candidates)
                {
                    if (!IsHostileEnemy(id, selfFaction))
                    {
                        continue;
                    }

                    var d = Vec2.Distance(selfPos, _f._units.GetPosition(id));
                    if (d < best)
                    {
                        best = d;
                    }
                }

                return best;
            }

            private bool IsHostileEnemy(Id candidateId, Id selfFaction)
            {
                if (candidateId.Equals(_selfId))
                {
                    return false;
                }

                if (!_f._units.Exists(candidateId) || !_f._units.IsAlive(candidateId))
                {
                    return false;
                }

                return _f._factions.IsHostile(selfFaction, _f._units.GetFaction(candidateId));
            }

            // -------------------------------------------------------------
            // time
            // -------------------------------------------------------------

            private ExprValue QueryTime(string key)
            {
                switch (key)
                {
                    case "sim_time":
                        return ExprValue.OfNumber(_f._simTimeProvider());
                    case "since_combat_start":
                        return ExprValue.OfNumber(_f._simTimeProvider() - _f._combatStartTimeProvider(_selfId));
                    case "day_cycle":
                        // 占位：昼夜循环不属于本任务范围（见任务书"占位"），恒返回 0。
                        return ExprValue.OfNumber(0);
                    case "turn_index":
                    case "round_index":
                        // 占位：离散（回合制）模式本项目暂不启用（ADR-0013），恒返回 0。
                        return ExprValue.OfInt(0);
                    case "is_my_turn":
                        // 占位：同上，离散模式暂不启用，恒返回 false。
                        return ExprValue.OfBool(false);
                    default:
                        _f._diagnostics.Warn($"未知的 time.{key} 引用，按默认值处理");
                        return _f.DefaultFor(ExprGroups.Time, key);
                }
            }

            // -------------------------------------------------------------
            // event
            // -------------------------------------------------------------

            private ExprValue QueryEvent(string key)
            {
                if (_triggeringEvent is IExprReadableEvent readable)
                {
                    // 判断记录（P4-3 契约缺口最小修补，见 RulesExprHostFactory 类型顶部
                    // "event.<field> 字段名映射"一节）：先按原样查找（兼容
                    // IExprReadableEvent.TryGetField 已经登记的任何字段名写法），查不到且 key 含
                    // 下划线（说明可能是内容侧按 04 惯例写的 snake_case，如 "is_crit"/"source_id"）
                    // 时再转成 camelCase 重试一次——ExprLexer 的标识符只接受全小写 a-z0-9_（见
                    // core/foundation/expr/core/ExprLexer.cs IsIdentBodyChar），content 里天然写不出
                    // IExprReadableEvent 登记的 camelCase 字段名（如 "isCrit"），本次转换正是补上
                    // 这一段命名映射，不改变 IExprReadableEvent 契约本身（仍以 camelCase 为登记名）。
                    if (readable.TryGetField(key, out var value))
                    {
                        return value;
                    }

                    var camelCaseKey = SnakeCaseToCamelCase(key);
                    if (!ReferenceEquals(camelCaseKey, key) && readable.TryGetField(camelCaseKey, out var convertedValue))
                    {
                        return convertedValue;
                    }
                }

                _f._diagnostics.Warn(
                    _triggeringEvent == null
                        ? $"event.{key}：本次求值没有绑定触发事件，按默认值处理"
                        : $"event.{key}：触发事件 \"{_triggeringEvent.Key}\" 不携带该字段（原样与 camelCase 转换均未命中），按默认值处理");

                // 判断记录：event.<field> 的具体类型随事件而异，RulesExprSchema（严格模式）对 event
                // 分组不逐字段登记类型，因此这里统一用 Bool(false) 作为缺失时的默认值——DefaultFor
                // 在没有精确签名时本就会退化为 Bool(false)，直接复用同一约定。
                return _f.DefaultFor(ExprGroups.Event, key);
            }

            // -------------------------------------------------------------
            // world / quest / player：委托给调用方注入的 IExprGroupProvider
            // -------------------------------------------------------------

            private ExprValue QueryExtraGroup(string group, string key, IReadOnlyList<ExprValue> args)
            {
                if (_f._extraGroups.TryGetValue(group, out var provider))
                {
                    return provider.Query(key, args);
                }

                if (_f._warnedMissingGroups.Add(group))
                {
                    _f._diagnostics.Warn(
                        $"分组 \"{group}\" 未注入 IExprGroupProvider（L4/游戏层未接入），本分组下全部查询按默认值处理（本条只警告一次）");
                }

                return _f.DefaultFor(group, key);
            }

            /// <summary>把 snake_case 字符串转成 camelCase（"is_crit" -&gt; "isCrit"，"source_id" -&gt;
            /// "sourceId"）；不含下划线时原样返回同一个字符串引用（<see cref="QueryEvent"/> 用
            /// <see cref="ReferenceEquals"/> 判断"是否发生了转换"，避免转换结果恰好等于原值时的
            /// 二次查找）。</summary>
            private static string SnakeCaseToCamelCase(string snakeCase)
            {
                if (string.IsNullOrEmpty(snakeCase) || snakeCase.IndexOf('_') < 0)
                {
                    return snakeCase;
                }

                var parts = snakeCase.Split('_');
                var sb = new System.Text.StringBuilder(snakeCase.Length);
                var isFirstSegment = true;
                for (var i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (part.Length == 0)
                    {
                        continue;
                    }

                    if (isFirstSegment)
                    {
                        sb.Append(part);
                        isFirstSegment = false;
                    }
                    else
                    {
                        sb.Append(char.ToUpperInvariant(part[0]));
                        if (part.Length > 1)
                        {
                            sb.Append(part, 1, part.Length - 1);
                        }
                    }
                }

                return sb.ToString();
            }

            private static double Pct(double current, double max) => max > 0 ? current / max : 0.0;

            private static bool ContainsTag(IReadOnlyList<Id> tags, Id tag)
            {
                for (var i = 0; i < tags.Count; i++)
                {
                    if (tags[i].Equals(tag))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
