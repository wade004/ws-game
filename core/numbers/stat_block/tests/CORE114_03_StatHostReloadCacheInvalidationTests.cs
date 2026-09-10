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
    /// CORE114-03 根治验收（外部审计 audit-76d16a5-20260910，见 <see
    /// cref="StatHost.RecomputeAllCachedStatsAfterReload"/> 判断记录）：<c>stat.definition</c>
    /// reload 后，未显式 <see cref="StatHost.SetBase"/> 过的单位下一次 <see
    /// cref="StatHost.GetStat"/> 必须看到新定义——不能因为"reload 前查询过一次、缓存住了旧值"就
    /// 与另一个从未查询过的同规则单位分叉出不同结果。改造自审计探针
    /// <c>CoreBoundaryProbe.StatQueryOrder</c>。
    /// </summary>
    public sealed class CORE114_03_StatHostReloadCacheInvalidationTests
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
            "[{\"id\": \"stat.core114_03\", \"name_key\": \"l10n.stat.core114_03.name\", \"group\": \"primary\", " +
            "\"default_base\": " + defaultBase.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}]";

        private static readonly Id Stat = new Id("stat.core114_03");

        [Fact]
        public void Reload_TwoUnregisteredOverrideUnits_BothSeeNewDefaultBase_RegardlessOfQueryOrder()
        {
            var bus = MakeBus();
            var source = new MutableSource().Add("stat.definition", Envelope("stat.definition", DefRow(0)));
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatSchemas.Definition);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var host = new StatHost(registry, bus);
            var unitA = new Id("unit.core114_03_a");
            var unitB = new Id("unit.core114_03_b");
            host.RegisterUnit(unitA);
            host.RegisterUnit(unitB);

            // 只查询 A，把它的派生值缓存住（外部审计复现的关键前置条件）。
            var beforeA = host.GetStat(unitA, Stat);
            Assert.Equal(0, beforeA);

            source.Replace("stat.definition", Envelope("stat.definition", DefRow(77)));
            var reload = registry.Reload("stat.definition");
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 1, reload.ErrorCount, reload.WarningCount));

            var afterA = host.GetStat(unitA, Stat); // 修复前：仍返回缓存住的旧值 0。
            var afterB = host.GetStat(unitB, Stat); // 从未查询过，直接现算，一直是 77。

            Assert.Equal(77, afterA);
            Assert.Equal(77, afterB);
            Assert.Equal(afterA, afterB);
        }

        [Fact]
        public void Reload_ExplicitSetBaseUnit_KeepsExplicitValueAfterReload()
        {
            var bus = MakeBus();
            var source = new MutableSource().Add("stat.definition", Envelope("stat.definition", DefRow(0)));
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatSchemas.Definition);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var host = new StatHost(registry, bus);
            var unit = new Id("unit.core114_03_explicit");
            host.RegisterUnit(unit);
            host.SetBase(unit, Stat, 500);
            Assert.Equal(500, host.GetStat(unit, Stat));

            source.Replace("stat.definition", Envelope("stat.definition", DefRow(77)));
            var reload = registry.Reload("stat.definition");
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 1, reload.ErrorCount, reload.WarningCount));

            // 显式 SetBase 过的值是运行期状态，定义表 default_base 变化不应覆盖它。
            Assert.Equal(500, host.GetStat(unit, Stat));
        }

        [Fact]
        public void Reload_CachedStatValueChanges_FiresStatChangedEvent()
        {
            var bus = MakeBus();
            var source = new MutableSource().Add("stat.definition", Envelope("stat.definition", DefRow(0)));
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatSchemas.Definition);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var host = new StatHost(registry, bus);
            var unit = new Id("unit.core114_03_event");
            host.RegisterUnit(unit);
            Assert.Equal(0, host.GetStat(unit, Stat)); // 缓存住 0。

            StatChangedEvent? seen = null;
            bus.Subscribe<StatChangedEvent>(StatBlockEventKeys.StatChanged, evt => seen = evt);

            source.Replace("stat.definition", Envelope("stat.definition", DefRow(77)));
            var reload = registry.Reload("stat.definition");
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 1, reload.ErrorCount, reload.WarningCount));
            bus.DispatchPending();

            Assert.NotNull(seen);
            Assert.Equal(unit, seen!.UnitId);
            Assert.Equal(Stat, seen.Stat);
            Assert.Equal(0, seen.OldValue);
            Assert.Equal(77, seen.NewValue);
        }

        [Fact]
        public void Reload_CachedStatValueUnchanged_DoesNotFireEvent()
        {
            var bus = MakeBus();
            var source = new MutableSource().Add("stat.definition", Envelope("stat.definition", DefRow(42)));
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatSchemas.Definition);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var host = new StatHost(registry, bus);
            var unit = new Id("unit.core114_03_nochange");
            host.RegisterUnit(unit);
            Assert.Equal(42, host.GetStat(unit, Stat));

            var fired = false;
            bus.Subscribe<StatChangedEvent>(StatBlockEventKeys.StatChanged, _ => fired = true);

            // 用同一个数值重新加载（内容未变，只是走了一遍 reload 流程）。
            source.Replace("stat.definition", Envelope("stat.definition", DefRow(42)));
            var reload = registry.Reload("stat.definition");
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 1, reload.ErrorCount, reload.WarningCount));
            bus.DispatchPending();

            Assert.False(fired);
            Assert.Equal(42, host.GetStat(unit, Stat));
        }
    }
}
