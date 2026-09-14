using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Combat;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// 分阶段落地计划 T-N0-5 验收（<c>combat.resist_curve</c> 侧）：<c>table</c> 分支改为委托通用
    /// <c>PiecewiseCurve</c> 插值后，结果与迁移前手写 <c>InterpolateTable</c> 逐位一致（段内、两端夹取、
    /// 空表为 0、封顶）；<c>saturation</c> 分支公式原样不动（T-N0-5 禁止事项），逐点核对。
    /// </summary>
    public sealed class ResistCurveTableInterpolationTests
    {
        private static ResistCurve Build(string json)
        {
            var raw = (JsonObject)JsonReader.Parse(json);
            var key = ((JsonString)raw["id"]).Value;
            return new ResistCurve(new DataRecord(CombatSchemas.ResistCurve, key, new Id(key), raw));
        }

        /// <summary>迁移前 <c>ResistCurve.InterpolateTable</c> 的原式（逐字复刻，作为基准）。</summary>
        private static double LegacyInterpolate(IReadOnlyList<ResistCurveEntry> entries, double value)
        {
            if (entries.Count == 0) return 0.0;
            if (value <= entries[0].Value) return entries[0].Reduction;
            var last = entries[entries.Count - 1];
            if (value >= last.Value) return last.Reduction;
            for (int i = 0; i < entries.Count - 1; i++)
            {
                var low = entries[i];
                var high = entries[i + 1];
                if (value >= low.Value && value <= high.Value)
                {
                    var t = (value - low.Value) / (high.Value - low.Value);
                    return low.Reduction + t * (high.Reduction - low.Reduction);
                }
            }
            return last.Reduction;
        }

        private const string TableJson = "{\"id\": \"combat.resist.t\", \"school\": \"school.frost\", \"kind\": \"table\", \"max_reduction\": 0.9, " +
            "\"entries\": [{\"value\": 0, \"reduction\": 0}, {\"value\": 100, \"reduction\": 0.25}, {\"value\": 400, \"reduction\": 0.6}, {\"value\": 1000, \"reduction\": 0.95}]}";

        [Theory]
        [InlineData(-50)]
        [InlineData(0)]
        [InlineData(37)]
        [InlineData(100)]
        [InlineData(250)]
        [InlineData(400)]
        [InlineData(733)]
        [InlineData(1000)]
        [InlineData(5000)]
        public void Table_ComputeReduction_MatchesLegacyFormulaBitForBit(double value)
        {
            var curve = Build(TableJson);
            var legacy = LegacyInterpolate(curve.Entries, value);
            var expected = legacy < 0.0 ? 0.0 : System.Math.Min(legacy, curve.MaxReduction);

            Assert.Equal(expected, curve.ComputeReduction(value, attackerLevel: 60));
        }

        [Fact]
        public void Table_EmptyEntries_ReturnsZero()
        {
            var curve = Build("{\"id\": \"combat.resist.e\", \"school\": \"school.frost\", \"kind\": \"table\", \"entries\": []}");

            Assert.Equal(0.0, curve.ComputeReduction(500, 10));
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(100, 1)]
        [InlineData(100, 60)]
        [InlineData(5000, 60)]
        public void Saturation_FormulaUnchanged(double value, int attackerLevel)
        {
            var curve = Build("{\"id\": \"combat.resist.s\", \"school\": \"school.physical\", \"kind\": \"saturation\", \"k\": 85, \"max_reduction\": 0.75}");
            var denom = value + 85 * attackerLevel;
            var expected = denom <= 0.0 ? 0.0 : value / denom;
            expected = System.Math.Min(expected < 0 ? 0 : expected, 0.75);

            Assert.Equal(expected, curve.ComputeReduction(value, attackerLevel));
        }
    }
}
