using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 一个发现的交付缺口（2026-09-21，ADR-0060）：<c>skill.aura_def</c> 新增可选字段
    /// <c>polarity</c>/<c>icon_ref</c>，<see cref="Core.Rules.Skill.AuraHost.GetActiveAuraSnapshots"/>
    /// 生产实现随真实施加流程同步透传。验收标准三条：①同时声明极性与图标引用的两个光环（一有利
    /// 一有害）取到声明值（极性取到 <see cref="AuraPolarity"/> 枚举值，不是裸字符串）；②未声明
    /// 该两字段的光环取到"未声明"（极性为 <see cref="AuraPolarity.Undeclared"/>，图标引用为
    /// <c>null</c>），且不影响其它既有字段（同 <see cref="AuraSnapshotTests"/> 既有断言口径逐位
    /// 一致）；③（presentation/ui 侧）见 <c>Tests.PresentationCommon.UiDataSourceTests</c> 对应
    /// 用例。另补一例：数据里 <c>polarity</c> 取值非法（不在 <c>beneficial</c>/<c>harmful</c> 之内）
    /// 时解析层直接抛异常，不静默降级为"未声明"（AGENTS.md §3，见 <c>AuraPolarityNames.Parse</c>
    /// 判断记录）。
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
            Assert.Equal(AuraPolarity.Beneficial, snapshots[0].Polarity);
            Assert.Equal(new Id(BuffIconRef), snapshots[0].IconRef);

            Assert.Equal(new Id("skill.aura_def.sample_debuff"), snapshots[1].AuraDefId);
            Assert.Equal(AuraPolarity.Harmful, snapshots[1].Polarity);
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

            // 未声明 → 未声明的表示（极性是枚举里的显式取值 Undeclared，不是猜出来的具体极性；
            // 图标引用是 null）。
            Assert.Equal(AuraPolarity.Undeclared, snapshot.Polarity);
            Assert.Null(snapshot.IconRef);

            // 其它既有字段逐位不变（同 AuraSnapshotTests 既有断言口径：身份/层数/剩余=总时长=声明值/名称键）。
            Assert.Equal(new Id("skill.aura_def.sample_unmarked"), snapshot.AuraDefId);
            Assert.Equal(1, snapshot.Stacks);
            Assert.Equal(duration, snapshot.Remaining);
            Assert.Equal(duration, snapshot.Total);
            Assert.Null(snapshot.NameKey);

            Assert.Empty(world.Diagnostics.Warnings);
        }

        /// <summary>
        /// 判断记录（非法取值不静默降级为"未声明"）：<c>skill.aura_def.polarity</c> 的合法取值集合
        /// 由 schema 登记为 <c>beneficial</c>/<c>harmful</c> 两值，加载期校验理论上已经挡住任何其它
        /// 文本；解析层（<c>AuraPolarityNames.Parse</c>，供 <c>SkillDefCache.ParseAuraDef</c> 调用）
        /// 仍按 AGENTS.md §3"运行时路径不静默降级"防御性处理——遇到未知文本直接抛
        /// <see cref="System.ArgumentException"/>，不当作"字段未声明"顶替，惯例同同一文件内
        /// <c>AuraEffectKindNames.Parse</c> 处理 <c>effects[].kind</c> 未知取值的既有做法。
        /// </summary>
        [Fact]
        public void AuraPolarityNames_Parse_UnknownText_ThrowsInsteadOfFallingBackToUndeclared()
        {
            Assert.Throws<System.ArgumentException>(() => AuraPolarityNames.Parse("not_a_real_polarity"));
        }
    }
}
