using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Xunit;

namespace Tests.Foundation.Feel
{
    /// <summary>字段登记元数据（半属、分组、允许操作、合成来源、单位、副手可叠加）的完整性与一致性。</summary>
    public class FeelFieldRegistryTests
    {
        private static FeelFieldMeta Meta(
            FeelHalf half = FeelHalf.Judging, FeelGroup group = FeelGroup.Movement,
            FeelOpSet ops = FeelOpSet.Set, FeelComposition comp = FeelComposition.CharacterPrimary,
            FeelUnit unit = FeelUnit.None, bool offhand = false) => new FeelFieldMeta(half, group, ops, comp, unit, offhand);

        [Fact]
        public void EveryDefaultField_CarriesCompleteMetadata()
        {
            var set = FeelFields.Default;
            Assert.True(set.Count >= 60);
            foreach (var def in set.Fields)
            {
                Assert.False(string.IsNullOrWhiteSpace(def.Description), def.Name);
                Assert.NotEqual(FeelOpSet.None, def.Ops);
                Assert.Equal(0, (int)(def.Ops & ~FeelFieldDef.LegalOpsFor(def.Kind)));
                Assert.True(Enum.IsDefined(typeof(FeelHalf), def.Half));
                Assert.True(Enum.IsDefined(typeof(FeelGroup), def.Group));
                Assert.True(Enum.IsDefined(typeof(FeelComposition), def.Composition));
                Assert.True(Enum.IsDefined(typeof(FeelUnit), def.Unit));
                if (def.IsNumeric) Assert.True(def.Min <= def.Max, def.Name);
            }
            Assert.Equal(set.Count, set.Fields.Select(f => f.Name).Distinct().Count());
        }

        [Fact]
        public void GroupAndHalf_AreConsistent_AndEveryGroupAndBothHalvesAreUsed()
        {
            var set = FeelFields.Default;
            foreach (var def in set.Fields)
            {
                switch (def.Group)
                {
                    case FeelGroup.Input:
                    case FeelGroup.Action:
                    case FeelGroup.Reaction:
                        Assert.Equal(FeelHalf.Judging, def.Half);
                        break;
                    case FeelGroup.Camera:
                    case FeelGroup.Effects:
                    case FeelGroup.Audio:
                        Assert.Equal(FeelHalf.Presenting, def.Half);
                        break;
                }
            }
            foreach (FeelGroup g in Enum.GetValues(typeof(FeelGroup))) Assert.Contains(set.Fields, f => f.Group == g);
            Assert.Contains(set.Fields, f => f.Half == FeelHalf.Judging && f.Group == FeelGroup.Movement);
            Assert.Contains(set.Fields, f => f.Half == FeelHalf.Presenting && f.Group == FeelGroup.Movement);
        }

        [Fact]
        public void OpsAreCompatibleWithValueKind_BooleanEnumTextAndIdAreSetOnly()
        {
            foreach (var def in FeelFields.Default.Fields)
            {
                switch (def.Kind)
                {
                    case FeelFieldKind.Bool:
                    case FeelFieldKind.Enum:
                    case FeelFieldKind.Text:
                    case FeelFieldKind.Id:
                        Assert.Equal(FeelOpSet.Set, def.Ops);
                        break;
                    case FeelFieldKind.List:
                        Assert.Equal(0, (int)(def.Ops & FeelOpSet.Multiply));
                        break;
                    default:
                        Assert.Equal(0, (int)(def.Ops & FeelOpSet.Remove));
                        break;
                }
            }
        }

        [Fact]
        public void OffhandStackable_OnlyWeaponPrimaryNumericFields_AndAtLeastOneExistsForEachDocumentedExample()
        {
            var stackable = FeelFields.Default.Fields.Where(f => f.OffhandStackable).ToArray();
            Assert.NotEmpty(stackable);
            Assert.All(stackable, f =>
            {
                Assert.Equal(FeelComposition.WeaponPrimary, f.Composition);
                Assert.True(f.Allows(FeelOp.Add) || f.Allows(FeelOp.Multiply));
            });
            // 05 第 3.4 节点名：音效增味与特效强度可叠加。
            Assert.Contains(stackable, f => f.Name == FeelFieldNames.SfxSweetenerTier);
            Assert.Contains(stackable, f => f.Name == FeelFieldNames.ImpactVfxScale);
            // 判定字段不可叠加（副手只改表现）。
            Assert.All(stackable, f => Assert.Equal(FeelHalf.Presenting, f.Half));
        }

