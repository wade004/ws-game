using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 消费方反馈第三批第 3 条（2026-09-21，见
    /// architecture/adr/0058-技能宿主契约纳入光环查询与效果落地出口.md）运行时验证：`ISkillHost`
    /// 缺 `AuraQuery`/`EffectSink` 只读入口——只持有 `ISkillHost` 引用的调用方拿不到这两个已经
    /// 接口化的出口，必须向下转型或直接持有具体类 `Core.Rules.Skill.SkillHost`。本测试只持有
    /// <see cref="ISkillHost"/> 接口引用（不下沉到具体实现类型），钉住"经接口拿到 `EffectSink` 施加
    /// 光环 → 经接口拿到 `AuraQuery` 查询到光环生效"这条完整运行时链路，证明的是"运行期真的能这样
    /// 调用"，不只是"接口新增成员后仍能编译通过"——与 <see cref="ISkillHostSkillBookContractTests"/>
    /// 同一惯例（同一批 ADR-0050 系列契约补齐的验证方式）。
    /// </summary>
    public sealed class ISkillHostAuraQueryEffectSinkContractTests
    {
        private static Core.Foundation.Common.Json.JsonObject SimpleAura(string id) => J.O(
            ("id", J.S(id)),
            ("max_stacks", J.N(1)),
            ("duration", J.N(5)),
            ("effects", J.A(
                J.O(("kind", J.S("mod_stat")),
                    ("params", J.O(("stat", J.S("stat.sample_power")), ("op", J.S("flat")), ("value", J.N(10))))))));

        /// <summary>
        /// 主验收用例：全程只经 <see cref="ISkillHost"/> 接口引用取得 <see cref="ISkillHost.EffectSink"/>
        /// 施加光环、再经 <see cref="ISkillHost.AuraQuery"/> 查询生效状态与层数，底层真实实现是
        /// <c>Core.Rules.Skill.SkillHost</c>（生产装配根实际使用的同一具体类型），验证接口引用
        /// 全程可驱动这条真实链路，不需要具体类型。
        /// </summary>
        [Fact]
        public void EffectSink_ApplyAura_Then_AuraQuery_ObservesIt_ThroughInterfaceReferenceOnly()
        {
            var aura = SimpleAura("skill.aura_def.sample_contract");
            var world = new SkillWorldBuilder().AuraDef(aura).Stat("stat.sample_power").Build();

            var target = new Id("unit.target");
            var source = new Id("unit.source");
            world.AddUnit(target);
            world.AddUnit(source);

            // 关键点：声明的静态类型是 ISkillHost，不是具体类 SkillHost——本测试之后取
            // EffectSink/AuraQuery 与调用它们，走的都是接口成员解析，验证的正是消费方反馈"面向接口
            // 编程做不到"这件事现在做得到。
            ISkillHost host = world.Host;

            var auraId = new Id("skill.aura_def.sample_contract");

            // 施加前：经接口引用取到的 AuraQuery 观测到"未生效"，层数为 0。
            Assert.NotNull(host.AuraQuery);
            Assert.False(host.AuraQuery!.HasAura(target, auraId));
            Assert.Equal(0, host.AuraQuery!.GetStacks(target, auraId));

            // 经接口引用取到的 EffectSink 施加光环。
            Assert.NotNull(host.EffectSink);
            host.EffectSink!.ApplyAura(target, auraId, source);
            world.Flush();

            // 施加后：同一个接口引用观测到的量从 false/0 变为 true/1——证明这条链路全程只靠
            // ISkillHost 接口引用即可驱动，且反映的是真实光环账本的状态变化。
            Assert.True(host.AuraQuery!.HasAura(target, auraId));
            Assert.Equal(1, host.AuraQuery!.GetStacks(target, auraId));

            // 与直接持有具体类型观测到的结果逐位一致——两者本就是同一个底层实例。
            Assert.True(world.Host.AuraQuery.HasAura(target, auraId));
            Assert.Equal(1, world.Host.AuraQuery.GetStacks(target, auraId));
        }

        // -----------------------------------------------------------------
        // 默认接口成员语义验证：只读查询允许显式降级——用一个只实现 ISkillHost 必需成员、完全不
        // 覆盖 AuraQuery/EffectSink 的最小假实现钉住默认实现本身的行为（同
        // ISkillHostSkillBookContractTests.MinimalSkillHostWithoutSkillBook 惯例）。
        // -----------------------------------------------------------------

        private sealed class MinimalSkillHostWithoutAuraOrEffectSink : ISkillHost
        {
            public Vec2 GetPosition(Id unitId) => throw new NotImplementedException();

            public IReadOnlyList<Id> FindUnits(Shape shape, Vec2 origin, UnitFilter filter) =>
                throw new NotImplementedException();

            public void ApplyStatMod(Id sourceId, Id unitId, Id stat, StatModifierOp op, double value) =>
                throw new NotImplementedException();

            public CastResult CastSkill(Id casterId, Id skillId, IReadOnlyList<Id> targets) =>
                throw new NotImplementedException();

            public double GetCooldown(Id unitId, Id skillId) => throw new NotImplementedException();

            public bool IsCasting(Id unitId) => throw new NotImplementedException();

            public void Interrupt(Id unitId, Id interrupterId, Id? lockSchool, double lockDuration) =>
                throw new NotImplementedException();

            // AuraQuery/EffectSink 均不覆盖——本测试要验证的正是它们各自的默认实现。
        }

        [Fact]
        public void DefaultImplementation_AuraQuery_DegradesToNull_WithoutThrowing()
        {
            ISkillHost host = new MinimalSkillHostWithoutAuraOrEffectSink();

            Assert.Null(host.AuraQuery);
        }

        [Fact]
        public void DefaultImplementation_EffectSink_DegradesToNull_WithoutThrowing()
        {
            ISkillHost host = new MinimalSkillHostWithoutAuraOrEffectSink();

            Assert.Null(host.EffectSink);
        }
    }
}
