using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;

namespace Core.Numbers.StatBlock
{
    /// <summary>
    /// <see cref="IStatHost"/> 默认实现（见 06_规则层_属性技能战斗AI.md 第 1 节）。构造时从
    /// <c>stat.definition</c> 读取全部属性定义（表不存在直接抛异常；表存在但没有校验通过则
    /// <see cref="IDataRegistryView"/> 自身的 <c>Get*</c> 方法会抛 <see cref="InvalidOperationException"/>，
    /// 见 04 第 4 节"报告含错误项即视为不可进入运行时"）；评级换算启用时另外读取
    /// <c>stat.rating_conversion</c>（表不存在则视为空曲线集合，换算时退化为直通，不阻断构造——
    /// 只有 <c>stat.definition</c> 是本模块的强依赖，见判断记录）。
    /// <para>
    /// 三段式聚合与浮点确定性（06 第 1.1 节、11 第 5 节"避免每 tick 产生大量临时分配"、
    /// 落地方案.md 第 4.1 节"同类项按声明顺序累加"）：<c>flat</c>/<c>pct</c> 按
    /// <see cref="AddModifier"/> 调用顺序（即 <see cref="List{T}"/> 的插入顺序）累加；
    /// <c>mult</c> 独立乘区先按同一 <see cref="StatModifier.MultGroup"/> 内的插入顺序相加，
    /// 各乘区再按"首次在该属性的修正列表中出现"的顺序连乘——同一组插入顺序两次运行必然产生
    /// 逐位相等的结果（见 tests 确定性用例）。
    /// </para>
    /// <para>
    /// 缓存与事件（06 第 1.3 节"事件"）：每属性缓存最终值，<see cref="SetBase"/>/
    /// <see cref="AddModifier"/>/<see cref="RemoveModifiersBySource"/> 只重算受影响的属性
    /// （不重算全部），并在变更前后各算一次最终值——两次结果不同（按 <c>double</c> 精确比较）
    /// 才 <see cref="IEventBus.Enqueue"/> 一次 <see cref="StatChangedEvent"/>；相同（含
    /// <see cref="SetBase"/> 设成同一个值）则不发。<see cref="GetStat"/> 命中缓存直接返回，
    /// 未命中（该单位该属性自注册以来从未被任何变更路径算过）才现算一次并写入缓存，不发事件
    /// （只读查询不产生变化）。
    /// </para>
    /// <para>
    /// 两轮拓扑序聚合（T-N1-2，ADR-0030 决策 2；06 第 1.1 节修订段）：<c>stat.definition.category
    /// == "derived"</c> 的属性走"第二轮"——基础值 = Σ(<c>derived_from</c> 来源属性最终值 × 系数)，
    /// 其余属性（第一轮）沿用既有 <c>unit.Base</c>/<c>default_base</c> 规则；两轮共用同一套三段式
    /// 公式与 <c>clamp</c> 夹取（见 <see cref="ComputeFinal"/>）。加载期按 <see cref="BuildDerivationGraph"/>
    /// 建派生图与稳定拓扑序、检测环（成环抛 <see cref="InvalidOperationException"/>，防御，正常数据
    /// 应已被 <c>StatDefinitionDerivationCycleValidationRule</c> 内容校验拦下）。上一段"只重算受影响
    /// 的属性"在派生场景下的完整含义：<see cref="SetBase"/>/<see cref="AddModifier"/>/
    /// <see cref="RemoveModifiersBySource"/>/<see cref="ResetBase"/> 之后，除了重算被直接改动的那个
    /// 属性本身，还经 <see cref="PropagateDerivedInvalidation"/> 按拓扑序把失效传播给全部（传递）
    /// 依赖它的、且此前已被缓存过的派生属性，逐个比较新旧值决定是否补发 <see cref="StatChangedEvent"/>。
    /// </para>
    /// </summary>
    public sealed class StatHost : IStatHost
    {
        private readonly IEventBus _bus;
        private readonly StatHostOptions _options;

        private readonly Dictionary<Id, StatDefinition> _definitions = new Dictionary<Id, StatDefinition>();
        private readonly Dictionary<Id, RatingConversion> _ratingConversions = new Dictionary<Id, RatingConversion>();
        private readonly Dictionary<Id, UnitStats> _units = new Dictionary<Id, UnitStats>();
        private readonly List<string> _warnings = new List<string>();

        /// <summary>T-N1-2（ADR-0030 决策 2）：全部属性 id 的稳定拓扑序（派生来源排在其派生属性
        /// 之前），由 <see cref="BuildDerivationGraph"/> 在每次 <see cref="ReloadFromRegistry"/>
        /// 时重建。用于 <see cref="RecomputeAllCachedStatsAfterReload"/>/
        /// <see cref="PropagateDerivedInvalidation"/> 按正确顺序批量重算——同层多个零入度候选按
        /// <see cref="Id"/> 的序数字符串序打破平局（禁止事项：不得依赖字典枚举顺序）。</summary>
        private List<Id> _topoOrder = new List<Id>();

        /// <summary>T-N1-2：反向依赖表——<c>source → 以 source 为 derived_from 直接来源之一的属性
        /// 列表</c>，由 <see cref="BuildDerivationGraph"/> 重建。<see cref="PropagateDerivedInvalidation"/>
        /// 用它做"从变化的来源出发，收集全部（传递）依赖它的派生属性"这一步。</summary>
        private readonly Dictionary<Id, List<Id>> _derivedDependents = new Dictionary<Id, List<Id>>();

        /// <summary>抗性维度关闭时，<see cref="GetStat"/> 命中 <c>group: resistance</c> 属性
        /// 会在这里记一条警告（见 <see cref="StatHostOptions.EnableResistanceGroup"/>）。供测试与
        /// 宿主诊断读取，本模块不依赖任何 L-1 引擎适配层的日志出口（同 event_bus 的
        /// <c>IEventDiagnostics</c> 设计取舍：不强制调用方先备好引擎适配层实现）。</summary>
        public IReadOnlyList<string> Warnings => _warnings;

        private readonly IDataRegistryView _registry;

