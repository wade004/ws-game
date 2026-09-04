using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    public class DisplayInfoRegistryTests
    {
        [Fact]
        public void Constructor_BuildsIndex_LookupByLogicalIdReturnsMatchingInfo()
        {
            var (registry, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.map"] = "[" + DisplayInfoTestSupport.GreyWolfSpriteRow + "]",
            });
            Assert.False(report.IsBlocking);

            var displayInfoRegistry = new DisplayInfoRegistry(registry, DisplayInfoTestSupport.CreateBus());

            var found = displayInfoRegistry.Lookup(new Id("creature.grey_wolf"));
            Assert.NotNull(found);
            Assert.Equal(new Id("display.grey_wolf"), found!.Id);

            Assert.Null(displayInfoRegistry.Lookup(new Id("creature.does_not_exist")));
        }

        [Fact]
        public void LookupByCategory_ReturnsOnlyRecordsInThatCategory()
        {
            var (registry, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.map"] = "[" + DisplayInfoTestSupport.GreyWolfSpriteRow + "," + DisplayInfoTestSupport.StoneGolemModelRow + "]",
                ["display.anim_set"] = "[" + DisplayInfoTestSupport.StoneGolemAnimSetRow + "]",
            });
            Assert.False(report.IsBlocking);

            var displayInfoRegistry = new DisplayInfoRegistry(registry, DisplayInfoTestSupport.CreateBus());

            var creatures = displayInfoRegistry.LookupByCategory(DisplayCategory.Creature);
            Assert.Equal(2, creatures.Count);

            var skills = displayInfoRegistry.LookupByCategory(DisplayCategory.Skill);
            Assert.Empty(skills);
        }

        [Fact]
        public void All_ReturnsEveryRegisteredDisplayInfo()
        {
            var (registry, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.map"] = "[" + DisplayInfoTestSupport.GreyWolfSpriteRow + "," + DisplayInfoTestSupport.PlayerHeroSpriteRow + "]",
            });
            Assert.False(report.IsBlocking);

            var displayInfoRegistry = new DisplayInfoRegistry(registry, DisplayInfoTestSupport.CreateBus());

            Assert.Equal(2, displayInfoRegistry.All.Count);
            Assert.Contains(displayInfoRegistry.All, i => i.LogicalId == new Id("creature.grey_wolf"));
            Assert.Contains(displayInfoRegistry.All, i => i.LogicalId == new Id("creature.player_hero"));
        }

        [Fact]
        public void Reload_RebuildsIndex_AndPublishesDisplayInfoReloadedEvent()
        {
            var bus = DisplayInfoTestSupport.CreateBus();
            var mutableSource = new MutableSingleTableSource(
                "display.map", Envelope("display.map", "[" + DisplayInfoTestSupport.GreyWolfSpriteRow + "]"));
            var registry = new Core.Foundation.DataRegistry.DataRegistry(mutableSource, bus);
            foreach (var schema in DisplaySchemas.All) registry.RegisterSchema(schema);
            Assert.False(registry.LoadAll().IsBlocking);

            var displayInfoRegistry = new DisplayInfoRegistry(registry, bus);
            Assert.NotNull(displayInfoRegistry.Lookup(new Id("creature.grey_wolf")));
            Assert.Null(displayInfoRegistry.Lookup(new Id("creature.player_hero")));

            // 数据源改为额外多一条记录，Reload 前先让 DataRegistry 重新加载该表。
            mutableSource.Json = Envelope("display.map",
                "[" + DisplayInfoTestSupport.GreyWolfSpriteRow + "," + DisplayInfoTestSupport.PlayerHeroSpriteRow + "]");
            Assert.False(registry.Reload("display.map").IsBlocking);

            var reloadedReceived = false;
            bus.Subscribe<DisplayInfoReloadedEvent>(DisplayInfoEventKeys.Reloaded, _ => reloadedReceived = true);

            displayInfoRegistry.Reload();

            Assert.True(reloadedReceived);
            Assert.NotNull(displayInfoRegistry.Lookup(new Id("creature.player_hero")));
        }

        [Fact]
        public void DuplicateLogicalId_ThrowsInvalidOperationException()
        {
            const string duplicateRow = @"
            {
              ""id"": ""display.grey_wolf_alt"",
              ""category"": ""creature"",
              ""logical_id"": ""creature.grey_wolf"",
              ""kind"": ""sprite"",
              ""sprite_set_id"": ""sprite.creature.wolf_grey_alt"",
              ""direction_count"": 4
            }";

            var (registry, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.map"] = "[" + DisplayInfoTestSupport.GreyWolfSpriteRow + "," + duplicateRow + "]",
            });
            Assert.False(report.IsBlocking); // 重复 logical_id 不是 DataRegistry 内建校验项，这里数据本身合法通过

            Assert.Throws<InvalidOperationException>(() => new DisplayInfoRegistry(registry, DisplayInfoTestSupport.CreateBus()));
        }

        private sealed class MutableSingleTableSource : IDataSource
        {
            private readonly string _tableName;
            public string Json;

            public MutableSingleTableSource(string tableName, string initialJson)
            {
                _tableName = tableName;
                Json = initialJson;
            }

            public IReadOnlyList<DataTableSource> ListTables() =>
                new[] { new DataTableSource(_tableName, "memory://" + _tableName, () => Json) };
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";
    }
}
