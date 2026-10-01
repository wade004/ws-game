using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Rules.Common;
using Core.Rules.Targeting;
using Xunit;

namespace Tests.Rules.Targeting
{
    /// <summary>
    /// T-M14（core 半，测试覆盖剩余项 2026-10-01）：<see cref="TargetChainDef"/> 构造函数（从 DataRecord 解析）的
    /// 错误路径——缺必填字段、<c>shape.kind</c>/<c>sort_by.key</c>/<c>sort_by.direction</c>/<c>overflow_policy</c> 非法取值、
    /// <c>filters</c> 元素非字符串、<c>max_targets</c> 负数与超 32 位范围（后者为本批发现的裸
    /// <see cref="OverflowException"/>，README 判断记录 14），以及各可选字段的缺省值。
    /// </summary>
    public class TargetChainDefErrorPathTests
    {
        private static DataRecord Record(string extra = "", bool withSource = true)
        {
            var json = "{\"id\":\"target.chain.ep_sample\"" + (withSource ? ",\"source\":\"caster\"" : "") + extra + "}";
            var raw = (JsonObject)JsonReader.Parse(json);
            return new DataRecord(TargetSchemas.ChainDef, "target.chain.ep_sample", new Id("target.chain.ep_sample"), raw);
        }

        private static DataFieldException Fail(string extra) =>
            Assert.Throws<DataFieldException>(() => new TargetChainDef(Record(extra)));

        [Fact]
        public void Defaults_AreSingleTargetTruncateNoShapeNoFilters()
        {
            var def = new TargetChainDef(Record());

            Assert.Equal(new Id("target.chain.ep_sample"), def.Id);
            Assert.Equal("caster", def.Source);
            Assert.Null(def.Shape);
            Assert.Empty(def.Filters);
            Assert.Null(def.SortBy);
            Assert.Equal(1, def.MaxTargets);
            Assert.Equal(TargetOverflowPolicy.Truncate, def.OverflowPolicy);
            Assert.Null(def.Fallback);
        }