        public StatHost(IDataRegistryView registry, IEventBus bus, StatHostOptions? options = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? new StatHostOptions();

            ReloadFromRegistry();

            // P2-05 同类缓存收口（外部审计 audit-c9ff301-20260909 followup-2026-09-10）：
            // _definitions/_ratingConversions 此前只在构造期从 registry 读取一次、永久常驻，
            // 与 SkillDefCache/ArchetypeRegistry 同一类"构造期一次性读 registry 建索引、之后
            // 只读"模式。本类型本就持有 registry 引用（原先只作为局部参数传给 Load* 方法，
            // 现改存字段），直接内部订阅、自行重新查询。
            //
            // CORE114-03 根治（外部审计 audit-76d16a5-20260910）：上一段判断记录此前还有一句
            // "_units 不受影响，reload 只刷新定义表本身，不倒退已经算过的属性缓存"——这句话是错的，
            // 已删除：<see cref="UnitStats.Cache"/>（<see cref="GetStat"/> 的派生值缓存）不是运行期
            // 状态，它是"上一次用某份定义算出的结果"，定义变了缓存就必须失效，和 <see cref="_units"/>
            // 里真正的运行态（<see cref="UnitStats.Base"/> 显式覆盖值、<see
            // cref="UnitStats.ModifiersByStat"/> 修正列表）不是一回事——真实审计复现：两个相同、都未
            // 显式 <see cref="SetBase"/> 过的单位，reload 前只查询过其中一个（缓存下 0），reload
            // <c>default_base</c> 为 77 后，先查询过的那个仍返回缓存里的旧值 0，另一个第一次查询直接
            // 现算得到 77——同一条规则下两个未修改过的单位就因为"查询顺序"分叉出不同结果
            // （STAT-QUERY-ORDER beforeA=0;afterA=0;afterB=77）。见 <see
            // cref="RecomputeAllCachedStatsAfterReload"/> 判断记录。
            _bus.Subscribe<Core.Foundation.DataRegistry.DataLoadCompletedEvent>(
                Core.Foundation.DataRegistry.DataRegistryEventKeys.LoadCompleted, _ => ReloadFromRegistry());
        }

        private void ReloadFromRegistry()
        {
            _definitions.Clear();
            _ratingConversions.Clear();
            LoadDefinitions(_registry);

            // T-N1-2（ADR-0030 决策 2）：派生图必须在 LoadDefinitions 填满 _definitions 之后、
            // RecomputeAllCachedStatsAfterReload 之前重建——后者按拓扑序重算缓存，依赖
            // BuildDerivationGraph 产出的 _topoOrder/_derivedDependents 已经反映本次 reload 之后的
            // 定义（新增/删除属性、derived_from 关系变化）。
            BuildDerivationGraph();

            if (_options.EnableRatingConversion)
            {
                LoadRatingConversions(_registry);
            }

            RecomputeAllCachedStatsAfterReload();
        }

        /// <summary>
        /// CORE114-03 根治（外部审计 audit-76d16a5-20260910）：<see cref="ReloadFromRegistry"/>
        /// 替换 <see cref="_definitions"/> 后，对每个已注册单位、每条此前已经被 <see
        /// cref="GetStat"/> 现算并写入过 <see cref="UnitStats.Cache"/> 的属性，用新定义立即重算一遍
        /// ——不等下一次 <see cref="GetStat"/> 调用才发现缓存是旧的（构造期首次调用时 <see
        /// cref="_units"/> 必为空，天然 no-op，不需要额外判空）。
        /// <para>
        /// 判断记录（只重算"已经算过"的属性，不主动补算全部已注册属性）：<see cref="GetStat"/> 对
        /// "该单位该属性自注册以来从未被任何变更路径算过"的属性本就不发 <see cref="StatChangedEvent"/>
        /// （只读查询不产生变化，见类型顶部注释），这里保持同一条口径——缓存里没有的属性表示"还没有
        /// 任何人关心过这个值"，不需要为它们提前触发一次事件；它们下一次被 <see cref="GetStat"/>
        /// 查询时自然会用（已经是新的）<see cref="_definitions"/> 现算，不存在分叉风险。
        /// </para>
        /// <para>
        /// 判断记录（显式 base 保留、派生缓存失效是两件事）：只重写 <see cref="UnitStats.Cache"/>，
        /// 不触碰 <see cref="UnitStats.Base"/>——显式 <see cref="SetBase"/> 过的值是调用方主动设定的
        /// 运行期状态，定义表 <c>default_base</c> 变化不应该覆盖它（<see cref="ComputeFinal"/> 本就
        /// 优先取 <c>unit.Base</c>，重算只是让"这份显式 base + 新定义的 min/max/modifiers 组合"重新
        /// 生效，不是重置显式值）。
        /// </para>
        /// <para>
        /// 判断记录（定义被移除：清除缓存、不发事件）：<see cref="_definitions"/> 里已经没有的属性
        /// key，说明这条属性定义已被整表 reload 删除——<see cref="RequireDefinition"/> 之后任何按
        /// 该 key 的查询都会抛 <see cref="ArgumentException"/>（既有契约，不在本次根治范围内），
        /// 继续把一个指向已消失定义的值留在缓存里没有意义，直接移除；不发 <see
        /// cref="StatChangedEvent"/>——这条属性已经不存在，没有"新值"可供下游消费。
        /// </para>
        /// <para>
        /// 判断记录（T-N1-2 追加，两轮拓扑序聚合）：<see cref="ComputeFinal"/> 对
        /// <c>category=derived</c> 的属性，其"基础值"经 <see cref="ResolveFinal"/> 读取来源属性的
        /// <em>当前</em> <see cref="UnitStats.Cache"/> 值——如果仍按 <c>cachedStats</c> 的原始字典
        /// 枚举顺序重算，一个"派生属性排在它的来源前面"的枚举顺序会让派生属性用到 reload 前的旧
        /// 来源值。因此本方法改为按 <see cref="_topoOrder"/>（来源恒先于派生）过滤后依次重算，
        /// 不再直接遍历 <c>unit.Cache.Keys</c> 的原始顺序（禁止事项：不得依赖字典枚举顺序）。
        /// </para>
        /// </summary>
        private void RecomputeAllCachedStatsAfterReload()
        {
            foreach (var unitEntry in _units)
            {
                var unitId = unitEntry.Key;
                var unit = unitEntry.Value;

                // 第一遍：按原始（已失效）缓存 key 集合清理"定义已被整表 reload 删除"的条目——
                // 这一步不依赖拓扑序，且必须先做，否则第二遍找不到这些 key 对应的 def。
                var cachedStats = new List<Id>(unit.Cache.Keys);
                var stillPresent = new HashSet<Id>();
                foreach (var stat in cachedStats)
                {
                    if (!_definitions.ContainsKey(stat))
                    {
                        unit.Cache.Remove(stat);
                        continue;
                    }
                    stillPresent.Add(stat);
                }

                // 第二遍：按拓扑序重算仍存在定义、且此前已经被缓存过的属性（同上一段判断记录，
                // 只重算"已经算过的"，不主动补算全部已注册属性）。
                for (int i = 0; i < _topoOrder.Count; i++)
                {
                    var stat = _topoOrder[i];
                    if (!stillPresent.Contains(stat))
                    {
                        continue;
                    }

                    var def = _definitions[stat];
                    var oldValue = unit.Cache[stat];
                    var newValue = ComputeFinal(unitId, unit, def);
                    unit.Cache[stat] = newValue;

                    if (newValue != oldValue)
                    {
                        _bus.Enqueue(new StatChangedEvent(unitId, stat, oldValue, newValue));
                    }
                }
            }
        }

