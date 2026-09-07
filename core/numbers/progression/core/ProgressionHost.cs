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
                var oldLevel = unit.Level;
                var newLevel = oldLevel + 1;
                // N08 收边补齐（外部审计 68c9bed，P2）：先提交等级、再发布 progression.level_up——
                // PublishImmediate 是同步派发，修复前 unit.Level 在发布时仍是旧值，事件处理器内如果
                // 反查 GetLevel(unitId)（而不是只读事件自带的 NewLevel 字段）会读到旧等级，
                // RulesAssembly 一类按等级重算派生属性的消费者可能因此用旧等级计算，直到下一次别的
                // 属性写入才被动补救（见外部审计 N08）。事件本身携带的 OldLevel/NewLevel 字段值不变，
                // 只调整"字段赋值"与"发布事件"两个语句的先后顺序。
                unit.Level = newLevel;
                _bus.PublishImmediate(new LevelUpEvent(unitId, oldLevel, newLevel));
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

        // -----------------------------------------------------------------
        // 存档（W1 收边补齐：10 第 2.2 节 player.progression 字段，见 core/numbers/progression/
        // core/ProgressionPersistable.cs 判断记录——本模块不直接实现 IPersistable，理由与
        // core/carriers/unit/core/UnitPersistable.cs 同款静态工厂模式一致：本模块可能同时管理
        // 多个单位（NPC 也可注册 Progression），但存档只关心"哪个单位是玩家"，这一决定权在调用方
        // （游戏层引导代码知道谁是玩家），本模块自己不应该假设"唯一一个已注册单位就是玩家"。
        // -----------------------------------------------------------------

        /// <summary>序列化 <paramref name="unitId"/> 当前的 Progression 状态（<c>curve_id</c>/
        /// <c>level</c>/<c>xp</c>）。<paramref name="unitId"/> 必须已经过 <see cref="RegisterUnit"/>
        /// 注册，否则抛 <see cref="ArgumentException"/>（与其它公开方法一致的前置校验，见
        /// <see cref="GetUnitOrThrow"/>）。</summary>
        public JsonValue SaveUnit(Id unitId)
        {
            var unit = GetUnitOrThrow(unitId);
            return new JsonObjectBuilder()
                .Add("curve_id", new JsonString(unit.Curve.Id.Value))
                .Add("level", new JsonNumber(unit.Level))
                .Add("xp", new JsonNumber(unit.Xp))
                .Build();
        }

        /// <summary>
        /// 读档专用状态恢复入口——与 <see cref="RegisterUnit"/> 的区别是允许指定非零
        /// <paramref name="xp"/>（<see cref="RegisterUnit"/> 恒 <c>xp=0</c>，语义是"全新单位从
        /// 起始等级开始"，不适合读档场景）。恢复状态后按 <see cref="ApplyGrowth"/> 同一逻辑重新
        /// 聚合 1..<paramref name="level"/> 的全部成长修正——见 10 第 2.5 节"属性快照默认不存……
        /// 存基础来源（装备、已知天赋等）后可在读档时重新聚合"，成长修正是该原则里的"基础来源"
        /// 之一，本模块负责在读档时重建它（<see cref="StatModifierWriter"/> 写入的修正不参与
        /// 存档，读档后必须由持有方重新写入）。<b>调用方必须确保 <paramref name="unitId"/> 已在
        /// 属性宿主（StatHost）完成注册</b>——本方法与 <see cref="ApplyGrowth"/> 一样直接调用
        /// <see cref="StatModifierWriter"/>/<see cref="StatModifierRemover"/>，未注册的单位会被
        /// 属性宿主自身的前置校验拒绝（本模块不重复做这层校验，职责边界见类型注释"不依赖具体属性
        /// 宿主实现"）。
        /// </summary>
        public void RestoreState(Id unitId, Id curveId, int level, long xp)
        {
            var curve = GetCurveOrThrow(curveId);
            if (level < 1 || level > curve.MaxLevel)
            {
                throw new ArgumentException(
                    $"读档等级 {level} 超出曲线 \"{curveId}\" 的范围 [1,{curve.MaxLevel}]", nameof(level));
            }

            var unit = new UnitState { Curve = curve, Level = level, Xp = xp };
            _units[unitId.Value] = unit;
            ApplyGrowth(unitId, unit);
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
