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

        // ==== C09（外部审计 7e63d66 第四轮，P2）：同一 host 回档须替换永久技能集合，不是只增不减 ====

        /// <summary>核心复现：同一个长期存活的 <see cref="SkillHost"/> 上先存一份只有 A 的快照，
        /// 之后（存档之后）又永久学会 B（集合变成 {A,B}），再读回那份只有 A 的旧快照——正确结果
        /// 应该只剩 A（B 是快照之外、之后才学会的，读一份更早的存档应当把它撤销）。修复前
        /// <see cref="KnownSkillsPersistable.Load"/> 只增不减，B 会原样保留，读档退化成"至少有快照
        /// 里那些技能"而不是"恰好是快照那个时间点的状态"。</summary>
        [Fact]
        public void Load_SameHost_ReplacesCurrentPermanentSkillSet_RemovingSkillsLearnedAfterSnapshot()
        {
            var world = new SkillWorldBuilder().Build();
            world.AddUnit(UnitId);

            world.Host.LearnSkill(UnitId, SkillFireball); // A
            var snapshotOnlyA = KnownSkillsPersistable.For(world.Host, UnitId).Save();

            world.Host.LearnSkill(UnitId, SkillHeal); // 存档之后又学会 B：当前集合变成 {A,B}
            Assert.True(world.Host.Knows(UnitId, SkillFireball));
            Assert.True(world.Host.Knows(UnitId, SkillHeal));

            KnownSkillsPersistable.For(world.Host, UnitId).Load(snapshotOnlyA);

            // 修复前该断言会失败：Heal 会原样保留在已知集合里。
            Assert.True(world.Host.Knows(UnitId, SkillFireball));
            Assert.False(world.Host.Knows(UnitId, SkillHeal));
            Assert.Single(world.Host.GetPermanentlyKnownSkills(UnitId));
        }

        /// <summary>C09 验收口径二："之后卸装备不误清永久 A"：读档把永久集合替换回只有 A 之后，
        /// A 又被一件装备以临时来源重新授予一次；卸下这件装备只应撤销装备来源这一份引用，A 的
        /// 永久来源份额不受影响，<c>Knows</c> 应继续为真——验证 C09 的替换语义只清空/重建永久来源
        /// 那一份账本（<see cref="SkillHost.ForgetSkill(Id,Id)"/> 不带来源参数，只归属永久哨兵来源，
        /// 见 <see cref="SkillHost"/> 来源引用计数判断记录），不会连带误伤装备等临时来源的独立生命
        /// 周期。</summary>
        [Fact]
        public void Load_SameHost_ReplacePermanentSet_DoesNotBreakIndependentEquipmentGrantLifecycle()
        {
            var world = new SkillWorldBuilder().Build();
            world.AddUnit(UnitId);
            var equipmentSource = new Id("item.inst_sample_c09_equipment");

            world.Host.LearnSkill(UnitId, SkillFireball); // A（永久）
            var snapshotOnlyA = KnownSkillsPersistable.For(world.Host, UnitId).Save();

            world.Host.LearnSkill(UnitId, SkillHeal); // B（永久，快照之外）

            KnownSkillsPersistable.For(world.Host, UnitId).Load(snapshotOnlyA); // 替换回只有 A
            Assert.True(world.Host.Knows(UnitId, SkillFireball));
            Assert.False(world.Host.Knows(UnitId, SkillHeal));

            // 装备也授予 A（模拟 EquipmentHost 经 SkillGranter 以装备实例 id 为来源调用）。
            world.Host.LearnSkill(UnitId, SkillFireball, equipmentSource);
            Assert.True(world.Host.Knows(UnitId, SkillFireball));

            // 卸下这件装备：只应撤销装备来源，A 的永久来源份额仍在，不应被误清。
            world.Host.ForgetSkill(UnitId, SkillFireball, equipmentSource);
            Assert.True(world.Host.Knows(UnitId, SkillFireball));
            Assert.Contains(SkillFireball, world.Host.GetPermanentlyKnownSkills(UnitId));
        }
    }
}
