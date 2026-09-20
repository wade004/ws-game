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
    /// ADR-0050《技能宿主契约纳入技能簿查询与学习成员》运行时验证：消费方反馈"`ISkillHost` 缺
    /// `GetKnownSkills`/`LearnSkill`/`LearnFromBook`/`Knows`，这些能力只在具体类 `SkillHost` 上，
    /// 面向接口编程做不到"——本测试只持有 <see cref="ISkillHost"/> 接口引用（不下沉到具体实现类型
    /// <c>Core.Rules.Skill.SkillHost</c>），钉住"学技能 → 查询已知 → 从技能书学"这条完整运行时链路，
    /// 证明的是"运行期真的能这样调用"，不只是"接口新增成员后仍能编译通过"。
    /// </summary>
    public sealed class ISkillHostSkillBookContractTests
    {
        private static Core.Foundation.Common.Json.JsonObject MinimalSkillDef(string id) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_sample")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S("target.chain.sample")),
            ("effects", J.A()));

        /// <summary>
        /// 主验收用例：全程只经 <see cref="ISkillHost"/> 接口引用调用
        /// <see cref="ISkillHost.LearnSkill"/>/<see cref="ISkillHost.Knows"/>/
        /// <see cref="ISkillHost.GetKnownSkills"/>/<see cref="ISkillHost.LearnFromBook"/> 四个新增
        /// 契约成员，底层真实实现是 <c>Core.Rules.Skill.SkillHost</c>（生产装配根实际使用的同一具体
        /// 类型），验证它们隐式满足接口签名且行为与既有具体类型调用（见
        /// <see cref="SkillBookTests"/>）逐字一致。
        /// </summary>
        [Fact]
        public void LearnSkill_Then_Knows_Then_LearnFromBook_WorkEndToEnd_ThroughInterfaceReferenceOnly()
        {
            var book = J.O(
                ("id", J.S("skill.book.sample_warrior")),
                ("entries", J.A(
                    J.O(("level", J.N(1)), ("skill_id", J.S("skill.sample_slash"))),
                    J.O(("level", J.N(5)), ("skill_id", J.S("skill.sample_cleave"))),
                    J.O(("level", J.N(10)), ("skill_id", J.S("skill.sample_execute"))))));

            var world = new SkillWorldBuilder()
                .Book(book)
                .SkillDef(MinimalSkillDef("skill.sample_slash"))
                .SkillDef(MinimalSkillDef("skill.sample_cleave"))
                .SkillDef(MinimalSkillDef("skill.sample_execute"))
                .SkillDef(MinimalSkillDef("skill.sample_direct"))
                .Build();

            // 关键点：声明的静态类型是 ISkillHost，不是具体类 SkillHost——本测试之后的每一次调用
            // 走的都是接口方法解析，验证的正是消费方反馈"面向接口编程做不到"这件事现在做得到。
            ISkillHost host = world.Host;

            var hero = new Id("unit.hero");
            world.AddUnit(hero);

            // 步骤 1：学技能（不带来源的两参数重载，本次收口范围内的那一个）。
            Assert.False(host.Knows(hero, new Id("skill.sample_direct")));
            host.LearnSkill(hero, new Id("skill.sample_direct"));

            // 步骤 2：查询已知——Knows 与 GetKnownSkills 两个查询成员。
            Assert.True(host.Knows(hero, new Id("skill.sample_direct")));
            var knownAfterDirect = host.GetKnownSkills(hero);
            Assert.Contains(new Id("skill.sample_direct"), knownAfterDirect);
            Assert.Single(knownAfterDirect);

            // 步骤 3：从技能书学——按等级学习全部 entries[].level <= level 的技能。
            host.LearnFromBook(hero, new Id("skill.book.sample_warrior"), level: 5);

            Assert.True(host.Knows(hero, new Id("skill.sample_slash")));
            Assert.True(host.Knows(hero, new Id("skill.sample_cleave")));
            Assert.False(host.Knows(hero, new Id("skill.sample_execute"))); // 等级 10 的条目未学到。

            var knownAfterBook = host.GetKnownSkills(hero);
            Assert.Equal(3, knownAfterBook.Count); // sample_direct + sample_slash + sample_cleave。
        }

        // -----------------------------------------------------------------
        // 默认接口成员语义验证：ADR-0050 决策"只读查询可显式降级、写路径不能静默降级"——用一个
        // 只实现 ISkillHost 必需成员、完全不覆盖四个新增成员的最小假实现钉住默认实现本身的行为。
        // -----------------------------------------------------------------

        private sealed class MinimalSkillHostWithoutSkillBook : ISkillHost
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

            // 四个新增成员均不覆盖——本测试要验证的正是它们各自的默认实现。
        }

        [Fact]
        public void DefaultImplementation_Knows_DegradesToFalse_WithoutThrowing()
        {
            ISkillHost host = new MinimalSkillHostWithoutSkillBook();

            Assert.False(host.Knows(new Id("unit.hero"), new Id("skill.sample_slash")));
        }

        [Fact]
        public void DefaultImplementation_GetKnownSkills_DegradesToEmptyList_WithoutThrowing()
        {
            ISkillHost host = new MinimalSkillHostWithoutSkillBook();

            Assert.Empty(host.GetKnownSkills(new Id("unit.hero")));
        }

        [Fact]
        public void DefaultImplementation_LearnSkill_ThrowsNotSupported_DoesNotSilentlyNoOp()
        {
            ISkillHost host = new MinimalSkillHostWithoutSkillBook();

            Assert.Throws<NotSupportedException>(
                () => host.LearnSkill(new Id("unit.hero"), new Id("skill.sample_slash")));
        }

        [Fact]
        public void DefaultImplementation_LearnFromBook_ThrowsNotSupported_DoesNotSilentlyNoOp()
        {
            ISkillHost host = new MinimalSkillHostWithoutSkillBook();

            Assert.Throws<NotSupportedException>(
                () => host.LearnFromBook(new Id("unit.hero"), new Id("skill.book.sample_warrior"), level: 1));
        }
    }
}
