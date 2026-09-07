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

        // -----------------------------------------------------------------
        // RC-04（见外部审计 architecture/落地计划/audit-b3b91ee-20260907/code-review.md RC-04、
        // validation-repros.txt R6）：无目标/超距/无视线三种"必然失败"的校验都在步骤 6/7，行动点
        // 消耗（步骤 5 之后、原步骤 6 之前）此前抢在它们前面执行——技能注定失败仍会真正扣掉行动点。
        // 修复后行动点消耗挪到全部校验通过之后，三种失败分支都不应触碰行动点账本。
        // -----------------------------------------------------------------

        private static JsonObject RangedSkillDef(string id, double range, double actionCost) =>
            J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(range)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("action_cost", J.N(actionCost)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(10)), ("coefficient", J.N(0))))))));

        /// <summary>只实现 <see cref="ISpatialQuery.HasLineOfSight"/>（本测试唯一用到的成员）、
        /// 恒返回 false 的最小假实现——<c>Adapters.Stub.StubSpatialQuery</c> 的 <c>HasLineOfSight</c>
        /// 硬编码恒 true，无法配置为"无视线"，且该桩不在本模块可写范围内，故本文件自建一个。</summary>
        private sealed class DenyLineOfSightSpatialQuery : Core.Foundation.EngineAdapter.ISpatialQuery
        {
            public System.Collections.Generic.IReadOnlyList<Id> QueryRadius(Vec2 center, double radius, Core.Foundation.EngineAdapter.QueryFilter filter) => System.Array.Empty<Id>();
            public System.Collections.Generic.IReadOnlyList<Id> QueryCone(Vec2 origin, double direction, double angle, double range, Core.Foundation.EngineAdapter.QueryFilter filter) => System.Array.Empty<Id>();
            public System.Collections.Generic.IReadOnlyList<Id> QueryLine(Vec2 from, Vec2 to, Core.Foundation.EngineAdapter.QueryFilter filter) => System.Array.Empty<Id>();
            public System.Collections.Generic.IReadOnlyList<Id> QueryRect(Vec2 min, Vec2 max, Core.Foundation.EngineAdapter.QueryFilter filter) => System.Array.Empty<Id>();
            public System.Collections.Generic.IReadOnlyList<Id> QueryShape(Core.Foundation.EngineAdapter.Shape shape, Core.Foundation.EngineAdapter.QueryFilter filter) => System.Array.Empty<Id>();
            public Id? Nearest(Vec2 point, Core.Foundation.EngineAdapter.QueryFilter filter) => null;
            public bool HasLineOfSight(Vec2 from, Vec2 to) => false;
            public void Register(Id id, Vec2 position, double radius, System.Collections.Generic.IReadOnlyList<string> tags) { }
            public void UpdatePosition(Id id, Vec2 position) { }
            public void Unregister(Id id) { }
            public void Clear() { }
        }

        [Fact]
        public void DiscreteStep_NoValidTarget_DoesNotConsumeActionPoints()
        {
            var builder = new SkillWorldBuilder();
            builder.Options.IsDiscreteStep = () => true;
            var consumeCalls = 0;
            builder.Options.TryConsumeActionPoints = (unitId, amount) => { consumeCalls++; return true; };

            var world = builder.SkillDef(RangedSkillDef("skill.sample_no_target", range: 5, actionCost: 2)).Build();
            var caster = new Id("unit.caster");
            world.AddUnit(caster);
            // 故意不给 target.chain.sample 设置任何目标——目标链解析为空，步骤 6 NoValidTarget。

            var result = world.Host.CastSkill(caster, new Id("skill.sample_no_target"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.NoValidTarget, result.Reason);
            Assert.Equal(0, consumeCalls);
        }

        [Fact]
        public void DiscreteStep_OutOfRange_DoesNotConsumeActionPoints()
        {
            var builder = new SkillWorldBuilder();
            builder.Options.IsDiscreteStep = () => true;
            var consumeCalls = 0;
            builder.Options.TryConsumeActionPoints = (unitId, amount) => { consumeCalls++; return true; };

            var world = builder.SkillDef(RangedSkillDef("skill.sample_out_of_range", range: 5, actionCost: 2)).Build();
            var caster = new Id("unit.caster");
            var target = new Id("unit.target");
            world.AddUnit(caster, new Vec2(0, 0));
            world.AddUnit(target, new Vec2(100, 0));
            world.Targets.SetChain(new Id("target.chain.sample"), target);

            var result = world.Host.CastSkill(caster, new Id("skill.sample_out_of_range"), System.Array.Empty<Id>());

            // 修复前：R6 复现——超距校验之前行动点已经被消费一次（见外部审计 validation-repros.txt）。
            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.OutOfRange, result.Reason);
            Assert.Equal(0, consumeCalls);
        }

        [Fact]
        public void DiscreteStep_NoLineOfSight_DoesNotConsumeActionPoints()
        {
            var builder = new SkillWorldBuilder();
            builder.Options.IsDiscreteStep = () => true;
            builder.SpatialQuery = new DenyLineOfSightSpatialQuery();
            var consumeCalls = 0;
            builder.Options.TryConsumeActionPoints = (unitId, amount) => { consumeCalls++; return true; };

            var world = builder.SkillDef(RangedSkillDef("skill.sample_no_los", range: 5, actionCost: 2)).Build();
            var caster = new Id("unit.caster");
            var target = new Id("unit.target");
            world.AddUnit(caster, new Vec2(0, 0));
            world.AddUnit(target, new Vec2(1, 0)); // 射程内，但视线被拒绝。
            world.Targets.SetChain(new Id("target.chain.sample"), target);

            var result = world.Host.CastSkill(caster, new Id("skill.sample_no_los"), System.Array.Empty<Id>());

            Assert.False(result.Success);
            Assert.Equal(CastFailureReason.LineOfSight, result.Reason);
            Assert.Equal(0, consumeCalls);
        }

        [Fact]
        public void DiscreteStep_AllValidationsPass_ConsumesActionPointsExactlyOnce()
        {
            // 对照组：全部校验通过时，行动点仍然会被消费（且只消费一次）——确认本次改动只是
            // 挪动调用时机，不是移除调用。
            var builder = new SkillWorldBuilder();
            builder.Options.IsDiscreteStep = () => true;
            var consumeCalls = 0;
            builder.Options.TryConsumeActionPoints = (unitId, amount) => { consumeCalls++; return true; };

            var world = builder.SkillDef(RangedSkillDef("skill.sample_valid_cast", range: 5, actionCost: 2)).Build();
            var caster = new Id("unit.caster");
            var target = new Id("unit.target");
            world.AddUnit(caster, new Vec2(0, 0));
            world.AddUnit(target, new Vec2(1, 0));
            world.Targets.SetChain(new Id("target.chain.sample"), target);

            var result = world.Host.CastSkill(caster, new Id("skill.sample_valid_cast"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            Assert.Equal(1, consumeCalls);
        }
    }
}
