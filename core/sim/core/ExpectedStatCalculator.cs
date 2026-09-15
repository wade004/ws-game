using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Numbers.StatBlock;

namespace Core.Sim
{
    /// <summary>
    /// T-N6-3a（ADR-0035 决策 2；数值总纲第 4.4 节）：期望属性求值组件——给定职业与期望品质，按
    /// <c>期望属性(L) = 等级成长(L) + Σ槽位 预算反解(E(L), 期望品质, 槽位)</c> 算出"标准玩家"在
    /// 每个等级上，主属性/派生属性/换算（百分比）属性的期望最终值（对 <c>stat.definition</c> 全表逐条
    /// 求值，不只是主属性）。
    /// <para>
    /// 判断记录（未找到可复用的"独立求值组件"，最小化自实现）：任务书提示"派生求值优先复用既有独立
    /// 求值组件（N3 落地时为预算分析准备的、不依赖活体单位的属性求值）……若不存在，最小化实现"。
    /// 检索范围：<c>core/rules/stat/</c>（该目录事实上不存在——属性模块是 <c>core/numbers/stat_block</c>，
    /// 不是 <c>core/rules</c> 下的模块）与 <c>SkillBudgetAnalyzer</c> 附近（<c>core/rules/skill/core/
    /// SkillBudgetAnalyzer.cs</c> 全文、<c>core/rules/common/contracts/ISkillBudgetAnchorProvider.cs</c>）：
    /// 两处都只有"消费"期望缩放属性值的钩子（<see cref="Core.Rules.Common.ISkillBudgetAnchorProvider
    /// .GetExpectedScalingStatValue"/>），没有"给定基础值集合、按 <c>stat.definition</c> 派生规则独立
    /// 算出最终值"这一步的既有实现——落地这一步的唯一现成逻辑是 <c>Core.Numbers.StatBlock.StatHost
    /// .ComputeFinal</c>/<c>ComputeDerivedBase</c>/<c>ResolveBaseValue</c>/<c>ConvertRating</c> 四个
    /// 私有方法，但它们要求先 <c>RegisterUnit</c> 出一个"活体单位"、经 <c>SetBase</c>/<c>AddModifier</c>
    /// 写入状态后才能求值，与"不依赖活体单位"这条任务书前提矛盾，且是私有方法，本模块不能反射越权
    /// 调用。本类型因此按 <c>StatHost.ComputeFinal</c> 同一套三段式聚合公式（基础值 + 装备贡献 → 百分比
    /// 属性经 <see cref="RatingConversionEvaluator"/> 换算 → <c>clamp</c> 夹取；派生属性递归取来源最终
    /// 值）独立最小实现，不复制 <c>StatHost</c> 任何私有代码，只复用它已公开的两个纯函数工具——
    /// <see cref="RatingConversionEvaluator"/>（点数→百分比）与 <see cref="ItemBudgetCurve
    /// .BuildStatBudgetInfo(IDataRegistryView, Id)"/>（按职业覆盖解析 <c>stat.weight</c>/换算曲线元信息，
    /// 与装备预算校验共用同一份权重解析，不重新发明一遍 <c>class_overrides</c> 查找逻辑）。
    /// </para>
    /// <para>
    /// 判断记录（<c>Σ槽位</c> 的槽位范围与 <c>statMix</c> 来源）：07 第 1.2 节武器槽的"强度"由
    /// <c>weapon_profile</c>（秒伤曲线口径）承载、不占用属性词条预算（见 <c>core/sim/tests/data/
    /// README.md</c> 判断记录 3），因此本类型只对 <c>item.slot_definition.is_weapon != true</c> 且
    /// <c>is_equipment != false</c> 的槽位调用 <see cref="IBudgetSolver.Solve"/> 求和；<c>statMix</c>
    /// 取该职业在 <c>stat.weight</c>（含 <c>class_overrides</c>）登记的全部属性，按班 <c>weight</c> 归一
    /// 化（任务书"statMix 来自该职业 stat.weight"）——本数据集 9 项权重均为 1.0，因此 9 项各占 1/9，
    /// 与 <c>core/sim/tests/data/README.md</c>"装备预算手算口径"描述的简化前提一致。<c>stat.weight</c>
    /// 未登记权重的属性不参与 statMix（<see cref="IBudgetSolver.Solve"/> 要求每项 ratio &gt; 0，登记
    /// 权重为 0 的属性同样被排除，否则反解会抛 <see cref="ArgumentException"/>，见该接口判断记录）。
    /// </para>
    /// <para>
    /// 判断记录（<c>stat.armor</c> 是否要复刻 <c>EquipmentHost.ApplyArmorValue</c> 的护甲曲线分支）：
    /// 真实装备-穿戴管线里护甲值走独立的 <c>item.armor_curve</c> 曲线，不经 <c>stats[]</c>/词缀预算反解
    /// （见 <c>EquipmentHost.ApplyArmorValue</c>）；但本类型是"标准玩家"这一独立、简化的期望值抽象——
    /// 它与 <see cref="StandardPlayerBuilder"/> 实际生成、装备的物品实例是两条独立求值路径，互不要求
    /// 数值相等（<see cref="StandardPlayerBuilder"/> 对着真实装备-穿戴管线复刻同一套护甲曲线公式，
    /// 见该类型判断记录）。<c>stat.armor</c> 若登记了 <c>stat.weight</c> 权重（本数据集确有），按与
    /// 其它 8 项属性完全一致的方式计入 statMix、参与预算反解——不额外分支复刻护甲曲线，避免本类型
    /// 同时维护两套"预算反解"与"护甲曲线"求值路径、徒增复杂度且没有任何契约条文要求两者必须走同一
    /// 分支（ADR-0035 决策 2/数值总纲第 4.4 节原文只给"Σ槽位 预算反解"一条公式，未点名护甲需要特殊
    /// 处理）。
    /// </para>
    /// </summary>
    public sealed class ExpectedStatCalculator
    {
        private readonly IDataRegistryView _view;
        private readonly AnchorTable _anchors;
        private readonly Id _classId;
        private readonly Id _qualityId;
        private readonly IBudgetSolver _budgetSolver;
        private readonly Id _budgetCurveId;