        [Fact]
        public void NullRecord_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new TargetChainDef(null!));
        }

        [Fact]
        public void MissingSource_ThrowsNamingSource()
        {
            var ex = Assert.Throws<DataFieldException>(() => new TargetChainDef(Record(withSource: false)));

            Assert.Equal("source", ex.Field);
        }

        [Theory]
        [InlineData(",\"shape\":{}", "shape.kind")]
        [InlineData(",\"shape\":{\"kind\":3}", "shape.kind")]
        [InlineData(",\"shape\":{\"kind\":\"sphere\",\"radius\":1}", "shape.kind")]
        [InlineData(",\"sort_by\":{}", "sort_by.key")]
        [InlineData(",\"sort_by\":{\"key\":\"coolness\"}", "sort_by.key")]
        [InlineData(",\"sort_by\":{\"key\":\"distance\",\"direction\":1}", "sort_by.direction")]
        [InlineData(",\"sort_by\":{\"key\":\"distance\",\"direction\":\"sideways\"}", "sort_by.direction")]
        [InlineData(",\"overflow_policy\":\"explode\"", "overflow_policy")]
        [InlineData(",\"overflow_policy\":\"Truncate\"", "overflow_policy")]
        [InlineData(",\"filters\":[\"alive\",5]", "filters")]
        [InlineData(",\"max_targets\":-1", "max_targets")]
        public void IllegalField_ThrowsNamingThatField(string extra, string field)
        {
            Assert.Equal(field, Fail(extra).Field);
        }

        [Fact]
        public void IllegalSortKeyMessage_ListsAllLegalKeys()
        {
            var ex = Fail(",\"sort_by\":{\"key\":\"coolness\"}");

            foreach (var legal in new[] { "distance", "hp_pct", "threat", "level" })
            {
                Assert.Contains(legal, ex.Message);
            }
        }

        [Fact]
        public void FiltersError_NamesTheElementIndex()
        {
            Assert.Contains("第 1 个", Fail(",\"filters\":[\"alive\",5]").Message);
        }

        /// <summary>T-M14 发现的缺陷复现：旧实现 <c>checked((int)…)</c> 抛裸 OverflowException，没有定位信息。</summary>
        [Theory]
        [InlineData(4294967296L)]
        [InlineData(2147483648L)]
        public void MaxTargetsBeyondInt32_ThrowsDataFieldException_InsteadOfBareOverflow(long value)
        {
            var ex = Fail(",\"max_targets\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture));

            Assert.Equal("max_targets", ex.Field);
        }

        [Fact]
        public void MaxTargetsAtInt32Max_AndZero_AreAccepted()
        {
            Assert.Equal(int.MaxValue, new TargetChainDef(Record(",\"max_targets\":" + int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture))).MaxTargets);
            Assert.Equal(0, new TargetChainDef(Record(",\"max_targets\":0")).MaxTargets);
        }

        [Theory]
        [InlineData("truncate", TargetOverflowPolicy.Truncate)]
        [InlineData("split", TargetOverflowPolicy.Split)]
        [InlineData("cap", TargetOverflowPolicy.Cap)]
        public void OverflowPolicy_EveryLegalTextMapsToItsEnumValue(string text, TargetOverflowPolicy expected)
        {
            Assert.Equal(expected, new TargetChainDef(Record(",\"overflow_policy\":\"" + text + "\"")).OverflowPolicy);
        }

        [Theory]
        [InlineData("distance", TargetSortKey.Distance)]
        [InlineData("hp_pct", TargetSortKey.HpPct)]
        [InlineData("threat", TargetSortKey.Threat)]
        [InlineData("level", TargetSortKey.Level)]
        public void SortKey_EveryLegalTextMapsToItsEnumValue_DirectionDefaultsToAsc(string text, TargetSortKey expected)
        {
            var def = new TargetChainDef(Record(",\"sort_by\":{\"key\":\"" + text + "\"}"));

            Assert.Equal(expected, def.SortBy!.Value.Key);
            Assert.Equal(SortDirection.Asc, def.SortBy.Value.Direction);
        }

        [Fact]
        public void SortDirection_DescIsParsed()
        {
            var def = new TargetChainDef(Record(",\"sort_by\":{\"key\":\"level\",\"direction\":\"desc\"}"));

            Assert.Equal(SortDirection.Desc, def.SortBy!.Value.Direction);
        }

        [Fact]
        public void Shape_IsAnchoredAtOriginWithZeroRotation_ForEveryKind_AndRectIsHalved()
        {
            var circle = new TargetChainDef(Record(",\"shape\":{\"kind\":\"circle\",\"radius\":3}")).Shape!.Value;
            var cone = new TargetChainDef(Record(",\"shape\":{\"kind\":\"cone\",\"radius\":5,\"angle\":1}")).Shape!.Value;
            var line = new TargetChainDef(Record(",\"shape\":{\"kind\":\"line\",\"length\":8,\"width\":2}")).Shape!.Value;
            var rect = new TargetChainDef(Record(",\"shape\":{\"kind\":\"rect\",\"length\":10,\"width\":4}")).Shape!.Value;

            Assert.Equal(ShapeKind.Circle, circle.Kind);
            Assert.Equal(3.0, circle.Radius);
            Assert.Equal(ShapeKind.Cone, cone.Kind);
            Assert.Equal(0.0, cone.Direction);
            Assert.Equal(ShapeKind.Line, line.Kind);
            Assert.Equal(0.0, line.Direction);
            Assert.Equal(ShapeKind.Rect, rect.Kind);
            Assert.Equal(new Vec2(10 / 2.0, 4 / 2.0), rect.HalfExtents);
            Assert.All(new[] { circle.Origin, cone.Origin, line.Origin, rect.Origin }, o => Assert.Equal(Vec2.Zero, o));
        }

        [Fact]
        public void Fallback_ParsedWhenValid_IgnoredWhenMalformed()
        {
            Assert.Equal(new Id("target.chain.ep_other"), new TargetChainDef(Record(",\"fallback\":\"target.chain.ep_other\"")).Fallback);
            Assert.Null(new TargetChainDef(Record(",\"fallback\":\"bad id\"")).Fallback);
        }

        [Fact]
        public void ErrorCarriesTableAndRecordKey()
        {
            var ex = Fail(",\"overflow_policy\":\"explode\"");

            Assert.Equal(TargetSchemas.ChainDef.Name, ex.Table);
            Assert.Equal("target.chain.ep_sample", ex.RecordKey);
        }
    }
}
