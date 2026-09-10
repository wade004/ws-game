using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Carriers.Item
{
    /// <summary>
    /// CORE114 静态候选验收（外部审计 audit-76d16a5-20260910 core-findings.md"静态边界与未列 P2"一节
    /// "Equipment set cache"）：<see cref="Core.Carriers.Item.EquipmentHost"/> 的
    /// <c>_appliedSetBonuses</c>（套装件数门槛已施加的光环句柄簿记）此前在 <c>item.set</c> reload
    /// 后不与新定义对账——<c>RecomputeSetBonuses</c> 只遍历"新定义里存在的门槛"决定是否施加/撤销，
    /// reload 把某个门槛删除/改高后，该门槛在旧记录（<c>_appliedSetBonuses</c>）里仍然"applied"，
    /// 但新定义的遍历根本不会再访问到这个门槛 key，句柄因此永久孤儿（不会被
    /// <see cref="AuraHandleLedger"/> 释放，光环持续生效，直到进程重建全新 host）。
    /// <para>
    /// 用真实 <see cref="CarriersAssembly"/>（同目录 <c>EquipmentSetBonusSharedAuraTests</c> 同一套
    /// 装配写法，真实 <c>AuraHost</c>/<c>EquipmentHost</c>/<c>InventoryHost</c> 全链路，不是模块内部
    /// 简化 fake）+ 合法 <c>item.template.set_id</c> 引用 + resident host（同一个 <c>EquipmentHost</c>
    /// 实例贯穿 equip→reload→unequip 全程，不重新构造）。
    /// </para>
    /// </summary>
    public sealed class CORE114_EquipmentSetBonusReloadOrphanTests
    {
        private static readonly Id MapId = new Id("map.core114_set_test");
        private static readonly Id PlayerTemplateId = new Id("creature.sample_core114_player");
        private static readonly Id TierNormal = new Id("creature.tier.core114_normal");
        private static readonly Id StatPower = new Id("stat.power");
        private static readonly Id BonusAuraDefId = new Id("skill.aura_def.core114_set_bonus");

        private static readonly Id SlotSetOne = new Id("item.slot.core114_set_one");
        private static readonly Id SlotSetTwo = new Id("item.slot.core114_set_two");

        private static readonly Id TemplateSetOne = new Id("item.sample_core114_set_one");
        private static readonly Id TemplateSetTwo = new Id("item.sample_core114_set_two");
        private static readonly Id SetId = new Id("item.set.core114_sample");

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class MutableSource : IDataSource
        {
            private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(System.StringComparer.Ordinal);
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

        private static string ItemSetRow(string bonusesJsonFragment) =>
            "[{\"id\": \"" + SetId.Value + "\", \"name_key\": \"l10n.item.set.core114_sample\", " +
            "\"pieces\": [\"" + TemplateSetOne.Value + "\", \"" + TemplateSetTwo.Value + "\"], " +
            "\"bonuses\": " + bonusesJsonFragment + "}]";

        private const string TwoPieceBonus = "[{\"count\": 2, \"aura_ref\": \"skill.aura_def.core114_set_bonus\"}]";
        private const string NoBonus = "[]";

        private static MutableSource BuildDataSource()
        {
            var statDefinitionRows = "[" +
                "{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 1}" +
                "]";

            var powerTypeRows = "[" +
                "{\"id\": \"" + WellKnownPowers.Health.Value + "\", \"name_key\": \"l10n.power.health.name\", " +
                "\"max_source\": {\"kind\": \"fixed\", \"value\": 1000}, " +
                "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
                "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}" +
                "]";

            var tierDefinitionRows = "[" +
                "{\"id\": \"" + TierNormal.Value + "\", \"name_key\": \"l10n.creature.tier.core114_normal.name\", " +
                "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}" +
                "]";

            var creatureTemplateRows = "[" +
                "{\"id\": \"" + PlayerTemplateId.Value + "\", \"name_key\": \"l10n.creature.core114_player.name\", " +
                "\"level\": 1, \"tier\": \"" + TierNormal.Value + "\", " +
                "\"base_stats\": {\"stat.power\": 1}, \"faction_id\": \"fac.core114_test\", " +
                "\"display_ref\": \"display.core114_player\"}" +
                "]";

            var hitTableRows = "[" +
                "{\"id\": \"combat.hit_table.default\", " +
                "\"miss\": {\"enabled\": false, \"base\": 0}, \"dodge\": {\"enabled\": false, \"base\": 0}, " +
                "\"parry\": {\"enabled\": false, \"base\": 0}, \"glancing_blow\": {\"enabled\": false, \"base\": 0}, " +
                "\"block\": {\"enabled\": false, \"base\": 0}, \"crit\": {\"enabled\": false, \"base\": 0}, " +
                "\"crit_multiplier_base\": 2.0}" +
                "]";

            var auraDefRows = "[" +
                "{\"id\": \"" + BonusAuraDefId.Value + "\", \"duration\": 60, \"max_stacks\": 1, " +
                "\"effects\": [{\"kind\": \"mod_stat\", \"params\": {" +
                "\"stat\": \"" + StatPower.Value + "\", \"op\": \"flat\", \"value\": 100}}]}" +
                "]";

            var itemSlotDefinitionRows = "[" +
                "{\"id\": \"" + SlotSetOne.Value + "\", \"name_key\": \"l10n.item.slot.core114_set_one\"}," +
                "{\"id\": \"" + SlotSetTwo.Value + "\", \"name_key\": \"l10n.item.slot.core114_set_two\"}" +
                "]";

            var itemQualityDefinitionRows = "[" +
                "{\"id\": \"item.quality.core114_common\", \"name_key\": \"l10n.item.quality.core114_common\"}" +
                "]";

            var itemTemplateRows = "[" +
                "{\"id\": \"" + TemplateSetOne.Value + "\", \"slot\": \"" + SlotSetOne.Value + "\", " +
                "\"quality\": \"item.quality.core114_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.core114_set_one\", \"stack_size\": 1, \"name_key\": \"l10n.item.core114_set_one\", " +
                "\"set_id\": \"" + SetId.Value + "\"}," +
                "{\"id\": \"" + TemplateSetTwo.Value + "\", \"slot\": \"" + SlotSetTwo.Value + "\", " +
                "\"quality\": \"item.quality.core114_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.core114_set_two\", \"stack_size\": 1, \"name_key\": \"l10n.item.core114_set_two\", " +
                "\"set_id\": \"" + SetId.Value + "\"}" +
                "]";

            var itemBudgetCurveRows = "[" +
                "{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]";

            return new MutableSource()
                .Add("stat.definition", Envelope("stat.definition", statDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", powerTypeRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name, tierDefinitionRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.Template.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.Template.Name, creatureTemplateRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", hitTableRows))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add("skill.aura_def", Envelope("skill.aura_def", auraDefRows))
                .Add("item.slot_definition", Envelope("item.slot_definition", itemSlotDefinitionRows))
                .Add("item.quality_definition", Envelope("item.quality_definition", itemQualityDefinitionRows))
                .Add("item.set", Envelope("item.set", ItemSetRow(TwoPieceBonus)))
                .Add("item.template", Envelope("item.template", itemTemplateRows))
                .Add("item.budget_curve", Envelope("item.budget_curve", itemBudgetCurveRows));
        }

        private static CarriersAssembly BuildAssembly(out Id playerId, out DataRegistry registry, out IEventBus bus, out MutableSource source)
        {
            bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            source = BuildDataSource();
            registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var assembly = new CarriersAssembly(bus, registry, rng, world, spatial);

            playerId = assembly.Creatures.Spawn(PlayerTemplateId, MapId, new Vec2(0, 0), 0);
            return assembly;
        }

        private static Id EquipFreshInstance(CarriersAssembly assembly, Id playerId, Id templateId, Id slot)
        {
            assembly.Inventory.AddItem(playerId, templateId, 1);
            var items = assembly.Inventory.ListItems(playerId);
            var instanceId = items[items.Count - 1].InstanceId;
            var result = assembly.Equipment.Equip(playerId, instanceId, slot);
            Assert.True(result.Success, $"装备 \"{templateId}\" 到槽位 \"{slot}\" 应当成功：{result.Reason}");
            return instanceId;
        }

        [Fact]
        public void SetBonusThresholdRemovedByReload_UnequippingAllPieces_ReleasesOrphanedAuraCompletely()
        {
            var assembly = BuildAssembly(out var playerId, out var registry, out var bus, out var source);

            EquipFreshInstance(assembly, playerId, TemplateSetOne, SlotSetOne);
            EquipFreshInstance(assembly, playerId, TemplateSetTwo, SlotSetTwo);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, BonusAuraDefId));
            Assert.Equal(1 + 100, assembly.Rules.Stats.GetStat(playerId, StatPower));

            // reload：套装门槛加成整条被删除（内容作者下架了这条门槛，或改到不可达的件数）。
            source.Replace("item.set", Envelope("item.set", ItemSetRow(NoBonus)));
            var reload = registry.Reload("item.set");
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 1, reload.ErrorCount, reload.WarningCount));

            // 逐件卸下——修复前：RecomputeSetBonuses 只遍历新定义（空 bonuses），旧门槛记录
            // （_appliedSetBonuses[(player,set)][2]）永远不会被访问到，光环句柄孤儿、永久生效。
            var unequippedOne = assembly.Equipment.Unequip(playerId, SlotSetOne);
            Assert.NotNull(unequippedOne);
            var unequippedTwo = assembly.Equipment.Unequip(playerId, SlotSetTwo);
            Assert.NotNull(unequippedTwo);

            Assert.False(assembly.Rules.Skill.AuraQuery.HasAura(playerId, BonusAuraDefId),
                "套装门槛定义已被 reload 删除，卸下全部套装件后旧门槛加成的光环句柄必须被释放，不能残留。");
            Assert.Equal(1, assembly.Rules.Stats.GetStat(playerId, StatPower));
        }

        [Fact]
        public void SetBonusThresholdRaisedByReload_RecomputeOnEquipChange_ReleasesOrphanedAura()
        {
            var assembly = BuildAssembly(out var playerId, out var registry, out var bus, out var source);

            EquipFreshInstance(assembly, playerId, TemplateSetOne, SlotSetOne);
            EquipFreshInstance(assembly, playerId, TemplateSetTwo, SlotSetTwo);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, BonusAuraDefId));

            // reload：门槛从 2 件改到 99 件（远超玩家实际能装备的件数——等价于"事实上不可达"）。
            const string raisedBonus = "[{\"count\": 99, \"aura_ref\": \"skill.aura_def.core114_set_bonus\"}]";
            source.Replace("item.set", Envelope("item.set", ItemSetRow(raisedBonus)));
            var reload = registry.Reload("item.set");
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));
            bus.PublishImmediate(new DataLoadCompletedEvent(registry.Tables.Count, 1, reload.ErrorCount, reload.WarningCount));

            // 任意一次装备变化触发 RecomputeSetBonuses：旧门槛 2（已不在新定义里）必须被当作孤儿清理，
            // 不能因为新定义只有门槛 99（currentCount 永远达不到）就让循环完全绕过旧记录。
            var unequippedOne = assembly.Equipment.Unequip(playerId, SlotSetOne);
            Assert.NotNull(unequippedOne);

            Assert.False(assembly.Rules.Skill.AuraQuery.HasAura(playerId, BonusAuraDefId),
                "旧门槛 2 在新定义里已不存在，卸下一件套装件触发重算后光环应当被释放。");
        }
    }
}
