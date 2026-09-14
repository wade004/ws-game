using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Rules.Combat
{
    /// <summary>曲线种类（见 06 第 4.3 节"公式形态为参数化曲线...架构只固定输入输出契约，
    /// 不固定具体数学式"）：本模块提供两种常见形态供数据选用，均只输出 [0, MaxReduction] 的
    /// 减免百分比。</summary>
    public enum ResistCurveKind
    {
        /// <summary>饱和曲线：<c>reduction = value / (value + k × attackerLevel)</c>。</summary>
        Saturation,

        /// <summary>分段线性插值：按 <see cref="ResistCurve.Entries"/>（已按 value 升序）插值。</summary>
        Table,
    }

    /// <summary>一条 <c>entries</c> 折线点。</summary>
    public readonly struct ResistCurveEntry
    {
        public double Value { get; }

        public double Reduction { get; }

        public ResistCurveEntry(double value, double reduction)
        {
            Value = value;
            Reduction = reduction;
        }
    }

    /// <summary>
    /// 一条 <c>combat.resist_curve</c> 记录的强类型视图 + 求值逻辑（见 06 第 4.3 节）。
    /// <see cref="ComputeReduction"/> 是本模块对"输入护甲/抗性值、输出 0~1 减免百分比"这一
    /// 契约的具体落地，结果恒夹到 <c>[0, MaxReduction]</c>。
    /// </summary>
    public sealed class ResistCurve
    {
        public Id Id { get; }

        public Id School { get; }

        public ResistCurveKind Kind { get; }

        public double K { get; }

        public IReadOnlyList<ResistCurveEntry> Entries { get; }

        public double MaxReduction { get; }

        /// <summary>T-N0-5：<see cref="ResistCurveKind.Table"/> 分支的插值载体——由 <see cref="Entries"/>
        /// 构造的通用分段线性曲线（x = 输入值，y = 减免）。<see cref="Entries"/> 保持原样公开（ABI 不变），
        /// 求值改为委托 <see cref="PiecewiseCurve.Evaluate"/>：越界夹取端点、段内 <c>lo + t × (hi − lo)</c>、
        /// 空表为 0，与此前本类型手写的 <c>InterpolateTable</c> 逐运算相同（合法数据下逐位一致；数据经
        /// <c>resist_curve_entries_monotonic</c> 保证 value 严格递增，通用曲线的稳定排序不改变顺序）。
        /// 饱和分支不经过本字段，公式原样保留（T-N0-5 禁止事项）。</summary>
        private readonly PiecewiseCurve _tableCurve;

        public ResistCurve(DataRecord record)
        {
            Id = record.GetId("id");
            School = record.GetId("school");

            var kindText = record.GetString("kind");
            Kind = kindText switch
            {
                "saturation" => ResistCurveKind.Saturation,
                "table" => ResistCurveKind.Table,
                _ => throw new DataFieldException(record.Table.Name, record.Key, "kind",
                    $"非法取值 \"{kindText}\"，只允许 \"saturation\" 或 \"table\""),
            };

            K = record.TryGetNumber("k", out var k) ? k : 0.0;
            MaxReduction = record.TryGetNumber("max_reduction", out var maxReduction) ? maxReduction : 0.75;

            var entries = new List<ResistCurveEntry>();
            if (record.TryGetArray("entries", out var arr))
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    if (!(arr[i] is JsonObject entryObj)
                        || !entryObj.TryGetValue("value", out var v) || !(v is JsonNumber vn)
                        || !entryObj.TryGetValue("reduction", out var r) || !(r is JsonNumber rn))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, "entries",
                            $"entries[{i}] 必须是 {{value: Number, reduction: Number}}");
                    }

                    entries.Add(new ResistCurveEntry(vn.Value, rn.Value));
                }
            }

            Entries = entries;

            var points = new CurvePoint[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                points[i] = new CurvePoint(entries[i].Value, entries[i].Reduction);
            }
            _tableCurve = new PiecewiseCurve(points);
        }

        /// <summary><paramref name="value"/> 为护甲/抗性属性当前值，<paramref name="attackerLevel"/>
        /// 为攻击者等级（见 06 第 4.3 节饱和曲线示例形态）。</summary>
        public double ComputeReduction(double value, int attackerLevel)
        {
            double raw;
            switch (Kind)
            {
                case ResistCurveKind.Saturation:
                    var denom = value + K * attackerLevel;
                    raw = denom <= 0.0 ? 0.0 : value / denom;
                    break;

                case ResistCurveKind.Table:
                    raw = _tableCurve.Evaluate(value);
                    break;

                default:
                    throw new InvalidOperationException($"未知的 ResistCurveKind：{Kind}");
            }

            if (raw < 0.0) raw = 0.0;
            return Math.Min(raw, MaxReduction);
        }
    }
}
