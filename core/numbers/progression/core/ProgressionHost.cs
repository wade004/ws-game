using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;

namespace Core.Numbers.Progression
{
    /// <summary>
    /// <see cref="IProgressionHost"/> 的默认实现（见本模块 README）。构造期从
    /// <see cref="IDataRegistryView"/> 一次性读取 <c>prog.level_curve</c>/<c>prog.xp_source</c>
    /// 建索引；之后只读，不重新查询 registry（与 localization 的 <c>L10nHost</c> 同一惯例）。
    /// <para>
    /// 判断记录（成长写入来源 id）：任务书原文"把该等级累计成长以来源 <c>prog.growth</c> 写入"，
    /// 因此全部单位、全部曲线共用同一个 <see cref="GrowthSourceId"/> 常量作为
    /// <see cref="StatModifierWriter"/>/<see cref="StatModifierRemover"/> 的 <c>sourceId</c> 参数
    /// ——区分不同单位靠 <c>unitId</c> 参数本身，属性宿主按 <c>(unitId, sourceId)</c> 二元组分组
    /// 叠加/移除，同一单位下 <c>prog.growth</c> 来源天然只有一份，不会与其它单位互相覆盖。
    /// </para>
    /// <para>
    /// 判断记录（<see cref="RegisterUnit"/> 不隐式写入成长）：<see cref="RegisterUnit"/> 既用于
    /// "全新单位从 1 级开始"，也用于"读档/初始化到某个已知等级"；后一种场景下单位的属性值应当
    /// 由存档数据本身决定，本模块若在 <see cref="RegisterUnit"/> 内强行按曲线重算并覆盖成长
    /// 修正，会与存档数据产生冲突。因此只有真正经由 <see cref="AddXp"/> 触发的升级路径才会写入
    /// 成长，<see cref="RegisterUnit"/> 本身不触碰 <see cref="StatModifierWriter"/>/
    /// <see cref="StatModifierRemover"/>。
    /// </para>
    /// <para>
    /// 判断记录（满级丢弃分支不发 <see cref="XpGainedEvent"/>）：任务书"到 max_level 后经验不再
    /// 累积（丢弃并记诊断）"与"AddXp...→ 发 progression.xp_gained"两句并列；本模块选择"确实没有
    /// 经验被记入"时不发放事件——<c>xp_gained</c> 语义是"记入了多少经验"，丢弃分支记入量为 0，
    /// 发一个 <c>amount=0</c> 的事件对下游（如浮字提示）没有意义，改用诊断警告表达"发生了什么"。
    /// </para>
    /// </summary>
    public sealed class ProgressionHost : IProgressionHost
    {
        private static readonly Id GrowthSourceId = new Id("prog.growth");

        private sealed class LevelEntry
        {
            public long XpToNext;
            public IReadOnlyList<KeyValuePair<string, double>> Growth = Array.Empty<KeyValuePair<string, double>>();
        }

        private sealed class CurveInfo
        {
            public Id Id;
            public int MaxLevel;
            public List<LevelEntry> Entries = new List<LevelEntry>();
        }

        private sealed class XpSourceInfo
        {
            public long BaseXp;
            public double Weight;
        }

        private sealed class UnitState
        {
            public CurveInfo Curve = null!;
            public int Level;
            public long Xp;
        }

        private readonly IEventBus _bus;
        private readonly StatModifierWriter _statModifierWriter;
        private readonly StatModifierRemover _statModifierRemover;
        private readonly IProgressionDiagnostics _diagnostics;

        private readonly Dictionary<string, CurveInfo> _curves = new Dictionary<string, CurveInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, XpSourceInfo> _xpSources = new Dictionary<string, XpSourceInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, UnitState> _units = new Dictionary<string, UnitState>(StringComparer.Ordinal);

