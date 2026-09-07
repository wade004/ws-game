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
    /// </summary>
    public sealed class StatHost : IStatHost
    {
        private readonly IEventBus _bus;
        private readonly StatHostOptions _options;

        private readonly Dictionary<Id, StatDefinition> _definitions = new Dictionary<Id, StatDefinition>();
        private readonly Dictionary<Id, RatingConversion> _ratingConversions = new Dictionary<Id, RatingConversion>();
        private readonly Dictionary<Id, UnitStats> _units = new Dictionary<Id, UnitStats>();
        private readonly List<string> _warnings = new List<string>();

        /// <summary>抗性维度关闭时，<see cref="GetStat"/> 命中 <c>group: resistance</c> 属性
        /// 会在这里记一条警告（见 <see cref="StatHostOptions.EnableResistanceGroup"/>）。供测试与
        /// 宿主诊断读取，本模块不依赖任何 L-1 引擎适配层的日志出口（同 event_bus 的
        /// <c>IEventDiagnostics</c> 设计取舍：不强制调用方先备好引擎适配层实现）。</summary>
        public IReadOnlyList<string> Warnings => _warnings;

        public StatHost(IDataRegistryView registry, IEventBus bus, StatHostOptions? options = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _options = options ?? new StatHostOptions();

            LoadDefinitions(registry);
            if (_options.EnableRatingConversion)
            {
                LoadRatingConversions(registry);
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

                var group = record.GetString("group");

                double defaultBase = 0;
                record.TryGetNumber("default_base", out defaultBase);

                double? min = null;
                if (record.TryGetNumber("min", out var minValue)) min = minValue;

                double? max = null;
                if (record.TryGetNumber("max", out var maxValue)) max = maxValue;

                var isRating = record.TryGetBool("is_rating", out var isRatingValue) && isRatingValue;

                Id? ratingRef = null;
                if (record.TryGetId("rating_conversion_ref", out var refValue)) ratingRef = refValue;

                _definitions[id] = new StatDefinition(id, group, defaultBase, min, max, isRating, ratingRef);
            }
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
                var entriesArray = record.GetArray("entries");

                var entries = new List<RatingEntry>(entriesArray.Count);
                for (int e = 0; e < entriesArray.Count; e++)
                {
                    if (!(entriesArray[e] is JsonObject entryObj))
                    {
                        throw new InvalidOperationException(
                            $"stat.rating_conversion[{id}].entries[{e}] 不是对象");
                    }
                    if (!entryObj.TryGetValue("level", out var levelVal) || !(levelVal is JsonNumber levelNum)
                        || !levelNum.TryGetInt64(out var level))
                    {
                        throw new InvalidOperationException(
                            $"stat.rating_conversion[{id}].entries[{e}] 缺少合法的 \"level\"（Int）");
                    }
                    if (!entryObj.TryGetValue("points_per_percent", out var pppVal) || !(pppVal is JsonNumber pppNum))
                    {
                        throw new InvalidOperationException(
                            $"stat.rating_conversion[{id}].entries[{e}] 缺少合法的 \"points_per_percent\"（Number）");
                    }
                    entries.Add(new RatingEntry((int)level, pppNum.Value));
                }
                entries.Sort((a, b) => a.Level.CompareTo(b.Level));

                _ratingConversions[id] = new RatingConversion(id, entries);
            }
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
        }

        public double GetBase(Id unitId, Id stat)
        {
            var unit = RequireUnit(unitId);
            var def = RequireDefinition(stat);
            return unit.Base.TryGetValue(stat, out var value) ? value : def.DefaultBase;
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
            }
        }

        // -----------------------------------------------------------------
        // 三段式聚合
        // -----------------------------------------------------------------

        private double ComputeFinal(Id unitId, UnitStats unit, StatDefinition def)
        {
            if (def.Group == "resistance" && !_options.EnableResistanceGroup)
            {
                _warnings.Add($"属性 \"{def.Id}\" 属于 resistance 分组，但 EnableResistanceGroup=false，GetStat 恒返回 0");
                return 0.0;
            }

            var baseValue = unit.Base.TryGetValue(def.Id, out var b) ? b : def.DefaultBase;

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
            if (!_ratingConversions.TryGetValue(def.RatingConversionRef.Value, out var conversion) || conversion.Entries.Count == 0)
            {
                return rawValue;
            }

            var level = _options.LevelLookup?.Invoke(unitId) ?? 1;
            var entries = conversion.Entries;

            if (level <= entries[0].Level)
            {
                return DivideByPointsPerPercent(rawValue, entries[0].PointsPerPercent);
            }
            if (level >= entries[entries.Count - 1].Level)
            {
                return DivideByPointsPerPercent(rawValue, entries[entries.Count - 1].PointsPerPercent);
            }

            var low = entries[0];
            var high = entries[entries.Count - 1];
            for (int i = 0; i < entries.Count - 1; i++)
            {
                if (level >= entries[i].Level && level <= entries[i + 1].Level)
                {
                    low = entries[i];
                    high = entries[i + 1];
                    break;
                }
            }

            var t = (level - low.Level) / (double)(high.Level - low.Level);
            var pointsPerPercent = low.PointsPerPercent + t * (high.PointsPerPercent - low.PointsPerPercent);
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
            public string Group { get; }
            public double DefaultBase { get; }
            public double? Min { get; }
            public double? Max { get; }
            public bool IsRating { get; }
            public Id? RatingConversionRef { get; }

            public StatDefinition(Id id, string group, double defaultBase, double? min, double? max, bool isRating, Id? ratingConversionRef)
            {
                Id = id;
                Group = group;
                DefaultBase = defaultBase;
                Min = min;
                Max = max;
                IsRating = isRating;
                RatingConversionRef = ratingConversionRef;
            }
        }

        private readonly struct RatingEntry
        {
            public int Level { get; }
            public double PointsPerPercent { get; }

            public RatingEntry(int level, double pointsPerPercent)
            {
                Level = level;
                PointsPerPercent = pointsPerPercent;
            }
        }

        private sealed class RatingConversion
        {
            public Id Id { get; }
            public List<RatingEntry> Entries { get; }

            public RatingConversion(Id id, List<RatingEntry> entries)
            {
                Id = id;
                Entries = entries;
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
