using Core.Carriers.Item;
using Core.Foundation.Common.Json;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// T-L14（测试覆盖剩余项 2026-10-01）：<see cref="ItemBudgetCurve.SumConsumed"/>（ADR-0032 决策 3 之前的旧式
    /// 预算消耗求和，保留供外部工具/旧测试继续编译）的直接用例：Σ|value|，<c>pct</c>/<c>mult</c> 按 ×100 折算，
    /// 缺 <c>op</c> 视作 <c>flat</c>，非对象/缺 <c>value</c>/非数字 <c>value</c> 的条目被跳过。
    /// </summary>
    public class ItemBudgetCurveSumConsumedTests
    {
        private const double PctScale = 100.0;

        private static JsonObject Entry(string op, double value) =>
            new JsonObjectBuilder().Add("stat", new JsonString("stat.strength")).Add("op", new JsonString(op)).Add("value", new JsonNumber(value)).Build();

        [Fact]
        public void EmptyArray_SumsToZero()
        {
            Assert.Equal(0.0, ItemBudgetCurve.SumConsumed(new JsonArray(new JsonValue[0])));
        }

        [Fact]
        public void FlatEntries_SumTheirAbsoluteValues()
        {
            var stats = new JsonArray(new JsonValue[] { Entry("flat", 4), Entry("flat", -3.5) });

            Assert.Equal(4 + 3.5, ItemBudgetCurve.SumConsumed(stats));
        }

        [Fact]
        public void PctAndMultEntries_AreScaledByOneHundred()
        {
            var stats = new JsonArray(new JsonValue[] { Entry("pct", 0.05), Entry("mult", -0.2) });

            Assert.Equal((0.05 + 0.2) * PctScale, ItemBudgetCurve.SumConsumed(stats), 9);
        }

        [Fact]
        public void MissingOp_IsTreatedAsFlat()
        {
            var noOp = new JsonObjectBuilder().Add("stat", new JsonString("stat.strength")).Add("value", new JsonNumber(6)).Build();

            Assert.Equal(6.0, ItemBudgetCurve.SumConsumed(new JsonArray(new JsonValue[] { noOp })));
        }

        [Fact]
        public void UnknownOp_IsNotScaled()
        {
            Assert.Equal(7.0, ItemBudgetCurve.SumConsumed(new JsonArray(new JsonValue[] { Entry("something_else", 7) })));
        }

        [Fact]
        public void MalformedEntries_AreSkipped_WithoutAffectingValidOnes()
        {
            var missingValue = new JsonObjectBuilder().Add("stat", new JsonString("stat.strength")).Add("op", new JsonString("flat")).Build();
            var textValue = new JsonObjectBuilder().Add("op", new JsonString("flat")).Add("value", new JsonString("ten")).Build();
            var stats = new JsonArray(new JsonValue[]
            {
                new JsonString("not an object"),
                new JsonNumber(99),
                missingValue,
                textValue,
                Entry("flat", 2),
            });

            Assert.Equal(2.0, ItemBudgetCurve.SumConsumed(stats));
        }

        [Fact]
        public void MixedEntries_SumFlatAndScaledTogether()
        {
            var stats = new JsonArray(new JsonValue[] { Entry("flat", 10), Entry("pct", 0.1), Entry("mult", 0.25) });

            Assert.Equal(10 + (0.1 + 0.25) * PctScale, ItemBudgetCurve.SumConsumed(stats), 9);
        }
    }
}
