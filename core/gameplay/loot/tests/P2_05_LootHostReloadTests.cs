using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Rng;
using Core.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Loot
{
    /// <summary>
    /// P2-05 同类缓存收口回归测试（外部审计 audit-c9ff301-20260909 followup-2026-09-10）：<see
    /// cref="LootHost"/> 的 <c>loot.table</c> 定义缓存与 <c>Core.Rules.Skill.SkillDefCache</c>/
    /// <c>Core.Numbers.Archetype.ArchetypeRegistry</c> 同一类"构造期一次性读 registry 建索引、之后
    /// 只读"模式，此前没有订阅 <see cref="DataLoadCompletedEvent"/>，reload <c>loot.table</c> 后
    /// resident host 继续按旧的掉落表定义结算。改造自 <c>P2_05_ArchetypeRegistryReloadTests</c> 同一
    /// 套 <c>MutableSource</c> 写法（<c>InMemoryDataSource.Add</c> 只能追加、不能就地覆写）。
    /// </summary>
    public sealed class P2_05_LootHostReloadTests
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

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        // 判断记录（100% 命中的表）：本测试只关心"reload 后 _tables 缓存是否被真正替换"，与掉落
        // 判定的随机分支无关——两版表都用 weight_or_chance=1、count_range 固定 1，消除随机性，只靠
        // ref 指向的不同物品 id 判断到底命中了哪一版定义（同 LootHostRollTests 既有惯例）。
        private static string TableRow(string itemId) =>
            "[{\"id\": \"loot.p2_05\", \"groups\": [" +
            "{\"roll_mode\": \"chance_each\", \"entries\": [" +
            "{\"ref\": \"" + itemId + "\", \"weight_or_chance\": 1, \"count_range\": {\"min\":1,\"max\":1}}" +
            "]}]}]";

        [Fact]
        public void Reload_PicksUpNewTableDefinition_AfterDataLoadCompleted()
        {
            var bus = LootTestSupport.NewEventBus();
            var source = new MutableSource().Add(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, TableRow("item.p2_05_before")));
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(LootSchemas.Table);
            registry.RegisterValidationRule(new LootContentValidationRule());
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = LootTestSupport.NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var inventory = new FakeInventoryHost();
            var exprFactory = new FakeExprHostFactory();
            var host = new LootHost(registry, new RngHost(1UL), bus, world, units, inventory, exprFactory, simTimeProvider: () => 0.0);

            var before = host.Roll(new Id("loot.p2_05"), new RollContext(new Id("unit.p2_05_source")));
            Assert.Single(before);
            Assert.Equal(new Id("item.p2_05_before"), before[0].TemplateId);

            // 修复前：LootHost 只在构造期读过一次 loot.table，reload 后 resident host 继续沿用旧的
            // TableDef，Roll 依然产出 item.p2_05_before。
            source.Replace(LootSchemas.Table.Name, Envelope(LootSchemas.Table.Name, TableRow("item.p2_05_after")));
            var reload = registry.Reload(LootSchemas.Table.Name);
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 1, reload.ErrorCount, reload.WarningCount));

            var after = host.Roll(new Id("loot.p2_05"), new RollContext(new Id("unit.p2_05_source")));
            Assert.Single(after);
            Assert.Equal(new Id("item.p2_05_after"), after[0].TemplateId);
        }
    }
}
