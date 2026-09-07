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

        // -----------------------------------------------------------------
        // RC-05（见外部审计 architecture/落地计划/audit-b3b91ee-20260907/code-review.md RC-05）：
        // SkillHost.LearnSkill(Id,Id,Id)/ForgetSkill(Id,Id,Id) 按来源引用计数——不带来源的
        // LearnSkill(Id,Id)/ForgetSkill(Id,Id)（天赋/任务/技能书/读档等"永久学习"路径）与带来源
        // 的重载（装备联动，来源=装备实例 id）共用同一份计数，任一来源撤销都不应影响其它仍在
        // 授予的来源。
        // -----------------------------------------------------------------

        [Fact]
        public void ForgetSkill_WithSource_DoesNotForget_WhenPermanentlyLearnedToo()
        {
            var world = new SkillWorldBuilder().Build();
            var hero = new Id("unit.hero");
            var skill = new Id("skill.sample_slash");
            var equipmentSource = new Id("item.inst_1");
            world.AddUnit(hero);

            // 永久学习（天赋/任务，不带来源——归属哨兵来源）+ 装备也授予同一技能。
            world.Host.LearnSkill(hero, skill);
            world.Host.LearnSkill(hero, skill, equipmentSource);
            Assert.True(world.Host.Knows(hero, skill));

            // 卸下装备（只撤销装备这个来源）：修复前 ForgetSkill 不计来源，会把永久学到的技能
            // 一起遗忘（见外部审计 RC-05"卸一件遗忘永久技能"）。
            world.Host.ForgetSkill(hero, skill, equipmentSource);

            Assert.True(world.Host.Knows(hero, skill));
        }

        [Fact]
        public void ForgetSkill_Permanent_DoesNotForget_WhenStillGrantedByEquipment()
        {
            var world = new SkillWorldBuilder().Build();
            var hero = new Id("unit.hero");
            var skill = new Id("skill.sample_slash");
            var equipmentSource = new Id("item.inst_1");
            world.AddUnit(hero);

            world.Host.LearnSkill(hero, skill);
            world.Host.LearnSkill(hero, skill, equipmentSource);

            // 反过来：撤销永久学习（如遗忘天赋点），装备仍然穿着，技能应保持已知。
            world.Host.ForgetSkill(hero, skill);

            Assert.True(world.Host.Knows(hero, skill));

            // 最后一个来源（装备）撤销后才真正遗忘。
            world.Host.ForgetSkill(hero, skill, equipmentSource);
            Assert.False(world.Host.Knows(hero, skill));
        }

        [Fact]
        public void ForgetSkill_WithSource_DoesNotForget_WhenAnotherEquipmentSourceRemains()
        {
            var world = new SkillWorldBuilder().Build();
            var hero = new Id("unit.hero");
            var skill = new Id("skill.sample_slash");
            var weaponSource = new Id("item.inst_weapon");
            var chestSource = new Id("item.inst_chest");
            world.AddUnit(hero);

            world.Host.LearnSkill(hero, skill, weaponSource);
            world.Host.LearnSkill(hero, skill, chestSource);

            world.Host.ForgetSkill(hero, skill, weaponSource);
            Assert.True(world.Host.Knows(hero, skill)); // 胸甲这个来源还在。

            world.Host.ForgetSkill(hero, skill, chestSource);
            Assert.False(world.Host.Knows(hero, skill)); // 全部来源撤销才真正遗忘。
        }

        [Fact]
        public void ForgetSkill_WithSource_NeverGranted_IsIdempotentNoOp()
        {
            var world = new SkillWorldBuilder().Build();
            var hero = new Id("unit.hero");
            var skill = new Id("skill.sample_slash");
            world.AddUnit(hero);

            var exception = Record.Exception(() => world.Host.ForgetSkill(hero, skill, new Id("item.inst_never_granted")));

            Assert.Null(exception);
            Assert.False(world.Host.Knows(hero, skill));
        }
    }
}
