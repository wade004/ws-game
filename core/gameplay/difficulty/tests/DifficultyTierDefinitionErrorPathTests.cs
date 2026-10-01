using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Difficulty;
using Xunit;

namespace Tests.Gameplay.Difficulty
{
    /// <summary>
    /// T-M14（core 半，测试覆盖剩余项 2026-10-01）：<see cref="DifficultyTierDefinition.FromRecord"/> 的错误路径与
    /// 缺省值——缺必填字段、类型不符、<c>item_level_offset</c> 超 32 位范围静默回绕（本批发现，README 判断记录 9）。
    /// </summary>
    public class DifficultyTierDefinitionErrorPathTests
    {
        private static DataRecord Record(string json)
        {
            var raw = (JsonObject)JsonReader.Parse(json);
            return new DataRecord(DifficultySchemas.Tier, "diff.tier.ep_sample", new Id("diff.tier.ep_sample"), raw);
        }

        private const string Base = "\"id\":\"diff.tier.ep_sample\",\"name_key\":\"l10n.diff.ep.name\",\"loot_multiplier\":1.5";

        [Fact]
        public void Defaults_ForOptionalFields()
        {
            var def = DifficultyTierDefinition.FromRecord(Record("{" + Base + "}"));

            Assert.Equal(1.5, def.LootMultiplier);
            Assert.Empty(def.ModifierAuraRefs);
            Assert.Null(def.AffixPoolRef);
            Assert.Equal(0, def.ItemLevelOffset);
            Assert.Equal(0.0, def.SortWeight);
            Assert.Equal(1.0, def.XpMultiplier);
        }

        [Fact]
        public void AllFieldsSupplied_AreCarriedThrough()
        {
            var def = DifficultyTierDefinition.FromRecord(Record(
                "{" + Base + ",\"modifier_aura_refs\":[\"aura.ep_a\",\"aura.ep_b\"],\"affix_pool_ref\":\"item.affix_pool.ep\"," +
                "\"item_level_offset\":-3,\"sort_weight\":2.5,\"xp_multiplier\":1.25}"));

            Assert.Equal(new[] { new Id("aura.ep_a"), new Id("aura.ep_b") }, def.ModifierAuraRefs);
            Assert.Equal(new Id("item.affix_pool.ep"), def.AffixPoolRef);
            Assert.Equal(-3, def.ItemLevelOffset);
            Assert.Equal(2.5, def.SortWeight);
            Assert.Equal(1.25, def.XpMultiplier);
        }

        [Fact]
        public void NullRecord_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => DifficultyTierDefinition.FromRecord(null!));
        }

        [Theory]
        [InlineData("{\"id\":\"diff.tier.ep_sample\",\"loot_multiplier\":1}", "name_key")]
        [InlineData("{\"id\":\"diff.tier.ep_sample\",\"name_key\":\"l10n.x\"}", "loot_multiplier")]
        [InlineData("{\"id\":\"diff.tier.ep_sample\",\"name_key\":\"l10n.x\",\"loot_multiplier\":\"lots\"}", "loot_multiplier")]
        [InlineData("{\"id\":\"diff.tier.ep_sample\",\"name_key\":\"bad id\",\"loot_multiplier\":1}", "name_key")]
        public void MissingOrMistypedRequiredField_ThrowsNamingTheField(string json, string field)
        {
            var ex = Assert.Throws<DataFieldException>(() => DifficultyTierDefinition.FromRecord(Record(json)));

            Assert.Equal(field, ex.Field);
            Assert.Equal("diff.tier.ep_sample", ex.RecordKey);
        }

        /// <summary>T-M14 发现的缺陷复现：旧实现 <c>(int)ilo</c> 静默回绕，4294967297 变成 1。</summary>
        [Theory]
        [InlineData(4294967297L)]
        [InlineData(2147483648L)]
        [InlineData(-2147483649L)]
        public void ItemLevelOffsetOutsideInt32_Throws_InsteadOfWrappingAround(long offset)
        {
            var ex = Assert.Throws<DataFieldException>(() => DifficultyTierDefinition.FromRecord(
                Record("{" + Base + ",\"item_level_offset\":" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}")));

            Assert.Equal("item_level_offset", ex.Field);
        }

        [Theory]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void ItemLevelOffsetAtInt32Bounds_IsAccepted(int offset)
        {
            var def = DifficultyTierDefinition.FromRecord(
                Record("{" + Base + ",\"item_level_offset\":" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}"));

            Assert.Equal(offset, def.ItemLevelOffset);
        }
    }
}