        private readonly Dictionary<int, IReadOnlyDictionary<Id, double>> _cache = new Dictionary<int, IReadOnlyDictionary<Id, double>>();

        private readonly Dictionary<string, double> _classBaseStats;
        private readonly Dictionary<int, Dictionary<string, double>> _growthByLevel;
        private readonly Dictionary<(string Target, string Source), double> _derivationOverrides;
        private readonly Dictionary<string, StatDef> _statDefs;
        private readonly List<Id> _equipmentSlotIds;
        private readonly IReadOnlyList<(Id Stat, double Ratio)> _statMix;
        private readonly IReadOnlyDictionary<Id, ItemBudgetCurve.StatBudgetInfo> _statInfo;

        private readonly struct StatDef
        {
            public string Category { get; }
            public IReadOnlyList<(string Source, double Coefficient)> DerivedFrom { get; }
            public double? ClampMin { get; }
            public double? ClampMax { get; }
            public double DefaultBase { get; }

            public StatDef(string category, IReadOnlyList<(string, double)> derivedFrom, double? clampMin, double? clampMax, double defaultBase)
            {
                Category = category;
                DerivedFrom = derivedFrom;
                ClampMin = clampMin;
                ClampMax = clampMax;
                DefaultBase = defaultBase;
            }
        }

