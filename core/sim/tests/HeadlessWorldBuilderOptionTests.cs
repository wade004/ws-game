using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.SaveSystem;
using Core.Gameplay.Loot;
using Core.Numbers.Archetype;
using Core.Sim;
using Xunit;

namespace Tests.Sim
{
    /// <summary>
    /// <see cref="HeadlessWorldBuilder.Build"/> 的选项转发与入口守卫直接用例（T-M5，2026-10-01 测试覆盖第四批）。
    /// 此前没有任何用例覆盖 <see cref="HeadlessWorldOptions"/> 的非默认取值：每个选项都经"装配后可观测的结果"断言，
    /// 不是只断言属性被赋值。
    /// </summary>
    public sealed class HeadlessWorldBuilderOptionTests
    {
        private static Core.Sim.HeadlessWorld Build(Action<HeadlessWorldOptions>? configure = null, IReadOnlyList<IDataSource>? sources = null)
        {
            var options = new HeadlessWorldOptions
            {
                DataSources = sources ?? SimTestWorldFactory.BuildEmbeddedDataSources(),
                Seed = 4242UL,
                MapId = SimTestWorldFactory.EmbeddedMapId,
                PlayerId = SimTestWorldFactory.PlayerId,
                PlayerFactionId = SimTestWorldFactory.FactionPlayer,
                PlayerClassId = SimTestWorldFactory.EmbeddedClassId,
                GameId = SimTestWorldFactory.EmbeddedGameId,
                StepSeconds = SimTestWorldFactory.StepSeconds,
            };
            configure?.Invoke(options);
            return HeadlessWorldBuilder.Build(options);
        }

        // -----------------------------------------------------------------
        // 入口守卫
        // -----------------------------------------------------------------

        [Fact]
        public void Build_NullOptions_ThrowsArgumentNullException()
        {
            Assert.Equal("options", Assert.Throws<ArgumentNullException>(() => HeadlessWorldBuilder.Build(null!)).ParamName);
        }

        [Fact]
        public void Build_EmptyDataSources_ThrowsArgumentExceptionNamingOptions()
        {
            var ex = Assert.Throws<ArgumentException>(() => HeadlessWorldBuilder.Build(new HeadlessWorldOptions()));

            Assert.Equal("options", ex.ParamName);
            Assert.Contains("DataSources", ex.Message);
        }

        [Fact]
        public void Build_NullDataSources_ThrowsArgumentExceptionNamingOptions()
        {
            var ex = Assert.Throws<ArgumentException>(() => HeadlessWorldBuilder.Build(new HeadlessWorldOptions { DataSources = null! }));

            Assert.Equal("options", ex.ParamName);
        }

        [Fact]
        public void HeadlessWorldOptions_DefaultsMatchTheDocumentedFixtureValues()
        {
            var options = new HeadlessWorldOptions();

            Assert.Empty(options.DataSources);
            Assert.Equal(20260905UL, options.Seed);
            Assert.Null(options.FileSystem);
            Assert.False(options.EnableDiscreteTimeModel);
            Assert.Null(options.SaveSystemOptions);
            Assert.False(options.FailOnUnknownTable);
            Assert.Equal(new Id("world.sample_field"), options.MapId);
            Assert.Equal(new Id("unit.sample_player"), options.PlayerId);
            Assert.Equal(new Id("fac.player"), options.PlayerFactionId);
            Assert.Equal(new Id("arch.class.sample_a"), options.PlayerClassId);
            Assert.Null(options.PlayerRaceId);
            Assert.Equal(1, options.PlayerLevel);
            Assert.Equal(new Vec2(0, 0), options.PlayerSpawnPosition);
            Assert.Equal(0.5, options.PlayerSpawnRadius);
            Assert.Equal(new Id("game.sample_e2e"), options.GameId);
            Assert.Equal(0.5, options.StepSeconds);
            Assert.Equal(4, options.MaxCatchUpSteps);
            Assert.Null(options.ExpectedQualityId);
            Assert.Null(options.CombatOptions);
            Assert.Null(options.LootOptions);
        }

