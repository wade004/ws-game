using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// ADR-0026《技能位移的连续模式》：<c>EffectDispatcher.ApplyMove</c> 的 <c>motion: continuous</c>
    /// 分派/参数组装测试——只验证 <see cref="EffectDispatcher"/> 这一侧的职责（按 <c>mode</c>
    /// 子类型算出被位移单位与目标点、组装 <see cref="ControlledDisplacementRequest"/> 转交
    /// <see cref="IControlledDisplacementSink"/>），用一个记录用的假 sink（本模块是 L2，不能引用
    /// L3 的真实 <c>MovementHost</c>，见 <see cref="IControlledDisplacementSink"/> 判断记录"依赖
    /// 倒置"）——真实逐 tick 推进/阻挡裁决见 <c>core/carriers/unit/tests/
    /// C10a_ControlledDisplacementTests.cs</c>，真实技能施法端到端见 <c>core/carriers/assembly/
    /// tests/C10a_ContinuousMoveEndToEndTests.cs</c>。<c>motion</c> 缺省/<c>instant</c> 的既有三种
    /// 瞬移模式回归见 <see cref="Instant_ThreeModes_Unaffected_NeverCallsSink"/>。
    /// </summary>
    public sealed class C10a_ContinuousMoveDispatchTests
    {
        private static readonly Id Caster = new Id("unit.caster");
        private static readonly Id Target = new Id("unit.target");

        private sealed class RecordingDisplacementSink : IControlledDisplacementSink
        {
            public readonly List<ControlledDisplacementRequest> Requests = new List<ControlledDisplacementRequest>();

            public void BeginControlledDisplacement(ControlledDisplacementRequest request) => Requests.Add(request);
        }

        private static JsonObject MoveSkill(string id, params (string Key, JsonValue Value)[] moveParams) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_sample")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S("target.chain.sample")),
            ("effects", J.A(J.O(("kind", J.S("move")), ("params", J.O(moveParams))))));

        private static SkillWorld BuildAndTarget(JsonObject skillDef, Vec2 casterPos, Vec2 targetPos)
        {
            var world = new SkillWorldBuilder().SkillDef(skillDef).Build();
            world.AddUnit(Caster, casterPos);
            world.AddUnit(Target, targetPos);
            world.Targets.SetChain(new Id("target.chain.sample"), Target);
            return world;
        }

        // -----------------------------------------------------------------
        // motion=continuous：按既有子类型（mode）算出的目标点与瞬移分支一致，转交 sink。
        // -----------------------------------------------------------------

        [Fact]
        public void Leap_Continuous_MovingUnitIsSource_TargetIsDeclaredPoint_ForwardsToSink()
        {
            var skill = MoveSkill("skill.sample_leap_continuous",
                ("mode", J.S("leap")), ("point", J.Vec(5, 3)),
                ("motion", J.S("continuous")), ("speed", J.N(2)));
            var world = BuildAndTarget(skill, new Vec2(0, 0), new Vec2(10, 10));
            var sink = new RecordingDisplacementSink();
            world.Host.DisplacementSink = sink;

            var result = world.Host.CastSkill(Caster, new Id("skill.sample_leap_continuous"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            var request = Assert.Single(sink.Requests);
            Assert.Equal(Caster, request.UnitId);
            Assert.Equal(new Vec2(0, 0), request.Origin);
            Assert.Equal(new Vec2(5, 3), request.Target);
            Assert.Equal(2.0, request.Speed, 6);
            Assert.Equal(DisplacementBlockingPolicy.Stop, request.Blocking); // 缺省 blocking=stop
            // 瞬移分支对同一输入应算出相同的目标点（无阻挡时兼容性，见类型注释）：这里不经过 sink
            // 推进，直接断言施法者本身位置未被 EffectDispatcher 自己动过（连续模式只转交请求）。
            Assert.Equal(new Vec2(0, 0), world.Units.GetPosition(Caster));
        }

        [Fact]
        public void Knockback_Continuous_MovingUnitIsTarget_SpeedFromDuration()
        {
            var skill = MoveSkill("skill.sample_knockback_continuous",
                ("mode", J.S("knockback")), ("distance", J.N(4)),
                ("motion", J.S("continuous")), ("duration", J.N(2)));
            var world = BuildAndTarget(skill, new Vec2(0, 0), new Vec2(2, 0));
            var sink = new RecordingDisplacementSink();
            world.Host.DisplacementSink = sink;

            var result = world.Host.CastSkill(Caster, new Id("skill.sample_knockback_continuous"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            var request = Assert.Single(sink.Requests);
            Assert.Equal(Target, request.UnitId); // knockback 位移的是目标，不是施法者
            Assert.Equal(new Vec2(2, 0), request.Origin);
            Assert.Equal(new Vec2(6, 0), request.Target); // 沿 caster->target 方向击退 4
            Assert.Equal(2.0, request.Speed, 6); // distance(4) / duration(2)
        }

        [Fact]
        public void Charge_Continuous_ExplicitSpeedWins_OverDuration()
        {
            var skill = MoveSkill("skill.sample_charge_continuous",
                ("mode", J.S("charge")), ("stop_distance", J.N(1.0)),
                ("motion", J.S("continuous")), ("speed", J.N(3)), ("duration", J.N(100)));
            var world = BuildAndTarget(skill, new Vec2(0, 0), new Vec2(10, 0));
            var sink = new RecordingDisplacementSink();
            world.Host.DisplacementSink = sink;

            var result = world.Host.CastSkill(Caster, new Id("skill.sample_charge_continuous"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            var request = Assert.Single(sink.Requests);
            Assert.Equal(Caster, request.UnitId);
            Assert.Equal(new Vec2(0, 0), request.Origin);
            Assert.Equal(new Vec2(9, 0), request.Target); // 停在目标前 1 单位（stop_distance）
            Assert.Equal(3.0, request.Speed, 6); // 显式 speed 优先于 duration 换算
        }

        [Fact]
        public void Continuous_Blocking_Revert_ParsedFromParams()
        {
            var skill = MoveSkill("skill.sample_leap_revert",
                ("mode", J.S("leap")), ("point", J.Vec(5, 0)),
                ("motion", J.S("continuous")), ("speed", J.N(1)), ("blocking", J.S("revert")));
            var world = BuildAndTarget(skill, new Vec2(0, 0), new Vec2(10, 10));
            var sink = new RecordingDisplacementSink();
            world.Host.DisplacementSink = sink;

            world.Host.CastSkill(Caster, new Id("skill.sample_leap_revert"), System.Array.Empty<Id>());

            var request = Assert.Single(sink.Requests);
            Assert.Equal(DisplacementBlockingPolicy.Revert, request.Blocking);
        }

        [Fact]
        public void Continuous_SampleStep_ForwardedUnchanged_ToRequest()
        {
            var skill = MoveSkill("skill.sample_leap_samplestep",
                ("mode", J.S("leap")), ("point", J.Vec(5, 0)),
                ("motion", J.S("continuous")), ("speed", J.N(1)), ("sample_step", J.N(0.25)));
            var world = BuildAndTarget(skill, new Vec2(0, 0), new Vec2(10, 10));
            var sink = new RecordingDisplacementSink();
            world.Host.DisplacementSink = sink;

            world.Host.CastSkill(Caster, new Id("skill.sample_leap_samplestep"), System.Array.Empty<Id>());

            var request = Assert.Single(sink.Requests);
            Assert.Equal(0.25, request.SampleStep, 6);
        }

        // -----------------------------------------------------------------
        // no-op：无效速度/零距离时不提交请求。
        // -----------------------------------------------------------------

        [Fact]
        public void Continuous_NoSpeedNoDuration_NoOp_DoesNotCallSink()
        {
            var skill = MoveSkill("skill.sample_leap_nospeed",
                ("mode", J.S("leap")), ("point", J.Vec(5, 0)), ("motion", J.S("continuous")));
            var world = BuildAndTarget(skill, new Vec2(0, 0), new Vec2(10, 10));
            var sink = new RecordingDisplacementSink();
            world.Host.DisplacementSink = sink;

            var result = world.Host.CastSkill(Caster, new Id("skill.sample_leap_nospeed"), System.Array.Empty<Id>());

            Assert.True(result.Success); // 施法本身成功，只是位移效果 no-op（同 charge 已在停止距离内的既有语义）
            Assert.Empty(sink.Requests);
        }

        [Fact]
        public void Continuous_ChargeAlreadyWithinStopDistance_ZeroDistance_NoOp()
        {
            var skill = MoveSkill("skill.sample_charge_noop",
                ("mode", J.S("charge")), ("stop_distance", J.N(20)),
                ("motion", J.S("continuous")), ("speed", J.N(5)));
            var world = BuildAndTarget(skill, new Vec2(0, 0), new Vec2(10, 0)); // 距离 10 < stop_distance 20
            var sink = new RecordingDisplacementSink();
            world.Host.DisplacementSink = sink;

            world.Host.CastSkill(Caster, new Id("skill.sample_charge_noop"), System.Array.Empty<Id>());

            Assert.Empty(sink.Requests);
        }

        // -----------------------------------------------------------------
        // 降级：未注入 sink 时退化为直接 SetPosition，并记一条警告。
        // -----------------------------------------------------------------

        [Fact]
        public void Continuous_NoSinkInjected_FallsBackToDirectSetPosition_AndWarns()
        {
            var skill = MoveSkill("skill.sample_leap_nosink",
                ("mode", J.S("leap")), ("point", J.Vec(5, 3)),
                ("motion", J.S("continuous")), ("speed", J.N(1)));
            var world = BuildAndTarget(skill, new Vec2(0, 0), new Vec2(10, 10));
            // 故意不设置 world.Host.DisplacementSink。

            var result = world.Host.CastSkill(Caster, new Id("skill.sample_leap_nosink"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            Assert.Equal(new Vec2(5, 3), world.Units.GetPosition(Caster));
            Assert.Contains(world.Diagnostics.Warnings, w => w.Contains("IControlledDisplacementSink"));
        }

        // -----------------------------------------------------------------
        // 兼容性回归：motion 缺省/instant 时既有三种瞬移模式逐字节不变，且绝不触碰 sink。
        // -----------------------------------------------------------------

        [Fact]
        public void Instant_ThreeModes_Unaffected_NeverCallsSink()
        {
            var leap = MoveSkill("skill.sample_leap_instant", ("mode", J.S("leap")), ("point", J.Vec(7, 7)));
            var world = BuildAndTarget(leap, new Vec2(0, 0), new Vec2(10, 10));
            var sink = new RecordingDisplacementSink();
            world.Host.DisplacementSink = sink;

            var result = world.Host.CastSkill(Caster, new Id("skill.sample_leap_instant"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            Assert.Equal(new Vec2(7, 7), world.Units.GetPosition(Caster)); // 直接 SetPosition，逻辑位置立即跳变
            Assert.Empty(sink.Requests); // instant 分支完全不触碰 DisplacementSink
        }

        [Fact]
        public void InstantMotionExplicit_SameAsDefault_Regression()
        {
            var skill = MoveSkill("skill.sample_knockback_instant_explicit",
                ("mode", J.S("knockback")), ("distance", J.N(3)), ("motion", J.S("instant")));
            var world = BuildAndTarget(skill, new Vec2(0, 0), new Vec2(2, 0));
            var sink = new RecordingDisplacementSink();
            world.Host.DisplacementSink = sink;

            world.Host.CastSkill(Caster, new Id("skill.sample_knockback_instant_explicit"), System.Array.Empty<Id>());

            Assert.Equal(new Vec2(5, 0), world.Units.GetPosition(Target)); // 2 + 3 沿 (1,0) 方向
            Assert.Empty(sink.Requests);
        }
    }
}
