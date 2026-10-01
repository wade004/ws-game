using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Achievement;
using Xunit;

namespace Tests.Gameplay.Achievement
{
    /// <summary>
    /// T-M14（core 半，测试覆盖剩余项 2026-10-01）：<see cref="AchievementDefinition.FromRecord"/> /
    /// <see cref="AchievementCriterion.FromRecord"/> 的错误路径：空 criteria、元素形状不符、未知 type、
    /// 坏 observe_event/target_ref、count 缺失/非整数/小数/超 32 位范围（后者为本批发现的静默回绕缺陷，
    /// 见 README 判断记录 9）。
    /// </summary>
    public class AchievementContractsErrorPathTests
    {
        private const string GoodCriterion =
            "{\"type\":\"kill_count\",\"observe_event\":\"unit.died\",\"target_ref\":\"creature.ac_wolf\",\"count\":3}";

        private static DataRecord Def(string criteria, bool withNameKey = true)
        {
            var json = "{\"id\":\"achv.ac_sample\"" + (withNameKey ? ",\"name_key\":\"l10n.achv.ac_sample.name\"" : "") +
                       ",\"criteria\":" + criteria + "}";
            var raw = (JsonObject)JsonReader.Parse(json);
            return new DataRecord(AchievementSchemas.Def, "achv.ac_sample", new Id("achv.ac_sample"), raw);
        }

        private static DataFieldException Fail(string criteria) =>
            Assert.Throws<DataFieldException>(() => AchievementDefinition.FromRecord(Def(criteria)));

        private static string Criterion(string type = "\"kill_count\"", string observe = "\"unit.died\"", string? target = "\"creature.ac_wolf\"", string count = "3") =>
            "{\"type\":" + type + ",\"observe_event\":" + observe + (target != null ? ",\"target_ref\":" + target : "") + ",\"count\":" + count + "}";

        [Fact]
        public void Definition_ValidBaseline_Parses()
        {
            var def = AchievementDefinition.FromRecord(Def("[" + GoodCriterion + "]"));

            var c = Assert.Single(def.Criteria);
            Assert.Equal(CriterionType.KillCount, c.Type);
            Assert.Equal(3, c.Count);
            Assert.Equal(new Id("creature.ac_wolf"), c.TargetRef);
            Assert.Null(c.FilterText);
        }

        [Fact]
        public void Definition_EmptyCriteria_Throws_NamingCriteria()
        {
            Assert.Equal("criteria", Fail("[]").Field);
        }

        [Fact]
        public void Definition_MissingNameKey_Throws_NamingNameKey()
        {
            var ex = Assert.Throws<DataFieldException>(() => AchievementDefinition.FromRecord(Def("[" + GoodCriterion + "]", withNameKey: false)));

            Assert.Equal("name_key", ex.Field);
        }

