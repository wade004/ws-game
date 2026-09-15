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
    /// 见 04 第 4 节"报告含错误项即视为不可进入运行时"）；换算层始终启用（T-N1-3，ADR-0030
    /// 决策 3），构造时无条件另外读取 <c>stat.rating_conversion</c>（表不存在则视为空曲线集合，
    /// 换算时退化为直通，不阻断构造——只有 <c>stat.definition</c> 是本模块的强依赖，见判断记录）。
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

            // T-N1-3（ADR-0030 决策 3）：换算层始终启用——LoadRatingConversions 无条件执行，不再由
            // StatHostOptions.EnableRatingConversion 门控（该属性已标废弃、StatHost 不再读取，见
            // StatHostOptions.EnableRatingConversion 判断记录）。表不存在时 LoadRatingConversions
            // 自身已有"不阻断构造，换算时对未引用到曲线的属性直通原值"的兜底，见该方法判断记录。
            LoadRatingConversions(_registry);

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

                // T-N1-3（ADR-0030 决策 3）：换算层触发条件改为 category=="percent"——不再读取
                // 废弃的 is_rating 字段（v1 数据的 is_rating 已由 1→2 迁移链折算进 category/
                // conversion_ref，见 StatSchemas.MigrateDefinitionV1ToV2；到这里读到的记录已经过
                // 迁移，直接读新字段即可，同 T-N1-2 对 category/clamp 的既有处理口径）。改读
                // conversion_ref（v2 新字段）取代 rating_conversion_ref（废弃字段，v1 数据经迁移
                // 已折算进 conversion_ref）。
                Id? conversionRef = null;
                if (record.TryGetId("conversion_ref", out var convRefValue)) conversionRef = convRefValue;

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

                // T-N1-7（ADR-0030 决策 5；06 第 4.1 节 2026-09-14 修订段"目标乘区"）：读取
                // scope（optional，缺省 "any"，同 stat.definition.scope 字段本身缺省语义，见
                // StatSchemas.ScopeValues 判断记录）。T-N1-1 已把该字段登记进 schema，但当时判断记录
                // 明确写"本任务只改 schema 与校验规则，不改 StatHost.cs"——StatHost 到 T-N1-6 为止
                // 从未真正消费过这个字段；本任务是第一个需要它的消费方（结算管线"目标乘区"按来源
                // 类别遍历 scope 匹配属性），这里补上加载期解析。
                var scope = "any";
                record.TryGetString("scope", out var scopeValue);
                if (!string.IsNullOrEmpty(scopeValue)) scope = scopeValue;

                _definitions[id] = new StatDefinition(id, category, defaultBase, min, max, conversionRef, derivedFrom, scope);
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
                // 换算层始终启用但没有任何曲线数据：不阻断构造，换算时对没有引用到曲线的属性
                // 直通原值（见 ConvertRating 判断记录）。
                return;
            }

            var records = registry.GetAll("stat.rating_conversion");
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var id = record.GetId("id");
                _ratingConversions[id] = ParseConversion(record, id);
            }
        }

        /// <summary>
        /// T-N1-3（ADR-0030 决策 3；04 第 3.6 节；数值设计 01 第 5 节"三种曲线形态"）：按一条
        /// <c>stat.rating_conversion</c> 记录登记的形态解析为 <see cref="RatingConversion"/>——
        /// <c>entries</c> 在先（"标准版：等级索引除数"，见 <see cref="ParseCurve"/>），否则
        /// <c>saturation</c>（"变态版：饱和曲线，除数随等级增长"，见 <see cref="ParseSaturation"/>）；
        /// 两者都缺是防御分支（正常数据应已被 <see cref="StatRatingConversionValidationRule"/> 的
        /// "恰好二选一"检查拦下），抛 <see cref="InvalidOperationException"/>，同本类型其它加载期
        /// 结构性错误的既有异常类型。
        /// </summary>
        private static RatingConversion ParseConversion(DataRecord record, Id id)
        {
            if (record.Has("entries"))
            {
                return RatingConversion.CreateBreakpoints(id, ParseCurve(record, id));
            }

            if (record.Has("saturation"))
            {
                var (k, cap) = ParseSaturation(record, id);
                return RatingConversion.CreateSaturation(id, k, cap);
            }

            throw new InvalidOperationException(
                $"stat.rating_conversion[{id}] 既没有 \"entries\" 也没有 \"saturation\"（两种曲线形态二选一，" +
                "正常数据应已被 StatRatingConversionValidationRule 拦下，这里是加载期防御）");
        }

        /// <summary>T-N1-3：解析 <c>saturation</c> 为 <c>(k, cap)</c>——<c>k</c> 必填（04 第 3.6 节
        /// <c>CurveSchema.SaturationField</c> 已在加载期用 <c>field_range</c> 保证 &gt; 0，这里再兜底一次
        /// 缺字段的结构性错误）；<c>cap</c> 缺省 1（同 <c>CurveSchema.SaturationField</c> 的字段登记
        /// 缺省语义）。</summary>
        private static (double K, double Cap) ParseSaturation(DataRecord record, Id id)
        {
            var saturation = record.GetObject("saturation");
            if (!TryReadNumber(saturation, CurveSchema.SaturationKFieldName, out var k))
            {
                throw new InvalidOperationException(
                    $"stat.rating_conversion[{id}].saturation 缺少合法的 \"k\"（Number）");
            }

            var cap = TryReadNumber(saturation, CurveSchema.SaturationCapFieldName, out var capValue) ? capValue : 1.0;
            return (k, cap);
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
        /// T-N1-7（<see cref="IStatHost.GetScope"/> 判断记录；复核返工撤回了同批次曾经新增的
        /// <c>GetDefinitionIdsByCategory</c>——类别扫描会把 ADR-0030 决策 9 里同属 <c>defense</c>
        /// 类别的护甲值当成百分比误计入目标乘区，识别"减免属性"改由
        /// <c>Core.Rules.Combat.CombatOptions</c> 显式列出 id 清单，本方法保留、继续按属性 id
        /// 查询）：真正接入内容数据的实现——属性已登记时返回其 <see cref="StatDefinition.Scope"/>
        /// （加载期已从 <c>stat.definition.scope</c> 解析，缺省 <c>"any"</c>，见
        /// <see cref="LoadDefinitions"/>）；未登记（<paramref name="stat"/> 不在
        /// <see cref="_definitions"/> 中）时同样返回 <c>"any"</c>——不抛异常，同接口成员判断记录
        /// "缺省 any 与属性缺失不需要调用方区分"。
        /// </summary>
        public string GetScope(Id stat)
        {
            return _definitions.TryGetValue(stat, out var def) ? def.Scope : "any";
        }

        /// <summary>
        /// RC-06 收边补齐：单位等级变化后，重算并按需广播全部经评级曲线换算（<see
        /// cref="StatDefinition.ConversionRef"/> 非空，见 <see cref="ConvertRating"/>）的属性。
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
                if (!def.ConversionRef.HasValue)
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
        /// T-N1-4（ADR-0030 决策 2"职业模板可覆盖派生系数（<c>arch.class.derivation_overrides</c>，
        /// 可选）"）：整体替换 <paramref name="unitId"/> 当前生效的派生系数覆盖——<b>全量替换语义</b>，
        /// 不是增量合并：上一次调用登记过、这一次 <paramref name="overrides"/> 里不再出现的
        /// <c>(stat, source)</c> 条目自动失效（等价于"先清旧覆盖再写新覆盖"，见 <see
        /// cref="Core.Numbers.Archetype.ArchetypeRegistry"/> 换职业接线点判断记录），调用方不需要
        /// 自己先调 <see cref="ClearDerivationCoefficientOverrides"/> 再调本方法。<paramref
        /// name="overrides"/> 为空数组等价于清空（与 <see cref="ClearDerivationCoefficientOverrides"/>
        /// 结果相同，提供后者只是让调用方表达意图更直接）。
        /// <para>
        /// 每条覆盖是 <c>(目标派生属性, 来源属性, 覆盖系数)</c> 三元组——同一 <c>(stat, source)</c> 在
        /// <paramref name="overrides"/> 里重复出现时保留最后一条（同 <see cref="AddModifier"/> 对同一
        /// 属性多条修正的既有"按顺序生效"惯例，这里退化为"后写覆盖先写"，因为覆盖表本身是键值对，
        /// 没有"多条同时生效"的语义空间）。<b>不做"覆盖是否指向 <paramref name="def"/>.<see
        /// cref="StatDefinition.DerivedFrom"/> 中真实存在的边"这类内容层面的校验</b>——那是内容加载期
        /// 的职责（见 <c>Core.Numbers.Archetype.ArchClassDerivationOverrideValidationRule</c>），指向
        /// 不存在边的覆盖在 <see cref="ComputeDerivedBase"/> 里天然读不到、不产生任何效果（防御性静默
        /// 忽略，同本类型一贯的"内容错误已由校验层拦下，运行时只做兜底"口径）。目标属性
        /// <c>category</c> 不是 <c>derived</c> 时同理——覆盖表写进去了，但 <see
        /// cref="ComputeDerivedBase"/> 根本不会为非 derived 属性调用，不产生效果。
        /// </para>
        /// <para>
        /// 判断记录（不加入 <see cref="IStatHost"/> 契约接口）：同 <see cref="ResetBase"/>——
        /// <c>Core.Rules.Assembly.RulesAssembly</c> 持有的是 <see cref="StatHost"/> 具体类型，不是
        /// <see cref="IStatHost"/> 接口，加入具体类型即可满足换职业/读档恢复的接线需求，不扩大既有
        /// 接口契约的语义范围，也不影响任何既有 <see cref="IStatHost"/> 实现的源码兼容性。
        /// </para>
        /// <para>
        /// 判断记录（重算与事件）：只重算"此前已经被缓存过"的受影响属性（同 <see
        /// cref="PropagateDerivedInvalidation"/>/<see cref="RecomputeAllCachedStatsAfterReload"/> 一贯
        /// 口径），受影响集合 = 本次覆盖表变化涉及的目标属性（新旧两份覆盖表 key 的并集）∪ 它们的全部
        /// 传递依赖者（覆盖变化的派生属性自己也可能是另一条派生关系的来源），按 <see
        /// cref="_topoOrder"/> 顺序处理，逐个比较新旧值决定是否补发 <see cref="StatChangedEvent"/>——
        /// 与来源属性变化触发的失效传播是同一条通知路径（06 第 1.1 节修订段"来源属性或派生系数变化时
        /// 派生属性重算，与既有'等级变化驱动评级缓存重算'同一通知路径"）。
        /// </para>
        /// </summary>
        public void SetDerivationCoefficientOverrides(Id unitId, IReadOnlyList<(Id Stat, Id Source, double Coefficient)> overrides)
        {
            var unit = RequireUnit(unitId);

            var newMap = new Dictionary<Id, Dictionary<Id, double>>();
            for (int i = 0; i < overrides.Count; i++)
            {
                var (stat, source, coefficient) = overrides[i];
                if (!newMap.TryGetValue(stat, out var bySource))
                {
                    bySource = new Dictionary<Id, double>();
                    newMap[stat] = bySource;
                }
                bySource[source] = coefficient; // 同一 (stat, source) 重复登记：保留最后一条。
            }

            var affected = new HashSet<Id>();
            if (unit.DerivationOverrides != null)
            {
                foreach (var stat in unit.DerivationOverrides.Keys) affected.Add(stat);
            }
            foreach (var stat in newMap.Keys) affected.Add(stat);

            unit.DerivationOverrides = newMap.Count > 0 ? newMap : null;

            RecomputeDerivationOverrideAffectedStats(unitId, unit, affected);
        }

        /// <summary>T-N1-4：清空 <paramref name="unitId"/> 当前生效的派生系数覆盖，等价于
        /// <c>SetDerivationCoefficientOverrides(unitId, Array.Empty&lt;...&gt;())</c>（见该方法判断
        /// 记录）。本就没有任何覆盖时是安全的幂等 no-op（不发 <see cref="StatChangedEvent"/>，同
        /// <see cref="ResetBase"/> 的既有幂等惯例）。</summary>
        public void ClearDerivationCoefficientOverrides(Id unitId)
        {
            var unit = RequireUnit(unitId);
            if (unit.DerivationOverrides == null)
            {
                return; // 幂等 no-op。
            }

            var affected = new HashSet<Id>(unit.DerivationOverrides.Keys);
            unit.DerivationOverrides = null;

            RecomputeDerivationOverrideAffectedStats(unitId, unit, affected);
        }

        /// <summary>T-N1-4：<paramref name="directlyAffected"/>（覆盖表变化直接涉及的目标属性）及其
        /// 全部传递依赖者中，"此前已经被缓存过"的属性按 <see cref="_topoOrder"/> 顺序重算，值变化则
        /// 补发 <see cref="StatChangedEvent"/>——与 <see cref="PropagateDerivedInvalidation"/> 同一套
        /// 算法，只是触发源是"覆盖表本身"而不是"某个属性的 base/modifier"。</summary>
        private void RecomputeDerivationOverrideAffectedStats(Id unitId, UnitStats unit, HashSet<Id> directlyAffected)
        {
            var allAffected = new HashSet<Id>(directlyAffected);
            foreach (var stat in directlyAffected)
            {
                CollectTransitiveDependents(stat, allAffected);
            }

            for (int i = 0; i < _topoOrder.Count; i++)
            {
                var stat = _topoOrder[i];
                if (!allAffected.Contains(stat) || !unit.Cache.TryGetValue(stat, out var oldValue))
                {
                    continue;
                }

                if (!_definitions.TryGetValue(stat, out var def))
                {
                    continue;
                }

                var newValue = ComputeFinal(unitId, unit, def);
                unit.Cache[stat] = newValue;

                if (newValue != oldValue)
                {
                    _bus.Enqueue(new StatChangedEvent(unitId, stat, oldValue, newValue));
                }
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

            // T-N1-3（ADR-0030 决策 3）：换算层始终启用，触发条件是 category=="percent" 本身——
            // 不再有 StatHostOptions.EnableRatingConversion 这道开关（该属性已标废弃且 StatHost
            // 不再读取它，见 StatHostOptions.EnableRatingConversion 判断记录）。ConvertRating 内部
            // 对 conversion_ref 缺省的属性直接返回原值（恒等曲线），所以本分支对"percent 但未引用
            // 曲线"的属性同样安全——不是"看似换算、实则被开关拦下"的隐藏分叉。
            if (def.Category == "percent")
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
        /// 判断记录（T-N1-2，设计层裁定（2026-09-14）：采纳）：显式 <see cref="SetBase"/> 值对任何属性（含
        /// <c>category=derived</c>）都优先生效——这是既有"未显式 <see cref="SetBase"/> 时退回
        /// <see cref="StatDefinition.DefaultBase"/>"规则（见 <see cref="GetBase"/>）向派生属性的
        /// 自然推广：派生属性没有被显式 <see cref="SetBase"/> 过时才用 <see cref="ComputeDerivedBase"/>
        /// 算出的 Σ(来源最终值×系数) 作为"基础值"，一旦调用方显式 <c>SetBase</c> 过，视为调用方主动
        /// 覆盖，不再理会 <c>derived_from</c>。ADR-0030 决策 2 原文"派生属性的基础值 =
        /// Σ(来源属性最终值×系数)"只给出默认公式，未明确与显式覆盖的优先级关系——本实现选择"显式覆盖
        /// 优先"是因为：(a) 与 <c>DefaultBase</c> 回退规则同构，不需要为 derived 类别新引入一套单独的
        /// 优先级语义；(b) 不这样做则 <see cref="SetBase"/> 对 derived 类别属性变成静默 no-op（写入
        /// <c>unit.Base</c> 但从不参与计算），对调用方是隐蔽的行为陷阱。设计层裁定（2026-09-14）：
        /// 采纳，与 T-N1-1 对不明确映射规则的处理口径一致。
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
        /// 允许为负（拍板 11）。
        /// <para>
        /// 判断记录（T-N1-4，ADR-0030 决策 2"职业模板可覆盖派生系数"）：每条来源的系数先查
        /// <paramref name="unit"/>.<see cref="UnitStats.DerivationOverrides"/>（按
        /// <c>(本属性 id, 来源 id)</c）二级查找），命中则用覆盖值取代 <c>stat.definition</c> 里登记的
        /// 默认系数；未命中（含整条属性都没有任何覆盖、或覆盖表里没有这一条具体的来源边）时回退到
        /// <paramref name="def"/> 自己的系数——覆盖只影响"用哪个数"，不改变来源集合本身，也不需要
        /// 在这里做"覆盖是否指向一条真实存在的 derived_from 边"的校验：覆盖表本就只按
        /// <paramref name="def"/>.<see cref="StatDefinition.DerivedFrom"/> 实际登记的来源顺序遍历查找，
        /// 一条指向不存在边的覆盖天然不会被读到（内容层面的"覆盖必须指向真实存在的边"校验见
        /// <c>Core.Numbers.Archetype.ArchClassDerivationOverrideValidationRule</c>，属于内容校验阶段，
        /// 不是本方法的职责）。
        /// </para>
        /// </summary>
        private double ComputeDerivedBase(Id unitId, UnitStats unit, StatDefinition def)
        {
            var sources = def.DerivedFrom;
            Dictionary<Id, double>? overridesForThisStat = null;
            unit.DerivationOverrides?.TryGetValue(def.Id, out overridesForThisStat);

            double sum = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                var coefficient = sources[i].Coefficient;
                if (overridesForThisStat != null && overridesForThisStat.TryGetValue(sources[i].Stat, out var overrideCoefficient))
                {
                    coefficient = overrideCoefficient;
                }
                sum += ResolveFinal(unitId, unit, sources[i].Stat) * coefficient;
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
        /// 换算层（06 第 1.1 节修订段"某些属性在参与三段式聚合前先过一层评级曲线"；T-N1-3，
        /// ADR-0030 决策 3："换算层始终存在，恒等曲线为默认"）。
        /// <para>
        /// <c>conversion_ref</c> 缺省（<see cref="StatDefinition.ConversionRef"/> 为 <c>null</c>）
        /// 即恒等曲线——直接返回 <paramref name="rawValue"/>（"保守版：恒等加 clamp 硬上限"，硬上限
        /// 由 <see cref="ComputeFinal"/> 末尾的 <c>clamp</c> 夹取承担，不在本方法内）。引用了曲线但
        /// 该 id 在 <see cref="_ratingConversions"/> 里查不到（内容错误：引用不存在的曲线，正常情况
        /// 应已由 <c>reference_integrity</c> 校验拦下）时同样退化为直通，同既有"内容错误被静默降级"
        /// 兜底口径。
        /// </para>
        /// <para>
        /// 引用到曲线时按其登记的形态求值（<see cref="RatingConversionShape"/>），T-N2-3 起委托
        /// <see cref="RatingConversionEvaluator.ToPercent"/>（正向求值式子已提升为公开静态工具，供
        /// 装备预算消耗公式共用，见该类型判断记录，本方法只负责取 <see cref="_ratingConversions"/>
        /// 与 <see cref="StatHostOptions.LevelLookup"/> 两份"本模块私有状态"再转交）：
        /// <see cref="RatingConversionShape.Breakpoints"/>（"标准版：等级索引除数"）——按等级在
        /// <c>entries</c>（已按 <c>x</c> 升序排列）上线性插值取得 <c>y</c>（每 1% 所需点数，越界取
        /// 端点），再用 <c>percent = rawValue / pointsPerPercent</c> 算出换算结果（2026-09-05 设计层
        /// 裁定：<c>x</c> 就是单位等级，<c>rawValue</c> 本身只作被除数，不参与插值，见
        /// <see cref="ParseCurve"/> 判断记录）；空曲线（迁移前遗留的极端情形）退化为直通。
        /// <see cref="RatingConversionShape.Saturation"/>（"变态版：饱和曲线，除数随等级增长"）——
        /// 公式与 <c>combat.resist_curve</c> 饱和分支（<c>ResistCurve.ComputeReduction</c>）同形态，
        /// 见 04 第 3.6 节 <c>CurveSchema.SaturationField</c> 判断记录。
        /// </para>
        /// <para>
        /// 单位等级经构造期注入的 <see cref="StatHostOptions.LevelLookup"/> 具名委托查询——
        /// <c>StatHost</c> 仍然不直接引用 <c>core/numbers/progression</c> 的任何类型（01 第 3 节
        /// "同层仅契约/仅事件"），由调用方把真正的等级来源（如
        /// <c>IProgressionHost.GetLevel</c>）适配成该委托签名后注入；委托为 <c>null</c> 时等级
        /// 一律按 1 处理。两种形态共用同一次 <c>LevelLookup</c> 查询。
        /// </para>
        /// </summary>
        private double ConvertRating(Id unitId, StatDefinition def, double rawValue)
        {
            if (!def.ConversionRef.HasValue)
            {
                return rawValue;
            }
            if (!_ratingConversions.TryGetValue(def.ConversionRef.Value, out var conversion))
            {
                return rawValue;
            }

            var level = _options.LevelLookup?.Invoke(unitId) ?? 1;

            // T-N2-3：正向求值委托 RatingConversionEvaluator.ToPercent（插值/饱和两分支的式子从本方法
            // 移出，改为 Core.Numbers.StatBlock 命名空间级公开静态工具，供 Core.Carriers.Item 的装备
            // 预算消耗公式复用同一份实现，见该类型判断记录；逐运算与迁移前本方法手写式子相同，既有
            // RatingConversionMigrationTests 等测试锁定结果不变）。
            return RatingConversionEvaluator.ToPercent(
                conversion.Shape, conversion.Curve, conversion.K, conversion.Cap, rawValue, level);
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

            /// <summary>T-N1-3（ADR-0030 决策 3）：取代此前的 <c>IsRating</c>/<c>RatingConversionRef</c>
            /// 两个字段——换算层的触发条件不再是独立的 <c>is_rating</c> 布尔值，而是
            /// <c>Category == "percent"</c> 本身（见 <see cref="ComputeFinal"/> 判断记录）；本字段只
            /// 承载"换算用哪条曲线"，取自 v2 <c>conversion_ref</c>（缺省 null，即恒等曲线，见
            /// <see cref="ConvertRating"/>）。</summary>
            public Id? ConversionRef { get; }

            /// <summary>T-N1-2（ADR-0030 决策 1/2）：仅 <see cref="Category"/> 为 <c>"derived"</c> 时
            /// 有意义，空列表合法（见 <see cref="ComputeDerivedBase"/> 判断记录）。</summary>
            public IReadOnlyList<(Id Stat, double Coefficient)> DerivedFrom { get; }

            /// <summary>T-N1-7（ADR-0030 决策 5）：取自 <c>stat.definition.scope</c>，缺省
            /// <c>"any"</c>；供 <see cref="IStatHost.GetScope"/> 对外暴露，见该接口成员判断记录。</summary>
            public string Scope { get; }

            public StatDefinition(
                Id id, string category, double defaultBase, double? min, double? max,
                Id? conversionRef, IReadOnlyList<(Id Stat, double Coefficient)> derivedFrom, string scope)
            {
                Id = id;
                Category = category;
                DefaultBase = defaultBase;
                Min = min;
                Max = max;
                ConversionRef = conversionRef;
                DerivedFrom = derivedFrom;
                Scope = scope;
            }
        }

        // T-N2-3：本类型此前在此处私有声明的 RatingConversionShape 枚举，已提升为命名空间级公开类型
        // Core.Numbers.StatBlock.RatingConversionShape（见 RatingConversionEvaluator.cs），供
        // Core.Carriers.Item 的装备预算消耗公式共用同一份"点数↔百分比"换算（硬性规则"禁止复制插值
        // 实现"）。本类型（同命名空间）直接引用该公开类型，不再维护独立的一份私有定义。

        private sealed class RatingConversion
        {
            public Id Id { get; }

            public RatingConversionShape Shape { get; }

            /// <summary>x = 单位等级，y = 该等级下每 1% 效果所需点数（T-N0-4 起以通用曲线承载）；仅
            /// <see cref="Shape"/> 为 <see cref="RatingConversionShape.Breakpoints"/> 时有意义，否则为
            /// <see cref="PiecewiseCurve.Empty"/>。</summary>
            public PiecewiseCurve Curve { get; }

            /// <summary>饱和形态除数系数，&gt; 0；仅 <see cref="Shape"/> 为
            /// <see cref="RatingConversionShape.Saturation"/> 时有意义（T-N1-3）。</summary>
            public double K { get; }

            /// <summary>饱和形态输出封顶，&gt; 0，缺省 1；仅 <see cref="Shape"/> 为
            /// <see cref="RatingConversionShape.Saturation"/> 时有意义（T-N1-3）。</summary>
            public double Cap { get; }

            private RatingConversion(Id id, RatingConversionShape shape, PiecewiseCurve curve, double k, double cap)
            {
                Id = id;
                Shape = shape;
                Curve = curve;
                K = k;
                Cap = cap;
            }

            public static RatingConversion CreateBreakpoints(Id id, PiecewiseCurve curve) =>
                new RatingConversion(id, RatingConversionShape.Breakpoints, curve, k: 0, cap: 0);

            public static RatingConversion CreateSaturation(Id id, double k, double cap) =>
                new RatingConversion(id, RatingConversionShape.Saturation, PiecewiseCurve.Empty, k, cap);
        }

        private sealed class UnitStats
        {
            public readonly Dictionary<Id, double> Base = new Dictionary<Id, double>();
            public readonly Dictionary<Id, List<StatModifier>> ModifiersByStat = new Dictionary<Id, List<StatModifier>>();
            public readonly Dictionary<Id, double> Cache = new Dictionary<Id, double>();

            /// <summary>T-N1-4（ADR-0030 决策 2"职业模板可覆盖派生系数"）：本单位当前生效的派生系数
            /// 覆盖——<c>目标派生属性 id → (来源属性 id → 覆盖系数)</c>；<c>null</c> 表示没有任何覆盖
            /// （既有单位的既有行为，绝大多数单位永远是这个状态），与 <see cref="Base"/>/
            /// <see cref="ModifiersByStat"/> 同属运行期状态，reload 不清空（见 <see
            /// cref="SetDerivationCoefficientOverrides"/> 判断记录）。</summary>
            public Dictionary<Id, Dictionary<Id, double>>? DerivationOverrides;
        }
    }
}
