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

        /// <summary>N07（外部审计 68c9bed，P2）：装备授予的临时技能（非 <c>PermanentGrantSource</c>
        /// 哨兵来源）不应被 <see cref="KnownSkillsPersistable.Save"/> 快照进存档——修复前
        /// <c>Save</c> 用 <c>GetKnownSkills</c>（全部来源并集）读取，装备授予的技能与永久学习的技能
        /// 混在一起写进同一个数组，无法区分。</summary>
        [Fact]
        public void Save_ExcludesSkillsGrantedOnlyByNonPermanentSource()
        {
            var world = new SkillWorldBuilder().Build();
            world.AddUnit(UnitId);
            var equipmentSource = new Id("item.inst_sample_equipment");
            world.Host.LearnSkill(UnitId, SkillFireball, equipmentSource); // 模拟装备授予（非永久）

            var saved = KnownSkillsPersistable.For(world.Host, UnitId).Save();

            var arr = Assert.IsType<JsonArray>(saved);
            Assert.Empty(arr);
        }

        /// <summary>N07 验收口径：装备授予技能 → 存 → 读 → 卸装备后 <c>Knows</c> 为假。真实链路里
        /// "装备恢复流程重新授予"发生在 <c>Core.Carriers.Item.ItemPersistable.Load</c> →
        /// <c>EquipmentHost.Equip</c> → <c>SkillGranter</c>（core/carriers/item，L3，不在本模块测试
        /// 范围），本用例在 <see cref="SkillHost"/> 层面直接调用同一个带来源重载模拟这一步，验证
        /// 修复后的完整往返：读档不再把装备技能误判为永久，卸装备后正确遗忘。</summary>
        [Fact]
        public void EquipmentGrantedSkill_AfterSaveLoad_IsNotPermanent_ForgottenOnUnequip()
        {
            var equipmentSource = new Id("item.inst_sample_equipment");

            var savingWorld = new SkillWorldBuilder().Build();
            savingWorld.AddUnit(UnitId);
            savingWorld.Host.LearnSkill(UnitId, SkillFireball, equipmentSource); // 装备授予的临时技能
            savingWorld.Host.LearnSkill(UnitId, SkillHeal); // 永久学习技能
            Assert.True(savingWorld.Host.Knows(UnitId, SkillFireball));

            var saved = KnownSkillsPersistable.For(savingWorld.Host, UnitId).Save();

            var loadingWorld = new SkillWorldBuilder().Build();
            loadingWorld.AddUnit(UnitId);
            KnownSkillsPersistable.For(loadingWorld.Host, UnitId).Load(saved);

            // 修复前该断言会失败：Fireball 会已经因为存档快照里混入了装备来源的技能而被当成永久
            // 技能重新学会。
            Assert.False(loadingWorld.Host.Knows(UnitId, SkillFireball));
            Assert.True(loadingWorld.Host.Knows(UnitId, SkillHeal));

            // 装备恢复流程重新以装备来源授予（真实链路见类型注释）。
            loadingWorld.Host.LearnSkill(UnitId, SkillFireball, equipmentSource);
            Assert.True(loadingWorld.Host.Knows(UnitId, SkillFireball));

            // 卸装备：只撤销装备来源，技能应变为不再已知（不再被误当永久技能保留）。
            loadingWorld.Host.ForgetSkill(UnitId, SkillFireball, equipmentSource);
            Assert.False(loadingWorld.Host.Knows(UnitId, SkillFireball));
        }

        /// <summary>一个技能同时被永久来源与装备来源授予时，仍应计入存档快照（与 <c>Knows</c> 的
        /// "任一来源即已知"语义一致），卸装备后凭永久来源继续保持已知。</summary>
        [Fact]
        public void Save_IncludesSkillGrantedByBothPermanentAndEquipmentSource()
        {
            var equipmentSource = new Id("item.inst_sample_equipment2");

            var world = new SkillWorldBuilder().Build();
            world.AddUnit(UnitId);
            world.Host.LearnSkill(UnitId, SkillFireball); // 永久
            world.Host.LearnSkill(UnitId, SkillFireball, equipmentSource); // 又被装备授予一次

            var saved = KnownSkillsPersistable.For(world.Host, UnitId).Save();
            var arr = Assert.IsType<JsonArray>(saved);
            Assert.Single(arr);
            Assert.Equal(SkillFireball.Value, Assert.IsType<JsonString>(arr[0]).Value);

            world.Host.ForgetSkill(UnitId, SkillFireball, equipmentSource);
            Assert.True(world.Host.Knows(UnitId, SkillFireball));
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