        [Fact]
        public void Definition_NullRecord_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => AchievementDefinition.FromRecord(null!));
        }

        [Fact]
        public void Definition_CriterionNotAnObject_Throws_WithIndexedPath()
        {
            Assert.Equal("criteria[1]", Fail("[" + GoodCriterion + ",\"oops\"]").Field);
        }

        [Fact]
        public void Definition_ErrorInLaterCriterion_ReportsThatIndex()
        {
            Assert.Equal("criteria[2].count", Fail("[" + GoodCriterion + "," + GoodCriterion + "," + Criterion(count: "\"many\"") + "]").Field);
        }

        [Theory]
        [InlineData("\"no_such_type\"")]
        [InlineData("\"KILL_COUNT\"")]
        [InlineData("7")]
        public void Criterion_UnknownOrMistypedType_Throws_ListingTheLegalValues(string type)
        {
            var ex = Fail("[" + Criterion(type: type) + "]");

            Assert.Equal("criteria[0].type", ex.Field);
            foreach (var legal in CriterionTypeIds.AllValues)
            {
                Assert.Contains(legal, ex.Message);
            }
        }

        [Theory]
        [InlineData("\"not an id\"")]
        [InlineData("12")]
        public void Criterion_BadObserveEvent_Throws(string observe)
        {
            Assert.Equal("criteria[0].observe_event", Fail("[" + Criterion(observe: observe) + "]").Field);
        }

        [Fact]
        public void Criterion_MissingObserveEvent_Throws()
        {
            Assert.Equal(
                "criteria[0].observe_event",
                Fail("[{\"type\":\"kill_count\",\"count\":1}]").Field);
        }

        [Fact]
        public void Criterion_MalformedTargetRef_Throws_ButAbsentOrNonStringTargetIsTolerated()
        {
            Assert.Equal("criteria[0].target_ref", Fail("[" + Criterion(target: "\"not an id\"") + "]").Field);

            var absent = AchievementDefinition.FromRecord(Def("[" + Criterion(type: "\"custom_event\"", target: null) + "]"));
            Assert.Null(absent.Criteria[0].TargetRef);

            // 非字符串的 target_ref 当作"未提供"（与解析器的宽松读取一致）。
            var numeric = AchievementDefinition.FromRecord(Def("[" + Criterion(target: "5") + "]"));
            Assert.Null(numeric.Criteria[0].TargetRef);
        }

        [Theory]
        [InlineData("\"3\"")]
        [InlineData("2.5")]
        [InlineData("null")]
        [InlineData("true")]
        public void Criterion_CountMissingOrNotAnInteger_Throws(string count)
        {
            Assert.Equal("criteria[0].count", Fail("[" + Criterion(count: count) + "]").Field);
        }

        [Fact]
        public void Criterion_CountAbsent_Throws()
        {
            Assert.Equal(
                "criteria[0].count",
                Fail("[{\"type\":\"kill_count\",\"observe_event\":\"unit.died\"}]").Field);
        }

        /// <summary>T-M14 发现的缺陷复现：旧实现 <c>(int)countValue</c> 静默回绕，4294967301 变成 5。</summary>
        [Theory]
        [InlineData(4294967301L)]
        [InlineData(2147483648L)]
        [InlineData(-4294967295L)]
        public void Criterion_CountOutsideInt32_ThrowsDataFieldException_InsteadOfWrappingAround(long count)
        {
            var ex = Fail("[" + Criterion(count: count.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "]");

            Assert.Equal("criteria[0].count", ex.Field);
        }

        [Fact]
        public void Criterion_CountAtInt32Max_IsAccepted()
        {
            var def = AchievementDefinition.FromRecord(Def("[" + Criterion(count: int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "]"));

            Assert.Equal(int.MaxValue, def.Criteria[0].Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Criterion_CountBelowOne_IsRejectedByConstructorGuard(int count)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AchievementDefinition.FromRecord(Def("[" + Criterion(count: count.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "]")));
        }

        [Fact]
        public void Criterion_FilterText_IsCarriedThrough_AndNonStringFilterIgnored()
        {
            var withFilter = AchievementDefinition.FromRecord(Def(
                "[{\"type\":\"custom_event\",\"observe_event\":\"item.equipped\",\"count\":1,\"filter\":\"self.level >= 2\"}]"));
            var nonString = AchievementDefinition.FromRecord(Def(
                "[{\"type\":\"custom_event\",\"observe_event\":\"item.equipped\",\"count\":1,\"filter\":5}]"));

            Assert.Equal("self.level >= 2", withFilter.Criteria[0].FilterText);
            Assert.Null(nonString.Criteria[0].FilterText);
        }

        [Fact]
        public void CriterionTypeIds_TryParse_RoundTripsEveryLegalValue_AndRejectsOthers()
        {
            foreach (var text in CriterionTypeIds.AllValues)
            {
                Assert.True(CriterionTypeIds.TryParse(text, out _), text);
            }

            Assert.False(CriterionTypeIds.TryParse("", out _));
            Assert.False(CriterionTypeIds.TryParse("Kill_Count", out _));
        }
    }
}