        // -----------------------------------------------------------------
        // 构造期加载
        // -----------------------------------------------------------------

        private void LoadDefinitions(IDataRegistryView registry)
        {
            var hasTable = false;
            var tables = registry.Tables;
            for (int i = 0; i < tables.Count; i++)
            {
                if (tables[i] == "stat.definition") { hasTable = true; break; }
            }
            if (!hasTable)
            {
                throw new InvalidOperationException(
                    "StatHost 需要 \"stat.definition\" 表（见 06 第 1.3 节数据表），但数据注册表中未加载该表");
            }

            var records = registry.GetAll("stat.definition");
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var id = record.GetId("id");

                // T-N1-2（ADR-0030 决策 1；拍板 1）：改读 category，不再读 group——v1 数据经
                // 1→2 迁移链已无条件补齐 category（见 StatSchemas.MigrateDefinitionV1ToV2），
                // schema 版本 2 起 category 本就 required:true，这里用 GetString（非 TryGetString）
                // 无条件读取，风格与此前读 group 一致。
                var category = record.GetString("category");

                double defaultBase = 0;
                record.TryGetNumber("default_base", out defaultBase);

                // T-N1-2（拍板 2）：改读嵌套 clamp.min/clamp.max，不再读平级 min/max——v1 数据经
                // 迁移链已把平级 min/max 嵌套进 clamp（仅当至少一个存在时才产生 clamp 对象，见
                // MigrateDefinitionV1ToV2 判断记录）。
                double? min = null;
                double? max = null;
                if (record.TryGetObject("clamp", out var clampObj))
                {
                    if (clampObj.TryGetValue("min", out var minRaw) && minRaw is JsonNumber minNum) min = minNum.Value;
                    if (clampObj.TryGetValue("max", out var maxRaw) && maxRaw is JsonNumber maxNum) max = maxNum.Value;
                }

                // is_rating/rating_conversion_ref 读取逻辑本任务不改（换算层触发条件改
                // category==percent 是 T-N1-3 的范围，见分阶段落地计划 T-N1-3 行）。
                var isRating = record.TryGetBool("is_rating", out var isRatingValue) && isRatingValue;

                Id? ratingRef = null;
                if (record.TryGetId("rating_conversion_ref", out var refValue)) ratingRef = refValue;

                // T-N1-2（ADR-0030 决策 2；拍板 11）：derived_from 仅在 category=derived 时才纳入
                // 派生计算图——非 derived 记录即使（不合法地）带了 derived_from，也已由
                // StatDefinitionValidationRule.CheckDerivedFromRequiresDerivedCategory 在内容校验
                // 阶段拦下，这里的防御姿态是直接忽略，不参与图构建/聚合（不重复发明校验）。
                IReadOnlyList<(Id Stat, double Coefficient)> derivedFrom = Array.Empty<(Id Stat, double Coefficient)>();
                if (category == "derived" && record.TryGetArray("derived_from", out var derivedFromArray))
                {
                    var list = new List<(Id Stat, double Coefficient)>(derivedFromArray.Count);
                    for (int d = 0; d < derivedFromArray.Count; d++)
                    {
                        if (derivedFromArray[d] is JsonObject entryObj
                            && entryObj.TryGetValue("stat", out var statRaw) && statRaw is JsonString statStr
                            && Id.TryParse(statStr.Value, out var sourceId)
                            && entryObj.TryGetValue("coefficient", out var coeffRaw) && coeffRaw is JsonNumber coeffNum)
                        {
                            list.Add((sourceId, coeffNum.Value));
                        }
                        // 元素形状不符（缺字段/类型不对）已由 DerivedFromEntrySchema 的
                        // required_field/field_type 报过，这里静默跳过，不重复报——同
                        // CurveMonotonicFiniteRule"形态不符不重复报"判断记录同一口径。
                    }
                    derivedFrom = list;
                }

                _definitions[id] = new StatDefinition(id, category, defaultBase, min, max, isRating, ratingRef, derivedFrom);
            }
        }

