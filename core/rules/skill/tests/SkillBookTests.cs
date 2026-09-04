using Core.Foundation.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary><c>skill.book</c> 学习（见 04 第 1.1 节 skill.book 行、落地方案 T2-4/T2-5/T2-6 综合
    /// 验收"skill.book 学习"）。</summary>
    public sealed class SkillBookTests
    {
        [Fact]
        public void LearnFromBook_LearnsAllEntriesAtOrBelowLevel()
        {
            var book = J.O(
                ("id", J.S("skill.book.sample_warrior")),
                ("entries", J.A(
                    J.O(("level", J.N(1)), ("skill_id", J.S("skill.sample_slash"))),
                    J.O(("level", J.N(5)), ("skill_id", J.S("skill.sample_cleave"))),
                    J.O(("level", J.N(10)), ("skill_id", J.S("skill.sample_execute"))))));

            var world = new SkillWorldBuilder().Book(book).Build();
            world.AddUnit(new Id("unit.hero"));

            world.Host.LearnFromBook(new Id("unit.hero"), new Id("skill.book.sample_warrior"), level: 5);

            Assert.True(world.Host.Knows(new Id("unit.hero"), new Id("skill.sample_slash")));
            Assert.True(world.Host.Knows(new Id("unit.hero"), new Id("skill.sample_cleave")));
            Assert.False(world.Host.Knows(new Id("unit.hero"), new Id("skill.sample_execute")));

            var known = world.Host.GetKnownSkills(new Id("unit.hero"));
            Assert.Equal(2, known.Count);
        }

        [Fact]
        public void LearnSkill_Directly_MarksAsKnown()
        {
            var world = new SkillWorldBuilder().Build();
            world.AddUnit(new Id("unit.hero"));

            Assert.False(world.Host.Knows(new Id("unit.hero"), new Id("skill.sample_slash")));

            world.Host.LearnSkill(new Id("unit.hero"), new Id("skill.sample_slash"));

            Assert.True(world.Host.Knows(new Id("unit.hero"), new Id("skill.sample_slash")));
        }
    }
}