        [Fact]
        public void Build_BlockingData_ThrowsInvalidOperationExceptionCarryingTheIssues()
        {
            // stat.definition 行缺 name_key / group：加载期阻断，装配根不得吞掉。
            var bad = new InMemoryDataSource().Add(
                "stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 2, \"rows\": [{\"id\": \"stat.cov_bad\"}]}");

            var ex = Assert.Throws<InvalidOperationException>(() => Build(sources: new IDataSource[] { bad }));

            Assert.Contains("stat.cov_bad", ex.Message);
        }

        // -----------------------------------------------------------------
        // 选项转发
        // -----------------------------------------------------------------

        [Fact]
        public void EnableDiscreteTimeModel_DefaultOff_NoTimeModelSwitch_OnWhenRequested()
        {
            // 离散时间模型需要 found.time_model 数据，嵌入式最小数据集不含，改用 framework + sample 两根。
            void UseSample(HeadlessWorldOptions o)
            {
                o.MapId = SimTestWorldFactory.MapId;
                o.PlayerClassId = SimTestWorldFactory.ClassSample;
                o.GameId = SimTestWorldFactory.GameId;
            }

            Assert.Null(Build(UseSample, SimTestWorldFactory.BuildSampleDataSources()).Gameplay.TimeModelSwitch);
            Assert.NotNull(Build(o => { UseSample(o); o.EnableDiscreteTimeModel = true; }, SimTestWorldFactory.BuildSampleDataSources())
                .Gameplay.TimeModelSwitch);
        }

        [Fact]
        public void PlayerRaceId_IsForwardedToTheArchetypeApplication()
        {
            var race = new Id("arch.race.sim_default");

            var withRace = Build(o => o.PlayerRaceId = race);
            var withoutRace = Build();

            var appliedWith = withRace.Events.OfType<ArchetypeAppliedEvent>().Single(e => e.UnitId.Equals(SimTestWorldFactory.PlayerId));
            var appliedWithout = withoutRace.Events.OfType<ArchetypeAppliedEvent>().Single(e => e.UnitId.Equals(SimTestWorldFactory.PlayerId));
            Assert.Equal(race, appliedWith.RaceId);
            Assert.Null(appliedWithout.RaceId);
        }

        [Fact]
        public void PlayerSpawnPosition_SetsTheEntityPosition_AndTheSpatialIndexEntry()
        {
            var position = new Vec2(12.5, -3.0);

            var world = Build(o => o.PlayerSpawnPosition = position);

            Assert.Equal(position, world.Player.Position);
            var hits = world.Spatial.QueryRadius(position, 0.0, new QueryFilter());
            Assert.Contains(SimTestWorldFactory.PlayerId, hits);
            Assert.Empty(world.Spatial.QueryRadius(new Vec2(0, 0), 0.0, new QueryFilter()));
        }

        [Fact]
        public void PlayerSpawnRadius_DeterminesTheSpatialQueryReach()
        {
            const double radius = 2.0;
            var world = Build(o => o.PlayerSpawnRadius = radius);
            var origin = new Vec2(0, 0);

            // 空间查询命中条件：Distance(center, entity) <= queryRadius + entity.Radius。
            Assert.Contains(SimTestWorldFactory.PlayerId, world.Spatial.QueryRadius(new Vec2(radius, 0), 0.0, new QueryFilter()));
            Assert.DoesNotContain(SimTestWorldFactory.PlayerId, world.Spatial.QueryRadius(new Vec2(radius + 0.5, 0), 0.0, new QueryFilter()));
            Assert.Equal(origin, world.Player.Position);
        }

        [Fact]
        public void StepSecondsAndMaxCatchUpSteps_AreAppliedToTheClock_AndBoundTheStepsPerAdvance()
        {
            const int maxCatchUp = 2;
            const double step = 0.25;
            var world = Build(o =>
            {
                o.StepSeconds = step;
                o.MaxCatchUpSteps = maxCatchUp;
            });

            Assert.Equal(step, world.Clock.StepSeconds);
            Assert.Equal(maxCatchUp, world.Clock.MaxCatchUpSteps);

            var before = world.Clock.TickIndex;
            world.Clock.Advance(step * 100);

            // 一次推进最多补 MaxCatchUpSteps 步，不管欠了多少时间。
            Assert.Equal(before + maxCatchUp, world.Clock.TickIndex);
        }

