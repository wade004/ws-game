using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Creature
{
    /// <summary>
    /// 阶段 3 整理"事项三"：<see cref="CreatureImmunityProvider"/> 对 <see cref="CreatureUnit.Immunities"/>
    /// 三种格式（学派 id / <c>effect.&lt;kind&gt;</c> / <c>control.&lt;flag&gt;</c>）以及"tier
    /// control_immune → 全部控制免疫"标记的解析，直接构造 <see cref="CreatureUnit"/> 验证，不经过
    /// <see cref="Core.Carriers.Creature.CreatureFactory"/>/DataRegistry（那条路径由
    /// <c>CreatureFactoryTests.Spawn_WritesImmunitiesFromTemplateAndTierControlImmune</c> 覆盖）。
    /// </summary>
    public class CreatureImmunityProviderTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id FactionId = new Id("fac.test_monster");
        private static readonly Id TemplateId = new Id("creature.sample_basic");

        private static (WorldSim world, CreatureImmunityProvider provider) NewWorld()
        {
            var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });
            var world = new WorldSim(bus);
            return (world, new CreatureImmunityProvider(world));
        }

        private static CreatureUnit AddUnit(WorldSim world, string entityIdSuffix, params string[] immunities)
        {
            var unit = new CreatureUnit(new Id($"creature.inst_{entityIdSuffix}"), MapId, FactionId, TemplateId);
            foreach (var immunity in immunities)
            {
                unit.Immunities.Add(new Id(immunity));
            }
            world.AddEntity(unit);
            return unit;
        }

        [Fact]
        public void IsImmune_SchoolEntry_BlocksAnyEffectKindOfThatSchool()
        {
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "1", "school.fire");

            Assert.True(provider.IsImmune(unit.EntityId, new Id("school.fire"), EffectKind.SchoolDamage));
            Assert.True(provider.IsImmune(unit.EntityId, new Id("school.fire"), EffectKind.Heal));
            Assert.False(provider.IsImmune(unit.EntityId, new Id("school.frost"), EffectKind.SchoolDamage));
        }

        [Fact]
        public void IsImmune_EffectKindEntry_BlocksThatKindRegardlessOfSchool()
        {
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "2", "effect.heal");

            Assert.True(provider.IsImmune(unit.EntityId, new Id("school.fire"), EffectKind.Heal));
            Assert.True(provider.IsImmune(unit.EntityId, new Id("school.frost"), EffectKind.Heal));
            Assert.False(provider.IsImmune(unit.EntityId, new Id("school.fire"), EffectKind.SchoolDamage));
        }

        [Fact]
        public void GetControlImmunity_ControlFlagEntry_ReturnsOnlyThatFlag()
        {
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "3", "control.no_move");

            Assert.Equal(ControlFlags.NoMove, provider.GetControlImmunity(unit.EntityId));
        }

        [Fact]
        public void GetControlImmunity_TierControlImmuneMarker_ReturnsAllFlags()
        {
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "4", "immunity.control_immune");

            var expected = ControlFlags.NoMove | ControlFlags.NoCast | ControlFlags.NoAttack | ControlFlags.NoInteract;
            Assert.Equal(expected, provider.GetControlImmunity(unit.EntityId));

            // 标记只影响控制免疫，不参与学派/效果免疫判定。
            Assert.False(provider.IsImmune(unit.EntityId, new Id("school.fire"), EffectKind.SchoolDamage));
        }

        [Fact]
        public void IsImmune_And_GetControlImmunity_NonCreatureOrMissingUnit_ReturnFalseAndNone()
        {
            var (_, provider) = NewWorld();
            var missingId = new Id("creature.inst_missing");

            Assert.False(provider.IsImmune(missingId, new Id("school.fire"), EffectKind.SchoolDamage));
            Assert.Equal(ControlFlags.None, provider.GetControlImmunity(missingId));
        }

        // -----------------------------------------------------------------
        // T-N3-6（ADR-0031 决策 8；06 第 3.3 节 2026-09-14 修订段）：IsControlCategoryImmune 按类别
        // 免疫；旧布尔 control_immune 迁移等价（true → 全部类别免疫，false → 无免疫）。
        // -----------------------------------------------------------------

        [Fact]
        public void IsControlCategoryImmune_CategoryEntry_BlocksOnlyThatCategory()
        {
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "5", "control_category.stun");

            Assert.True(provider.IsControlCategoryImmune(unit.EntityId, "stun"));
            Assert.False(provider.IsControlCategoryImmune(unit.EntityId, "root"));

            // control_category.<category> 只影响按类别控制免疫，不参与学派/效果免疫、也不参与既有
            // 按标志位的 GetControlImmunity（两个前缀是正交维度，见 CreatureImmunityProvider 判断记录）。
            Assert.False(provider.IsImmune(unit.EntityId, new Id("school.fire"), EffectKind.SchoolDamage));
            Assert.Equal(ControlFlags.None, provider.GetControlImmunity(unit.EntityId));
        }

        [Fact]
        public void IsControlCategoryImmune_MultipleCategoryEntries_BlocksEachDeclaredCategory()
        {
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "6", "control_category.stun", "control_category.fear");

            Assert.True(provider.IsControlCategoryImmune(unit.EntityId, "stun"));
            Assert.True(provider.IsControlCategoryImmune(unit.EntityId, "fear"));
            Assert.False(provider.IsControlCategoryImmune(unit.EntityId, "root"));
            Assert.False(provider.IsControlCategoryImmune(unit.EntityId, "silence"));
        }

        [Fact]
        public void IsControlCategoryImmune_UnregisteredCategoryText_ReturnsFalse()
        {
            // "未登记类别的控制视为不免疫"：即便该单位对其它类别有免疫声明，查询一个不在
            // ControlCategoryValues.All 六值集合内的拼写/未知类别文本也应返回 false，不放大为
            // 意外的全面免疫。
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "7", "control_category.stun");

            Assert.False(provider.IsControlCategoryImmune(unit.EntityId, "charm"));
        }

        [Fact]
        public void IsControlCategoryImmune_TierControlImmuneMarker_BlocksAnyCategory_MigrationEquivalence()
        {
            // 迁移等价（true 一侧）：旧布尔 tier.control_immune=true 写入的整体标记对任意类别查询
            // 都应返回 true——不需要在数据里逐一枚举六个类别，语义上等价于"全部类别免疫"。
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "8", "immunity.control_immune");

            Assert.True(provider.IsControlCategoryImmune(unit.EntityId, "stun"));
            Assert.True(provider.IsControlCategoryImmune(unit.EntityId, "polymorph"));
        }

        [Fact]
        public void IsControlCategoryImmune_NoImmunityEntries_ReturnsFalse_MigrationEquivalence()
        {
            // 迁移等价（false 一侧）：旧布尔 tier.control_immune=false（不写任何标记）时，按类别
            // 查询同既有 GetControlImmunity 一样恒为不免疫，行为与改动前一致。
            var (world, provider) = NewWorld();
            var unit = AddUnit(world, "9");

            Assert.False(provider.IsControlCategoryImmune(unit.EntityId, "stun"));
        }
    }
}