        /// <param name="view">已装载的数据只读视图（<c>arch.class</c>/<c>stat.definition</c>/
        /// <c>stat.weight</c>/<c>stat.rating_conversion</c>/<c>prog.level_curve</c>/
        /// <c>item.slot_definition</c>/<c>item.quality_definition</c> 均须已加载）。</param>
        /// <param name="anchors">已构造的 <c>sim.anchor</c> 类型化读取，供 <see cref="Compute"/> 查
        /// <c>E(L)</c>。</param>
        /// <param name="classId">标准玩家职业，指向 <c>arch.class</c>。</param>
        /// <param name="qualityId">标准玩家期望装备品质，指向 <c>item.quality_definition</c>。</param>
        /// <param name="budgetSolver">预算反解实现，通常 <c>new Core.Carriers.Item.BudgetSolver()</c>
        /// （纯函数、无状态，见该类型判断记录，调用方可自由传入自己的实例）。</param>
        /// <param name="budgetCurveId"><c>item.budget_curve</c> 曲线 id，缺省
        /// <see cref="Core.Carriers.Assembly.CarriersSchemaCatalog.DefaultItemBudgetCurveId"/>
        /// （<c>item.budget.default</c>，与运行期 <c>ItemOptions.BudgetCurveId</c> 默认值一致）。</param>
        public ExpectedStatCalculator(
            IDataRegistryView view,
            AnchorTable anchors,
            Id classId,
            Id qualityId,
            IBudgetSolver budgetSolver,
            Id? budgetCurveId = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _anchors = anchors ?? throw new ArgumentNullException(nameof(anchors));
            _classId = classId;
            _qualityId = qualityId;
            _budgetSolver = budgetSolver ?? throw new ArgumentNullException(nameof(budgetSolver));
            _budgetCurveId = budgetCurveId ?? new Id("item.budget.default");

            var classRecord = view.Get("arch.class", classId)
                ?? throw new ArgumentException($"arch.class 未登记 \"{classId}\"", nameof(classId));

            _classBaseStats = ReadNumberObject(classRecord.TryGetObject("base_stats", out var baseStats) ? baseStats : null);

            _derivationOverrides = new Dictionary<(string, string), double>();
            if (classRecord.TryGetArray("derivation_overrides", out var overridesArr))
            {
                foreach (var raw in overridesArr)
                {
                    if (raw is JsonObject ov &&
                        ov.TryGetValue("stat", out var statRaw) && statRaw is JsonString statStr &&
                        ov.TryGetValue("source", out var sourceRaw) && sourceRaw is JsonString sourceStr &&
                        ov.TryGetValue("coefficient", out var coeffRaw) && coeffRaw is JsonNumber coeffNum)
                    {
                        _derivationOverrides[(statStr.Value, sourceStr.Value)] = coeffNum.Value;
                    }
                }
            }

            _growthByLevel = new Dictionary<int, Dictionary<string, double>>();
            if (classRecord.TryGetId("level_curve_ref", out var curveRef))
            {
                var curveRecord = view.Get("prog.level_curve", curveRef);
                if (curveRecord != null && curveRecord.TryGetArray("entries", out var entries))
                {
                    foreach (var raw in entries)
                    {
                        if (raw is JsonObject entry &&
                            entry.TryGetValue("level", out var levelRaw) && levelRaw is JsonNumber levelNum)
                        {
                            var growth = entry.TryGetValue("growth", out var growthRaw) && growthRaw is JsonObject growthObj
                                ? ReadNumberObject(growthObj)
                                : new Dictionary<string, double>();
                            _growthByLevel[(int)levelNum.Value] = growth;
                        }
                    }
                }
            }

            _statDefs = new Dictionary<string, StatDef>();
            foreach (var def in view.GetAll("stat.definition"))
            {
                var category = def.GetString("category");
                var derivedFrom = new List<(string, double)>();
                if (def.TryGetArray("derived_from", out var derivedArr))
                {
                    foreach (var raw in derivedArr)
                    {
                        if (raw is JsonObject entry &&
                            entry.TryGetValue("stat", out var s) && s is JsonString ss &&
                            entry.TryGetValue("coefficient", out var c) && c is JsonNumber cn)
                        {
                            derivedFrom.Add((ss.Value, cn.Value));
                        }
                    }
                }

                double? clampMin = null, clampMax = null;
                if (def.TryGetObject("clamp", out var clamp))
                {
                    if (clamp.TryGetValue("min", out var minRaw) && minRaw is JsonNumber minNum) clampMin = minNum.Value;
                    if (clamp.TryGetValue("max", out var maxRaw) && maxRaw is JsonNumber maxNum) clampMax = maxNum.Value;
                }

                var defaultBase = def.TryGetNumber("default_base", out var db) ? db : 0.0;

                _statDefs[def.GetString("id")] = new StatDef(category, derivedFrom, clampMin, clampMax, defaultBase);
            }

            _equipmentSlotIds = new List<Id>();
            foreach (var slot in view.GetAll("item.slot_definition"))
            {
                var isEquipment = !slot.TryGetBool("is_equipment", out var eqFlag) || eqFlag;
                var isWeapon = slot.TryGetBool("is_weapon", out var wpFlag) && wpFlag;
                if (isEquipment && !isWeapon)
                {
                    _equipmentSlotIds.Add(slot.GetId("id"));
                }
            }

            _statInfo = ItemBudgetCurve.BuildStatBudgetInfo(view, classId);

            var mix = new List<(Id, double)>();
            double totalWeight = 0;
            var weightRows = new List<(Id Stat, double Weight)>();
            foreach (var w in view.GetAll("stat.weight"))
            {
                var statId = w.GetId("stat");
                var weight = _statInfo.TryGetValue(statId, out var info) ? info.Weight : 0.0;
                if (weight > 0)
                {
                    weightRows.Add((statId, weight));
                    totalWeight += weight;
                }
            }
            if (totalWeight > 0)
            {
                foreach (var (statId, weight) in weightRows)
                {
                    mix.Add((statId, weight / totalWeight));
                }
            }
            _statMix = mix;
        }