        /// <summary>
        /// T-N1-2（ADR-0030 决策 2）：加载期建派生图——反向依赖表 <see cref="_derivedDependents"/>
        /// （source → 以它为直接来源的派生属性列表）与全部属性 id 的稳定拓扑序
        /// <see cref="_topoOrder"/>（Kahn 算法，来源恒排在派生之前）。
        /// <para>
        /// 判断记录（稳定性）：候选零入度集合用 <see cref="SortedSet{T}"/>（<see cref="Id"/> 已实现
        /// <see cref="IComparable{T}"/>，按序数字符串序比较），每一步取当前最小 id 出队——这保证"同一
        /// 输入不同内容登记顺序"（即 <c>stat.definition.json</c> 里几行的先后顺序、或
        /// <see cref="IDataRegistryView.GetAll"/> 返回顺序变化）不改变最终拓扑序（禁止事项：不得依赖
        /// 字典枚举顺序），也不改变任何 <see cref="ComputeFinal"/> 算出的数值本身——各属性的最终值只由
        /// 图结构与各自的 <see cref="StatDefinition.DerivedFrom"/> 系数决定，拓扑序只影响"重算一批
        /// 属性时按什么顺序处理"，不参与任何单个属性自己的三段式公式。
        /// </para>
        /// <para>
        /// 判断记录（环检测是防御，不是替代校验）：正常数据流程下，<c>derived_from</c> 成环应该已经
        /// 被 <c>StatDefinitionDerivationCycleValidationRule</c>（内容校验阶段）拦下，不会进入这里；
        /// 本方法遇到环仍抛 <see cref="InvalidOperationException"/>（04 第 4 节"报告含错误项即视为
        /// 不可进入运行时"的运行时兜底一侧），未知来源 id（引用不存在的属性）不建边——那属于
        /// <c>reference_integrity</c> 校验的职责，这里只做"不让不完整的图污染拓扑排序"的防御，
        /// <see cref="ResolveFinal"/> 真正用到未知来源时会用另一条异常报告（同 <see cref="RequireDefinition"/>
        /// 的既有报错风格）。
        /// </para>
        /// </summary>
        private void BuildDerivationGraph()
        {
            _derivedDependents.Clear();

            var allIds = new List<Id>(_definitions.Keys);
            allIds.Sort();

            var indegree = new Dictionary<Id, int>(allIds.Count);
            foreach (var id in allIds)
            {
                indegree[id] = 0;
            }

            // (source, target) 去重：同一来源在同一属性的 derived_from 里重复登记两次（内容作者
            // 手误）不应该让入度被多计一次，否则该属性会被错误地判定为"还有未满足的依赖"。
            var seenEdges = new HashSet<(Id Source, Id Target)>();
            foreach (var id in allIds)
            {
                var def = _definitions[id];
                if (def.Category != "derived" || def.DerivedFrom.Count == 0)
                {
                    continue;
                }

                for (int i = 0; i < def.DerivedFrom.Count; i++)
                {
                    var sourceId = def.DerivedFrom[i].Stat;
                    if (!_definitions.ContainsKey(sourceId))
                    {
                        continue; // 未知来源：见方法判断记录，交给 reference_integrity 校验。
                    }

                    if (seenEdges.Add((sourceId, id)))
                    {
                        indegree[id]++;
                        if (!_derivedDependents.TryGetValue(sourceId, out var dependents))
                        {
                            dependents = new List<Id>();
                            _derivedDependents[sourceId] = dependents;
                        }
                        dependents.Add(id);
                    }
                }
            }

            var available = new SortedSet<Id>();
            foreach (var id in allIds)
            {
                if (indegree[id] == 0) available.Add(id);
            }

            var order = new List<Id>(allIds.Count);
            while (available.Count > 0)
            {
                var next = available.Min;
                available.Remove(next);
                order.Add(next);

                if (_derivedDependents.TryGetValue(next, out var dependents))
                {
                    for (int i = 0; i < dependents.Count; i++)
                    {
                        var dependent = dependents[i];
                        indegree[dependent]--;
                        if (indegree[dependent] == 0)
                        {
                            available.Add(dependent);
                        }
                    }
                }
            }

            if (order.Count != allIds.Count)
            {
                var cyclic = new List<string>();
                foreach (var id in allIds)
                {
                    if (indegree[id] > 0) cyclic.Add(id.Value);
                }
                cyclic.Sort(StringComparer.Ordinal);
                throw new InvalidOperationException(
                    "stat.definition 的 derived_from 派生关系存在环，涉及属性：" + string.Join(", ", cyclic) +
                    "（内容校验应已由 StatDefinitionDerivationCycleValidationRule 拦下，这里是加载期防御）");
            }

            _topoOrder = order;
        }

