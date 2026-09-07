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
    /// C08 收口（外部审计 7e63d66 第四轮，P2）：<c>AllowMultiSourceTiming=false</c>（默认）、
    /// <c>maxStacks=1</c>、<c>StackOverflowPolicy.Replace</c> 下，装备 A 授予的 aura 得到句柄 h1；
    /// 装备 B（不同槽位，授予同一个 <c>aura_def</c>）触发 Replace 策略，AuraHost 内部删除 h1、创建
    /// h2。修复前 <see cref="Core.Carriers.Item.EquipmentHost"/> 按各自装备时拿到的句柄各自记账
    /// （A 仍记 h1，B 记 h2），任一件先卸下都会按自己记录的（可能已失效的）句柄错误判断"是否还有
    /// 其它来源"，导致另一件仍装备着却没有了应有光环。
    /// <para>
    /// 用真实 <see cref="Core.Carriers.Assembly.CarriersAssembly"/>（真实 <c>CreatureFactory</c>/
    /// <c>AuraHost</c>/<c>EquipmentHost</c>/<c>InventoryHost</c> 全链路组合，不是任何模块内部的
    /// fake unit access）驱动，本文件是本类专属的新测试文件（不改动本模块既有
    /// <c>EquipmentHostTests.cs</c>/<c>TestSupport.cs</c>，复用其 <c>internal</c> 帮助方法/类型均
    /// 只读引用，未修改）。
    /// </para>
    /// </summary>
    public sealed class EquipmentReplaceHandleTests
    {
        private static readonly Id MapId = new Id("map.c08_test");
        private static readonly Id PlayerTemplateId = new Id("creature.sample_c08_player");
        private static readonly Id TierNormal = new Id("creature.tier.c08_normal");
        private static readonly Id StatPower = new Id("stat.power");
        private static readonly Id SharedAuraDefId = new Id("skill.aura_def.c08_shared_trinket_aura");
        private static readonly Id SlotTrinketA = new Id("item.slot.c08_trinket_a");
        private static readonly Id SlotTrinketB = new Id("item.slot.c08_trinket_b");
        private static readonly Id TemplateTrinketA = new Id("item.sample_c08_trinket_a");
        private static readonly Id TemplateTrinketB = new Id("item.sample_c08_trinket_b");

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private static InMemoryDataSource BuildDataSource()
        {
            var statDefinitionRows = "[" +
                "{\"id\": \"stat.power\", \"name_key\": \"l10n.stat.power.name\", \"group\": \"primary\", \"default_base\": 0}" +
                "]";

            var powerTypeRows = "[" +
                "{\"id\": \"" + WellKnownPowers.Health.Value + "\", \"name_key\": \"l10n.power.health.name\", " +
                "\"max_source\": {\"kind\": \"fixed\", \"value\": 1000}, " +
                "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
                "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}" +
                "]";

            var tierDefinitionRows = "[" +
                "{\"id\": \"" + TierNormal.Value + "\", \"name_key\": \"l10n.creature.tier.c08_normal.name\", " +
                "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}" +
                "]";

            var creatureTemplateRows = "[" +
                "{\"id\": \"" + PlayerTemplateId.Value + "\", \"name_key\": \"l10n.creature.c08_player.name\", " +
                "\"level\": 1, \"tier\": \"" + TierNormal.Value + "\", " +
                "\"base_stats\": {\"stat.power\": 1}, \"faction_id\": \"fac.c08_test\", " +
                "\"display_ref\": \"display.c08_player\"}" +
                "]";

            var hitTableRows = "[" +
                "{\"id\": \"combat.hit_table.default\", " +
                "\"miss\": {\"enabled\": false, \"base\": 0}, \"dodge\": {\"enabled\": false, \"base\": 0}, " +
                "\"parry\": {\"enabled\": false, \"base\": 0}, \"glancing_blow\": {\"enabled\": false, \"base\": 0}, " +
                "\"block\": {\"enabled\": false, \"base\": 0}, \"crit\": {\"enabled\": false, \"base\": 0}, " +
                "\"crit_multiplier_base\": 2.0}" +
                "]";

            var auraDefRows = "[" +
                "{\"id\": \"" + SharedAuraDefId.Value + "\", \"duration\": 60, \"max_stacks\": 1, " +
                "\"effects\": [{\"kind\": \"mod_stat\", \"params\": {" +
                "\"stat\": \"" + StatPower.Value + "\", \"op\": \"flat\", \"value\": 100}}]}" +
                "]";

            var itemSlotDefinitionRows = "[" +
                "{\"id\": \"" + SlotTrinketA.Value + "\", \"name_key\": \"l10n.item.slot.c08_trinket_a\"}," +
                "{\"id\": \"" + SlotTrinketB.Value + "\", \"name_key\": \"l10n.item.slot.c08_trinket_b\"}" +
                "]";

            var itemQualityDefinitionRows = "[" +
                "{\"id\": \"item.quality.c08_common\", \"name_key\": \"l10n.item.quality.c08_common\"}" +
                "]";

            // A、B 是两个不同槽位（可以同时装备，互不排挤），但都 grants 同一个 aura_def——
            // AllowMultiSourceTiming=false 时 AuraHost 按 (targetId, defId, null) 共享同一个槽位，
            // 与"两件不同槽位的装备恰好授予同一条光环"这一 07 允许的合法配置完全对应（C08 复现前提）。
            var itemTemplateRows = "[" +
                "{\"id\": \"" + TemplateTrinketA.Value + "\", \"slot\": \"" + SlotTrinketA.Value + "\", " +
                "\"quality\": \"item.quality.c08_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.c08_trinket_a\", \"stack_size\": 1, \"name_key\": \"l10n.item.c08_trinket_a\", " +
                "\"grants\": {\"auras\": [\"" + SharedAuraDefId.Value + "\"]}}," +
                "{\"id\": \"" + TemplateTrinketB.Value + "\", \"slot\": \"" + SlotTrinketB.Value + "\", " +
                "\"quality\": \"item.quality.c08_common\", \"item_level\": 1, " +
                "\"display_ref\": \"display.c08_trinket_b\", \"stack_size\": 1, \"name_key\": \"l10n.item.c08_trinket_b\", " +
                "\"grants\": {\"auras\": [\"" + SharedAuraDefId.Value + "\"]}}" +
                "]";

            var itemBudgetCurveRows = "[" +
                "{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]";

            return new InMemoryDataSource()
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
                .Add("item.template", Envelope("item.template", itemTemplateRows))
                .Add("item.budget_curve", Envelope("item.budget_curve", itemBudgetCurveRows));
        }

        private static CarriersAssembly BuildAssembly(out Id playerId)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = BuildDataSource();
            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);

            // C08 复现前提：AllowMultiSourceTiming 保持默认 false（同 slot key 共享实例），
            // StackOverflowPolicy 显式设为 Replace（默认是 RefreshOnly，不会换句柄，不构成本条
            // 缺陷的触发条件）。
            var skillOptions = new SkillOptions { StackOverflowPolicy = StackOverflowPolicy.Replace };

            var assembly = new CarriersAssembly(bus, registry, rng, world, spatial, skillOptions: skillOptions);

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
        public void ReplacePolicy_TwoItemsGrantSameAura_EitherUnequippedFirst_KeepsAuraUntilBothUnequipped()
        {
            var assembly = BuildAssembly(out var playerId);

            // 装备 A：AuraHost 创建实例 h1。
            EquipFreshInstance(assembly, playerId, TemplateTrinketA, SlotTrinketA);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId));

            // 装备 B：与 A 共享同一个 AuraHost 槽位（同 defId、AllowMultiSourceTiming=false），层数
            // 从 1 叠加到 2，超过 max_stacks=1，触发 StackOverflowPolicy.Replace——AuraHost 内部把 h1
            // 换成全新的 h2。修复前 EquipmentHost 对 A 的授予记录仍然停留在已经失效的 h1。
            EquipFreshInstance(assembly, playerId, TemplateTrinketB, SlotTrinketB);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId));

            // 卸下 B：修复前该断言会失败——EquipmentHost 按 B 记录的句柄（本就是真正存活的 h2）
            // 计数会被错误地判定为"最后一个引用"（因为 A 名下的份额从未被计入 h2），把光环连带 A
            // 还装备着这件事一起清空。
            var unequippedB = assembly.Equipment.Unequip(playerId, SlotTrinketB);
            Assert.NotNull(unequippedB);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId),
                "卸下 B 后，A 仍装备着，共享光环应当继续保留");
            Assert.Equal(1 + 100, assembly.Rules.Stats.GetStat(playerId, StatPower));

            // 卸下 A（最后一件）：光环才应当真正清空。
            var unequippedA = assembly.Equipment.Unequip(playerId, SlotTrinketA);
            Assert.NotNull(unequippedA);
            Assert.False(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId),
                "两件装备全部卸下后，共享光环应当被清空");
            Assert.Equal(1, assembly.Rules.Stats.GetStat(playerId, StatPower));
        }

        [Fact]
        public void ReplacePolicy_TwoItemsGrantSameAura_UnequipAFirst_KeepsAuraUntilBothUnequipped()
        {
            var assembly = BuildAssembly(out var playerId);

            EquipFreshInstance(assembly, playerId, TemplateTrinketA, SlotTrinketA);
            EquipFreshInstance(assembly, playerId, TemplateTrinketB, SlotTrinketB);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId));

            // 反过来先卸 A：验证迁移不是单向偏袒某一件装备，两种卸载顺序都应保持"任一件仍装备着，
            // 光环就还在"。
            var unequippedA = assembly.Equipment.Unequip(playerId, SlotTrinketA);
            Assert.NotNull(unequippedA);
            Assert.True(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId),
                "卸下 A 后，B 仍装备着，共享光环应当继续保留");

            var unequippedB = assembly.Equipment.Unequip(playerId, SlotTrinketB);
            Assert.NotNull(unequippedB);
            Assert.False(assembly.Rules.Skill.AuraQuery.HasAura(playerId, SharedAuraDefId),
                "两件装备全部卸下后，共享光环应当被清空");
        }
    }
}
