using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>光环叠加/刷新/多来源合并/永久光环/叠加超限策略三种（见落地方案 T2-5 行"叠加、刷新
    /// ……"、06 第 3.8 节叠加与刷新规则）。</summary>
    public sealed class AuraStackingTests
    {
        private static Core.Foundation.Common.Json.JsonObject StackableAura(
            string id, int maxStacks, double? duration = 5)
        {
            var fields = new System.Collections.Generic.List<(string, Core.Foundation.Common.Json.JsonValue)>
            {
                ("id", J.S(id)),
                ("max_stacks", J.N(maxStacks)),
                ("effects", J.A(
                    J.O(("kind", J.S("mod_stat")),
                        ("params", J.O(("stat", J.S("stat.sample_power")), ("op", J.S("flat")), ("value", J.N(10))))))),
            };

            if (duration.HasValue)
            {
                fields.Add(("duration", J.N(duration.Value)));
            }

            return J.O(fields.ToArray());
        }

        [Fact]
        public void Stacking_IncreasesStacks_AndEmitsStackChanged()
        {
            var aura = StackableAura("skill.aura_def.sample_stack", maxStacks: 3);
            var world = new SkillWorldBuilder().AuraDef(aura).Stat("stat.sample_power").Build();
            world.AddUnit(new Id("unit.target"));

            var sink = world.Host.EffectSink;
            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_stack"), new Id("unit.source"));
            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_stack"), new Id("unit.source"));
            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_stack"), new Id("unit.source"));
            world.Flush();

            Assert.Equal(3, world.Host.AuraQuery.GetStacks(new Id("unit.target"), new Id("skill.aura_def.sample_stack")));

            var stackChanged = world.Of<AuraStackChangedEvent>().ToList();
            Assert.Equal(2, stackChanged.Count);
            Assert.Equal((1, 2), (stackChanged[0].OldStacks, stackChanged[0].NewStacks));
            Assert.Equal((2, 3), (stackChanged[1].OldStacks, stackChanged[1].NewStacks));
        }

        [Fact]
        public void Reapply_SameSource_RefreshesDuration()
        {
            var aura = StackableAura("skill.aura_def.sample_refresh", maxStacks: 1, duration: 5);
            var world = new SkillWorldBuilder().AuraDef(aura).Stat("stat.sample_power").Build();
            world.AddUnit(new Id("unit.target"));

            var sink = world.Host.EffectSink;
            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_refresh"), new Id("unit.source"));

            world.Host.Update(4.0);
            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_refresh")));

            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_refresh"), new Id("unit.source"));

            world.Host.Update(4.0);
            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_refresh")));

            world.Host.Update(2.0);
            Assert.False(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_refresh")));
        }

        [Fact]
        public void MultiSource_MergesIntoOneInstance_WhenAllowMultiSourceTimingDisabled()
        {
            var aura = StackableAura("skill.aura_def.sample_merge", maxStacks: 5);
            var builder = new SkillWorldBuilder().AuraDef(aura).Stat("stat.sample_power");
            builder.Options.AllowMultiSourceTiming = false;
            var world = builder.Build();
            world.AddUnit(new Id("unit.target"));

            var sink = world.Host.EffectSink;
            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_merge"), new Id("unit.source_a"));
            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_merge"), new Id("unit.source_b"));

            Assert.Equal(2, world.Host.AuraQuery.GetStacks(new Id("unit.target"), new Id("skill.aura_def.sample_merge")));
            Assert.Single(world.Host.AuraQuery.GetActiveAuraDefs(new Id("unit.target")));
        }

        [Fact]
        public void PermanentAura_NeverExpires()
        {
            var aura = StackableAura("skill.aura_def.sample_permanent", maxStacks: 1, duration: null);
            var world = new SkillWorldBuilder().AuraDef(aura).Stat("stat.sample_power").Build();
            world.AddUnit(new Id("unit.target"));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_permanent"), new Id("unit.source"));

            world.Host.Update(10_000);

            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_permanent")));
        }

        [Fact]
        public void StackOverflow_Ignore_DoesNothing()
        {
            RunOverflowScenario(StackOverflowPolicy.Ignore, out var stacksAfter, out var refreshedRemaining);
            Assert.Equal(1, stacksAfter);
            // Ignore：既不刷新持续时间也不改变层数——第二次施加对光环状态完全没有影响，
            // 光环仍按第一次施加时的原计划到期。
            Assert.False(refreshedRemaining);
        }

        [Fact]
        public void StackOverflow_RefreshOnly_RefreshesDurationButNotStacks()
        {
            RunOverflowScenario(StackOverflowPolicy.RefreshOnly, out var stacksAfter, out var refreshedRemaining);
            Assert.Equal(1, stacksAfter);
            Assert.True(refreshedRemaining);
        }

        [Fact]
        public void StackOverflow_Replace_ResetsToFreshInstance()
        {
            var aura = StackableAura("skill.aura_def.sample_overflow", maxStacks: 1, duration: 5);
            var builder = new SkillWorldBuilder().AuraDef(aura).Stat("stat.sample_power");
            builder.Options.StackOverflowPolicy = StackOverflowPolicy.Replace;
            var world = builder.Build();
            world.AddUnit(new Id("unit.target"));

            var first = world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_overflow"), new Id("unit.source"));
            var second = world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_overflow"), new Id("unit.source"));

            Assert.NotEqual(first, second);
            Assert.Equal(1, world.Host.AuraQuery.GetStacks(new Id("unit.target"), new Id("skill.aura_def.sample_overflow")));
        }

        /// <summary>施加两次 max_stacks=1 的光环（第二次必然进入叠加超限分支），推进到"若被刷新则
        /// 仍存活、若未被刷新则已到期"的时间点，据此反推该策略是否刷新了持续时间。</summary>
        private static void RunOverflowScenario(StackOverflowPolicy policy, out int stacksAfter, out bool stillAliveAfterOriginalDuration)
        {
            var aura = StackableAura("skill.aura_def.sample_overflow2", maxStacks: 1, duration: 5);
            var builder = new SkillWorldBuilder().AuraDef(aura).Stat("stat.sample_power");
            builder.Options.StackOverflowPolicy = policy;
            var world = builder.Build();
            world.AddUnit(new Id("unit.target"));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_overflow2"), new Id("unit.source"));
            world.Host.Update(4.0); // 还剩 1 秒
            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_overflow2"), new Id("unit.source")); // 超限，触发策略

            stacksAfter = world.Host.AuraQuery.GetStacks(new Id("unit.target"), new Id("skill.aura_def.sample_overflow2"));

            world.Host.Update(2.0); // 若未刷新：原剩余 1 秒早已到期；若刷新：还剩 3 秒
            stillAliveAfterOriginalDuration = world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_overflow2"));
        }
    }
}