        [Fact]
        public void LootOptions_DefaultPickupRange_RejectsAFarDrop_WidenedRangeAcceptsIt()
        {
            var farPosition = new Vec2(5, 0);
            var defaultOptions = new LootOptions();
            Assert.True(farPosition.X > defaultOptions.PickupRange, "夹具前提：掉落点超出默认拾取半径");
            var template = new Id("item.sim_main_hand_l1_common");

            var narrow = Build();
            var narrowLoot = narrow.Gameplay.Loot.Drop(SimTestWorldFactory.EmbeddedMapId, farPosition, new[] { new ItemStack(template, 1) });
            var narrowResult = narrow.Gameplay.Loot.PickUp(SimTestWorldFactory.PlayerId, narrowLoot);

            var wide = Build(o => o.LootOptions = new LootOptions { PickupRange = farPosition.X + 1.0 });
            var wideLoot = wide.Gameplay.Loot.Drop(SimTestWorldFactory.EmbeddedMapId, farPosition, new[] { new ItemStack(template, 1) });
            var wideResult = wide.Gameplay.Loot.PickUp(SimTestWorldFactory.PlayerId, wideLoot);

            Assert.False(narrowResult.Success);
            Assert.Equal(LootPickupFailureReason.TooFar, narrowResult.Reason);
            Assert.True(wideResult.Success, wideResult.Reason.ToString());
        }

        [Fact]
        public void SaveSystemOptions_Provided_IsUsed_DefaultFollowsGameId()
        {
            var custom = new SaveSystemOptions(new Id("game.cov_custom")) { CurrentSaveVersion = 7, MaxSlots = 3 };

            var withCustom = Build(o => o.SaveSystemOptions = custom);
            var withDefault = Build();

            Assert.Equal(7, withCustom.SaveSystem.CurrentSaveVersion);
            Assert.Equal(new SaveSystemOptions(SimTestWorldFactory.EmbeddedGameId).CurrentSaveVersion, withDefault.SaveSystem.CurrentSaveVersion);
        }

        [Fact]
        public void FailOnUnknownTable_OffTolerates_OnBlocksATableWithoutSchema()
        {
            var unknownSource = new InMemoryDataSource().Add(
                "cov.unregistered_table",
                "{\"table\": \"cov.unregistered_table\", \"schema_version\": 1, \"rows\": [{\"id\": \"cov.unregistered_table.a\"}]}");
            IReadOnlyList<IDataSource> Sources()
            {
                var list = SimTestWorldFactory.BuildEmbeddedDataSources().ToList();
                list.Add(unknownSource);
                return list;
            }

            var tolerant = Build(o => o.FailOnUnknownTable = false, Sources());
            Assert.False(tolerant.LoadReport.IsBlocking);

            Assert.Throws<InvalidOperationException>(() => Build(o => o.FailOnUnknownTable = true, Sources()));
        }

        [Fact]
        public void ExpectedQualityId_Explicit_IsForwardedToTheAutoWiredAnchorProvider()
        {
            var good = Build(o => o.ExpectedQualityId = new Id("item.quality.sim_rare"));
            var bogus = Build(o => o.ExpectedQualityId = new Id("item.quality.cov_nonexistent"));

            // 不存在的品质使自动装配的锚点提供者在技能预算校验里解析失败（报告里点名该品质 id）；
            // 登记过的品质则不出现——证明 ExpectedQualityId 确实被转发给了提供者。
            Assert.Contains(bogus.LoadReport.Issues, i => i.ToString().Contains("item.quality.cov_nonexistent"));
            Assert.DoesNotContain(good.LoadReport.Issues, i => i.ToString().Contains("item.quality.cov_nonexistent"));
            Assert.DoesNotContain(good.LoadReport.Issues, i => i.ToString().Contains("skill_budget_record_unparseable"));
        }
    }
}
