using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Numbers.StatBlock
{
    /// <summary>
    /// P2-05 同类缓存收口回归测试（外部审计 audit-c9ff301-20260909 followup-2026-09-10）：<see
    /// cref="StatHost"/> 的 <c>stat.definition</c>/<c>stat.rating_conversion</c> 缓存与
    /// <c>Core.Rules.Skill.SkillDefCache</c>/<c>Core.Numbers.Archetype.ArchetypeRegistry</c> 同一类
    /// "构造期一次性读 registry 建索引、之后只读"模式，此前没有订阅 <see
    /// cref="DataLoadCompletedEvent"/>，reload <c>stat.definition</c> 后 resident host 继续返回旧的
    /// <see cref="StatDefinition"/>。改造自 <c>P2_05_ArchetypeRegistryReloadTests</c> 同一套
    /// <c>MutableSource</c> 写法（<c>InMemoryDataSource.Add</c> 只能追加、不能就地覆写）。
    /// </summary>
    public sealed class P2_05_StatHostReloadTests
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
                new EventDefinition(StatBlockEventKeys.StatChanged, "stat",
                    new[] { "unitId", "stat", "oldValue", "newValue" }),
            });
            return new EventBus(catalog);
        }

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static string DefRow(double defaultBase) =>
            "[{\"id\": \"stat.p2_05\", \"name_key\": \"l10n.stat.p2_05.name\", \"group\": \"primary\", " +
            "\"default_base\": " + defaultBase.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}]";

        [Fact]
        public void Reload_PicksUpNewDefaultBase_AfterDataLoadCompleted()
        {
            var bus = MakeBus();
            var source = new MutableSource().Add("stat.definition", Envelope("stat.definition", DefRow(0)));
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatSchemas.Definition);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var host = new StatHost(registry, bus);
            var unit = new Id("unit.p2_05_stat_reload");
            host.RegisterUnit(unit);

            // reload 前：新注册单位未 SetBase 过，GetStat 走 default_base=0。
            Assert.Equal(0, host.GetStat(unit, new Id("stat.p2_05")));

            source.Replace("stat.definition", Envelope("stat.definition", DefRow(77)));
            var reload = registry.Reload("stat.definition");
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 1, reload.ErrorCount, reload.WarningCount));

            // 修复前：StatHost 只在构造期读过一次 stat.definition，reload 后继续沿用旧的
            // default_base=0，新注册单位也读不到新值。
            var freshUnit = new Id("unit.p2_05_stat_reload_fresh");
            host.RegisterUnit(freshUnit);
            Assert.Equal(77, host.GetStat(freshUnit, new Id("stat.p2_05")));
        }

        [Fact]
        public void Reload_DoesNotResetAlreadyRegisteredUnitCachedValue()
        {
            var bus = MakeBus();
            var source = new MutableSource().Add("stat.definition", Envelope("stat.definition", DefRow(0)));
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatSchemas.Definition);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var host = new StatHost(registry, bus);
            var unit = new Id("unit.p2_05_stat_reload_existing");
            host.RegisterUnit(unit);
            host.SetBase(unit, new Id("stat.p2_05"), 500);
            Assert.Equal(500, host.GetStat(unit, new Id("stat.p2_05")));

            source.Replace("stat.definition", Envelope("stat.definition", DefRow(9)));
            var reload = registry.Reload("stat.definition");
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 1, reload.ErrorCount, reload.WarningCount));

            // 已注册单位此前 SetBase 过的最终值缓存（_units，运行期状态）不受定义表 reload 影响，
            // 与 ArchetypeRegistry.ReloadFromRegistry 判断记录"reload 只刷新定义表本身，不倒退已
            // 应用的效果"一致。
            Assert.Equal(500, host.GetStat(unit, new Id("stat.p2_05")));
        }
    }
}