        [Fact]
        public void MilliSecondJudgingFields_HaveTicks_AndOtherFieldsDoNot()
        {
            foreach (var def in FeelFields.Default.Fields)
            {
                Assert.Equal(def.Unit == FeelUnit.Milliseconds && def.Half == FeelHalf.Judging, def.HasTicks);
            }
        }

        [Fact]
        public void NameConstants_MatchTheDefaultRegistryExactly()
        {
            var constants = typeof(FeelFieldNames)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && !f.IsDefined(typeof(System.ObsoleteAttribute), false)) // ADR-0143 删去的字段只留已过时常量，不再登记
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToHashSet();
            var registered = FeelFields.Default.Fields.Select(f => f.Name).ToHashSet();
            Assert.Equal(registered, constants);
        }

        [Fact]
        public void ToFieldSchema_CarriesTheFeelMetadataAndRange()
        {
            foreach (var def in FeelFields.Default.Fields)
            {
                var schema = def.ToFieldSchema();
                Assert.Equal(def.Name, schema.Name);
                Assert.NotNull(schema.Feel);
                Assert.Equal(def.Half, schema.Feel!.Half);
                Assert.Equal(def.Group, schema.Feel.Group);
                Assert.Equal(def.Ops, schema.Feel.Ops);
                Assert.Equal(def.Composition, schema.Feel.Composition);
                Assert.Equal(def.Unit, schema.Feel.Unit);
                Assert.Equal(def.OffhandStackable, schema.Feel.OffhandStackable);
                // 预设值子字段不在登记层标必填：必填性沿 extends 链合并后由 feel_preset_missing_field 检查。
                Assert.False(schema.Required);
                if (def.IsNumeric) Assert.NotNull(schema.Range);
            }
        }

        [Fact]
        public void WriteValueSchema_HasNoNativeRange_SoRelativeRangesAreCheckedByOperationSemantics()
        {
            var accel = FeelFields.Default.Get(FeelFieldNames.AccelMs);
            Assert.Null(accel.ToWriteValueSchema().Range);
            Assert.NotNull(accel.ToFieldSchema().Range);
        }

        [Fact]
        public void WithFeel_CanOnlyBeSetOnce_AndFieldsWithoutMetadataHaveNone()
        {
            var plain = new FieldSchema("x", FieldKind.Number, required: false, description: "d");
            Assert.Null(plain.Feel);
            plain.WithFeel(Meta());
            Assert.NotNull(plain.Feel);
            Assert.Throws<InvalidOperationException>(() => plain.WithFeel(Meta()));
        }

        [Fact]
        public void FeelFieldMeta_RejectsInconsistentCombinations()
        {
            Assert.Throws<ArgumentException>(() => Meta(ops: FeelOpSet.None));
            Assert.Throws<ArgumentException>(() => Meta(comp: FeelComposition.CharacterPrimary, ops: FeelOpSet.Set | FeelOpSet.Add, offhand: true));
            Assert.Throws<ArgumentException>(() => Meta(comp: FeelComposition.WeaponPrimary, ops: FeelOpSet.Set, offhand: true));
            var ok = Meta(comp: FeelComposition.WeaponPrimary, ops: FeelOpSet.Set | FeelOpSet.Add, offhand: true);
            Assert.True(ok.OffhandStackable);
        }

        [Fact]
        public void FeelFieldDef_RejectsOpsIncompatibleWithKind_AndGroupHalfMismatch()
        {
            // 布尔字段登记 add：非法。
            Assert.Throws<ArgumentException>(() => new FeelFieldDef("b", FeelFieldKind.Bool,
                Meta(ops: FeelOpSet.Set | FeelOpSet.Add), "d"));
            // 输入组登记为呈现型：非法。
            Assert.Throws<ArgumentException>(() => new FeelFieldDef("n", FeelFieldKind.Number,
                Meta(half: FeelHalf.Presenting, group: FeelGroup.Input, ops: FeelOpSet.Set), "d", 0, 1));
            // 数值字段缺范围：非法。
            Assert.Throws<ArgumentException>(() => new FeelFieldDef("n", FeelFieldKind.Number, Meta(), "d"));
            // 说明为空：非法。
            Assert.Throws<ArgumentException>(() => new FeelFieldDef("b", FeelFieldKind.Bool, Meta(), " "));
            // 同名字段：非法。
            var a = new FeelFieldDef("b", FeelFieldKind.Bool, Meta(), "d");
            Assert.Throws<ArgumentException>(() => new FeelFieldSet(new[] { a, a }));
        }
    }
}
