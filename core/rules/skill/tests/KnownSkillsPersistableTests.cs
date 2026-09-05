using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>G1 遗留恢复：<c>player.known_skills</c> 段（<see cref="KnownSkillsPersistable"/>）的
    /// SectionKey/存读档往返。</summary>
    public sealed class KnownSkillsPersistableTests
    {
        private static readonly Id UnitId = new Id("unit.hero");
        private static readonly Id SkillFireball = new Id("skill.fireball");
        private static readonly Id SkillHeal = new Id("skill.heal");

        [Fact]
        public void SectionKey_MatchesSaveSections()
        {
            var world = new SkillWorldBuilder().Build();
            var persistable = KnownSkillsPersistable.For(world.Host, UnitId);

            Assert.Equal(SaveSections.PlayerKnownSkills, persistable.SectionKey);
        }

        [Fact]
        public void Save_Empty_ProducesEmptyArray()
        {
            var world = new SkillWorldBuilder().Build();
            var persistable = KnownSkillsPersistable.For(world.Host, UnitId);

            var saved = persistable.Save();

            var arr = Assert.IsType<JsonArray>(saved);
            Assert.Empty(arr);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsKnownSkills_IntoFreshHost()
        {
            var savingWorld = new SkillWorldBuilder().Build();
            savingWorld.AddUnit(UnitId);
            savingWorld.Host.LearnSkill(UnitId, SkillFireball);
            savingWorld.Host.LearnSkill(UnitId, SkillHeal);

            var saved = KnownSkillsPersistable.For(savingWorld.Host, UnitId).Save();

            var loadingWorld = new SkillWorldBuilder().Build();
            loadingWorld.AddUnit(UnitId);
            KnownSkillsPersistable.For(loadingWorld.Host, UnitId).Load(saved);

            Assert.True(loadingWorld.Host.Knows(UnitId, SkillFireball));
            Assert.True(loadingWorld.Host.Knows(UnitId, SkillHeal));
            Assert.Equal(2, loadingWorld.Host.GetKnownSkills(UnitId).Count);
        }

        [Fact]
        public void Load_NullData_LeavesEmpty()
        {
            var world = new SkillWorldBuilder().Build();
            world.AddUnit(UnitId);

            KnownSkillsPersistable.For(world.Host, UnitId).Load(JsonNull.Instance);

            Assert.Empty(world.Host.GetKnownSkills(UnitId));
        }

        [Fact]
        public void Load_RejectsWrongShape()
        {
            var world = new SkillWorldBuilder().Build();
            var persistable = KnownSkillsPersistable.For(world.Host, UnitId);

            Assert.Throws<System.FormatException>(() => persistable.Load(new JsonString("not-an-array")));
        }

        [Fact]
        public void Load_RejectsNonIdEntry()
        {
            var world = new SkillWorldBuilder().Build();
            var persistable = KnownSkillsPersistable.For(world.Host, UnitId);
            var badData = new JsonArray(new JsonValue[] { new JsonString("not an id") });

            Assert.Throws<System.FormatException>(() => persistable.Load(badData));
        }

        [Fact]
        public void Load_ThenKnows_ReturnsTrue_ForRestoredSkill()
        {
            // 与 Core.Carriers.Unit.SkillBindingPersistable 的联动前提（该类型在 L3，不在本模块测试
            // 范围内验证——完整链路见 core/gameplay/tests/EndToEndTests.cs）：player.known_skills 先
            // 还原完毕后，Knows 才能对已恢复技能返回 true，供 ISkillBindingHost.Bind 的已知技能校验
            // 使用。
            var world = new SkillWorldBuilder().Build();
            world.AddUnit(UnitId);
            var saved = new JsonArray(new JsonValue[] { new JsonString(SkillFireball.Value) });

            KnownSkillsPersistable.For(world.Host, UnitId).Load(saved);

            Assert.True(world.Host.Knows(UnitId, SkillFireball));
        }
    }
}