        /// <summary>该等级"标准玩家"的期望属性表——<c>stat.definition</c> 全表逐条的期望最终值。构造期
        /// 一次性解析、按等级缓存（同 <see cref="AnchorTable"/> 判断记录"构造期一次性解析"一贯做法，
        /// 缓存键是等级，不是整表）。</summary>
        public IReadOnlyDictionary<Id, double> Compute(int level)
        {
            if (_cache.TryGetValue(level, out var cached))
            {
                return cached;
            }

            var result = ComputeCore(level);
            _cache[level] = result;
            return result;
        }

        private IReadOnlyDictionary<Id, double> ComputeCore(int level)
        {
            var anchorRow = _anchors.Get(level);
            var itemLevel = (int)Math.Round(anchorRow.ExpectedItemLevel, MidpointRounding.AwayFromZero);
            if (itemLevel < 1) itemLevel = 1;

            var equipmentFlat = new Dictionary<string, double>();
            if (_statMix.Count > 0)
            {
                foreach (var slotId in _equipmentSlotIds)
                {
                    var solved = _budgetSolver.Solve(itemLevel, _qualityId, slotId, _statMix, _budgetCurveId, _view);
                    foreach (var kv in solved.Values)
                    {
                        equipmentFlat.TryGetValue(kv.Key.Value, out var existing);
                        equipmentFlat[kv.Key.Value] = existing + kv.Value;
                    }
                }
            }

            var resolved = new Dictionary<string, double>(StringComparer.Ordinal);
            var resolving = new HashSet<string>(StringComparer.Ordinal);

            double Resolve(string statId)
            {
                if (resolved.TryGetValue(statId, out var already))
                {
                    return already;
                }

                if (!resolving.Add(statId))
                {
                    // 派生环应已被 StatDefinitionDerivationCycleValidationRule 在内容校验期阻断；本
                    // 分支是防御性兜底（调用方绕过校验直接注入数据），避免无限递归耗尽栈。
                    throw new InvalidOperationException($"检测到属性派生环，涉及 \"{statId}\"（应已被内容校验阻断）");
                }

                if (!_statDefs.TryGetValue(statId, out var def))
                {
                    resolving.Remove(statId);
                    resolved[statId] = 0.0;
                    return 0.0;
                }

                double baseValue;
                if (def.Category == "derived")
                {
                    double sum = 0;
                    foreach (var (source, coefficientDefault) in def.DerivedFrom)
                    {
                        var coefficient = _derivationOverrides.TryGetValue((statId, source), out var ov)
                            ? ov
                            : coefficientDefault;
                        sum += Resolve(source) * coefficient;
                    }
                    baseValue = sum;
                }
                else
                {
                    baseValue = _classBaseStats.TryGetValue(statId, out var cb) ? cb : def.DefaultBase;
                    for (var lvl = 2; lvl <= level; lvl++)
                    {
                        if (_growthByLevel.TryGetValue(lvl, out var growth) && growth.TryGetValue(statId, out var inc))
                        {
                            baseValue += inc;
                        }
                    }
                }

                equipmentFlat.TryGetValue(statId, out var flat);
                var value = baseValue + flat;

                if (def.Category == "percent" && _statInfo.TryGetValue(new Id(statId), out var info) &&
                    info.IsPercentCategory && info.ConversionShape.HasValue)
                {
                    value = RatingConversionEvaluator.ToPercent(
                        info.ConversionShape.Value, info.ConversionCurve, info.ConversionK, info.ConversionCap, value, level);
                }

                if (def.ClampMin.HasValue) value = Math.Max(value, def.ClampMin.Value);
                if (def.ClampMax.HasValue) value = Math.Min(value, def.ClampMax.Value);

                resolving.Remove(statId);
                resolved[statId] = value;
                return value;
            }

            foreach (var statId in _statDefs.Keys)
            {
                Resolve(statId);
            }

            return resolved.ToDictionary(kv => new Id(kv.Key), kv => kv.Value);
        }

        private static Dictionary<string, double> ReadNumberObject(JsonObject? obj)
        {
            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            if (obj == null)
            {
                return result;
            }

            foreach (var kv in obj)
            {
                if (kv.Value is JsonNumber num)
                {
                    result[kv.Key] = num.Value;
                }
            }

            return result;
        }
    }
}
