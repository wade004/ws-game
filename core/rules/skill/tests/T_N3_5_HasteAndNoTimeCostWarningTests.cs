using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// T-N3-5（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 10；06 第
    /// 3.1/3.6 节 2026-09-14 修订段；13 新游戏接入指南"公共冷却与节拍锁"行；分阶段落地计划第 14 节
    /// N3 任务表第五行）：急速策略项与 <c>CastPipeline.ComputeCastTime</c> 接急速与下限；04 第 5 节
    /// "无时间成本"警告。见 <see cref="SkillOptions.HasteAffectsActionTime"/>/
    /// <see cref="SkillOptions.HasteStat"/>/<see cref="SkillOptions.MinActionSeconds"/>/
    /// <see cref="SkillOptions.MaxHastePct"/>、<see cref="SkillNoTimeCostWarningRule"/> 判断记录。
    /// </summary>
    public sealed class T_N3_5_HasteAndNoTimeCostWarningTests
    {
        private static readonly Id Caster = new Id("unit.n35_caster");
        private static readonly Id Target = new Id("unit.n35_target");
        private static readonly Id ChainId = new Id("target.chain.n35_sample");
        private static readonly Id HasteStat = new Id("stat.n35_haste");

        private static Core.Foundation.Common.Json.JsonObject CastTimeSkill(
            string id, double castTime, bool respectsGcd = false) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_n35")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(castTime)),
            ("respects_gcd", J.B(respectsGcd)),
            ("target_shape_ref", J.S(ChainId.Value)),
            ("effects", J.A()));

        // -----------------------------------------------------------------
        // 急速缩短动作时长与下限（验收标准"急速缩短与下限 2 组"）。
        // -----------------------------------------------------------------

        [Fact]
        public void Haste_Enabled_ShortensCastTime_ByFormula()
        {
            var skill = CastTimeSkill("skill.n35_haste_basic", castTime: 4.0);
            var builder = new SkillWorldBuilder().SkillDef(skill).Stat(HasteStat.Value);
            builder.Options.HasteAffectsActionTime = true;
            builder.Options.HasteStat = HasteStat;

            var world = builder.Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            world.Stats.SetBase(Caster, HasteStat, 100); // 100% 急速。

            var result = world.Host.CastSkill(Caster, new Id("skill.n35_haste_basic"), System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);
            var start = world.Of<SkillCastStartEvent>().Single();
            // castTime / (1 + haste% / 100) = 4.0 / (1 + 1.0) = 2.0。
            Assert.Equal(2.0, start.CastTime, 9);
        }

        [Fact]
        public void Haste_RawValueAboveMaxHastePct_ClampedBeforeApplied()
        {
            var skill = CastTimeSkill("skill.n35_haste_cap", castTime: 6.0);
            var builder = new SkillWorldBuilder().SkillDef(skill).Stat(HasteStat.Value);
            builder.Options.HasteAffectsActionTime = true;
            builder.Options.HasteStat = HasteStat;
            builder.Options.MaxHastePct = 50; // 硬上限 50%。

            var world = builder.Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            world.Stats.SetBase(Caster, HasteStat, 999); // 远超硬上限，应被夹到 50。

            var result = world.Host.CastSkill(Caster, new Id("skill.n35_haste_cap"), System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);
            var start = world.Of<SkillCastStartEvent>().Single();
            // 6.0 / (1 + 0.5) = 4.0（不是按 999% 折算出的接近 0）。
            Assert.Equal(4.0, start.CastTime, 9);
        }

        [Fact]
        public void Haste_ShortenedBelowMinActionSeconds_ClampsToFloor()
        {
            var skill = CastTimeSkill("skill.n35_haste_floor", castTime: 10.0);
            var builder = new SkillWorldBuilder().SkillDef(skill).Stat(HasteStat.Value);
            builder.Options.HasteAffectsActionTime = true;
            builder.Options.HasteStat = HasteStat;
            builder.Options.MaxHastePct = 1000; // 允许高倍急速，让折算结果真正落到下限之下。
            builder.Options.MinActionSeconds = 2.5;

            var world = builder.Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            world.Stats.SetBase(Caster, HasteStat, 900); // 900% 急速：10.0 / (1+9) = 1.0，低于下限 2.5。

            var result = world.Host.CastSkill(Caster, new Id("skill.n35_haste_floor"), System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);
            var start = world.Of<SkillCastStartEvent>().Single();
            Assert.Equal(2.5, start.CastTime, 9);
        }

        [Fact]
        public void Haste_ZeroPercent_DoesNotApplyFloor_EvenWhenBelowMinActionSeconds()
        {
            // 判断记录（CastPipeline.ComputeCastTime）：下限只夹住"急速造成的缩短"，haste<=0（本例
            // 属性基础值为 0，未获得任何有效急速）时不套用下限——authoring 本就低于 MinActionSeconds
            // 的瞬发/短读条技能不应被本策略项意外拉长。
            var skill = CastTimeSkill("skill.n35_haste_zero", castTime: 1.0);
            var builder = new SkillWorldBuilder().SkillDef(skill).Stat(HasteStat.Value);
            builder.Options.HasteAffectsActionTime = true;
            builder.Options.HasteStat = HasteStat;
            builder.Options.MinActionSeconds = 5.0; // 远高于原始 cast_time。

            var world = builder.Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            // 未 SetBase：属性最终值缺省 0（见 SkillWorldBuilder.Stat 的 defaultBase=0）。

            var result = world.Host.CastSkill(Caster, new Id("skill.n35_haste_zero"), System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);
            var start = world.Of<SkillCastStartEvent>().Single();
            Assert.Equal(1.0, start.CastTime, 9);
        }

        // -----------------------------------------------------------------
        // 回归：默认关闭时行为完全不变（硬性规则"禁止默认开启急速缩短"）。
        // -----------------------------------------------------------------

        [Fact]
        public void Haste_DisabledByDefault_CastTimeUnaffected_Regression()
        {
            var skill = CastTimeSkill("skill.n35_haste_off", castTime: 4.0);
            var builder = new SkillWorldBuilder().SkillDef(skill).Stat(HasteStat.Value);
            // Options 未改动：SkillOptions.HasteAffectsActionTime 缺省 false。

            var world = builder.Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);
            world.Stats.SetBase(Caster, HasteStat, 500); // 即使属性值很高也不应生效。

            Assert.False(world.Options.HasteAffectsActionTime);

            var result = world.Host.CastSkill(Caster, new Id("skill.n35_haste_off"), System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);
            var start = world.Of<SkillCastStartEvent>().Single();
            Assert.Equal(4.0, start.CastTime, 9);
        }

        [Fact]
        public void Haste_EnabledButHasteStatNotConfigured_CastTimeUnaffected()
        {
            var skill = CastTimeSkill("skill.n35_haste_nostat", castTime: 4.0);
            var builder = new SkillWorldBuilder().SkillDef(skill);
            builder.Options.HasteAffectsActionTime = true; // 开启但未设置 HasteStat（缺省 null）。

            var world = builder.Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(ChainId, Target);

            var result = world.Host.CastSkill(Caster, new Id("skill.n35_haste_nostat"), System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);
            var start = world.Of<SkillCastStartEvent>().Single();
            Assert.Equal(4.0, start.CastTime, 9);
        }

        // -----------------------------------------------------------------
        // "无时间成本"警告（04 第 5 节；SkillNoTimeCostWarningRule）：正负例。
        // -----------------------------------------------------------------

        [Fact]
        public void Validation_ActiveSkill_CastTimeZero_RespectsGcdTrue_ProducesWarning()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(CastTimeSkill("skill.n35_warn_positive", castTime: 0, respectsGcd: true))
                .ValidationRule(new SkillNoTimeCostWarningRule())
                .Validate();

            Assert.False(report.IsBlocking, "Warning 不应阻断（NonEscalatable）");
            var issue = report.Issues.Single(i => i.Check == "skill_no_time_cost");
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal("skill.def", issue.Table);
        }

        [Fact]
        public void Validation_ActiveSkill_WithNonZeroCastTime_NoWarning()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(CastTimeSkill("skill.n35_warn_has_cast_time", castTime: 1.5, respectsGcd: true))
                .ValidationRule(new SkillNoTimeCostWarningRule())
                .Validate();

            Assert.DoesNotContain(report.Issues, i => i.Check == "skill_no_time_cost");
        }

        [Fact]
        public void Validation_ActiveSkill_DeclaredReactive_RespectsGcdFalse_NoWarning()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(CastTimeSkill("skill.n35_warn_reactive", castTime: 0, respectsGcd: false))
                .ValidationRule(new SkillNoTimeCostWarningRule())
                .Validate();

            Assert.DoesNotContain(report.Issues, i => i.Check == "skill_no_time_cost");
        }

        [Fact]
        public void Validation_ChannelSkill_CastTimeZero_ChannelTimeNonZero_NoWarning()
        {
            var skill = J.O(
                ("id", J.S("skill.n35_warn_channel")),
                ("school", J.S("skill.school_n35")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("channel_time", J.N(3.0)),
                ("respects_gcd", J.B(true)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A()));

            var report = new SkillWorldBuilder()
                .SkillDef(skill)
                .ValidationRule(new SkillNoTimeCostWarningRule())
                .Validate();

            Assert.DoesNotContain(report.Issues, i => i.Check == "skill_no_time_cost");
        }

        [Fact]
        public void Validation_PassiveSkill_CastTimeZero_NoWarning()
        {
            var skill = J.O(
                ("id", J.S("skill.n35_warn_passive")),
                ("school", J.S("skill.school_n35")),
                ("kind", J.S("passive")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(true)),
                ("target_shape_ref", J.S(ChainId.Value)),
                ("effects", J.A()));

            var report = new SkillWorldBuilder()
                .SkillDef(skill)
                .ValidationRule(new SkillNoTimeCostWarningRule())
                .Validate();

            Assert.DoesNotContain(report.Issues, i => i.Check == "skill_no_time_cost");
        }
    }
}
