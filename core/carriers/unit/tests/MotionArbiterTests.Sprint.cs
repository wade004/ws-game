// M5-S4（ADR-0147，手感设计/02 第 3.2 节）：冲刺移动模式。复现：Sprint 目标速度 = 基础移速 × sprint_speed_ratio；
// 不变量：缺省（无 sprint_speed_ratio）时 Sprint 与 Run 逐位一致，且无运动层的既有路径对 Sprint 与 Run 逐位一致。
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Xunit;

namespace Tests.Carriers.Unit
{
    public partial class MotionArbiterTests
    {
        [Fact]
        public void Sprint_TargetSpeedIsBaseSpeedTimesSprintRatio_AndRatioChangesTakeEffectNextTick()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.AccelMs, 0.0);
            fx.Set(FeelFieldNames.TurnRateDegS, 0.0);
            fx.Set(FeelFieldNames.SprintSpeedRatio, 1.6);
            fx.Move(1, 0, "Sprint");
            fx.Tick();
            Near(Speed * 1.6 * Dt, fx.Pos.X);
            Assert.Equal(MoveMode.Sprint, fx.Player.MovementState.Mode);

            fx.Set(FeelFieldNames.SprintSpeedRatio, 2.0); // 热改：下一 tick 生效
            fx.Move(1, 0, "Sprint");
            fx.Tick();
            Near(Speed * 1.6 * Dt + Speed * 2.0 * Dt, fx.Pos.X);

            fx.Move(1, 0, "Run"); // 退出冲刺：回到基础移速
            fx.Tick();
            Near(Speed * 1.6 * Dt + Speed * 2.0 * Dt + Speed * Dt, fx.Pos.X);
        }

        [Fact]
        public void Sprint_WithoutSprintRatio_EqualsRun_AndLegacyPathTreatsSprintAsRun()
        {
            List<double[]> Run(bool motion, string mode)
            {
                var fx = Build(motion: motion);
                var rows = new List<double[]>();
                for (var i = 0; i < 12; i++)
                {
                    fx.Move(i < 8 ? 1 : 0, i < 8 ? 0.5 : 1, mode);
                    fx.Tick();
                    rows.Add(new[] { fx.Pos.X, fx.Pos.Y, fx.Player.Facing });
                }

                return rows;
            }

            foreach (var motion in new[] { true, false })
            {
                var run = Run(motion, "Run");
                var sprint = Run(motion, "Sprint");
                for (var i = 0; i < run.Count; i++)
                {
                    Assert.True(run[i].SequenceEqual(sprint[i]), $"motion={motion} 第 {i} tick Sprint 与 Run 的位置/朝向不是逐位一致");
                }
            }
        }

        [Fact]
        public void MotionProfile_ReadsSprintRatio_LegacyEquivalentIsOne()
        {
            var fx = Build();
            fx.Set(FeelFieldNames.SprintSpeedRatio, 1.7);
            Assert.Equal(1.7, MotionProfile.Read(fx.Feel.Resolver.ResolveJudging(HeroId)).SprintSpeedRatio);
            Assert.Equal(1.0, MotionProfile.LegacyEquivalent.SprintSpeedRatio);
        }

        [Fact]
        public void DataMotionCurveSource_ResolvesBakedRows_AndMissingIsNullNotLinear()
        {
            var source = new InMemoryDataSource();
            source.Add("skill.motion_curve",
                "{ \"table\": \"skill.motion_curve\", \"schema_version\": 1, \"rows\": [ { \"id\": \"skill.motion_curve.lunge_a\", " +
                "\"points\": [ {\"x\":0,\"y\":0}, {\"x\":0.5,\"y\":0.1}, {\"x\":1,\"y\":1} ] } ] }");
            var bus = new Core.Foundation.EventBus.EventBus(
                Core.Foundation.EventBus.EventCatalog.FromDefinitions(Array.Empty<Core.Foundation.EventBus.EventDefinition>()),
                new Core.Foundation.EventBus.EventBusOptions { StrictCatalog = false });
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            registry.RegisterSchema(Core.Rules.Skill.SkillSchemas.MotionCurve);
            Assert.Equal(0, registry.LoadAll().ErrorCount);

            var curves = new DataMotionCurveSource(registry);
            var c = curves.GetCurve("skill.motion_curve.lunge_a");
            Assert.NotNull(c);
            Assert.Equal(0.1, c!.Evaluate(0.5), 9);
            Assert.Same(c, curves.GetCurve("skill.motion_curve.lunge_a")); // 记录未变则复用解析结果
            Assert.Null(curves.GetCurve("skill.motion_curve.nope"));
            Near(2.0 * 0.1, 2.0 * MotionMath.EvalCurve("custom:skill.motion_curve.lunge_a", 0.5, curves));
            Assert.Throws<InvalidOperationException>(() => MotionMath.EvalCurve("custom:nope", 0.5, curves));
        }
    }
}
