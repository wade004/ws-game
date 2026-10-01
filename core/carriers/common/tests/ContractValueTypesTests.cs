using System;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Xunit;

namespace Tests.Carriers.Common
{
    /// <summary>
    /// T-L10（测试覆盖剩余项 2026-10-01）：<c>core/carriers/common/contracts</c> 里手写相等性/哈希的值类型的直接用例——
    /// <see cref="EquippedItemIdentity"/>、<see cref="LootRollOutcome"/>、<see cref="EquippedWeaponSummary"/>、
    /// <see cref="InteractionTarget"/>。重点是三条不变量：相等的值哈希必相等；任一字段不同则不相等；
    /// 可变序列字段（词缀）按顺序比较、构造时空参被归一为空序列。
    /// </summary>
    public class ContractValueTypesTests
    {
        private static readonly Id InstA = new Id("item.inst_a");
        private static readonly Id InstB = new Id("item.inst_b");
        private static readonly Id TplX = new Id("item.template_x");
        private static readonly Id TplY = new Id("item.template_y");
        private static readonly Id QualityRare = new Id("item.quality.rare");
        private static readonly Id QualityCommon = new Id("item.quality.common");
        private static readonly Id AffixOne = new Id("item.affix.one");
        private static readonly Id AffixTwo = new Id("item.affix.two");

        // ----------------------------- EquippedItemIdentity -----------------------------

