using Core.Foundation.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 一个发现的交付缺口（2026-09-21，ADR-0060）：<c>skill.aura_def</c> 新增可选字段
    /// <c>polarity</c>/<c>icon_ref</c>，<see cref="Core.Rules.Skill.AuraHost.GetActiveAuraSnapshots"/>
    /// 生产实现随真实施加流程同步透传。验收标准三条：①同时声明极性与图标引用的两个光环（一有利
    /// 一有害）取到声明值；②未声明该两字段的光环取到"未声明"（<c>null</c>），且不影响其它既有
    /// 字段（同 <see cref="AuraSnapshotTests"/> 既有断言口径逐位一致）；③（presentation/ui 侧）
    /// 见 <c>Tests.PresentationCommon.UiDataSourceTests</c> 对应用例。
    /// </summary>
    public sealed class AuraPolarityIconRefTests
    {
        private const string BeneficialPolarity = "beneficial";
        private const string HarmfulPolarity = "harmful";
        private const string BuffIconRef = "icon.aura.sample_fortify";
        private const string DebuffIconRef = "icon.aura.sample_burn";

        private static Core.Foundation.Common.Json.JsonObject AuraWithPolarityAndIcon(
            string id, double duration, string polarity, string iconRef)
        {
            return J.O(
                ("id", J.S(id)),
                ("max_stacks", J.N(1)),
                ("duration", J.N(duration)),
                ("polarity", J.S(polarity)),
                ("icon_ref", J.S(iconRef)),
                ("effects", J.A(
                    J.O(("kind", J.S("mod_stat")),
                        ("params", J.O(("stat", J.S("stat.sample_power")), ("op", J.S("flat")), ("value", J.N(1))))))));
        }

        private static Core.Foundation.Common.Json.JsonObject AuraWithoutPolarityOrIcon(string id, double duration)
        {
            return J.O(
                ("id", J.S(id)),
                ("max_stacks", J.N(1)),
                ("duration", J.N(duration)),
                ("effects", J.A(
                    J.O(("kind", J.S("mod_stat")),
                        ("params", J.O(("stat", J.S("stat.sample_power")), ("op", J.S("flat")), ("value", J.N(1))))))));
        }

        [Fact]
        public void TwoAuras_OneBeneficialOneHarmful_BothDeclareIconRef_SnapshotsExposeDeclaredValues()
        {
            var buffDuration = 8.0;
            var debuffDuration = 6.0;
            var buff = AuraWithPolarityAndIcon("skill.aura_def.sample_buff", buffDuration, BeneficialPolarity, BuffIconRef);
            var debuff = AuraWithPolarityAndIcon("skill.aura_def.sample_debuff", debuffDuration, HarmfulPolarity, DebuffIconRef);

            var world = new SkillWorldBuilder().AuraDef(buff).AuraDef(debuff).Stat("stat.sample_power").Build();
            var target = new Id("unit.target");
            world.AddUnit(target);

            var sink = world.Host.EffectSink;
            sink.ApplyAura(target, new Id("skill.aura_def.sample_buff"), new Id("unit.source"));
            sink.ApplyAura(target, new Id("skill.aura_def.sample_debuff"), new Id("unit.source"));
            world.Flush();

            var snapshots = world.Host.AuraQuery.GetActiveAuraSnapshots(target);
            Assert.Equal(2, snapshots.Count);

            Assert.Equal(new Id("skill.aura_def.sample_buff"), snapshots[0].AuraDefId);
            Assert.Equal(BeneficialPolarity, snapshots[0].Polarity);
            Assert.Equal(new Id(BuffIconRef), snapshots[0].IconRef);

            Assert.Equal(new Id("skill.aura_def.sample_debuff"), snapshots[1].AuraDefId);
            Assert.Equal(HarmfulPolarity, snapshots[1].Polarity);
            Assert.Equal(new Id(DebuffIconRef), snapshots[1].IconRef);

            Assert.Empty(world.Diagnostics.Warnings);
        }

        [Fact]
        public void AuraWithoutPolarityOrIconRef_ResolvesToUndeclared_NotFabricatedDefault_OtherFieldsUnaffected()
        {
            var duration = 5.0;
            var undeclared = AuraWithoutPolarityOrIcon("skill.aura_def.sample_unmarked", duration);
            var world = new SkillWorldBuilder().AuraDef(undeclared).Stat("stat.sample_power").Build();
            var target = new Id("unit.target");
            world.AddUnit(target);

            var sink = world.Host.EffectSink;
            sink.ApplyAura(target, new Id("skill.aura_def.sample_unmarked"), new Id("unit.source"));
            world.Flush();

            var snapshot = Assert.Single(world.Host.AuraQuery.GetActiveAuraSnapshots(target));

            // 未声明 → 未声明的表示（null），不是猜出来的具体极性/图标。
            Assert.Null(snapshot.Polarity);
            Assert.Null(snapshot.IconRef);

            // 其它既有字段逐位不变（同 AuraSnapshotTests 既有断言口径：身份/层数/剩余=总时长=声明值/名称键）。
            Assert.Equal(new Id("skill.aura_def.sample_unmarked"), snapshot.AuraDefId);
            Assert.Equal(1, snapshot.Stacks);
            Assert.Equal(duration, snapshot.Remaining);
            Assert.Equal(duration, snapshot.Total);
            Assert.Null(snapshot.NameKey);

            Assert.Empty(world.Diagnostics.Warnings);
        }
    }
}
