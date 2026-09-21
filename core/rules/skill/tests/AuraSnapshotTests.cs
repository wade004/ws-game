using System.Linq;
using Core.Foundation.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>消费方反馈第 4 条（2026-09-21，ADR-0056）：<c>IAuraQuery.GetActiveAuraSnapshots</c>
    /// 生产实现（<see cref="Core.Rules.Skill.AuraHost"/>）随真实施加/移除流程的取值变化，以及列表
    /// 顺序的确定性（按实例创建顺序，不依赖字典枚举顺序，AGENTS.md §3）。</summary>
    public sealed class AuraSnapshotTests
    {
        private static Core.Foundation.Common.Json.JsonObject SimpleAura(string id, double duration)
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
        public void NoActiveAuras_ReturnsEmptyList()
        {
            var world = new SkillWorldBuilder().Build();
            world.AddUnit(new Id("unit.target"));

            Assert.Empty(world.Host.AuraQuery.GetActiveAuraSnapshots(new Id("unit.target")));
        }

        [Fact]
        public void ActiveAuras_ExposeIdentityStacksRemainingTotal_InCreationOrder_AndShrinkOnRemoval()
        {
            var auraA = SimpleAura("skill.aura_def.sample_a", duration: 5.0);
            var auraB = SimpleAura("skill.aura_def.sample_b", duration: 3.0);
            var world = new SkillWorldBuilder().AuraDef(auraA).AuraDef(auraB).Stat("stat.sample_power").Build();
            var target = new Id("unit.target");
            world.AddUnit(target);

            var sink = world.Host.EffectSink;
            var refA = sink.ApplyAura(target, new Id("skill.aura_def.sample_a"), new Id("unit.source"));
            sink.ApplyAura(target, new Id("skill.aura_def.sample_b"), new Id("unit.source"));
            world.Flush();

            var snapshots = world.Host.AuraQuery.GetActiveAuraSnapshots(target);
            Assert.Equal(2, snapshots.Count);

            // 顺序即施加顺序（先 A 后 B）——不依赖字典枚举顺序。
            Assert.Equal(new Id("skill.aura_def.sample_a"), snapshots[0].AuraDefId);
            Assert.Equal(1, snapshots[0].Stacks);
            Assert.Equal(5.0, snapshots[0].Remaining);
            Assert.Equal(5.0, snapshots[0].Total);

            Assert.Equal(new Id("skill.aura_def.sample_b"), snapshots[1].AuraDefId);
            Assert.Equal(1, snapshots[1].Stacks);
            Assert.Equal(3.0, snapshots[1].Remaining);
            Assert.Equal(3.0, snapshots[1].Total);

            // 两次查询顺序一致（确定性）。
            var snapshotsAgain = world.Host.AuraQuery.GetActiveAuraSnapshots(target);
            Assert.Equal(
                snapshots.Select(s => s.AuraDefId).ToList(),
                snapshotsAgain.Select(s => s.AuraDefId).ToList());

            // 时间推进：剩余时长按流逝时间单调递减，总时长（原始声明值）不变。
            world.Host.Update(1.0);
            var afterTick = world.Host.AuraQuery.GetActiveAuraSnapshots(target);
            Assert.Equal(4.0, afterTick[0].Remaining);
            Assert.Equal(5.0, afterTick[0].Total);
            Assert.Equal(2.0, afterTick[1].Remaining);
            Assert.Equal(3.0, afterTick[1].Total);

            // 移除第一个（A）后长度收缩为 1，剩下的是 B。
            sink.RemoveAura(target, refA);
            var afterRemoval = world.Host.AuraQuery.GetActiveAuraSnapshots(target);
            Assert.Single(afterRemoval);
            Assert.Equal(new Id("skill.aura_def.sample_b"), afterRemoval[0].AuraDefId);
        }

        [Fact]
        public void NameKey_ResolvesFromAuraDef_WhenRegistered_NullWhenFieldOmitted_NoDiagnosticEitherWay()
        {
            var withName = J.O(
                ("id", J.S("skill.aura_def.sample_named")),
                ("max_stacks", J.N(1)),
                ("duration", J.N(5.0)),
                ("name_key", J.S("l10n.aura.sample_named.name")),
                ("effects", J.A(
                    J.O(("kind", J.S("mod_stat")),
                        ("params", J.O(("stat", J.S("stat.sample_power")), ("op", J.S("flat")), ("value", J.N(1))))))));
            var withoutName = SimpleAura("skill.aura_def.sample_unnamed", duration: 5.0);

            var world = new SkillWorldBuilder().AuraDef(withName).AuraDef(withoutName).Stat("stat.sample_power").Build();
            var target = new Id("unit.target");
            world.AddUnit(target);

            var sink = world.Host.EffectSink;
            sink.ApplyAura(target, new Id("skill.aura_def.sample_named"), new Id("unit.source"));
            sink.ApplyAura(target, new Id("skill.aura_def.sample_unnamed"), new Id("unit.source"));
            world.Flush();

            var snapshots = world.Host.AuraQuery.GetActiveAuraSnapshots(target);
            Assert.Equal(new Id("l10n.aura.sample_named.name"), snapshots[0].NameKey);
            Assert.Null(snapshots[1].NameKey);

            // name_key 是可选字段（ADR-0056 决策 4），缺省是合法留白，不是数据不一致，不应记诊断。
            Assert.Empty(world.Diagnostics.Warnings);
        }
    }
}