        public ProgressionHost(
            IDataRegistryView registry,
            IEventBus bus,
            StatModifierWriter statModifierWriter,
            StatModifierRemover statModifierRemover,
            IProgressionDiagnostics? diagnostics = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _statModifierWriter = statModifierWriter ?? throw new ArgumentNullException(nameof(statModifierWriter));
            _statModifierRemover = statModifierRemover ?? throw new ArgumentNullException(nameof(statModifierRemover));
            _diagnostics = diagnostics ?? new InMemoryProgressionDiagnostics();

            foreach (var record in registry.GetAll("prog.level_curve"))
            {
                var curve = ParseCurve(record);
                _curves[curve.Id.Value] = curve;
            }

            foreach (var record in registry.GetAll("prog.xp_source"))
            {
                var id = record.GetId("id");
                var baseXp = record.GetInt("base_xp");
                var weight = record.TryGetNumber("weight", out var w) ? w : 1.0;
                _xpSources[id.Value] = new XpSourceInfo { BaseXp = baseXp, Weight = weight };
            }
        }

        // -----------------------------------------------------------------
        // 加载期解析：不信任上游一定跑过 ProgLevelCurveValidationRule，
        // 这里做一次防御性重复校验（与 L10nHost 的 ValidateNoFallbackCycle 同一惯例）。
        // -----------------------------------------------------------------
        private static CurveInfo ParseCurve(DataRecord record)
        {
            var id = record.GetId("id");
            var maxLevel = (int)record.GetInt("max_level");
            var entriesJson = record.GetArray("entries");

            if (entriesJson.Count != maxLevel)
            {
                throw new ArgumentException(
                    $"曲线 \"{id}\" 的 entries 数量（{entriesJson.Count}）与 max_level（{maxLevel}）不一致" +
                    "（应已被 ProgLevelCurveValidationRule 拦截，见该规则的判断记录）");
            }

            var entries = new List<LevelEntry>(entriesJson.Count);
            for (var i = 0; i < entriesJson.Count; i++)
            {
                var expectedLevel = i + 1;
                if (!(entriesJson[i] is JsonObject entryObj))
                {
                    throw new ArgumentException($"曲线 \"{id}\" entries[{i}] 不是对象");
                }

                var level = (int)RequireInt(entryObj, "level", id, i);
                if (level != expectedLevel)
                {
                    throw new ArgumentException(
                        $"曲线 \"{id}\" entries[{i}] 的 level 应为 {expectedLevel}，实际 {level}" +
                        "（应已被 ProgLevelCurveValidationRule 拦截）");
                }

                var xpToNext = RequireInt(entryObj, "xp_to_next", id, i);

                var growth = new List<KeyValuePair<string, double>>();
                if (entryObj.TryGetValue("growth", out var growthValue) && growthValue is JsonObject growthObj)
                {
                    foreach (var kv in growthObj)
                    {
                        if (kv.Value is JsonNumber num)
                        {
                            growth.Add(new KeyValuePair<string, double>(kv.Key, num.Value));
                        }
                    }
                }

                entries.Add(new LevelEntry { XpToNext = xpToNext, Growth = growth });
            }

            return new CurveInfo { Id = id, MaxLevel = maxLevel, Entries = entries };
        }

        private static long RequireInt(JsonObject entryObj, string field, Id curveId, int index)
        {
            if (entryObj.TryGetValue(field, out var v) && v is JsonNumber n && n.TryGetInt64(out var value))
            {
                return value;
            }
            throw new ArgumentException($"曲线 \"{curveId}\" entries[{index}].{field} 缺失或不是整数");
        }

        // -----------------------------------------------------------------
        // IProgressionHost
        // -----------------------------------------------------------------

        public void RegisterUnit(Id unitId, Id curveId, int startLevel = 1)
        {
            var curve = GetCurveOrThrow(curveId);
            if (startLevel < 1 || startLevel > curve.MaxLevel)
            {
                throw new ArgumentException(
                    $"起始等级 {startLevel} 超出曲线 \"{curveId}\" 的范围 [1,{curve.MaxLevel}]", nameof(startLevel));
            }

            _units[unitId.Value] = new UnitState { Curve = curve, Level = startLevel, Xp = 0 };
        }