        private void LoadRatingConversions(IDataRegistryView registry)
        {
            var tables = registry.Tables;
            var hasTable = false;
            for (int i = 0; i < tables.Count; i++)
            {
                if (tables[i] == "stat.rating_conversion") { hasTable = true; break; }
            }
            if (!hasTable)
            {
                // 评级换算启用但没有任何曲线数据：不阻断构造，换算时对没有引用到曲线的属性
                // 直通原值（见 ConvertRating 判断记录）。
                return;
            }

            var records = registry.GetAll("stat.rating_conversion");
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var id = record.GetId("id");
                _ratingConversions[id] = new RatingConversion(id, ParseCurve(record, id));
            }
        }

        /// <summary>T-N0-4：解析 <c>entries</c> 为 <see cref="PiecewiseCurve"/>（x = 单位等级，y = 每 1% 所需
        /// 点数）。经 <c>DataRegistry</c> 加载的记录已由 1→2 迁移改名为 <c>{x, y}</c>；未经迁移直接构造的
        /// 记录仍接受 v1 的 <c>{level, points_per_percent}</c>（禁止删除旧字段读取路径）。缺字段/类型不对
        /// 抛 <see cref="InvalidOperationException"/>（沿本方法既有异常类型：内容数据的结构性错误）。</summary>
        private static PiecewiseCurve ParseCurve(DataRecord record, Id id)
        {
            var entriesArray = record.GetArray("entries");
            var points = new CurvePoint[entriesArray.Count];
            for (int e = 0; e < entriesArray.Count; e++)
            {
                if (!(entriesArray[e] is JsonObject entryObj))
                {
                    throw new InvalidOperationException(
                        $"stat.rating_conversion[{id}].entries[{e}] 不是对象");
                }

                double x, y;
                if (TryReadInt(entryObj, CurveSchema.XFieldName, out var xLevel) && TryReadNumber(entryObj, CurveSchema.YFieldName, out y))
                {
                    x = xLevel;
                }
                else if (TryReadInt(entryObj, "level", out var legacyLevel) && TryReadNumber(entryObj, "points_per_percent", out y))
                {
                    x = legacyLevel;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"stat.rating_conversion[{id}].entries[{e}] 缺少合法的 \"x\"（Int）/\"y\"（Number）（或 v1 的 \"level\"/\"points_per_percent\"）");
                }

                points[e] = new CurvePoint(x, y);
            }

            return new PiecewiseCurve(points);
        }

        private static bool TryReadInt(JsonObject obj, string key, out long value)
        {
            if (obj.TryGetValue(key, out var raw) && raw is JsonNumber num && num.TryGetInt64(out value))
            {
                return true;
            }
            value = 0;
            return false;
        }

        private static bool TryReadNumber(JsonObject obj, string key, out double value)
        {
            if (obj.TryGetValue(key, out var raw) && raw is JsonNumber num)
            {
                value = num.Value;
                return true;
            }
            value = 0;
            return false;
        }

        // -----------------------------------------------------------------
        // 单位注册
        // -----------------------------------------------------------------

        public void RegisterUnit(Id unitId)
        {
            if (_units.ContainsKey(unitId))
            {
                throw new InvalidOperationException($"单位 \"{unitId}\" 已经通过 RegisterUnit 注册过");
            }
            _units[unitId] = new UnitStats();
        }

        public void UnregisterUnit(Id unitId)
        {
            _units.Remove(unitId);
        }

        public bool IsRegistered(Id unitId) => _units.ContainsKey(unitId);

        // -----------------------------------------------------------------
        // 读写
        // -----------------------------------------------------------------

        public void SetBase(Id unitId, Id stat, double value)
        {
            var unit = RequireUnit(unitId);
            var def = RequireDefinition(stat);

            var oldValue = ComputeFinal(unitId, unit, def);
            unit.Base[stat] = value;
            var newValue = ComputeFinal(unitId, unit, def);
            unit.Cache[stat] = newValue;

            if (newValue != oldValue)
            {
                _bus.Enqueue(new StatChangedEvent(unitId, stat, oldValue, newValue));
            }

            PropagateDerivedInvalidation(unitId, unit, stat);
        }

        public double GetBase(Id unitId, Id stat)
        {
            var unit = RequireUnit(unitId);
            var def = RequireDefinition(stat);
            return unit.Base.TryGetValue(stat, out var value) ? value : def.DefaultBase;
        }

        /// <summary>
        /// CORE-110-02 根治（architecture/落地计划/audit-ac3b622-20260909，P2，已确认）：清除某单位
        /// 某属性此前显式 <see cref="SetBase"/> 过的值，恢复成"从未显式设置过"的状态——恢复后
        /// <see cref="GetBase"/>/<see cref="GetStat"/> 重新退回 <c>stat.definition.default_base</c>。
        /// 供 <c>Core.Rules.Assembly.RulesAssembly.ReloadArchetypeAndRace</c> 在同图换职业时清理
        /// "旧职业声明过、新职业未声明"的基础属性键——真实探针复现：换职业只对新旧职业共同声明的
        /// 键调用 <see cref="SetBase"/>（覆盖写入），旧职业独有的键从未被任何调用触碰，永久残留旧
        /// 职业的基础值（见该方法判断记录）。
        /// <para>
        /// 判断记录（用"移除显式值"而不是"SetBase 成 default_base"）：<see cref="StatHost"/> 没有区分
        /// "显式设成 default_base"与"从未显式设置过"两种状态的必要——二者对 <see cref="GetBase"/>/
        /// <see cref="GetStat"/> 的返回值完全等价——但移除字典条目比重新查一遍 default_base 再写回更
        /// 直接，语义上也更贴合"这个键的显式来源（旧职业）已经不再存在"，与 <see
        /// cref="RemoveModifiersBySource"/>"按来源整体撤销"的既有惯例一致（只是 base 值只有唯一
        /// 一份、没有多来源叠加，撤销即直接移除）。
        /// </para>
        /// </summary>
        public void ResetBase(Id unitId, Id stat)
        {
            var unit = RequireUnit(unitId);
            var def = RequireDefinition(stat);

            if (!unit.Base.ContainsKey(stat))
            {
                // 幂等 no-op：该属性本就没有被显式 SetBase 过（已经等于 default_base），不需要
                // 移除任何东西，也不应该因为"重算一次"就发出一条值未变化的 StatChanged。
                return;
            }

            var oldValue = ComputeFinal(unitId, unit, def);
            unit.Base.Remove(stat);
            var newValue = ComputeFinal(unitId, unit, def);
            unit.Cache[stat] = newValue;

            if (newValue != oldValue)
            {
                _bus.Enqueue(new StatChangedEvent(unitId, stat, oldValue, newValue));
            }

            PropagateDerivedInvalidation(unitId, unit, stat);
        }

        public double GetStat(Id unitId, Id stat)
        {
            var unit = RequireUnit(unitId);
            var def = RequireDefinition(stat);

            if (unit.Cache.TryGetValue(stat, out var cached))
            {
                return cached;
            }

            var value = ComputeFinal(unitId, unit, def);
            unit.Cache[stat] = value;
            return value;
        }

        public void AddModifier(Id unitId, StatModifier modifier)
        {
            var unit = RequireUnit(unitId);
            var def = RequireDefinition(modifier.Stat);

            var oldValue = ComputeFinal(unitId, unit, def);

            if (!unit.ModifiersByStat.TryGetValue(modifier.Stat, out var list))
            {
                list = new List<StatModifier>();
                unit.ModifiersByStat[modifier.Stat] = list;
            }
            list.Add(modifier);

            var newValue = ComputeFinal(unitId, unit, def);
            unit.Cache[modifier.Stat] = newValue;

            if (newValue != oldValue)
            {
                _bus.Enqueue(new StatChangedEvent(unitId, modifier.Stat, oldValue, newValue));
            }

            PropagateDerivedInvalidation(unitId, unit, modifier.Stat);
        }

        public void RemoveModifiersBySource(Id unitId, Id sourceId)
        {
            var unit = RequireUnit(unitId);

            // 快照受影响属性的 key 集合：RemoveAll 在遍历过程中修改的是各属性自己的修正列表，
            // 不是 ModifiersByStat 字典本身，这里不需要防御字典结构变化，只是避免在同一次
            // foreach 里既读又写字典的心智负担。
            var stats = new List<Id>(unit.ModifiersByStat.Keys);
            for (int i = 0; i < stats.Count; i++)
            {
                var stat = stats[i];
                var list = unit.ModifiersByStat[stat];

                var hasSource = false;
                for (int m = 0; m < list.Count; m++)
                {
                    if (list[m].SourceId == sourceId) { hasSource = true; break; }
                }
                if (!hasSource) continue;

                var def = _definitions[stat];
                var oldValue = ComputeFinal(unitId, unit, def);

                list.RemoveAll(mod => mod.SourceId == sourceId);

                var newValue = ComputeFinal(unitId, unit, def);
                unit.Cache[stat] = newValue;

                if (newValue != oldValue)
                {
                    _bus.Enqueue(new StatChangedEvent(unitId, stat, oldValue, newValue));
                }

                PropagateDerivedInvalidation(unitId, unit, stat);
            }
        }

        public IReadOnlyList<StatModifier> GetModifiers(Id unitId, Id stat)
        {
            var unit = RequireUnit(unitId);
            RequireDefinition(stat);
            return unit.ModifiersByStat.TryGetValue(stat, out var list) ? list.ToArray() : Array.Empty<StatModifier>();
        }

        /// <summary>
        /// RC-06 收边补齐：单位等级变化后，重算并按需广播全部经评级曲线换算（<see
        /// cref="StatDefinition.RatingConversionRef"/> 非空，见 <see cref="ConvertRating"/>）的属性。
        /// <para>
        /// 判断记录：<see cref="GetStat"/> 的缓存（<see cref="UnitStats.Cache"/>）只在
        /// <see cref="SetBase"/>/<see cref="AddModifier"/>/<see cref="RemoveModifiersBySource"/> 三个
        /// 写入入口更新——这三者改变的都是"三段式聚合"的输入（base/modifier），但
        /// <see cref="ConvertRating"/> 依赖的单位等级（经 <see cref="StatHostOptions.LevelLookup"/>
        /// 查询）不经过这三个入口，缓存会一直停留在"注册/上次任意一次写入操作时的等级"算出的值上，
        /// 升级/掉级后不会自动更新（见外部审计 RC-06）。本方法只重算带评级曲线的属性——不带评级
        /// 曲线的属性与等级无关，不受影响，全量重算徒增开销。供 <c>RulesAssembly</c> 订阅
        /// <c>progression.level_up</c> 后对该单位调用（见该组装根判断记录）。单位未注册按幂等
        /// no-op 处理（不抛异常）——事件驱动的调用时机与单位注册时机之间没有强保证的先后顺序。
        /// </para>
        /// <para>
        /// 判断记录（T-N1-2 追加）：06 第 1.1 节修订段"来源属性或派生系数变化时派生属性重算，与既有
        /// '等级变化驱动评级缓存重算'同一通知路径"——本方法重算某条评级属性后，如果恰好有其它派生
        /// 属性以它为 <c>derived_from</c> 来源，同样要经 <see cref="PropagateDerivedInvalidation"/>
        /// 传播失效，否则"评级属性只因等级变化而改变"这条路径会绕过派生缓存失效，留下过期值。
        /// </para>
        /// </summary>
        public void RecomputeRatingStats(Id unitId)
        {
            if (!_units.TryGetValue(unitId, out var unit))
            {
                return;
            }

            foreach (var def in _definitions.Values)
            {
                if (!def.RatingConversionRef.HasValue)
                {
                    continue;
                }

                var hasCached = unit.Cache.TryGetValue(def.Id, out var oldValue);
                var newValue = ComputeFinal(unitId, unit, def);
                unit.Cache[def.Id] = newValue;

                if (hasCached && newValue != oldValue)
                {
                    _bus.Enqueue(new StatChangedEvent(unitId, def.Id, oldValue, newValue));
                }

                PropagateDerivedInvalidation(unitId, unit, def.Id);
            }
        }

        /// <summary>
        /// T-N1-2（ADR-0030 决策 2）：<paramref name="changedStat"/> 的某一路输入（base/modifier，
        /// 或如 <see cref="RecomputeRatingStats"/> 场景下的评级换算结果）发生变化后，把失效传播给
        /// 全部（传递）以它为 <c>derived_from</c> 来源的派生属性——只处理"此前已经被
        /// <see cref="GetStat"/> 或本方法自己算过并写入 <see cref="UnitStats.Cache"/>"的属性，未被
        /// 任何人查询过的属性维持 <see cref="RecomputeAllCachedStatsAfterReload"/> 同一判断记录口径：
        /// 不主动补算，下一次 <see cref="GetStat"/> 现算时自然读到最新状态（<see cref="ComputeFinal"/>
        /// 从不记忆旧输入）。
        /// <para>
        /// 判断记录（必须按拓扑序处理受影响集合）：<see cref="ComputeFinal"/> 对派生属性的来源值经
        /// <see cref="ResolveFinal"/> 优先读取 <see cref="UnitStats.Cache"/>——如果受影响集合内的两个
        /// 派生属性 A、B 存在链式依赖（A 的来源之一是 B），必须先重算 B 再重算 A，否则 A 会读到 B
        /// 尚未刷新的旧缓存值。这里按 <see cref="_topoOrder"/>（来源恒先于派生）过滤后依次处理，
        /// 不直接遍历受影响集合本身的（无序）枚举顺序。
        /// </para>
        /// </summary>
        private void PropagateDerivedInvalidation(Id unitId, UnitStats unit, Id changedStat)
        {
            if (!_derivedDependents.ContainsKey(changedStat))
            {
                return; // 快速路径：没有任何属性以 changedStat 为派生来源，多数属性都会走这一分支。
            }

            var affected = new HashSet<Id>();
            CollectTransitiveDependents(changedStat, affected);
            if (affected.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _topoOrder.Count; i++)
            {
                var stat = _topoOrder[i];
                if (!affected.Contains(stat) || !unit.Cache.TryGetValue(stat, out var oldValue))
                {
                    continue;
                }

                var def = _definitions[stat];
                var newValue = ComputeFinal(unitId, unit, def);
                unit.Cache[stat] = newValue;

                if (newValue != oldValue)
                {
                    _bus.Enqueue(new StatChangedEvent(unitId, stat, oldValue, newValue));
                }
            }
        }

        /// <summary>收集 <paramref name="statId"/> 的全部传递依赖者（直接以它为 <c>derived_from</c>
        /// 来源的属性，以及依赖那些属性的属性，递归下去）写入 <paramref name="result"/>。图在加载期
        /// 已经过 <see cref="BuildDerivationGraph"/> 的环检测，这里的递归深度受属性总数严格约束，不会
        /// 无限递归。</summary>
        private void CollectTransitiveDependents(Id statId, HashSet<Id> result)
        {
            if (!_derivedDependents.TryGetValue(statId, out var directDependents))
            {
                return;
            }

            for (int i = 0; i < directDependents.Count; i++)
            {
                var dependent = directDependents[i];
                if (result.Add(dependent))
                {
                    CollectTransitiveDependents(dependent, result);
                }
            }
        }

        // -----------------------------------------------------------------
        // 两轮拓扑序聚合（T-N1-2，ADR-0030 决策 2）
        // -----------------------------------------------------------------

        /// <summary>
        /// 三段式聚合（06 第 1.1 节），T-N1-2 起"基础值"这一输入按 <see cref="ResolveBaseValue"/>
        /// 分两路取得——<c>category != derived</c> 的属性（第一轮）取 <c>unit.Base</c> 显式覆盖值或
        /// <see cref="StatDefinition.DefaultBase"/>；<c>category == derived</c> 的属性（第二轮）走
        /// <see cref="ComputeDerivedBase"/> 的 Σ(来源属性最终值 × 系数) 公式。flat/pct/mult 三段的
        /// 顺序与 <c>multGroup</c> 算法本身不变（禁止事项）；<c>clamp</c> 仍是本方法最后一步——对
        /// 第一轮属性即"该属性自己完成三段式聚合之后"，对第二轮（派生）属性即"第二轮聚合之后"
        /// （拍板 2），两者是同一段代码，不需要按轮次分叉。
        /// </summary>
        private double ComputeFinal(Id unitId, UnitStats unit, StatDefinition def)
        {
            if (def.Category == "defense" && !_options.EnableResistanceGroup)
            {
                _warnings.Add($"属性 \"{def.Id}\" 属于 defense 类别，但 EnableResistanceGroup=false，GetStat 恒返回 0");
                return 0.0;
            }

            var baseValue = ResolveBaseValue(unitId, unit, def);

            double flatSum = 0;
            double pctSum = 0;
            List<string>? groupOrder = null;
            Dictionary<string, double>? groupSums = null;

            if (unit.ModifiersByStat.TryGetValue(def.Id, out var mods))
            {
                for (int i = 0; i < mods.Count; i++)
                {
                    var m = mods[i];
                    switch (m.Op)
                    {
                        case StatModifierOp.Flat:
                            flatSum += m.Value;
                            break;
                        case StatModifierOp.Pct:
                            pctSum += m.Value;
                            break;
                        case StatModifierOp.Mult:
                            groupOrder ??= new List<string>();
                            groupSums ??= new Dictionary<string, double>();
                            if (!groupSums.ContainsKey(m.MultGroup))
                            {
                                groupSums[m.MultGroup] = 0;
                                groupOrder.Add(m.MultGroup);
                            }
                            groupSums[m.MultGroup] += m.Value;
                            break;
                    }
                }
            }

            var value = baseValue + flatSum;

            if (_options.EnableRatingConversion && def.IsRating)
            {
                value = ConvertRating(unitId, def, value);
            }

            value *= (1 + pctSum);

            if (groupOrder != null)
            {
                for (int i = 0; i < groupOrder.Count; i++)
                {
                    value *= (1 + groupSums![groupOrder[i]]);
                }
            }

            if (def.Min.HasValue) value = Math.Max(value, def.Min.Value);
            if (def.Max.HasValue) value = Math.Min(value, def.Max.Value);

            return value;
        }

        /// <summary>
        /// 判断记录（T-N1-2，待设计层确认）：显式 <see cref="SetBase"/> 值对任何属性（含
        /// <c>category=derived</c>）都优先生效——这是既有"未显式 <see cref="SetBase"/> 时退回
        /// <see cref="StatDefinition.DefaultBase"/>"规则（见 <see cref="GetBase"/>）向派生属性的
        /// 自然推广：派生属性没有被显式 <see cref="SetBase"/> 过时才用 <see cref="ComputeDerivedBase"/>
        /// 算出的 Σ(来源最终值×系数) 作为"基础值"，一旦调用方显式 <c>SetBase</c> 过，视为调用方主动
        /// 覆盖，不再理会 <c>derived_from</c>。ADR-0030 决策 2 原文"派生属性的基础值 =
        /// Σ(来源属性最终值×系数)"只给出默认公式，未明确与显式覆盖的优先级关系——本实现选择"显式覆盖
        /// 优先"是因为：(a) 与 <c>DefaultBase</c> 回退规则同构，不需要为 derived 类别新引入一套单独的
        /// 优先级语义；(b) 不这样做则 <see cref="SetBase"/> 对 derived 类别属性变成静默 no-op（写入
        /// <c>unit.Base</c> 但从不参与计算），对调用方是隐蔽的行为陷阱。已在任务汇报标注"待设计层
        /// 确认"，与 T-N1-1 对不明确映射规则的处理口径一致。
        /// </summary>
        private double ResolveBaseValue(Id unitId, UnitStats unit, StatDefinition def)
        {
            if (unit.Base.TryGetValue(def.Id, out var explicitBase))
            {
                return explicitBase;
            }

            return def.Category == "derived" ? ComputeDerivedBase(unitId, unit, def) : def.DefaultBase;
        }

        /// <summary>ADR-0030 决策 2"第二轮：派生属性的基础值 = Σ(来源属性最终值 × 派生系数)"。
        /// <paramref name="def"/>.<see cref="StatDefinition.DerivedFrom"/> 允许为空（<c>category
        /// =derived</c> 但未登记来源的属性，合法但基础值恒为 0，与"没有任何 flat 修正的属性基础值
        /// 为 0"同一语义，不是错误）；<see cref="StatDefinition.DerivedFrom"/>.<c>Coefficient</c>
        /// 允许为负（拍板 11）。</summary>
        private double ComputeDerivedBase(Id unitId, UnitStats unit, StatDefinition def)
        {
            var sources = def.DerivedFrom;
            double sum = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                sum += ResolveFinal(unitId, unit, sources[i].Stat) * sources[i].Coefficient;
            }
            return sum;
        }

        /// <summary>取属性 <paramref name="statId"/>（多为派生属性的来源，但不限于此）在
        /// <paramref name="unit"/> 上的当前最终值——命中 <see cref="UnitStats.Cache"/> 直接返回
        /// （该缓存要么本就正确/未受本轮变化影响，要么已经被 <see cref="PropagateDerivedInvalidation"/>
        /// 按拓扑序提前刷新过，见该方法判断记录）；未命中则现算一次（<em>不</em>写入缓存——本方法只是
        /// "取值"的内部读操作，写缓存的决定权归 <see cref="GetStat"/>/<see cref="PropagateDerivedInvalidation"/>/
        /// <see cref="RecomputeAllCachedStatsAfterReload"/> 这些真正驱动缓存生命周期的入口，避免本方法
        /// 悄悄为"从未被任何人查询过的属性"创建缓存条目，破坏既有"只缓存已经算过的"判断记录口径）。
        /// 若 <paramref name="statId"/> 找不到定义（<c>derived_from</c> 引用完整性问题，正常情况下已被
        /// <c>reference_integrity</c> 内容校验拦下），抛 <see cref="InvalidOperationException"/>——风格同
        /// <see cref="RequireDefinition"/> 对未知 stat 的既有报错，只是异常类型对齐本类型其它加载期
        /// 防御性检查（<see cref="BuildDerivationGraph"/>）已用的 <see cref="InvalidOperationException"/>。</summary>
        private double ResolveFinal(Id unitId, UnitStats unit, Id statId)
        {
            if (unit.Cache.TryGetValue(statId, out var cached))
            {
                return cached;
            }

            if (!_definitions.TryGetValue(statId, out var sourceDef))
            {
                throw new InvalidOperationException(
                    $"派生来源属性 \"{statId}\" 未在 stat.definition 中定义（derived_from 引用完整性应已由内容校验拦下）");
            }

            return ComputeFinal(unitId, unit, sourceDef);
        }

        /// <summary>
        /// 评级换算（06 第 1.1 节"某些属性在参与三段式聚合前先过一层评级曲线"）。
        /// <para>
        /// 判断记录（2026-09-05，设计层裁定，取代原判断记录）：曲线 <c>entries[].level</c> 就是
        /// 单位等级——按等级在 <c>entries</c>（已按 <c>level</c> 升序排列）上线性插值取得
        /// <c>points_per_percent</c>（越界取端点），再用 <c>percent = rawValue / pointsPerPercent</c>
        /// 算出换算结果；<c>rawValue</c>（<c>base + Σflat</c>）本身只作为被除数，不参与插值。
        /// 单位等级经构造期注入的 <see cref="StatHostOptions.LevelLookup"/> 具名委托查询——
        /// <c>StatHost</c> 仍然不直接引用 <c>core/numbers/progression</c> 的任何类型（01 第 3 节
        /// "同层仅契约/仅事件"），由调用方把真正的等级来源（如
        /// <c>IProgressionHost.GetLevel</c>）适配成该委托签名后注入；委托为 <c>null</c> 时等级
        /// 一律按 1 处理。
        /// </para>
        /// </summary>
        private double ConvertRating(Id unitId, StatDefinition def, double rawValue)
        {
            if (!def.RatingConversionRef.HasValue)
            {
                return rawValue;
            }
            if (!_ratingConversions.TryGetValue(def.RatingConversionRef.Value, out var conversion) || conversion.Curve.Count == 0)
            {
                return rawValue;
            }

            var level = _options.LevelLookup?.Invoke(unitId) ?? 1;

            // T-N0-4：插值委托 PiecewiseCurve.Evaluate（越界夹取端点、段内 lo + t × (hi − lo)，与迁移前
            // 本方法手写的式子逐运算相同，见 PiecewiseCurve 判断记录 2）。
            var pointsPerPercent = conversion.Curve.Evaluate(level);
            return DivideByPointsPerPercent(rawValue, pointsPerPercent);
        }

        private static double DivideByPointsPerPercent(double rawValue, double pointsPerPercent)
        {
            return pointsPerPercent == 0 ? 0.0 : rawValue / pointsPerPercent;
        }

        // -----------------------------------------------------------------
        // 内部辅助
        // -----------------------------------------------------------------

        private UnitStats RequireUnit(Id unitId)
        {
            if (!_units.TryGetValue(unitId, out var unit))
            {
                throw new InvalidOperationException($"单位 \"{unitId}\" 未注册（先调用 RegisterUnit）");
            }
            return unit;
        }

        private StatDefinition RequireDefinition(Id stat)
        {
            if (!_definitions.TryGetValue(stat, out var def))
            {
                throw new ArgumentException($"属性 \"{stat}\" 未在 stat.definition 中定义", nameof(stat));
            }
            return def;
        }

        // -----------------------------------------------------------------
        // 内部类型
        // -----------------------------------------------------------------

        private sealed class StatDefinition
        {
            public Id Id { get; }

            /// <summary>T-N1-2：取代此前的 <c>Group</c> 字段（<c>StatHost</c> 不再读取
            /// <c>stat.definition.group</c>，见 <see cref="LoadDefinitions"/> 判断记录）。</summary>
            public string Category { get; }

            public double DefaultBase { get; }

            /// <summary>T-N1-2：来源改为嵌套 <c>clamp.min</c>（拍板 2），不再是平级 <c>min</c>。</summary>
            public double? Min { get; }

            /// <summary>T-N1-2：来源改为嵌套 <c>clamp.max</c>（拍板 2），不再是平级 <c>max</c>。</summary>
            public double? Max { get; }

            public bool IsRating { get; }
            public Id? RatingConversionRef { get; }

            /// <summary>T-N1-2（ADR-0030 决策 1/2）：仅 <see cref="Category"/> 为 <c>"derived"</c> 时
            /// 有意义，空列表合法（见 <see cref="ComputeDerivedBase"/> 判断记录）。</summary>
            public IReadOnlyList<(Id Stat, double Coefficient)> DerivedFrom { get; }

            public StatDefinition(
                Id id, string category, double defaultBase, double? min, double? max, bool isRating,
                Id? ratingConversionRef, IReadOnlyList<(Id Stat, double Coefficient)> derivedFrom)
            {
                Id = id;
                Category = category;
                DefaultBase = defaultBase;
                Min = min;
                Max = max;
                IsRating = isRating;
                RatingConversionRef = ratingConversionRef;
                DerivedFrom = derivedFrom;
            }
        }

        private sealed class RatingConversion
        {
            public Id Id { get; }

            /// <summary>x = 单位等级，y = 该等级下每 1% 效果所需点数（T-N0-4 起以通用曲线承载）。</summary>
            public PiecewiseCurve Curve { get; }

            public RatingConversion(Id id, PiecewiseCurve curve)
            {
                Id = id;
                Curve = curve;
            }
        }

        private sealed class UnitStats
        {
            public readonly Dictionary<Id, double> Base = new Dictionary<Id, double>();
            public readonly Dictionary<Id, List<StatModifier>> ModifiersByStat = new Dictionary<Id, List<StatModifier>>();
            public readonly Dictionary<Id, double> Cache = new Dictionary<Id, double>();
        }
    }
}