        [Fact]
        public void EquippedItemIdentity_EqualValues_AreEqual_WithSameHash_AndOperatorsAgree()
        {
            var a = new EquippedItemIdentity(InstA, TplX);
            var b = new EquippedItemIdentity(new Id(InstA.Value), new Id(TplX.Value));

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.True(a.Equals((object)b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void EquippedItemIdentity_DifferentInstanceOrTemplate_AreNotEqual()
        {
            var baseline = new EquippedItemIdentity(InstA, TplX);

            Assert.NotEqual(baseline, new EquippedItemIdentity(InstB, TplX));
            Assert.NotEqual(baseline, new EquippedItemIdentity(InstA, TplY));
            Assert.True(baseline != new EquippedItemIdentity(InstB, TplY));
            Assert.False(baseline.Equals("not an identity"));
            Assert.False(baseline.Equals(null));
        }

        [Fact]
        public void EquippedItemIdentity_SwappedFields_AreNotEqual()
        {
            // 实例 Id 与模板 Id 互换必须区分（哈希不能是对称运算的纯异或）。
            var forward = new EquippedItemIdentity(InstA, TplX);
            var swapped = new EquippedItemIdentity(TplX, InstA);

            Assert.NotEqual(forward, swapped);
        }

        [Fact]
        public void EquippedItemIdentity_ToString_MentionsInstanceAndTemplate()
        {
            var text = new EquippedItemIdentity(InstA, TplX).ToString();

            Assert.Contains(InstA.Value, text);
            Assert.Contains(TplX.Value, text);
        }

        // ----------------------------- LootRollOutcome -----------------------------

        [Fact]
        public void LootRollOutcome_RejectsNonPositiveCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LootRollOutcome(TplX, 0, null, null, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LootRollOutcome(TplX, -1, null, null, null));
        }

        [Fact]
        public void LootRollOutcome_NullAffixes_NormalizedToEmpty()
        {
            var outcome = new LootRollOutcome(TplX, 2, null, null, null);

            Assert.NotNull(outcome.Affixes);
            Assert.Empty(outcome.Affixes);
        }

        [Fact]
        public void LootRollOutcome_EqualValues_AreEqual_WithSameHash()
        {
            var a = new LootRollOutcome(TplX, 3, QualityRare, new[] { AffixOne, AffixTwo }, 12);
            var b = new LootRollOutcome(new Id(TplX.Value), 3, new Id(QualityRare.Value), new[] { AffixOne, AffixTwo }, 12);

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void LootRollOutcome_AnyFieldDifferent_IsNotEqual()
        {
            var baseline = new LootRollOutcome(TplX, 3, QualityRare, new[] { AffixOne, AffixTwo }, 12);

            Assert.NotEqual(baseline, new LootRollOutcome(TplY, 3, QualityRare, new[] { AffixOne, AffixTwo }, 12));
            Assert.NotEqual(baseline, new LootRollOutcome(TplX, 4, QualityRare, new[] { AffixOne, AffixTwo }, 12));
            Assert.NotEqual(baseline, new LootRollOutcome(TplX, 3, QualityCommon, new[] { AffixOne, AffixTwo }, 12));
            Assert.NotEqual(baseline, new LootRollOutcome(TplX, 3, null, new[] { AffixOne, AffixTwo }, 12));
            Assert.NotEqual(baseline, new LootRollOutcome(TplX, 3, QualityRare, new[] { AffixOne }, 12));
            Assert.NotEqual(baseline, new LootRollOutcome(TplX, 3, QualityRare, new[] { AffixOne, AffixTwo }, 13));
            Assert.NotEqual(baseline, new LootRollOutcome(TplX, 3, QualityRare, new[] { AffixOne, AffixTwo }, null));
        }

        [Fact]
        public void LootRollOutcome_AffixOrderMatters()
        {
            var forward = new LootRollOutcome(TplX, 1, null, new[] { AffixOne, AffixTwo }, null);
            var reversed = new LootRollOutcome(TplX, 1, null, new[] { AffixTwo, AffixOne }, null);

            Assert.NotEqual(forward, reversed);
        }

        [Fact]
        public void LootRollOutcome_NullAndEmptyAffixes_AreEqual()
        {
            var withNull = new LootRollOutcome(TplX, 1, null, null, null);
            var withEmpty = new LootRollOutcome(TplX, 1, null, Array.Empty<Id>(), null);

            Assert.Equal(withNull, withEmpty);
            Assert.Equal(withNull.GetHashCode(), withEmpty.GetHashCode());
        }

        [Fact]
        public void LootRollOutcome_FromStack_ToStack_RoundTrips_AndLeavesOptionalFieldsUnset()
        {
            var stack = new ItemStack(TplX, 5);

            var outcome = LootRollOutcome.FromStack(stack);

            Assert.Equal(TplX, outcome.TemplateId);
            Assert.Equal(5, outcome.Count);
            Assert.Null(outcome.QualityId);
            Assert.Null(outcome.ItemLevel);
            Assert.Empty(outcome.Affixes);
            Assert.Equal(stack, outcome.ToStack());
        }

        [Fact]
        public void LootRollOutcome_ToString_RendersTemplateCountAndUnsetPlaceholders()
        {
            var text = new LootRollOutcome(TplX, 2, null, null, null).ToString();

            Assert.Contains(TplX.Value, text);
            Assert.Contains("x2", text);
            Assert.Contains("<template>", text);
        }

        // ----------------------------- EquippedWeaponSummary -----------------------------

        [Fact]
        public void EquippedWeaponSummary_EqualValues_AreEqual_WithSameHash()
        {
            var a = new EquippedWeaponSummary(TplX, QualityRare, new[] { AffixOne, AffixTwo });
            var b = new EquippedWeaponSummary(TplX, QualityRare, new[] { AffixOne, AffixTwo });

            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void EquippedWeaponSummary_AnyFieldOrAffixOrderDifferent_IsNotEqual()
        {
            var baseline = new EquippedWeaponSummary(TplX, QualityRare, new[] { AffixOne, AffixTwo });

            Assert.NotEqual(baseline, new EquippedWeaponSummary(TplY, QualityRare, new[] { AffixOne, AffixTwo }));
            Assert.NotEqual(baseline, new EquippedWeaponSummary(TplX, QualityCommon, new[] { AffixOne, AffixTwo }));
            Assert.NotEqual(baseline, new EquippedWeaponSummary(TplX, QualityRare, new[] { AffixTwo, AffixOne }));
            Assert.NotEqual(baseline, new EquippedWeaponSummary(TplX, QualityRare, new[] { AffixOne }));
            Assert.False(baseline.Equals("not a summary"));
        }

        [Fact]
        public void EquippedWeaponSummary_NullAffixes_EqualsEmptyAffixes()
        {
            var withNull = new EquippedWeaponSummary(TplX, QualityRare, null);
            var withEmpty = new EquippedWeaponSummary(TplX, QualityRare, Array.Empty<Id>());

            Assert.Empty(withNull.AffixIds);
            Assert.Equal(withNull, withEmpty);
            Assert.Equal(withNull.GetHashCode(), withEmpty.GetHashCode());
        }

        // ----------------------------- InteractionTarget -----------------------------

        [Fact]
        public void InteractionTarget_RoundTripsFields()
        {
            var target = new InteractionTarget(new Id("gobj.chest_1"), InteractionTargetKind.GameObject, 2.5);

            Assert.Equal(new Id("gobj.chest_1"), target.EntityId);
            Assert.Equal(InteractionTargetKind.GameObject, target.Kind);
            Assert.Equal(2.5, target.Distance);
        }

        [Fact]
        public void InteractionTarget_Default_IsZeroed()
        {
            var target = default(InteractionTarget);

            Assert.Equal(default(Id), target.EntityId);
            Assert.Equal(default(InteractionTargetKind), target.Kind);
            Assert.Equal(0.0, target.Distance);
        }

        [Theory]
        [InlineData(InteractionTargetKind.GameObject)]
        [InlineData(InteractionTargetKind.Creature)]
        [InlineData(InteractionTargetKind.Loot)]
        public void InteractionTargetKind_EveryValueIsDefinedAndNamedAndSurvivesTextRoundTrip(InteractionTargetKind kind)
        {
            Assert.True(Enum.IsDefined(typeof(InteractionTargetKind), kind));
            Assert.Equal(kind, Enum.Parse<InteractionTargetKind>(kind.ToString()));
        }
    }
}