        public int GetLevel(Id unitId) => GetUnitOrThrow(unitId).Level;

        public long GetXp(Id unitId) => GetUnitOrThrow(unitId).Xp;

        public long GetXpToNext(Id unitId)
        {
            var unit = GetUnitOrThrow(unitId);
            return unit.Curve.Entries[unit.Level - 1].XpToNext;
        }

        public void AddXp(Id unitId, Id sourceId, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "经验增量不能为负");

            var unit = GetUnitOrThrow(unitId);

            if (unit.Level >= unit.Curve.MaxLevel)
            {
                _diagnostics.Warn(
                    $"单位 \"{unitId}\" 已达曲线 \"{unit.Curve.Id}\" 满级（{unit.Curve.MaxLevel}），" +
                    $"丢弃经验 {amount}（来源 \"{sourceId}\"）");
                return;
            }

            _bus.PublishImmediate(new XpGainedEvent(unitId, sourceId, amount));
            unit.Xp += amount;

            var leveledUp = false;
            while (unit.Level < unit.Curve.MaxLevel)
            {
                var xpToNext = unit.Curve.Entries[unit.Level - 1].XpToNext;
                if (xpToNext <= 0 || unit.Xp < xpToNext)
                {
                    break;
                }

                unit.Xp -= xpToNext;
                var newLevel = unit.Level + 1;
                _bus.PublishImmediate(new LevelUpEvent(unitId, unit.Level, newLevel));
                unit.Level = newLevel;
                leveledUp = true;
            }

            if (unit.Level >= unit.Curve.MaxLevel && unit.Xp > 0)
            {
                _diagnostics.Warn(
                    $"单位 \"{unitId}\" 升至曲线 \"{unit.Curve.Id}\" 满级（{unit.Curve.MaxLevel}）过程中，" +
                    $"残余经验 {unit.Xp} 被丢弃");
                unit.Xp = 0;
            }

            if (leveledUp)
            {
                ApplyGrowth(unitId, unit);
            }
        }

        public void GrantFromSource(Id unitId, Id xpSourceId, double multiplier = 1)
        {
            if (!_xpSources.TryGetValue(xpSourceId.Value, out var source))
            {
                throw new ArgumentException($"未知经验来源 \"{xpSourceId}\"", nameof(xpSourceId));
            }

            var raw = source.BaseXp * source.Weight * multiplier;
            var amount = raw <= 0 ? 0L : (long)Math.Round(raw, MidpointRounding.AwayFromZero);
            AddXp(unitId, xpSourceId, amount);
        }

        // -----------------------------------------------------------------
        // 内部
        // -----------------------------------------------------------------

        private void ApplyGrowth(Id unitId, UnitState unit)
        {
            var order = new List<string>();
            var totals = new Dictionary<string, double>(StringComparer.Ordinal);

            for (var level = 2; level <= unit.Level; level++)
            {
                foreach (var kv in unit.Curve.Entries[level - 1].Growth)
                {
                    if (!totals.ContainsKey(kv.Key))
                    {
                        order.Add(kv.Key);
                        totals[kv.Key] = 0;
                    }
                    totals[kv.Key] += kv.Value;
                }
            }

            _statModifierRemover(unitId, GrowthSourceId);
            foreach (var statKey in order)
            {
                _statModifierWriter(unitId, new Id(statKey), "flat", totals[statKey], GrowthSourceId);
            }
        }

        private CurveInfo GetCurveOrThrow(Id curveId)
        {
            if (!_curves.TryGetValue(curveId.Value, out var curve))
            {
                throw new ArgumentException($"未知等级曲线 \"{curveId}\"", nameof(curveId));
            }
            return curve;
        }

        private UnitState GetUnitOrThrow(Id unitId)
        {
            if (!_units.TryGetValue(unitId.Value, out var unit))
            {
                throw new ArgumentException($"单位 \"{unitId}\" 未通过 RegisterUnit 注册", nameof(unitId));
            }
            return unit;
        }
    }
}
