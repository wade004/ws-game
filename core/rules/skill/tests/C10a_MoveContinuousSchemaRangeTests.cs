using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// ADR-0026《技能位移的连续模式》验收：<c>skill.def.effects[].kind == "move"</c> 新增的
    /// <c>speed</c>/<c>duration</c>/<c>sample_step</c> 三个数值字段（若声明必须 &gt; 0，见
    /// <c>SkillSchemas.cs</c> <c>EffectKind.Move</c> 变体登记判断记录）与 <c>motion</c>/<c>blocking</c>
    /// 两个枚举字段的取值集合，在 <see cref="SkillWorldBuilder.Validate"/> 加载阶段即被正确接受/
    /// 拒绝——惯例同 <see cref="SkillEffectParamRangeTests"/>（ADR-0021 同一口径）。
    /// </summary>
    public sealed class C10a_MoveContinuousSchemaRangeTests
    {
        private static readonly Id SkillId = new Id("skill.svng_move_continuous");

        private static JsonObject MoveSkill(params (string Key, JsonValue Value)[] moveParams) => J.O(
            ("id", J.S(SkillId.Value)),
            ("school", J.S("skill.school_svng")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S("target.chain.svng_sample")),
            ("effects", J.A(J.O(("kind", J.S("move")), ("params", J.O(moveParams))))));

        // -----------------------------------------------------------------
        // speed > 0
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        public void Speed_ZeroOrNegative_BlockedAtLoad(double speed)
        {
            var report = new SkillWorldBuilder()
                .SkillDef(MoveSkill(("motion", J.S("continuous")), ("speed", J.N(speed))))
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "effects[0].params.speed");
        }

        [Fact]
        public void Speed_Positive_Passes()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(MoveSkill(("motion", J.S("continuous")), ("speed", J.N(2))))
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // duration > 0
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(0.0)]
        [InlineData(-2.0)]
        public void Duration_ZeroOrNegative_BlockedAtLoad(double duration)
        {
            var report = new SkillWorldBuilder()
                .SkillDef(MoveSkill(("motion", J.S("continuous")), ("duration", J.N(duration))))
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "effects[0].params.duration");
        }

        [Fact]
        public void Duration_Positive_Passes()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(MoveSkill(("motion", J.S("continuous")), ("duration", J.N(1.5))))
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // sample_step > 0
        // -----------------------------------------------------------------

        [Fact]
        public void SampleStep_Negative_BlockedAtLoad()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(MoveSkill(("motion", J.S("continuous")), ("speed", J.N(1)), ("sample_step", J.N(-0.1))))
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "field_range" && i.Field == "effects[0].params.sample_step");
        }

        [Fact]
        public void SampleStep_Omitted_Passes()
        {
            // 省略时由 L3 MovementOptions.DefaultDisplacementSampleStep 兜底，登记层不强制必填。
            var report = new SkillWorldBuilder()
                .SkillDef(MoveSkill(("motion", J.S("continuous")), ("speed", J.N(1))))
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // motion / blocking 枚举取值集合
        // -----------------------------------------------------------------

        [Fact]
        public void Motion_UnknownValue_BlockedAtLoad()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(MoveSkill(("motion", J.S("teleport_like_but_not_registered"))))
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Field == "effects[0].params.motion");
        }

        [Theory]
        [InlineData("instant")]
        [InlineData("continuous")]
        public void Motion_RegisteredValues_Pass(string motion)
        {
            var report = new SkillWorldBuilder()
                .SkillDef(MoveSkill(("motion", J.S(motion)), ("speed", J.N(1))))
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void Blocking_UnknownValue_BlockedAtLoad()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(MoveSkill(("motion", J.S("continuous")), ("speed", J.N(1)), ("blocking", J.S("bounce"))))
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Field == "effects[0].params.blocking");
        }

        [Theory]
        [InlineData("stop")]
        [InlineData("revert")]
        public void Blocking_RegisteredValues_Pass(string blocking)
        {
            var report = new SkillWorldBuilder()
                .SkillDef(MoveSkill(("motion", J.S("continuous")), ("speed", J.N(1)), ("blocking", J.S(blocking))))
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        // -----------------------------------------------------------------
        // 既有 mode（charge|leap|knockback）取值集合不受本次扩容影响的回归。
        // -----------------------------------------------------------------

        [Fact]
        public void ExistingModeField_StillValidatedIndependently_UnknownValueBlocked()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(MoveSkill(("mode", J.S("not_a_real_mode"))))
                .Validate();

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Field == "effects[0].params.mode");
        }

        [Fact]
        public void ExistingThreeModes_OmittedMotion_Passes_DefaultsToInstant()
        {
            var report = new SkillWorldBuilder()
                .SkillDef(MoveSkill(("mode", J.S("knockback")), ("distance", J.N(5))))
                .Validate();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }
    }
}
