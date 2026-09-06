using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// W1 收边补齐（A3 审计 #4/#5）：施法管线离散模式两处接线点——
    /// <c>skill.def.action_cost</c> 行动点消耗（<see cref="SkillOptions.TryConsumeActionPoints"/>）与
    /// 公共冷却离散恒关闭（<see cref="SkillOptions.IsDiscreteStep"/>）。二者均是可选注入点，未装配
    /// 时行为与本次改动之前完全一致（连续模式）。
    /// </summary>
    public sealed class DiscreteModeCastPipelineTests
    {
        private static JsonObject SkillDef(
            string id = "skill.sample_bolt",
            bool respectsGcd = true,
            double actionCost = 0)
        {
            var fields = new List<(string, JsonValue)>
            {
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(respectsGcd)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(10)), ("coefficient", J.N(0))))))),
            };

            if (actionCost != 0)
            {
                fields.Add(("action_cost", J.N(actionCost)));
            }

            return J.O(fields.ToArray());
        }

        // -----------------------------------------------------------------
        // action_cost（步骤 5b）
        // -----------------------------------------------------------------

        [Fact]
        public void DiscreteStep_ActionCostGreaterThanZero_ConsumesInjectedLedger_WhenSufficient()
        {
            var builder = new SkillWorldBuilder();
            builder.Options.IsDiscreteStep = () => true;

            var consumeCalls = new List<(Id Unit, double Amount)>();
            builder.Options.TryConsumeActionPoints = (unitId, amount) =>
            {
                consumeCalls.Add((unitId, amount));
                return true;
            };

            var world = builder.SkillDef(SkillDef(actionCost: 2, respectsGcd: false)).Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.sample"), caster);

            var result = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            Assert.Single(consumeCalls);
            Assert.Equal(caster, consumeCalls[0].Unit);
            Assert.Equal(2, consumeCalls[0].Amount);
        }

        [Fact]
        public void DiscreteStep_ActionCostGreaterThanZero_FailsWithInsufficientActionPoints_WhenLedgerRefuses()
        {
            var builder = new SkillWorldBuilder();
            builder.Options.IsDiscreteStep = () => true;
            builder.Options.TryConsumeActionPoints = (unitId, amount) => false; // 账本恒拒绝。

            var world = builder.SkillDef(SkillDef(actionCost: 1, respectsGcd: false)).Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.sample"), caster);

            var result = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.InsufficientActionPoints, result.Reason);
        }

        [Fact]
        public void DiscreteStep_ActionCostZero_NeverConsultsLedger()
        {
            var builder = new SkillWorldBuilder();
            builder.Options.IsDiscreteStep = () => true;

            var called = false;
            builder.Options.TryConsumeActionPoints = (unitId, amount) => { called = true; return true; };

            // action_cost 未声明（默认 0）——离散步内也不应该消耗/查询行动点账本。
            var world = builder.SkillDef(SkillDef(actionCost: 0, respectsGcd: false)).Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.sample"), caster);

            var result = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            Assert.False(called);
        }

        [Fact]
        public void ContinuousMode_ActionCostIgnored_EvenWhenLedgerWouldRefuse()
        {
            var builder = new SkillWorldBuilder();
            // IsDiscreteStep 未装配（默认 null，等价于"恒连续模式"）。
            builder.Options.TryConsumeActionPoints = (unitId, amount) => false;

            var world = builder.SkillDef(SkillDef(actionCost: 5, respectsGcd: false)).Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.sample"), caster);

            var result = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());

            // 连续模式：action_cost 恒忽略，即便账本委托会拒绝也不影响施法结果。
            Assert.True(result.Success);
        }

        // -----------------------------------------------------------------
        // GCD 离散恒关闭（步骤 4 + 步骤 9 触发）
        // -----------------------------------------------------------------

        [Fact]
        public void DiscreteStep_GcdEnabled_StepFourAlwaysPasses_AndDoesNotStartGcd()
        {
            var builder = new SkillWorldBuilder();
            builder.Options.GcdEnabled = true;
            builder.Options.GcdDuration = 1.5;
            builder.Options.IsDiscreteStep = () => true;

            var world = builder.SkillDef(SkillDef(respectsGcd: true)).Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.sample"), caster);

            var first = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(first.Success);

            // 连续模式下这里会被 GcdActive 挡下（见 CastPipelineFailureTests.GcdActive_FailsWhenEnabled）；
            // 离散步内公共冷却恒关闭，立即再次施放应成功。
            var second = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(second.Success);
        }

        [Fact]
        public void ContinuousMode_GcdEnabled_StillBlocks_WhenIsDiscreteStepNotInjected()
        {
            // 对照组：IsDiscreteStep 未装配时行为应与本次改动之前完全一致（GcdActive 生效）。
            var builder = new SkillWorldBuilder();
            builder.Options.GcdEnabled = true;
            builder.Options.GcdDuration = 1.5;

            var world = builder.SkillDef(SkillDef(respectsGcd: true)).Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);
            world.Targets.SetChain(new Id("target.chain.sample"), caster);

            var first = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.True(first.Success);

            var second = world.Host.CastSkill(caster, new Id("skill.sample_bolt"), System.Array.Empty<Id>());
            Assert.False(second.Success);
            Assert.Equal(CastFailureReason.GcdActive, second.Reason);
        }
    }
}
