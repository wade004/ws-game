using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.Archetype;
using Xunit;

namespace Tests.Numbers.Archetype
{
    /// <summary>
    /// P2-05 关联根治回归测试（外部审计 audit-c9ff301-20260909）：<see cref="ArchetypeRegistry"/>
    /// 与 <c>Core.Rules.Skill.SkillDefCache</c> 同一类"构造期一次性建索引、之后只读"缓存，此前没有
    /// 订阅 <see cref="DataLoadCompletedEvent"/>，reload <c>arch.class</c> 后 resident registry 继续
    /// 返回旧的 <c>ClassDefinition</c>。
    /// </summary>
    public sealed class P2_05_ArchetypeRegistryReloadTests
    {
        private sealed class MutableSource : IDataSource
        {
            private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);
            public MutableSource Add(string table, string text) { _texts[table] = text; return this; }
            public void Replace(string table, string text) => _texts[table] = text;
            public IReadOnlyList<DataTableSource> ListTables()
            {
                var result = new List<DataTableSource>();
                foreach (var pair in _texts)
                {
                    var table = pair.Key;
                    result.Add(new DataTableSource(table, "memory://" + table, () => _texts[table]));
                }
                return result;
            }
        }

        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                new EventDefinition(ArchetypeEventKeys.Applied, "archetype",
                    new[] { "unitId", "classId", "raceId" }),
            });
            return new EventBus(catalog);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static string ClassRow(double strength) =>
            "[{\"id\": \"arch.class.p2_05\", \"name_key\": \"l10n.arch.class.p2_05.name\", " +
            "\"primary_stat\": \"stat.strength\", \"base_stats\": {\"stat.strength\": " + strength + "}, " +
            "\"power_types\": []}]";

        private static double StrengthOf(ClassDefinition def)
        {
            foreach (var pair in def.BaseStats)
            {
                if (pair.Key == "stat.strength") return pair.Value;
            }
            throw new InvalidOperationException("stat.strength not found in BaseStats");
        }

        [Fact]
        public void P2_05_Reload_PicksUpNewClassBaseStats_AfterDataLoadCompleted()
        {
            var bus = MakeBus();
            var source = new MutableSource()
                .Add("arch.class", Envelope("arch.class", ClassRow(10)))
                .Add("arch.race", Envelope("arch.race", "[]"))
                .Add("arch.talent_tree", Envelope("arch.talent_tree", "[]"));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(ArchSchemas.Class);
            registry.RegisterSchema(ArchSchemas.Race);
            registry.RegisterSchema(ArchSchemas.TalentTree);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var registryHost = new ArchetypeRegistry(
                registry, bus,
                statBaseWriter: (_, __, ___) => { },
                statModifierWriter: (_, __, ___, ____, _____) => { },
                powerRegistrar: (_, __) => { });

            var before = registryHost.GetClass(new Id("arch.class.p2_05"));
            Assert.NotNull(before);
            Assert.Equal(10, StrengthOf(before!));

            source.Replace("arch.class", Envelope("arch.class", ClassRow(25)));
            var reload = registry.Reload("arch.class");
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 1, reload.ErrorCount, reload.WarningCount));

            var after = registryHost.GetClass(new Id("arch.class.p2_05"));
            Assert.NotNull(after);
            Assert.Equal(25, StrengthOf(after!));
        }
    }
}
